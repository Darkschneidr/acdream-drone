using AcDream.Plugin.Abstractions;
using AcDream.Runtime.Plugins;

namespace AcDream.Runtime.Tests.Plugins;

public sealed class PeerHubTests
{
    [Fact]
    public void SocketEndpointFitsWhenPrivateDirectoryUsesMostOfThePlatformPathLimit()
    {
        string root = Path.Combine(Path.GetPathRoot(Path.GetFullPath("."))!, new string('p', 80));
        var endpoint = PeerHubEndpoint.ForDirectory(root);
        Assert.InRange(System.Text.Encoding.UTF8.GetByteCount(endpoint.SocketPath), 1, 104);
        _ = new System.Net.Sockets.UnixDomainSocketEndPoint(endpoint.SocketPath);
    }

    [Fact]
    public async Task NewCastListenerDemandArrivesBeforeItsState()
    {
        await using var fixture = new HubFixture();
        using var source = await fixture.Connect(1, "source", "world", PluginPeerCapabilities.Casts);
        using var listener = await fixture.Connect(2, "listener", "world", PluginPeerCapabilities.Casts);
        var demand = await PeerHubProtocol.ReadAsync(source, fixture.Token);
        Assert.Equal("demand", demand!.Kind);
        Assert.True(demand.Capabilities.HasFlag(PluginPeerCapabilities.Casts));
        var state = await PeerHubProtocol.ReadAsync(source, fixture.Token);
        Assert.Equal("state", state!.Kind);
        Assert.Equal("listener", state.Client.Name);
    }

    [Fact]
    public async Task CommandsArePushedOnlyToSubscribedMatchingWorldAndTags()
    {
        await using var fixture = new HubFixture();
        using var sender = await fixture.Connect(1, "one", "world", PluginPeerCapabilities.SendCommands, ["army"]);
        using var receiver = await fixture.Connect(2, "two", "world", PluginPeerCapabilities.Commands, ["army"]);
        using var otherWorld = await fixture.Connect(3, "three", "other", PluginPeerCapabilities.Commands, ["army"]);
        using var disabled = await fixture.Connect(4, "four", "world", PluginPeerCapabilities.ClientState, ["army"]);
        Guid id = Guid.NewGuid();
        await PeerHubProtocol.WriteAsync(sender, new() { Kind = "command", Id = id, Line = "/example", Tags = ["army"], DelayMilliseconds = 500, CreatedUtc = DateTimeOffset.UtcNow }, fixture.Token);
        var delivered = await fixture.ReadKind(receiver, "command");
        Assert.Equal(id, delivered.Id);
        Assert.Equal(500, delivered.DelayMilliseconds);
        Assert.Equal("/example", delivered.Line);
        Assert.True((await fixture.ReadKind(sender, "ack")).Accepted);
        await PeerHubProtocol.WriteAsync(otherWorld, new() { Kind = "command", Id = Guid.NewGuid(), Line = "/other", CreatedUtc = DateTimeOffset.UtcNow }, fixture.Token);
        Assert.Equal("ack", (await fixture.ReadKind(otherWorld, "ack")).Kind);
        // Sending does not subscribe sender to receive commands.
        await PeerHubProtocol.WriteAsync(receiver, new() { Kind = "command", Id = Guid.NewGuid(), Line = "/reply", CreatedUtc = DateTimeOffset.UtcNow }, fixture.Token);
        Assert.True((await fixture.ReadKind(receiver, "ack")).Accepted);
        Assert.Empty(fixture.Errors);
    }

    [Fact]
    public async Task DuplicateIdentityDisconnectDoesNotBlockOtherClientsOrShutdown()
    {
        await using var fixture = new HubFixture();
        Guid identity = Guid.NewGuid();
        using var first = await fixture.Connect(1, "one", "world", PluginPeerCapabilities.ClientState, identity: identity);
        using var duplicate = await fixture.Endpoint.ConnectAsync(fixture.Token);
        await PeerHubProtocol.WriteAsync(duplicate, new() { Kind = "register", Id = identity,
            Client = Client(2, 2, "duplicate", "world", []), Capabilities = PluginPeerCapabilities.ClientState }, fixture.Token);
        Assert.Null(await PeerHubProtocol.ReadAsync(duplicate, fixture.Token));
        using var third = await fixture.Connect(3, "three", "world", PluginPeerCapabilities.ClientState);
        Assert.Equal("three", (await fixture.ReadKind(first, "state")).Client.Name);
        Assert.Contains(fixture.Errors, error => error.Contains("duplicate identity"));
    }

    [Fact]
    public async Task StateDemandRequiresAnotherParticipantAndDisconnectRemovesPeer()
    {
        await using var fixture = new HubFixture();
        using var first = await fixture.Connect(1, "one", "world", PluginPeerCapabilities.ClientState);
        using var second = await fixture.Connect(2, "two", "world", PluginPeerCapabilities.ClientState);
        Assert.Equal("two", (await fixture.ReadKind(first, "state")).Client.Name);
        Assert.True((await fixture.ReadKind(first, "demand")).Capabilities.HasFlag(PluginPeerCapabilities.ClientState));
        second.Dispose();
        Assert.NotEqual(Guid.Empty, (await fixture.ReadKind(first, "remove")).Id);
        Assert.Equal(PluginPeerCapabilities.None, (await fixture.ReadKind(first, "demand")).Capabilities);
    }

    [Fact]
    public async Task CastsArriveThroughMemoryWithoutAnyPeerFiles()
    {
        await using var fixture = new HubFixture();
        using var source = await fixture.Connect(1, "one", "world", PluginPeerCapabilities.Casts);
        using var registry = new LocalPluginPeerRegistry(fixture.Directory, memoryOnly: true);
        using var transport = new LocalPeerTransport(fixture.Endpoint, registry, _ => { }, startHub: () => throw new InvalidOperationException("Test hub is already running."));
        transport.UpdateSelf(Client(registry.ClientId, 2, "two", "world", []));
        using var lease = transport.Subscribe(PluginPeerCapabilities.Casts);
        await Until(() => registry.CaptureRemoteClients().Count == 1);
        _ = await fixture.ReadKind(source, "demand");
        await PeerHubProtocol.WriteAsync(source, new() { Kind = "cast", Cast = new PluginPeerCast(1, 1, 1, 9, 100, 200, 20, true) }, fixture.Token);
        await Until(() => registry.CaptureRemoteCasts(0, "world", 2).Count == 1);
        var cast = Assert.Single(registry.CaptureRemoteCasts(0, "world", 2));
        Assert.Equal(100u, cast.SpellId);
        Assert.InRange(cast.SecondsRemaining, 15, 20);
        Assert.Empty(System.IO.Directory.GetFiles(fixture.Directory, "peer-*.json"));
        lease!.Dispose();
        Assert.False(transport.IsActive);
    }

    [Fact]
    public void ZeroSubscriptionsNeverStartTransportAndLeasesCombine()
    {
        string root = Path.Combine(Path.GetTempPath(), "peer-" + Guid.NewGuid().ToString("N"));
        using var registry = new LocalPluginPeerRegistry(root, memoryOnly: true);
        int starts = 0;
        using var transport = new LocalPeerTransport(PeerHubEndpoint.ForDirectory(root), registry, _ => { }, startHub: () => starts++);
        transport.UpdateSelf(Client(registry.ClientId, 1, "one", "world", []));
        Assert.False(transport.IsActive);
        Assert.Equal(0, starts);
        Assert.False(System.IO.Directory.Exists(root));
        using var casts = transport.Subscribe(PluginPeerCapabilities.Casts);
        using var state = transport.Subscribe(PluginPeerCapabilities.ClientState);
        casts!.Dispose();
        Assert.True(transport.Has(PluginPeerCapabilities.ClientState));
        Assert.False(transport.Has(PluginPeerCapabilities.Casts));
        state!.Dispose();
        Assert.False(transport.IsActive);
    }

    [Fact]
    public void OutboxCoalescesStateAndRefusesEventOverflow()
    {
        using var outbox = new PeerOutbox();
        Guid id = Guid.NewGuid();
        for (int i = 0; i < 1000; i++) Assert.True(outbox.Enqueue(new() { Kind = "state", Id = id }));
        for (int i = 0; i < 256; i++) Assert.True(outbox.Enqueue(new() { Kind = "cast" }));
        Assert.False(outbox.Enqueue(new() { Kind = "command" }));
    }

    private static PluginNetworkClient Client(uint clientId, uint player, string name, string world, string[] tags) =>
        new(clientId, player, name, world, new PluginNavigationPosition(1, 1, 1, 1, 0, true), tags, 100, 100, 100, 100, 100, 100, 0);
    private static async Task Until(Func<bool> predicate)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!predicate()) await Task.Delay(10, timeout.Token);
    }
    private sealed class HubFixture : IAsyncDisposable
    {
        internal string Directory { get; } = Path.Combine(Path.GetTempPath(), "peer-" + Guid.NewGuid().ToString("N"));
        internal PeerHubEndpoint Endpoint { get; }
        private readonly CancellationTokenSource _token = new(TimeSpan.FromSeconds(10));
        internal CancellationToken Token => _token.Token;
        internal System.Collections.Concurrent.ConcurrentQueue<string> Errors { get; } = [];
        private readonly Task _server;
        private readonly Dictionary<Stream, List<PeerWireMessage>> _pending = [];
        internal HubFixture()
        {
            Endpoint = PeerHubEndpoint.ForDirectory(Directory);
            _server = new PeerHubServer(Endpoint, Errors.Enqueue).RunAsync(Token);
        }
        internal async Task<Stream> Connect(uint player, string name, string world, PluginPeerCapabilities capabilities, string[]? tags = null, Guid? identity = null)
        {
            Stream stream = await Endpoint.ConnectAsync(Token);
            await PeerHubProtocol.WriteAsync(stream, new() { Kind = "register", Id = identity ?? Guid.NewGuid(), Client = Client(player, player, name, world, tags ?? []), Capabilities = capabilities }, Token);
            await ReadKind(stream, "ready");
            return stream;
        }
        internal async Task<PeerWireMessage> ReadKind(Stream stream, string kind)
        {
            if (!_pending.TryGetValue(stream, out var pending)) _pending[stream] = pending = [];
            var existing = pending.FirstOrDefault(m => m.Kind == kind);
            if (existing is not null) { pending.Remove(existing); return existing; }
            while (await PeerHubProtocol.ReadAsync(stream, Token) is { } message)
            {
                if (message.Kind == kind) return message;
                pending.Add(message);
            }
            throw new IOException("Disconnected waiting for " + kind);
        }
        public async ValueTask DisposeAsync()
        {
            _token.Cancel(); await _server; _token.Dispose();
            System.IO.Directory.Delete(Directory, recursive: true);
        }
    }
}
