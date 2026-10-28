using System.Buffers.Binary;
using System.Text.Json;
using AcDream.Plugin.Abstractions;

namespace AcDream.Runtime.Plugins;

internal sealed record PeerWireMessage
{
    public string Kind { get; init; } = "";
    public Guid Id { get; init; }
    public PluginPeerCapabilities Capabilities { get; init; }
    public PluginNetworkClient Client { get; init; }
    public PluginPeerCast Cast { get; init; }
    public string Line { get; init; } = "";
    public string[] Tags { get; init; } = [];
    public int DelayMilliseconds { get; init; }
    public DateTimeOffset CreatedUtc { get; init; }
    public bool Accepted { get; init; }
    public bool Initial { get; init; }
}

internal static class PeerHubProtocol
{
    internal const int MaximumMessageBytes = 65536;
    internal static async Task<PeerWireMessage?> ReadAsync(Stream stream, CancellationToken token)
    {
        byte[] prefix = new byte[4];
        int first = await stream.ReadAsync(prefix.AsMemory(0, 1), token).ConfigureAwait(false);
        if (first == 0) return null;
        await stream.ReadExactlyAsync(prefix.AsMemory(1), token).ConfigureAwait(false);
        int length = BinaryPrimitives.ReadInt32LittleEndian(prefix);
        if (length is <= 0 or > MaximumMessageBytes) throw new InvalidDataException("Invalid peer message length.");
        byte[] bytes = new byte[length];
        await stream.ReadExactlyAsync(bytes, token).ConfigureAwait(false);
        return JsonSerializer.Deserialize<PeerWireMessage>(bytes) ?? throw new InvalidDataException("Empty peer message.");
    }
    internal static async Task WriteAsync(Stream stream, PeerWireMessage message, CancellationToken token)
    {
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(message);
        if (bytes.Length > MaximumMessageBytes) throw new InvalidDataException("Peer message is too large.");
        byte[] prefix = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(prefix, bytes.Length);
        await stream.WriteAsync(prefix, token).ConfigureAwait(false);
        await stream.WriteAsync(bytes, token).ConfigureAwait(false);
        await stream.FlushAsync(token).ConfigureAwait(false);
    }
}

// State replaces earlier state; events retain order and explicitly refuse overflow.
internal sealed class PeerOutbox : IDisposable
{
    private readonly object _gate = new();
    private readonly Queue<PeerWireMessage> _events = [];
    private readonly Dictionary<Guid, PeerWireMessage> _state = [];
    private readonly SemaphoreSlim _ready = new(0, 1);
    private bool _disposed;
    internal bool Enqueue(PeerWireMessage message)
    {
        lock (_gate)
        {
            if (_disposed) return false;
            if (message.Kind == "state" && !message.Initial)
            {
                if (_state.Count >= 256 && !_state.ContainsKey(message.Id)) return false;
                _state[message.Id] = message;
            }
            else
            {
                if (_events.Count >= 256) return false;
                if (message.Kind == "remove") _state.Remove(message.Id);
                _events.Enqueue(message);
            }
            if (_ready.CurrentCount == 0) _ready.Release();
            return true;
        }
    }
    internal async Task RunAsync(Stream stream, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            await _ready.WaitAsync(token).ConfigureAwait(false);
            while (true)
            {
                PeerWireMessage? message;
                lock (_gate)
                {
                    if (_events.Count > 0) message = _events.Dequeue();
                    else if (_state.Count > 0)
                    {
                        var pair = _state.First(); message = pair.Value; _state.Remove(pair.Key);
                    }
                    else break;
                }
                await PeerHubProtocol.WriteAsync(stream, message, token).ConfigureAwait(false);
            }
        }
    }
    public void Dispose() { lock (_gate) { _disposed = true; _events.Clear(); _state.Clear(); } }
}
