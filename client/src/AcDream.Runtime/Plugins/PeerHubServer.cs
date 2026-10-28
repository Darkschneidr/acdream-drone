using System.IO.Pipes;
using System.Net.Sockets;
using AcDream.Plugin.Abstractions;

namespace AcDream.Runtime.Plugins;

internal sealed class PeerHubServer
{
    private readonly object _gate = new();
    private readonly Dictionary<Guid, Connection> _clients = [];
    private readonly PeerHubEndpoint _endpoint;
    private readonly Action<string> _report;
    private DateTimeOffset _lastActivity = DateTimeOffset.UtcNow;
    internal PeerHubServer(PeerHubEndpoint endpoint, Action<string>? report = null)
    { _endpoint = endpoint; _report = report ?? Console.Error.WriteLine; }

    internal async Task RunAsync(CancellationToken token)
    {
        _endpoint.Prepare();
        FileStream ownership;
        try { ownership = new FileStream(Path.Combine(_endpoint.Directory, _endpoint.Name + ".lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException) { return; } // Another hub owns this endpoint.
        using (ownership)
        using (var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token))
        {
            Socket? listener = null;
            var connections = new List<Task>();
            if (!OperatingSystem.IsWindows())
            {
                if (File.Exists(_endpoint.SocketPath)) File.Delete(_endpoint.SocketPath);
                listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                listener.Bind(new UnixDomainSocketEndPoint(_endpoint.SocketPath)); listener.Listen(64);
            }
            Task idle = StopWhenIdleAsync(lifetime);
            try
            {
                while (!lifetime.IsCancellationRequested)
                {
                    Stream stream;
                    if (OperatingSystem.IsWindows())
                    {
                        var pipe = new NamedPipeServerStream(_endpoint.Name, PipeDirection.InOut, 64,
                            PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                        try { await pipe.WaitForConnectionAsync(lifetime.Token).ConfigureAwait(false); stream = pipe; }
                        catch { pipe.Dispose(); throw; }
                    }
                    else stream = new NetworkStream(await listener!.AcceptAsync(lifetime.Token).ConfigureAwait(false), ownsSocket: true);
                    lock (_gate) _lastActivity = DateTimeOffset.UtcNow;
                    connections.RemoveAll(t => t.IsCompleted);
                    connections.Add(HandleAsync(stream, lifetime.Token));
                }
            }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
            finally
            {
                lifetime.Cancel(); listener?.Dispose();
                await Task.WhenAll(connections).ConfigureAwait(false);
                await idle.ConfigureAwait(false);
                if (!OperatingSystem.IsWindows() && File.Exists(_endpoint.SocketPath)) File.Delete(_endpoint.SocketPath);
            }
        }
    }
    private async Task StopWhenIdleAsync(CancellationTokenSource lifetime)
    {
        try
        {
            while (!lifetime.IsCancellationRequested)
            {
                await Task.Delay(1000, lifetime.Token).ConfigureAwait(false);
                bool idle;
                lock (_gate) idle = _clients.Count == 0 && DateTimeOffset.UtcNow - _lastActivity > TimeSpan.FromSeconds(15);
                if (idle) await lifetime.CancelAsync().ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
    }
    private async Task HandleAsync(Stream stream, CancellationToken token)
    {
        using (stream)
        using (var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token))
        using (var connection = new Connection(stream, lifetime))
        {
            Task writer = connection.Outbox.RunAsync(stream, lifetime.Token);
            try
            {
                while (await PeerHubProtocol.ReadAsync(stream, lifetime.Token).ConfigureAwait(false) is { } message)
                {
                    HandleLocked(connection, message);
                }
            }
            catch (Exception error) when (error is IOException or InvalidDataException or System.Text.Json.JsonException or OperationCanceledException)
            { if (!lifetime.IsCancellationRequested) _report("Peer hub connection closed: " + error.Message); }
            finally
            {
                lock (_gate)
                {
                    if (connection.Id != Guid.Empty && _clients.Remove(connection.Id))
                        foreach (var peer in _clients.Values) Send(peer, new() { Kind = "remove", Id = connection.Id });
                    _lastActivity = DateTimeOffset.UtcNow;
                    UpdateDemand();
                }
                lifetime.Cancel(); stream.Dispose();
                try { await writer.ConfigureAwait(false); }
                catch (Exception error) when (error is IOException or OperationCanceledException or ObjectDisposedException)
                { if (error is IOException) _report("Peer hub writer closed: " + error.Message); }
            }
        }
    }
    private void HandleLocked(Connection sender, PeerWireMessage message)
    {
        lock (_gate) Handle(sender, message);
    }
    private void Handle(Connection sender, PeerWireMessage message)
    {
        if (message.Kind == "register")
        {
            if (message.Id == Guid.Empty || (message.Capabilities & ~(PluginPeerCapabilities.ClientState | PluginPeerCapabilities.Casts | PluginPeerCapabilities.Commands | PluginPeerCapabilities.SendCommands)) != 0)
                throw new InvalidDataException("Invalid peer registration.");
            if (sender.Id == Guid.Empty)
            {
                if (_clients.Count >= 64 || _clients.ContainsKey(message.Id)) throw new InvalidDataException("Peer limit or duplicate identity.");
                sender.Id = message.Id; _clients.Add(sender.Id, sender);
            }
            else if (sender.Id != message.Id) throw new InvalidDataException("Peer identity changed.");
            ValidateClient(message.Client);
            if (sender.Client.PlayerId != 0 && (sender.Client.PlayerId != message.Client.PlayerId
                || sender.Client.WorldName != message.Client.WorldName))
                foreach (var peer in _clients.Values.Where(c => c != sender && SameWorld(c, sender)))
                    Send(peer, new() { Kind = "remove", Id = sender.Id });
            sender.Client = message.Client; sender.Capabilities = message.Capabilities;
            // A peer must know its publication demand before another client can
            // observe the new listener and act on that state.
            UpdateDemand();
            foreach (var other in _clients.Values.Where(c => c != sender && SameWorld(c, sender)))
            {
                if (NeedsState(sender) && ParticipatesInState(other)) SendState(sender, other, initial: true);
                if (NeedsState(other) && ParticipatesInState(sender)) SendState(other, sender);
            }
            Send(sender, new() { Kind = "ready" });
            return;
        }
        if (sender.Id == Guid.Empty) throw new InvalidDataException("Unregistered peer.");
        if (message.Kind == "state")
        {
            ValidateClient(message.Client);
            if (message.Client.PlayerId != sender.Client.PlayerId || message.Client.WorldName != sender.Client.WorldName)
                throw new InvalidDataException("State identity changed without registration.");
            sender.Client = message.Client;
            foreach (var other in _clients.Values.Where(c => c != sender && SameWorld(c, sender) && NeedsState(c)))
                SendState(other, sender);
        }
        else if (message.Kind == "cast" && sender.Capabilities.HasFlag(PluginPeerCapabilities.Casts))
        {
            var cast = message.Cast;
            if (cast.CasterObjectId != sender.Client.PlayerId || cast.TargetObjectId == 0 || cast.SpellId == 0
                || cast.EffectiveSkill < 0 || !double.IsFinite(cast.SecondsRemaining) || cast.SecondsRemaining < 0 || cast.SecondsRemaining > 86400)
                throw new InvalidDataException("Invalid peer cast.");
            foreach (var other in _clients.Values.Where(c => c != sender && SameWorld(c, sender) && c.Capabilities.HasFlag(PluginPeerCapabilities.Casts)))
                Send(other, message with { Id = sender.Id, Client = sender.Client, CreatedUtc = DateTimeOffset.UtcNow });
        }
        else if (message.Kind == "command")
        {
            if (message.Id == Guid.Empty || message.Line.Length is 0 or > 512 || message.Line.Any(char.IsControl)
                || message.Tags.Length > 16 || message.Tags.Any(t => t is null || t.Length > 64)
                || message.DelayMilliseconds is < 0 or > 60000 || DateTimeOffset.UtcNow - message.CreatedUtc > TimeSpan.FromSeconds(15))
                throw new InvalidDataException("Invalid peer command.");
            var targets = _clients.Values.Where(c => c != sender && SameWorld(c, sender)
                && c.Capabilities.HasFlag(PluginPeerCapabilities.Commands)
                && (message.Tags.Length == 0 || message.Tags.Intersect(c.Client.Tags, StringComparer.OrdinalIgnoreCase).Any()))
                .OrderBy(c => c.Client.ClientId).ThenBy(c => c.Id).ToArray();
            bool accepted = true;
            for (int i = 0; i < targets.Length; i++)
                accepted &= Send(targets[i], message with { Client = sender.Client, DelayMilliseconds = checked((i + 1) * message.DelayMilliseconds) });
            Send(sender, new() { Kind = "ack", Id = message.Id, Accepted = accepted });
        }
    }
    private static void ValidateClient(PluginNetworkClient client)
    {
        if (client.ClientId == 0 || client.PlayerId == 0 || string.IsNullOrWhiteSpace(client.Name) || client.Name.Length > 128
            || client.WorldName is null || client.WorldName.Length > 128 || client.Tags is null || client.Tags.Count > 128
            || client.Tags.Any(t => t is null || t.Length > 64) || !double.IsFinite(client.Position.EastWest)
            || !double.IsFinite(client.Position.NorthSouth) || !double.IsFinite(client.Position.Elevation) || !float.IsFinite(client.Heading))
            throw new InvalidDataException("Invalid peer client state.");
    }
    private void UpdateDemand()
    {
        foreach (var client in _clients.Values)
        {
            PluginPeerCapabilities demand = PluginPeerCapabilities.None;
            foreach (var other in _clients.Values.Where(c => c != client && SameWorld(c, client)))
                demand |= other.Capabilities;
            demand &= client.Capabilities;
            if (demand == client.Demand) continue;
            client.Demand = demand; Send(client, new() { Kind = "demand", Capabilities = demand });
        }
    }
    private static bool SameWorld(Connection a, Connection b) => a.Client.WorldName == b.Client.WorldName && a.Client.PlayerId != b.Client.PlayerId;
    private static bool NeedsState(Connection c) => (c.Capabilities & (PluginPeerCapabilities.ClientState | PluginPeerCapabilities.Casts)) != 0;
    private static bool ParticipatesInState(Connection c) => c.Capabilities != PluginPeerCapabilities.None;
    private static bool Send(Connection connection, PeerWireMessage message)
    {
        if (connection.Outbox.Enqueue(message)) return true;
        _ = connection.Lifetime.CancelAsync();
        return false;
    }
    private static void SendState(Connection target, Connection source, bool initial = false) => Send(target, new() { Kind = "state", Id = source.Id, Client = source.Client, Initial = initial });
    private sealed class Connection(Stream stream, CancellationTokenSource lifetime) : IDisposable
    {
        internal Guid Id;
        internal PluginPeerCapabilities Capabilities;
        internal PluginPeerCapabilities Demand;
        internal PluginNetworkClient Client;
        internal Stream Stream { get; } = stream;
        internal CancellationTokenSource Lifetime { get; } = lifetime;
        internal PeerOutbox Outbox { get; } = new();
        public void Dispose() => Outbox.Dispose();
    }
}
