using AcDream.Launcher.Core.Orchestration;
using AcDream.Launcher.Core.Profiles;

namespace AcDream.Launcher.Automation;

internal sealed record ControlRequest(string Command, string? Server = null,
    string? Account = null, string? Session = null);

internal sealed record ControlReply(bool Ok, object? Data = null, string? Error = null);

/// <summary>Uses the same session owner as the UI; callers cannot supply credentials or commands.</summary>
internal sealed class LauncherControlHandler(ILauncherOrchestrator launcher,
    Func<CancellationToken, Task<ControlReply>>? reloadClient = null,
    Func<string?>? clientVersion = null)
{
    public async Task<ControlReply> HandleAsync(ControlRequest request, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        launcher.PollStatus();
        LauncherStateSnapshot state = launcher.GetSnapshot();
        token.ThrowIfCancellationRequested();
        switch (request.Command)
        {
            case "status":
                return new(true, new
                {
                    ready = state.IsInstallationReady,
                    clientVersion = clientVersion?.Invoke(),
                    accounts = state.Servers.SelectMany(server => server.Accounts.Select(account => new
                    {
                        server = server.Name, account = account.AccountName,
                        character = account.SelectedCharacter,
                        mode = account.SelectedLaunchMode?.ToString(),
                        active = account.HasRunningActivity,
                    })).ToArray(),
                    sessions = state.Sessions.Select(ProjectSession).ToArray(),
                });
            case "reload-client":
                if (state.Sessions.Any(s => s.IsActive))
                    return new(false, Error: "Stop every session before reloading the client version.");
                return reloadClient is null ? new(false, Error: "Client reload is unavailable.")
                    : await reloadClient(token);
            case "start":
                LauncherAccountSnapshot? selected = state.Servers
                    .Where(s => string.Equals(s.Name, request.Server, StringComparison.Ordinal))
                    .SelectMany(s => s.Accounts)
                    .SingleOrDefault(a => string.Equals(a.AccountName, request.Account, StringComparison.Ordinal));
                if (selected is null)
                    return new(false, Error: "Saved server/account not found.");
                if (selected.SelectedLaunchMode != LaunchMode.Headless || string.IsNullOrWhiteSpace(selected.SelectedCharacter))
                    return new(false, Error: "Select a saved character and Headless mode in the launcher first.");
                if (selected.HasRunningActivity)
                    return new(false, Error: "Account already has an active session.");
                LauncherSessionSnapshot started = await launcher.LaunchAsync(selected.ServerName,
                    selected.AccountName, selected.SelectedCharacter, LaunchMode.Headless, token);
                return new(true, ProjectSession(started));
            case "stop":
                LauncherSessionSnapshot? session = state.Sessions.SingleOrDefault(s => s.SessionId == request.Session);
                if (session is null)
                    return new(false, Error: "Session not found.");
                if (!session.IsActive)
                    return new(true, ProjectSession(session));
                if (session.LaunchMode != LaunchMode.Headless)
                    return new(false, Error: "Only headless sessions can be stopped through this interface.");
                // Request logout through the existing console owner. Status is polled by
                // the caller; a delayed logout must never become a forced process kill.
                return launcher.TrySendConsoleLine(session.SessionId, "/quit")
                    ? new(true, new { session = session.SessionId, logoutRequested = true })
                    : new(false, Error: "Session cannot accept a logout request yet.");
            default:
                return new(false, Error: "Unknown command. Use status, start, stop or reload-client.");
        }
    }

    private static object ProjectSession(LauncherSessionSnapshot s) => new
    {
        session = s.SessionId, server = s.ServerName, account = s.AccountName,
        character = s.CharacterName, mode = s.LaunchMode?.ToString(),
        state = s.State.ToString(), active = s.IsActive, exitCode = s.ExitCode,
        graceful = s.ExitedGracefully,
    };
}
