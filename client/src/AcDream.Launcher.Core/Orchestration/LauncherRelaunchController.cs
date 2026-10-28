using AcDream.Launcher.Core.Profiles;

namespace AcDream.Launcher.Core.Orchestration;

/// <summary>A checked account's current saved launch choice, independent of visual filters.</summary>
public sealed record LauncherRelaunchSelection(string Server, string Account, string? Character, LaunchMode Mode);

/// <summary>Schedules normal launcher launches for observed play sessions; owns no processes.</summary>
public sealed class LauncherRelaunchController(ILauncherOrchestrator orchestrator, TimeProvider? timeProvider = null) : IDisposable
{
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;
    private readonly Dictionary<(string Server, string Account), Entry> _entries = [];
    private bool _enabled;
    private bool _disposed;
    private int _delaySeconds = 180;

    public event Action? StateChanged;
    public event Action<Exception>? LaunchFailed;
    public bool IsLaunching { get; private set; }
    public int DelaySeconds
    {
        get => _delaySeconds;
        set
        {
            if (value is < 5 or > 3600) throw new ArgumentOutOfRangeException(nameof(value));
            _delaySeconds = value;
        }
    }

    /// <summary>Updates the bounded set of checked accounts and adopts only active or newly seen play sessions.</summary>
    public void Observe(LauncherStateSnapshot snapshot, IReadOnlyList<LauncherRelaunchSelection> checkedAccounts, bool enabled)
    {
        if (_disposed) return;
        if (!enabled)
        {
            CancelEntries();
            _enabled = false;
            return;
        }
        _enabled = true;
        var configured = snapshot.Servers.SelectMany(server => server.Accounts.Select(account =>
            (Server: server.Name, Account: account.AccountName))).ToHashSet();
        var selected = checkedAccounts.Where(selection => configured.Contains((selection.Server, selection.Account)))
            .ToDictionary(selection => (selection.Server, selection.Account));
        var sessions = new Dictionary<string, LauncherSessionSnapshot>(StringComparer.Ordinal);
        var latestPlays = new Dictionary<(string Server, string Account), LauncherSessionSnapshot>();
        foreach (var session in snapshot.Sessions)
        {
            sessions[session.SessionId] = session;
            var key = (session.ServerName, session.AccountName);
            if (session.Kind == LauncherActivityKind.Play &&
                (!latestPlays.TryGetValue(key, out var previous) || session.CreatedAt > previous.CreatedAt))
                latestPlays[key] = session;
        }
        foreach (var key in _entries.Keys.Where(key => !selected.ContainsKey(key)).ToArray())
        {
            _entries[key].Cancellation?.Cancel();
            _entries.Remove(key);
        }
        foreach (var (key, choice) in selected)
        {
            var latest = latestPlays.GetValueOrDefault(key);
            if (!_entries.TryGetValue(key, out Entry? entry))
            {
                entry = new Entry(choice) { SeenSession = latest?.SessionId, SeenCreatedAt = latest?.CreatedAt };
                _entries.Add(key, entry);
                if (latest?.IsProcessOrStartActive == true) entry.TrackedSession = latest.SessionId;
            }
            else
            {
                entry.Selection = choice;
                if (latest is not null && latest.SessionId != entry.SeenSession &&
                    (entry.SeenCreatedAt is null || latest.CreatedAt >= entry.SeenCreatedAt))
                {
                    entry.SeenSession = latest.SessionId;
                    entry.SeenCreatedAt = latest.CreatedAt;
                    entry.TrackedSession = latest.SessionId;
                    entry.TerminalSince = null;
                }
            }
            if (entry.TrackedSession is null) continue;
            var tracked = sessions.GetValueOrDefault(entry.TrackedSession);
            if (tracked?.IsProcessOrStartActive == true)
                entry.TerminalSince = null;
            else
                entry.TerminalSince ??= _clock.GetTimestamp();
        }
    }

    /// <summary>Attempts at most one due launch; capability holds are checked again on every poll.</summary>
    public async Task PollAsync(bool mayLaunch)
    {
        if (_disposed || !_enabled || IsLaunching || !mayLaunch) return;
        foreach (var (key, entry) in _entries.ToArray())
        {
            if (entry.TerminalSince is not { } since ||
                _clock.GetElapsedTime(since).TotalSeconds < DelaySeconds) continue;
            var choice = entry.Selection;
            if (choice.Mode == LaunchMode.Headless && choice.Character is null) continue;
            if (!orchestrator.GetAccountLaunchCapability(choice.Server, choice.Account, choice.Mode).IsAvailable)
                continue;
            using var cancellation = new CancellationTokenSource();
            entry.Cancellation = cancellation;
            IsLaunching = true;
            StateChanged?.Invoke();
            try
            {
                var launched = await orchestrator.LaunchAsync(choice.Server, choice.Account,
                    choice.Character, choice.Mode, cancellation.Token).ConfigureAwait(true);
                if (!_disposed && _enabled && _entries.GetValueOrDefault(key) == entry)
                {
                    entry.TrackedSession = launched.SessionId;
                    entry.SeenSession = launched.SessionId;
                    entry.SeenCreatedAt = launched.CreatedAt;
                    entry.TerminalSince = launched.IsProcessOrStartActive ? null : _clock.GetTimestamp();
                }
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
            catch (Exception error)
            {
                if (!_disposed) LaunchFailed?.Invoke(error);
            }
            finally
            {
                if (!_disposed && _enabled && _entries.GetValueOrDefault(key) == entry && entry.TerminalSince is not null)
                    entry.TerminalSince = _clock.GetTimestamp();
                entry.Cancellation = null;
                IsLaunching = false;
                StateChanged?.Invoke();
            }
            return;
        }
    }

    private void CancelEntries()
    {
        foreach (var entry in _entries.Values) entry.Cancellation?.Cancel();
        _entries.Clear();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _enabled = false;
        CancelEntries();
    }

    private sealed class Entry(LauncherRelaunchSelection selection)
    {
        public LauncherRelaunchSelection Selection = selection;
        public string? SeenSession;
        public DateTimeOffset? SeenCreatedAt;
        public string? TrackedSession;
        public long? TerminalSince;
        public CancellationTokenSource? Cancellation;
    }
}
