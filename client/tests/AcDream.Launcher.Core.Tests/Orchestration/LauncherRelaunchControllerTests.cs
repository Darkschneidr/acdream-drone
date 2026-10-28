using AcDream.Launcher.Core.Launching;
using AcDream.Launcher.Core.Orchestration;
using AcDream.Launcher.Core.Profiles;

namespace AcDream.Launcher.Core.Tests.Orchestration;

public sealed class LauncherRelaunchControllerTests
{
    private static readonly LauncherRelaunchSelection Choice = new("World", "Account", "Character", LaunchMode.Headless);

    [Fact]
    public async Task HostExitDoesNotStartDelayUntilPhysicalExitAndStartupCompletion()
    {
        using var host = new Host(); var clock = new Clock();
        using var scheduler = new LauncherRelaunchController(host, clock);
        host.Sessions = [Session(1, live: true)];
        scheduler.Observe(host.GetSnapshot(), [Choice], true);
        host.Sessions = [Session(1, live: true) with { State = LauncherActivityState.Exited }];
        scheduler.Observe(host.GetSnapshot(), [Choice], true);
        clock.Advance(500); await scheduler.PollAsync(true);
        Assert.Empty(host.Launches);
        host.Sessions = [Session(1, live: false) with { StartupInFlight = true }];
        scheduler.Observe(host.GetSnapshot(), [Choice], true);
        clock.Advance(500); await scheduler.PollAsync(true);
        Assert.Empty(host.Launches);
        host.Sessions = [Session(1, live: false)];
        scheduler.Observe(host.GetSnapshot(), [Choice], true);
        clock.Advance(179); await scheduler.PollAsync(true);
        Assert.Empty(host.Launches);
        clock.Advance(1); await scheduler.PollAsync(true);
        Assert.Equal(Choice, Assert.Single(host.Launches));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(23)]
    public async Task BothManualStopAndCrashRestartUsingCurrentSelectionAfterDelay(int exitCode)
    {
        using var host = new Host(); var clock = new Clock();
        using var scheduler = new LauncherRelaunchController(host, clock) { DelaySeconds = 5 };
        host.Sessions = [Session(1, true)]; scheduler.Observe(host.GetSnapshot(), [Choice], true);
        host.Sessions = [Session(1, false) with { ExitCode = exitCode, ExitedGracefully = exitCode == 0 }];
        var changed = Choice with { Character = "Different", Mode = LaunchMode.Gui };
        scheduler.Observe(host.GetSnapshot(), [changed], true);
        clock.Advance(5); await scheduler.PollAsync(true);
        Assert.Equal(changed, Assert.Single(host.Launches));
    }

    [Fact]
    public async Task EnablingNeverRevivesHistoryOrProbesButObservesFuturePlayLaunches()
    {
        using var host = new Host(); var clock = new Clock();
        using var scheduler = new LauncherRelaunchController(host, clock) { DelaySeconds = 5 };
        host.Sessions = [Session(1, false)]; scheduler.Observe(host.GetSnapshot(), [Choice], true);
        clock.Advance(20); await scheduler.PollAsync(true); Assert.Empty(host.Launches);
        host.Sessions = [Session(2, false) with { Kind = LauncherActivityKind.Probe }, Session(1, false)];
        scheduler.Observe(host.GetSnapshot(), [Choice], true);
        clock.Advance(20); await scheduler.PollAsync(true); Assert.Empty(host.Launches);
        // A launch through any caller can fail before the next observation.
        host.Sessions = [Session(3, false), Session(1, false)];
        scheduler.Observe(host.GetSnapshot(), [Choice], true);
        clock.Advance(5); await scheduler.PollAsync(true);
        Assert.Single(host.Launches);
    }

    [Fact]
    public async Task FailedAttemptWaitsFullDelayAndCapabilityHoldIsNotBypassed()
    {
        using var host = new Host(); var clock = new Clock();
        using var scheduler = new LauncherRelaunchController(host, clock) { DelaySeconds = 5 };
        host.Sessions = [Session(1, true)]; scheduler.Observe(host.GetSnapshot(), [Choice], true);
        host.Sessions = [Session(1, false)]; scheduler.Observe(host.GetSnapshot(), [Choice], true);
        clock.Advance(5);
        host.Capability = LauncherCapability.Unavailable("Reconnect hold");
        await scheduler.PollAsync(true); Assert.Empty(host.Launches);
        host.Capability = LauncherCapability.Available;
        await scheduler.PollAsync(false); Assert.Empty(host.Launches);
        host.Launch = _ => throw new LauncherOperationException("Failure");
        int errors = 0; scheduler.LaunchFailed += _ => errors++;
        await scheduler.PollAsync(true); Assert.Single(host.Launches);
        clock.Advance(4); await scheduler.PollAsync(true); Assert.Single(host.Launches);
        clock.Advance(1); await scheduler.PollAsync(true); Assert.Equal(2, host.Launches.Count);
        Assert.Equal(2, errors);
    }

    [Theory]
    [InlineData("disable")]
    [InlineData("uncheck")]
    [InlineData("remove")]
    [InlineData("dispose")]
    public async Task RemovingIntentCancelsInflightAndNeverQueuesAnotherAttempt(string action)
    {
        using var host = new Host(); var clock = new Clock();
        using var scheduler = new LauncherRelaunchController(host, clock) { DelaySeconds = 5 };
        host.Sessions = [Session(1, true)]; scheduler.Observe(host.GetSnapshot(), [Choice], true);
        host.Sessions = [Session(1, false)]; scheduler.Observe(host.GetSnapshot(), [Choice], true);
        var completion = new TaskCompletionSource<LauncherSessionSnapshot>();
        CancellationToken launchToken = default;
        host.Launch = token => { launchToken = token; return completion.Task; };
        clock.Advance(5); Task attempt = scheduler.PollAsync(true);
        Assert.True(scheduler.IsLaunching);
        await scheduler.PollAsync(true); Assert.Single(host.Launches);
        if (action == "dispose") scheduler.Dispose();
        else
        {
            if (action == "remove") host.HasAccount = false;
            scheduler.Observe(host.GetSnapshot(), action == "uncheck" ? [] : [Choice], action != "disable");
        }
        Assert.True(launchToken.IsCancellationRequested);
        completion.SetResult(Session(2, false)); await attempt;
        clock.Advance(100); await scheduler.PollAsync(true);
        Assert.Single(host.Launches);
    }

    [Fact]
    public async Task ClearingFinishedHistoryKeepsArmedIntentWithoutResurrectingOlderSessions()
    {
        using var host = new Host(); var clock = new Clock();
        using var scheduler = new LauncherRelaunchController(host, clock) { DelaySeconds = 5 };
        host.Sessions = [Session(1, false), Session(2, true)]; scheduler.Observe(host.GetSnapshot(), [Choice], true);
        host.Sessions = [Session(1, false), Session(2, false)]; scheduler.Observe(host.GetSnapshot(), [Choice], true);
        clock.Advance(3);
        host.Sessions = [Session(1, false)]; scheduler.Observe(host.GetSnapshot(), [Choice], true);
        clock.Advance(2); await scheduler.PollAsync(true);
        Assert.Single(host.Launches);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(3601)]
    public void DelayRejectsOutOfRange(int seconds)
    {
        using var host = new Host();
        using var scheduler = new LauncherRelaunchController(host);
        Assert.Equal(180, scheduler.DelaySeconds);
        Assert.Throws<ArgumentOutOfRangeException>(() => scheduler.DelaySeconds = seconds);
    }

    [Fact]
    public void StopGateSurvivesHostExitAndStartupUntilPhysicalTermination()
    {
        using var host = new Host(); var gate = new LauncherStopGate();
        host.Sessions = [Session(1, true), Session(2, true) with { AccountName = "Other" }];
        Assert.True(gate.TryBegin("1", host.GetSnapshot()));
        Assert.True(gate.TryBegin("2", host.GetSnapshot()));
        Assert.False(gate.TryBegin("1", host.GetSnapshot()));
        host.Sessions[0] = Session(1, true) with { State = LauncherActivityState.Exited };
        gate.Observe(host.GetSnapshot()); Assert.True(gate.IsPending);
        host.Sessions[0] = Session(1, false) with { StartupInFlight = true };
        gate.Observe(host.GetSnapshot()); Assert.True(gate.IsPending);
        host.Sessions[0] = Session(1, false);
        gate.Observe(host.GetSnapshot()); Assert.True(gate.IsPending);
        Assert.False(gate.IsStopping("1"));
        Assert.True(gate.IsStopping("2"));
        host.Sessions[1] = Session(2, false);
        gate.Observe(host.GetSnapshot()); Assert.False(gate.IsPending);
    }

    private static LauncherSessionSnapshot Session(int id, bool live) => new(id.ToString(), LauncherActivityKind.Play,
        "World", "Account", "Character", LaunchMode.Headless, live ? LauncherActivityState.InWorld : LauncherActivityState.Exited,
        "Status", live ? null : 0, null, DateTimeOffset.UnixEpoch.AddSeconds(id), HasLiveProcess: live);

    private sealed class Clock : TimeProvider
    {
        private long _seconds;
        public override long TimestampFrequency => 1;
        public override long GetTimestamp() => _seconds;
        public void Advance(int seconds) => _seconds += seconds;
    }

    private sealed class Host : ILauncherOrchestrator
    {
        public List<LauncherSessionSnapshot> Sessions = [];
        public bool HasAccount = true;
        public LauncherCapability Capability = LauncherCapability.Available;
        public List<LauncherRelaunchSelection> Launches = [];
        public Func<CancellationToken, Task<LauncherSessionSnapshot>>? Launch;
        public event EventHandler? StateChanged { add { } remove { } }
        public LauncherStateSnapshot GetSnapshot() => new([new("World", "localhost", 9000, HasAccount ?
            [new("World", "Account", [], false, "Ready")] : [])], Sessions, new(true, false, true, true, "Windows", null), true, "Ready");
        public LauncherCapability GetLaunchCapability(LaunchMode mode) => Capability;
        public LauncherCapability GetAccountLaunchCapability(string serverName, string accountName, LaunchMode mode) => Capability;
        public LauncherCapability GetProbeCapability(string serverName, string accountName) => Capability;
        public Task<LauncherSessionSnapshot> LaunchAsync(string serverName, string accountName, string? characterName, LaunchMode mode, CancellationToken cancellationToken = default)
        {
            Launches.Add(new(serverName, accountName, characterName, mode));
            return Launch?.Invoke(cancellationToken) ?? Task.FromResult(Session(100 + Launches.Count, true));
        }
        public void LoadProfiles() { }
        public void SetInstallRecord(LauncherInstallRecord? record) { }
        public void AddServer(string name, string host, int port) => throw new NotSupportedException();
        public void EditServer(string name, string newName, string newHost, int newPort) => throw new NotSupportedException();
        public void RemoveServer(string name) => throw new NotSupportedException();
        public void AddAccount(string serverName, string accountName, string password) => throw new NotSupportedException();
        public void EditAccount(string serverName, string accountName, string newAccountName, string? newPassword) => throw new NotSupportedException();
        public void RemoveAccount(string serverName, string accountName) => throw new NotSupportedException();
        public void AddCharacter(string serverName, string accountName, string characterName, string? characterId) => throw new NotSupportedException();
        public void EditCharacterIdentity(string serverName, string accountName, string characterName, string newCharacterName, string? newCharacterId) => throw new NotSupportedException();
        public void UpdateCharacterSettings(string serverName, string accountName, string characterName, LaunchMode mode, IReadOnlyList<string>? plugins) => throw new NotSupportedException();
        public void UpdateAccountSelection(string serverName, string accountName, string? selectedCharacter, LaunchMode selectedLaunchMode) => throw new NotSupportedException();
        public void RemoveCharacter(string serverName, string accountName, string characterName) => throw new NotSupportedException();
        public Task<LauncherSessionSnapshot> ProbeAsync(string serverName, string accountName, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task StopSessionAsync(string sessionId, TimeSpan timeout, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void PollStatus() { }
        public void ClearFinishedSessions() { }
        public void Dispose() { }
    }
}
