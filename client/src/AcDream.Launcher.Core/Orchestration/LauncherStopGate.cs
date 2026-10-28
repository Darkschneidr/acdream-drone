namespace AcDream.Launcher.Core.Orchestration;

/// <summary>Tracks each stop request until its process and startup work finish.</summary>
public sealed class LauncherStopGate
{
    private readonly HashSet<string> _sessions = new(StringComparer.Ordinal);
    public bool IsPending => _sessions.Count != 0;

    /// <summary>Whether this session already has a stop pending.</summary>
    public bool IsStopping(string sessionId) => _sessions.Contains(sessionId);

    public bool TryBegin(string sessionId, LauncherStateSnapshot snapshot)
    {
        Observe(snapshot);
        if (IsStopping(sessionId) || !snapshot.Sessions.Any(session => session.SessionId == sessionId
                && session.IsProcessOrStartActive)) return false;
        _sessions.Add(sessionId);
        return true;
    }

    public void Observe(LauncherStateSnapshot snapshot)
    {
        _sessions.RemoveWhere(id => !snapshot.Sessions.Any(session =>
            session.SessionId == id && session.IsProcessOrStartActive));
    }
}
