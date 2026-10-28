using System.Buffers.Binary;
using System.IO.Pipes;
using System.Reflection;
using System.Text.Json;
using AcDream.Launcher.Automation;
using AcDream.Launcher.Core.Orchestration;
using AcDream.Launcher.Core.Profiles;
using AcDream.Platform;

namespace AcDream.Launcher.Tests;

public sealed class LauncherControlTests
{
    [Fact]
    public void PipeIsOptInAndNamesAreValidated()
    {
        static ApplicationPathSet Paths() => new("config", "data", "cache");
        Assert.Null(LauncherStartupOptions.Parse([], Paths).ControlPipe);
        Assert.Equal("test-123", LauncherStartupOptions.Parse(["--control-pipe", "test-123"], Paths).ControlPipe);
        foreach (string invalid in new[] { "a/b", "a\\b", "a b", new string('a', 65), "ä" })
            Assert.Throws<LauncherStartupOptionsException>(() => LauncherStartupOptions.Parse(["--control-pipe", invalid], Paths));
        Assert.Throws<LauncherStartupOptionsException>(() => LauncherStartupOptions.Parse(
            ["--control-pipe", "one", "--control-pipe", "two"], Paths));
    }

    [Fact]
    public async Task StartsSavedSelectionAndRejectsDuplicateOrGraphicalLaunch()
    {
        var (owner, spy) = CreateOwner();
        var handler = new LauncherControlHandler(owner);
        Assert.True((await handler.HandleAsync(new("start", "world", "account"), TestContext.Current.CancellationToken)).Ok);
        object?[] call = Assert.Single(spy.Launches);
        Assert.Equal("world", call[0]);
        Assert.Equal("account", call[1]);
        Assert.Equal("Saved character", call[2]);
        Assert.Equal(LaunchMode.Headless, call[3]);
        spy.Account = spy.Account with { HasRunningActivity = true };
        Assert.False((await handler.HandleAsync(new("start", "world", "account"), TestContext.Current.CancellationToken)).Ok);
        spy.Account = spy.Account with { HasRunningActivity = false, SelectedLaunchMode = LaunchMode.Gui };
        Assert.False((await handler.HandleAsync(new("start", "world", "account"), TestContext.Current.CancellationToken)).Ok);
        Assert.False((await handler.HandleAsync(new("start", "other", "account"), TestContext.Current.CancellationToken)).Ok);
        Assert.Single(spy.Launches);
    }

    [Fact]
    public async Task StopRequestsQuitWithoutForceStopAndStatusOmitsCommands()
    {
        var (owner, spy) = CreateOwner();
        var handler = new LauncherControlHandler(owner);
        Assert.True((await handler.HandleAsync(new("stop", Session: "session"), TestContext.Current.CancellationToken)).Ok);
        Assert.Equal(("session", "/quit"), Assert.Single(spy.ConsoleLines));
        Assert.False((await handler.HandleAsync(new("stop", Session: "missing"), TestContext.Current.CancellationToken)).Ok);
        string status = JsonSerializer.Serialize(await handler.HandleAsync(new("status"), TestContext.Current.CancellationToken));
        Assert.DoesNotContain("secret-command", status);
        Assert.DoesNotContain("LoginCommands", status);
        spy.Session = spy.Session with { State = LauncherActivityState.Exited, ExitCode = 0, ExitedGracefully = true };
        Assert.True((await handler.HandleAsync(new("stop", Session: "session"), TestContext.Current.CancellationToken)).Ok);
        Assert.Single(spy.ConsoleLines);
    }

    [Fact]
    public async Task ClientReloadRefusesActiveSessionsAndUsesVerifierWhenIdle()
    {
        var (owner, spy) = CreateOwner();
        int reloads = 0;
        var handler = new LauncherControlHandler(owner, _ =>
        {
            reloads++;
            return Task.FromResult(new ControlReply(true));
        });
        Assert.False((await handler.HandleAsync(new("reload-client"), TestContext.Current.CancellationToken)).Ok);
        Assert.Equal(0, reloads);
        spy.Session = spy.Session with { State = LauncherActivityState.Exited };
        Assert.True((await handler.HandleAsync(new("reload-client"), TestContext.Current.CancellationToken)).Ok);
        Assert.Equal(1, reloads);
    }

    [Fact]
    public async Task ExpiredRequestCannotStopAClient()
    {
        var (owner, spy) = CreateOwner();
        using var expired = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        await expired.CancelAsync();
        var handler = new LauncherControlHandler(owner);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => handler.HandleAsync(new("stop", Session: "session"), expired.Token));
        Assert.Empty(spy.ConsoleLines);
    }

    // On Unix a pipe is a socket under the temp folder; macOS limits that path
    // to 104 characters and its temp folder already takes ~50.
    private static string PipeName() => "lt-" + Guid.NewGuid().ToString("N")[..12];

    [Fact]
    public async Task DeadlineReleasesConnectionEvenWhenHandlerHasNotReturned()
    {
        string name = PipeName();
        var pending = new TaskCompletionSource<ControlReply>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var server = new LauncherControlServer(name,
            (request, _) => request.Command == "pending" ? pending.Task : Task.FromResult(new ControlReply(true)),
            _ => { }, TimeSpan.FromMilliseconds(200));
        await Assert.ThrowsAsync<EndOfStreamException>(() => Exchange(name, "{\"command\":\"pending\"}"u8.ToArray()));
        Assert.True((await Exchange(name, "{\"command\":\"status\"}"u8.ToArray())).GetProperty("ok").GetBoolean());
        pending.SetResult(new ControlReply(true));
    }

    [Fact]
    public async Task PipeHandlesFramingMalformedRequestsAndCancellation()
    {
        string name = PipeName();
        int calls = 0;
        using var server = new LauncherControlServer(name, (request, _) =>
        {
            calls++;
            return Task.FromResult(new ControlReply(true, request.Command));
        }, _ => { });
        Assert.True((await Exchange(name, "{\"command\":\"status\"}"u8.ToArray())).GetProperty("ok").GetBoolean());
        Assert.False((await Exchange(name, "{"u8.ToArray())).GetProperty("ok").GetBoolean());
        Assert.False((await Exchange(name, [], LauncherControlServer.MaximumRequestBytes + 1)).GetProperty("ok").GetBoolean());
        Assert.True((await Exchange(name, "{\"command\":\"status\"}"u8.ToArray())).GetProperty("ok").GetBoolean());
        Assert.Equal(2, calls);
        server.Dispose();
        await server.Completion.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
    }

    private static async Task<JsonElement> Exchange(string name, byte[] payload, int? declaredLength = null)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var client = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
        await client.ConnectAsync(deadline.Token);
        byte[] prefix = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(prefix, declaredLength ?? payload.Length);
        await client.WriteAsync(prefix, deadline.Token);
        if (payload.Length > 0)
            await client.WriteAsync(payload, deadline.Token);
        await client.ReadExactlyAsync(prefix, deadline.Token);
        byte[] response = new byte[BinaryPrimitives.ReadInt32LittleEndian(prefix)];
        await client.ReadExactlyAsync(response, deadline.Token);
        return JsonSerializer.Deserialize<JsonElement>(response);
    }

    private static (ILauncherOrchestrator Owner, OwnerSpy Spy) CreateOwner()
    {
        ILauncherOrchestrator owner = DispatchProxy.Create<ILauncherOrchestrator, OwnerSpy>();
        return (owner, (OwnerSpy)owner);
    }

    public class OwnerSpy : DispatchProxy
    {
        internal List<object?[]> Launches { get; } = [];
        internal List<(string Session, string Line)> ConsoleLines { get; } = [];
        internal LauncherAccountSnapshot Account { get; set; } = new("world", "account", [], false, "idle",
            "Saved character", LaunchMode.Headless, AccountLoginCommands: ["secret-command"]);
        internal LauncherSessionSnapshot Session { get; set; } = new("session", LauncherActivityKind.Play,
            "world", "account", "Saved character", LaunchMode.Headless, LauncherActivityState.InWorld,
            "", null, null, DateTimeOffset.UtcNow);

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            switch (method?.Name)
            {
                case "PollStatus": return null;
                case "GetSnapshot": return new LauncherStateSnapshot(
                    [new("world", "localhost", 9000, [Account])], [Session],
                    new(true, false, true, true, "Windows", null), true, "ready");
                case "LaunchAsync": Launches.Add(args!); return Task.FromResult(Session);
                case "TrySendConsoleLine": ConsoleLines.Add(((string)args![0]!, (string)args[1]!)); return true;
                default: throw new InvalidOperationException("Unexpected owner call: " + method?.Name);
            }
        }
    }
}

