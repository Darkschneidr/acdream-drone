using System.Net.Sockets;
using AcDream.Plugin.Abstractions;

namespace AcDream.Runtime.Plugins;

internal sealed class LocalPeerTransport : IDisposable
{
    private readonly object _gate = new();
    private readonly PeerHubEndpoint _endpoint;
    private readonly LocalPluginPeerRegistry _registry;
    private readonly Action<string> _report;
    private readonly Action<PeerWireMessage> _command;
    private readonly Action _startHub;
    private readonly Dictionary<long, PluginPeerCapabilities> _leases = [];
    private long _nextLease;
    private CancellationTokenSource? _lifetime;
    private PeerOutbox? _outbox;
    private PluginNetworkClient? _self;
    private PluginPeerCapabilities _capabilities;
    private PluginPeerCapabilities _demand;
    private bool _disposed;
    private bool _ready;
    private readonly HashSet<Guid> _receivedCommands = [];
    private readonly Queue<Guid> _commandOrder = [];
    private readonly Dictionary<Guid, IDisposable> _transientCommands = [];
    private readonly List<PeerWireMessage> _initialCommands = [];
    internal LocalPeerTransport(PeerHubEndpoint endpoint, LocalPluginPeerRegistry registry,
        Action<PeerWireMessage> command, Action<string>? report = null, Action? startHub = null)
    {
        _endpoint = endpoint; _registry = registry; _command = command;
        _report = report ?? Console.Error.WriteLine; _startHub = startHub ?? endpoint.StartHub;
    }
    internal bool IsConnected { get { lock (_gate) return !_disposed && _ready && _outbox is not null; } }
    internal bool IsActive { get { lock (_gate) return !_disposed && _capabilities != PluginPeerCapabilities.None; } }
    internal bool HasCastDemand { get { lock (_gate) return (_demand & PluginPeerCapabilities.Casts) != 0; } }
    internal bool HasSelf { get { lock (_gate) return _self is not null; } }
    internal bool NeedsState { get { lock (_gate) return (_demand & PluginPeerCapabilities.ClientState) != 0; } }
    internal bool Has(PluginPeerCapabilities capability) { lock (_gate) return (_capabilities & capability) != 0; }
    internal IDisposable? Subscribe(PluginPeerCapabilities capabilities)
    {
        if (capabilities == PluginPeerCapabilities.None || (capabilities & ~(PluginPeerCapabilities.ClientState | PluginPeerCapabilities.Casts | PluginPeerCapabilities.Commands | PluginPeerCapabilities.SendCommands)) != 0) return null;
        lock (_gate)
        {
            if (_disposed) return null;
            long id = ++_nextLease; _leases.Add(id, capabilities); UpdateCapabilities();
            return new Lease(this, id);
        }
    }
    private void Release(long id)
    {
        lock (_gate) { if (_leases.Remove(id)) UpdateCapabilities(); }
    }
    private void UpdateCapabilities()
    {
        _capabilities = _leases.Values.Aggregate(PluginPeerCapabilities.None, (a, b) => a | b);
        if (_capabilities == PluginPeerCapabilities.None)
        {
            _lifetime?.Cancel(); _lifetime = null; _outbox = null; _demand = PluginPeerCapabilities.None;
            _registry.ClearPushedClients(); return;
        }
        if (_outbox is not null && _self is { } self) QueueRegister(self);
        if (_lifetime is null && _self is not null)
        {
            _lifetime = new CancellationTokenSource();
            CancellationTokenSource lifetime = _lifetime;
            _ = Task.Run(() => RunAsync(lifetime));
        }
    }
    internal void UpdateSelf(PluginNetworkClient self)
    {
        lock (_gate)
        {
            if (_disposed) return;
            bool identityChanged = _self is not { } previous || previous.PlayerId != self.PlayerId || previous.WorldName != self.WorldName
                || !previous.Tags.SequenceEqual(self.Tags, StringComparer.OrdinalIgnoreCase);
            bool changed = _self is not { } old || old with { Tags = self.Tags } != self;
            if (_self is { } identity && (identity.PlayerId != self.PlayerId || identity.WorldName != self.WorldName))
                _registry.ClearPushedClients();
            _self = self;
            if (_outbox is not null && identityChanged) QueueRegister(self);
            else if (_outbox is not null && changed && NeedsState)
                if (!_outbox.Enqueue(new() { Kind = "state", Id = _registry.InstanceId, Client = self })) FailQueue();
            UpdateCapabilitiesIfNeeded();
        }
    }
    private void UpdateCapabilitiesIfNeeded()
    {
        if (_lifetime is null && _capabilities != PluginPeerCapabilities.None) UpdateCapabilities();
    }
    private void QueueRegister(PluginNetworkClient self)
    {
        _ready = false;
        if (!_outbox!.Enqueue(new() { Kind = "register", Id = _registry.InstanceId, Client = self, Capabilities = _capabilities })) FailQueue();
    }
    internal bool SendCast(PluginPeerCast cast)
    {
        lock (_gate)
        {
            if (!Has(PluginPeerCapabilities.Casts)) return false;
            // Own casts remain available to a bridge even with no local consumer.
            if ((_demand & PluginPeerCapabilities.Casts) == 0) return true;
            bool queued = _outbox?.Enqueue(new() { Kind = "cast", Id = _registry.InstanceId, Cast = cast }) == true;
            if (!queued) FailQueue(); return queued;
        }
    }
    internal bool SendCommand(string line, string[] tags, int delay)
    {
        IDisposable? lease = Subscribe(PluginPeerCapabilities.SendCommands);
        if (lease is null) return false;
        Guid id = Guid.NewGuid();
        lock (_gate)
        {
            var message = new PeerWireMessage { Kind = "command", Id = id, Line = line, Tags = tags,
                DelayMilliseconds = delay, CreatedUtc = DateTimeOffset.UtcNow };
            if (_outbox is not null)
            {
                if (!_outbox.Enqueue(message)) { lease.Dispose(); FailQueue(); return false; }
            }
            else
            {
                if (_initialCommands.Count >= 256) { lease.Dispose(); return false; }
                _initialCommands.Add(message);
            }
            _transientCommands.Add(id, lease);
        }
        _ = ExpireCommandAsync(id);
        return true;
    }
    private async Task ExpireCommandAsync(Guid id)
    {
        await Task.Delay(TimeSpan.FromSeconds(15)).ConfigureAwait(false);
        lock (_gate)
            if (_transientCommands.Remove(id, out var lease)) { _initialCommands.RemoveAll(m => m.Id == id); lease.Dispose(); _report("Peer broadcast acknowledgement timed out; command was not replayed."); }
    }
    private void FailQueue()
    {
        _report("Peer event queue is full; connection closed to expose the gap."); _lifetime?.Cancel();
    }
    private async Task RunAsync(CancellationTokenSource lifetime)
    {
        int backoff = 1;
        bool attemptedStart = false;
        try
        {
            while (!lifetime.IsCancellationRequested)
            {
                try
                {
                    using Stream stream = await _endpoint.ConnectAsync(lifetime.Token).ConfigureAwait(false);
                    using var connection = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
                    using var outbox = new PeerOutbox();
                    lock (_gate)
                    {
                        if (!ReferenceEquals(_lifetime, lifetime)) return;
                        _outbox = outbox; QueueRegister(_self!.Value);
                        foreach (var message in _initialCommands) if (!outbox.Enqueue(message)) FailQueue();
                        _initialCommands.Clear();
                    }
                    backoff = 1; attemptedStart = false;
                    Task writer = outbox.RunAsync(stream, connection.Token);
                    try
                    {
                        while (await PeerHubProtocol.ReadAsync(stream, connection.Token).ConfigureAwait(false) is { } message)
                            Receive(message, lifetime);
                    }
                    finally
                    {
                        connection.Cancel(); stream.Dispose();
                        try { await writer.ConfigureAwait(false); } catch (Exception e) when (e is OperationCanceledException or IOException or ObjectDisposedException) { if (e is IOException) _report("Peer writer disconnected: " + e.Message); }
                        lock (_gate)
                        {
                            if (ReferenceEquals(_outbox, outbox)) { _outbox = null; _demand = PluginPeerCapabilities.None; _registry.ClearPushedClients(); }
                            foreach (var lease in _transientCommands.Values.ToArray()) lease.Dispose();
                            _transientCommands.Clear();
                            _initialCommands.Clear();
                        }
                    }
                }
                catch (Exception e) when (e is IOException or SocketException or TimeoutException)
                {
                    if (lifetime.IsCancellationRequested) break;
                    if (!attemptedStart)
                    {
                        attemptedStart = true;
                        try { _startHub(); } catch (Exception startError) when (startError is IOException or System.ComponentModel.Win32Exception)
                        { _report("Peer hub startup failed: " + startError.Message); }
                    }
                    else _report("Peer transport unavailable: " + e.Message);
                }
                if (!lifetime.IsCancellationRequested)
                { await Task.Delay(TimeSpan.FromSeconds(backoff), lifetime.Token).ConfigureAwait(false); backoff = Math.Min(30, backoff * 2); }
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception error) { _report("Peer transport stopped: " + error.Message); }
        finally
        {
            lock (_gate) if (ReferenceEquals(_lifetime, lifetime)) { _lifetime = null; _outbox = null; _demand = PluginPeerCapabilities.None; }
            lifetime.Dispose();
        }
    }
    private void Receive(PeerWireMessage message, CancellationTokenSource lifetime)
    {
        bool deliverCommand = false;
        lock (_gate)
        {
            if (_disposed || !ReferenceEquals(_lifetime, lifetime) || lifetime.IsCancellationRequested) return;
            switch (message.Kind)
            {
                case "ready": _ready = true; break;
                case "demand": _demand = message.Capabilities; break;
                case "state": _registry.PushClient(message.Id, message.Client); break;
                case "remove": _registry.RemovePushedClient(message.Id); break;
                case "cast":
                    _registry.PushClient(message.Id, message.Client);
                    _registry.PushCast(message.Id, message.Cast, message.CreatedUtc); break;
                case "command":
                    if (!Has(PluginPeerCapabilities.Commands) || !_receivedCommands.Add(message.Id)) break;
                    _commandOrder.Enqueue(message.Id);
                    while (_commandOrder.Count > 256) _receivedCommands.Remove(_commandOrder.Dequeue());
                    deliverCommand = true; break;
                case "ack":
                    if (_transientCommands.Remove(message.Id, out var lease)) lease.Dispose();
                    if (!message.Accepted) _report("Peer broadcast was refused by at least one recipient."); break;
            }
        }
        if (deliverCommand) _command(message);
    }
    internal void Withdraw()
    {
        lock (_gate)
        {
            _self = null; _lifetime?.Cancel(); _lifetime = null; _outbox = null;
            _demand = PluginPeerCapabilities.None; _registry.ClearPushedClients();
        }
    }
    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true; _leases.Clear(); _lifetime?.Cancel(); _lifetime = null;
            _outbox = null; _registry.ClearPushedClients();
        }
    }
    private sealed class Lease(LocalPeerTransport owner, long id) : IDisposable
    {
        private LocalPeerTransport? _owner = owner;
        public void Dispose() => Interlocked.Exchange(ref _owner, null)?.Release(id);
    }
}
