using AcDream.Plugin.Abstractions;
namespace AcDream.Core.Plugins;

// Leases are owned by the plugin scope, even if the plugin forgets to dispose them.
internal sealed class ScopedPeerAutomation(INetworkAutomation source) : INetworkAutomation, IDisposable
{
    private readonly object _gate = new();
    private readonly List<IDisposable> _leases = [];
    private bool _disposed;
    public bool IsAvailable => !_disposed && source.IsAvailable;
    public bool IsConnected => !_disposed && source.IsConnected;
    public bool SupportsSubscriptions => !_disposed && source.SupportsSubscriptions;
    private INetworkAutomation Active => _disposed ? NoOpAutomationSurface.Instance.Network : source;
    public IDisposable? Subscribe(PluginPeerCapabilities capabilities)
    {
        lock (_gate)
        {
            if (_disposed) return null;
            IDisposable? lease = source.Subscribe(capabilities);
            if (lease is null) return null;
            var owned = new Lease(this, lease);
            _leases.Add(owned);
            return owned;
        }
    }
    public void Dispose()
    {
        IDisposable[] leases;
        lock (_gate) { _disposed = true; leases = _leases.ToArray(); _leases.Clear(); }
        foreach (var lease in leases) lease.Dispose();
    }
    private sealed class Lease(ScopedPeerAutomation owner, IDisposable inner) : IDisposable
    {
        private IDisposable? _inner = inner;
        public void Dispose()
        {
            Interlocked.Exchange(ref _inner, null)?.Dispose();
            lock (owner._gate) owner._leases.Remove(this);
        }
    }
    public IReadOnlyList<PluginNetworkClient> CaptureClients() => Active.CaptureClients();
    public bool AnnounceCastAttempt(uint targetObjectId,
        uint spellId,
        int effectiveSkill) => Active.AnnounceCastAttempt(targetObjectId, spellId, effectiveSkill);
    public bool AnnounceCastSuccess(uint targetObjectId,
        uint spellId,
        int effectiveSkill,
        double durationSeconds) => Active.AnnounceCastSuccess(targetObjectId, spellId, effectiveSkill, durationSeconds);
    public IReadOnlyList<PluginPeerCast> CaptureCasts(long afterSequence) => Active.CaptureCasts(afterSequence);
    public bool TryCaptureSelf(out PluginNetworkClient self) => Active.TryCaptureSelf(out self);
    public bool SetTags(IReadOnlyList<string> tags) => Active.SetTags(tags);
    public bool BroadcastCommand(string line,
        IReadOnlyList<string> tags,
        int delayMilliseconds) => Active.BroadcastCommand(line, tags, delayMilliseconds);
    public IReadOnlyList<PluginPeerCommand> CaptureCommands(long afterSequence) => Active.CaptureCommands(afterSequence);
    public IReadOnlyList<PluginPeerCast> CaptureOwnCasts(long afterSequence) => Active.CaptureOwnCasts(afterSequence);
    public IReadOnlyList<PluginPeerCommand> CaptureOwnCommands(long afterSequence) => Active.CaptureOwnCommands(afterSequence);
    public bool ImportRemoteClient(PluginNetworkClient client) => Active.ImportRemoteClient(client);
    public bool ImportRemoteCast(uint casterObjectId,
        uint targetObjectId,
        uint spellId,
        int effectiveSkill,
        double secondsRemaining,
        bool landed) => Active.ImportRemoteCast(casterObjectId, targetObjectId, spellId, effectiveSkill, secondsRemaining, landed);
    public bool ImportRemoteCommand(uint senderObjectId,
        string line,
        IReadOnlyList<string> tags,
        int delayMilliseconds) => Active.ImportRemoteCommand(senderObjectId, line, tags, delayMilliseconds);
}
