using System.Collections.ObjectModel;
using AcDream.Launcher.Core.Orchestration;
using AcDream.Launcher.Core.Profiles;

namespace AcDream.Launcher.ViewModels;

/// <summary>One chip of the profile filter above the accounts: "All", or one profile tag.</summary>
public sealed class ProfileFilterChipViewModel : ObservableObject
{
    private bool _isSelected;

    public ProfileFilterChipViewModel(string label, string? profile, Action<ProfileFilterChipViewModel> select)
    {
        Label = label;
        Profile = profile;
        SelectCommand = new RelayCommand(() => select(this));
    }

    public string Label { get; }

    /// <summary>The tag this chip shows, or null for "All".</summary>
    public string? Profile { get; }

    public bool IsSelected { get => _isSelected; set => SetProperty(ref _isSelected, value); }

    public RelayCommand SelectCommand { get; }
}

public sealed partial class LauncherWindowViewModel
{
    private string? _profileFilter;
    private bool _showOnlyCheckedAccounts;
    private bool _relaunchCheckedClientsAutomatically;
    private string _relaunchDelaySecondsText = "180";
    private bool _refreshingAccountRows;

    public bool ShowOnlyCheckedAccounts
    {
        get => _showOnlyCheckedAccounts;
        set { if (SetProperty(ref _showOnlyCheckedAccounts, value)) ApplyProfileFilter(); }
    }

    public bool RelaunchCheckedClientsAutomatically
    {
        get => _relaunchCheckedClientsAutomatically;
        set
        {
            if (SetProperty(ref _relaunchCheckedClientsAutomatically, value))
                ObserveSessionControls(_orchestrator.GetSnapshot());
        }
    }

    public string RelaunchDelaySecondsText
    {
        get => _relaunchDelaySecondsText;
        set
        {
            if (!SetProperty(ref _relaunchDelaySecondsText, value)) return;
            if (int.TryParse(value, out int seconds) && seconds is >= 5 and <= 3600)
                _automaticRelaunch.DelaySeconds = seconds;
            OnPropertyChanged(nameof(IsRelaunchDelayValid));
        }
    }

    public bool IsRelaunchDelayValid => int.TryParse(_relaunchDelaySecondsText, out int seconds) && seconds is >= 5 and <= 3600;
    internal Task AutomaticRelaunchTask { get; private set; } = Task.CompletedTask;

    private void ObserveSessionControls(LauncherStateSnapshot snapshot)
    {
        bool wasStopping = _stopGate.IsPending;
        _stopGate.Observe(snapshot);
        _automaticRelaunch.Observe(snapshot, RelaunchCheckedClientsAutomatically
            ? [.. AllAccountRows.Where(row => row.IsChecked).Select(row =>
                new LauncherRelaunchSelection(row.ServerName, row.AccountName, row.CharacterName, row.Mode))] : [],
            RelaunchCheckedClientsAutomatically);
        if (wasStopping != _stopGate.IsPending) NotifyCommandStates();
    }

    private void OnAutomaticRelaunchStateChanged() { if (!_disposed) NotifyCommandStates(); }
    private void OnAutomaticRelaunchFailed(Exception error)
    {
        LastError = SafeDisplayError(error, secret: null);
        OperationStatus = "Automatic relaunch failed; another attempt waits the full delay.";
    }

    private async Task RunAutomaticRelaunchAsync()
    {
        try
        {
            await _automaticRelaunch.PollAsync(CanInteract && IsRelaunchDelayValid &&
                !_isInstallationChecking && !_isClientCompatibilityCheckBlocking).ConfigureAwait(true);
        }
        catch (Exception error) { if (!_disposed) OnAutomaticRelaunchFailed(error); }
    }

    private void OnAccountRowChanged()
    {
        ApplyProfileFilter();
        if (!_refreshingAccountRows && RelaunchCheckedClientsAutomatically) ObserveSessionControls(_orchestrator.GetSnapshot());
    }
    private IReadOnlyDictionary<(string Server, string Account), LauncherCapability> _graphicalAvailability =
        new Dictionary<(string, string), LauncherCapability>();
    private IReadOnlyDictionary<(string Server, string Account), LauncherCapability> _headlessAvailability =
        new Dictionary<(string, string), LauncherCapability>();
    private IReadOnlyDictionary<(string Server, string Account), LauncherCapability> _characterSelectAvailability =
        new Dictionary<(string, string), LauncherCapability>();

    public ObservableCollection<LauncherAccountGroupViewModel> Accounts { get; } = [];
    public bool HasAccounts => Accounts.Count != 0;

    /// <summary>"All" and one chip per profile tag, in the order the tags first appear.</summary>
    public ObservableCollection<ProfileFilterChipViewModel> ProfileFilters { get; } = [];

    public bool HasProfileFilters => ProfileFilters.Count > 1;

    /// <summary>The profile tag the accounts are filtered by, or null for all accounts.</summary>
    public string? ProfileFilter
    {
        get => _profileFilter;
        set
        {
            if (SetProperty(ref _profileFilter, value))
            {
                ApplyProfileFilter();
            }
        }
    }

    public AsyncRelayCommand LaunchCheckedCommand { get; private set; } = null!;
    public string CheckedSelectionSummary => $"{CheckedRows.Count()} selected · {CheckedRows.Count(row => row.CanPlay)} ready";

    /// <summary>"Play selected (2)".</summary>
    public string PlayCheckedText => $"Play selected ({CheckedRows.Count()})";

    private IEnumerable<LauncherAccountServerRowViewModel> AllAccountRows => Accounts.SelectMany(account => account.Rows);

    /// <summary>The ticked rows the profile filter shows: what Play selected starts.</summary>
    private IEnumerable<LauncherAccountServerRowViewModel> CheckedRows =>
        Accounts.Where(account => account.IsVisible).SelectMany(account => account.Rows)
            .Where(row => row.IsVisible && row.IsChecked);

    /// <summary>Whether launching the checked rows would actually do something, so the button can
    /// show gold only when it is ready rather than whenever it is on screen.</summary>
    public bool HasPlayableCheckedRows => CheckedRows.Any(row => row.CanPlay);

    private void InitializeAccountCommands() => LaunchCheckedCommand = new AsyncRelayCommand(
        () => LaunchRowsAsync([.. CheckedRows]),
        () => CanStartRows && CheckedRows.Any(row => row.CanPlay));

    public void RefreshProfiles() => RefreshFromCore();

    private void RefreshAccountRows(LauncherStateSnapshot snapshot)
    {
        _refreshingAccountRows = true;
        try
        {
            var retained = new HashSet<LauncherAccountGroupViewModel>();
            int index = 0;
            var grouped = snapshot.Servers
                .SelectMany(server => server.Accounts.Select(account => (server, account)))
                .GroupBy(pair => pair.account.AccountName, StringComparer.Ordinal);
            foreach (var entries in grouped)
            {
                LauncherServerSnapshot firstServer = entries.First().server;
                LauncherAccountGroupViewModel? group = Accounts.FirstOrDefault(item =>
                    item.AccountName == entries.Key);
                if (group is null)
                {
                    group = new LauncherAccountGroupViewModel(
                        firstServer.Name, entries.Key, OpenAccountPlugins, () => CanInteract);
                    Accounts.Insert(Math.Min(index, Accounts.Count), group);
                }
                else if (Accounts.IndexOf(group) != index && index < Accounts.Count)
                {
                    Accounts.Move(Accounts.IndexOf(group), index);
                }

                index++;
                retained.Add(group);
                var retainedRows = new HashSet<LauncherAccountServerRowViewModel>();
                int rowIndex = 0;
                foreach ((LauncherServerSnapshot server, LauncherAccountSnapshot account) in entries)
                {
                    LauncherAccountServerRowViewModel? row = group.Rows.FirstOrDefault(item =>
                        item.ServerName == server.Name);
                    if (row is null)
                    {
                        row = new LauncherAccountServerRowViewModel(account.AccountName, server.Name,
                            GetRowDisabledReason, OnAccountRowChanged, item => LaunchRowsAsync([item]),
                            StopSessionAsync,
                            new LauncherRowActions(OpenLogonCommandsFor, OpenAccountPlugins,
                                OpenCharacterPlugins, OpenLogsFolder, RemoveRowCharacter),
                            () => CanInteract && !_startingRows.Contains((server.Name, account.AccountName)), CanStopSession);
                        row.UseSelectionStore(SaveRowSelection);
                        row.UseCheckedSelectionStore(
                            _checkedAccountsLoaded && _checkedAccountStore?.Contains(server.Name, account.AccountName) == true,
                            SaveCheckedAccounts);
                        row.UseConsole(OpenSessionConsole);
                        group.Rows.Insert(Math.Min(rowIndex, group.Rows.Count), row);
                    }
                    else if (group.Rows.IndexOf(row) != rowIndex && rowIndex < group.Rows.Count)
                    {
                        group.Rows.Move(group.Rows.IndexOf(row), rowIndex);
                    }

                    rowIndex++;
                    retainedRows.Add(row);
                    row.Update(server, account, snapshot.Sessions.OrderByDescending(session => session.CreatedAt).FirstOrDefault(session =>
                        session.ServerName == server.Name && session.AccountName == account.AccountName));
                }
                foreach (LauncherAccountServerRowViewModel row in group.Rows.Where(row => !retainedRows.Contains(row)).ToArray())
                    group.Rows.Remove(row);
                group.Update([.. entries.Select(entry => entry.account)], PluginDisplayName);
            }

            foreach (LauncherAccountGroupViewModel group in Accounts.Where(group => !retained.Contains(group)).ToArray())
                Accounts.Remove(group);
            RefreshProfileFilters(snapshot);
        }
        finally { _refreshingAccountRows = false; }
    }

    /// <summary>Rebuilds the chips from the accounts' tags, keeping the chosen one while it exists.</summary>
    private void RefreshProfileFilters(LauncherStateSnapshot snapshot)
    {
        string[] tags = [.. snapshot.Servers.SelectMany(server => server.Accounts)
            .SelectMany(account => account.Profiles).Distinct(StringComparer.OrdinalIgnoreCase)];
        if (_profileFilter is not null && !tags.Contains(_profileFilter, StringComparer.OrdinalIgnoreCase))
        {
            _profileFilter = null;
            OnPropertyChanged(nameof(ProfileFilter));
        }

        string?[] wanted = [null, .. tags];
        if (!ProfileFilters.Select(chip => chip.Profile).SequenceEqual(wanted, StringComparer.Ordinal))
        {
            ProfileFilters.Clear();
            foreach (string? tag in wanted)
                ProfileFilters.Add(new ProfileFilterChipViewModel(tag ?? "All", tag, chip => ProfileFilter = chip.Profile));
            OnPropertyChanged(nameof(HasProfileFilters));
        }

        ApplyProfileFilter();
    }

    private void ApplyProfileFilter()
    {
        foreach (ProfileFilterChipViewModel chip in ProfileFilters)
            chip.IsSelected = string.Equals(chip.Profile, _profileFilter, StringComparison.OrdinalIgnoreCase);
        foreach (LauncherAccountGroupViewModel group in Accounts)
        {
            foreach (LauncherAccountServerRowViewModel row in group.Rows)
                row.IsVisible = (_profileFilter is null || row.HasProfile(_profileFilter))
                    && (!ShowOnlyCheckedAccounts || row.IsChecked);
            group.IsVisible = group.Rows.Any(row => row.IsVisible);
        }
        NotifyAccountCommands();
    }

    private void SaveRowSelection(LauncherAccountServerRowViewModel row)
    {
        try
        {
            _orchestrator.UpdateAccountSelection(row.ServerName, row.AccountName, row.CharacterName, row.Mode);
        }
        catch (Exception ex)
        {
            LastError = SafeDisplayError(ex, secret: null);
        }
    }

    private void SaveCheckedAccounts()
    {
        if (_checkedAccountStore is null) return;
        try
        {
            if (!_checkedAccountsLoaded)
                throw new InvalidOperationException("Checked accounts could not be loaded; selections cannot be saved.");
            _checkedAccountStore.Save(AllAccountRows.Where(row => row.IsChecked)
                .Select(row => new LauncherCheckedAccount(row.ServerName, row.AccountName)));
        }
        catch (Exception ex)
        {
            LastError = SafeDisplayError(ex, secret: null);
        }
    }

    private readonly HashSet<(string Server, string Account)> _startingRows = new();
    private readonly HashSet<CancellationTokenSource> _rowLaunches = new();
    private bool CanStartRows => CanInteract;

    private string? GetRowDisabledReason(LauncherAccountServerRowViewModel row) =>
        !CanStartRows ? "Finish the current operation or close the dialog first."
        : _startingRows.Contains((row.ServerName, row.AccountName)) ? "This account is starting."
        : GetRowLaunchBlock(row);

    private string? GetRowLaunchBlock(LauncherAccountServerRowViewModel row) => GetRowLaunchBlock(row, row.CharacterName, row.Mode);

    private string? GetRowLaunchBlock(LauncherAccountServerRowViewModel row, string? characterName, LaunchMode mode, bool fresh = false)
    {
        if (_isInstallationChecking || _isClientCompatibilityCheckBlocking) return "Checking installation and client compatibility…";
        if (row.IsActive) return "This account already has an active session on this server.";
        if (mode == LaunchMode.Headless && characterName is null) return "Choose a character for Headless mode.";
        if (row.SelectedLaunchMode is not ("Graphical" or "Headless")) return "Choose Graphical or Headless.";
        LauncherStateSnapshot? current = fresh ? _orchestrator.GetSnapshot() : _snapshot;
        LauncherAccountSnapshot? account = current?.Servers.FirstOrDefault(server => server.Name == row.ServerName)
            ?.Accounts.FirstOrDefault(account => account.AccountName == row.AccountName);
        if (account is null) return "This account is no longer configured on this server.";
        if (account.HasRunningActivity || current!.Sessions.Any(session => session.IsProcessOrStartActive
            && session.ServerName == row.ServerName && session.AccountName == row.AccountName)) return "This account already has an active session on this server.";
        if (characterName is { } name && !account.Characters.Any(character => character.Name == name)) return "Choose a current character.";
        LauncherCapability capability = fresh
            ? _orchestrator.GetAccountLaunchCapability(row.ServerName, row.AccountName, mode)
            : (mode == LaunchMode.Headless ? _headlessAvailability
                : mode == LaunchMode.GuiSelect ? _characterSelectAvailability : _graphicalAvailability)
                .GetValueOrDefault((row.ServerName, row.AccountName), LauncherCapability.Unavailable("Launch availability has not been checked."));
        return capability.IsAvailable ? null : capability.Reason ?? "Launch unavailable.";
    }

    private async Task LaunchRowsAsync(LauncherAccountServerRowViewModel[] rows)
    {
        if (!CanStartRows) return;
        var pending = rows.Where(row => !_startingRows.Contains((row.ServerName, row.AccountName))).Distinct().Select(row => (Row: row, Character: row.CharacterName, Mode: row.Mode)).ToArray();
        if (pending.Length == 0) return;
        using var cancellation = new CancellationTokenSource();
        CancellationToken token = cancellation.Token;
        _rowLaunches.Add(cancellation);
        foreach (var item in pending) _startingRows.Add((item.Row.ServerName, item.Row.AccountName));
        NotifyCommandStates();
        LastError = null;
        int started = 0;
        var errors = new List<string>();
        try
        {
            foreach (var item in pending)
            {
                token.ThrowIfCancellationRequested();
                string? block = GetRowLaunchBlock(item.Row, item.Character, item.Mode, fresh: true);
                if (block is not null) { errors.Add($"{item.Row.AccountName} / {item.Row.ServerName}: {block}"); continue; }
                OperationStatus = $"Launching {started + 1} of {pending.Length}…";
                try
                {
                    var result = await _orchestrator.LaunchAsync(item.Row.ServerName, item.Row.AccountName,
                        item.Character, item.Mode, token).ConfigureAwait(true);
                    if (result.State is not (LauncherActivityState.Failed or LauncherActivityState.Cancelled) && result.ExitCode is not (> 0 or < 0)) started++;
                    else errors.Add($"{item.Row.AccountName} / {item.Row.ServerName}: {result.Error ?? result.Status}");
                }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { throw; }
                catch (Exception ex) { errors.Add($"{item.Row.AccountName} / {item.Row.ServerName}: {SafeDisplayError(ex, secret: null)}"); }
            }
            OperationStatus = $"Started {started} of {pending.Length} selected sessions.";
        }
        catch (OperationCanceledException) { OperationStatus = $"Launch cancelled. Started {started} sessions."; }
        finally
        {
            LastError = errors.Count == 0 ? null : string.Join(Environment.NewLine, errors);
            _rowLaunches.Remove(cancellation);
            foreach (var item in pending) _startingRows.Remove((item.Row.ServerName, item.Row.AccountName));
            if (!_disposed)
            {
                NotifyCommandStates();
                RefreshFromCore();
            }
        }
    }

    private void OpenLogonCommandsFor(LauncherAccountServerRowViewModel row) =>
        OpenTextEditor(LauncherTextEditorKind.LogonCommands);

    private void OpenLogsFolder(LauncherAccountServerRowViewModel row) =>
        _installFolder?.Folders.FirstOrDefault(folder => folder.Label == "Logs")?.OpenCommand.Execute(null);

    private void RemoveRowCharacter(LauncherAccountServerRowViewModel row)
    {
        if (row.CharacterName is not { } name) return;
        (string server, string account) = (row.ServerName, row.AccountName);
        EditorDialog.Open(
            ProfileEditorKind.Remove,
            $"Remove {name}?",
            _ =>
            {
                _orchestrator.RemoveCharacter(server, account, name);
                OperationStatus = $"Removed {name}. Refreshing the account's characters brings it back.";
            },
            message: $"This removes {name} from {account} on {server}, with its own plugin list if it has one. "
                + "The next login to the account brings the character back, using the account's plugins.");
    }

    /// <summary>
    /// Raised when a session's console should be shown. Showing a window is
    /// the view's business; the view model only says which console.
    /// </summary>
    public event Action<SessionConsoleViewModel>? ConsoleRequested;

    private void OpenSessionConsole(string sessionId, string title) =>
        ConsoleRequested?.Invoke(
            new SessionConsoleViewModel(_orchestrator, sessionId, "Console · " + title));

    private void NotifyAccountCommands()
    {
        RefreshAccountAvailability();
        LaunchCheckedCommand?.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(HasPlayableCheckedRows));
        OnPropertyChanged(nameof(CheckedSelectionSummary));
        OnPropertyChanged(nameof(PlayCheckedText));
        OnPropertyChanged(nameof(HasAccounts));
        foreach (LauncherAccountGroupViewModel group in Accounts) group.EditPluginsCommand.NotifyCanExecuteChanged();
        foreach (LauncherAccountServerRowViewModel row in AllAccountRows) row.NotifyState();
    }

    private void RefreshAccountAvailability()
    {
        _graphicalAvailability = _orchestrator.GetAccountLaunchCapabilities(LaunchMode.Gui);
        _headlessAvailability = _orchestrator.GetAccountLaunchCapabilities(LaunchMode.Headless);
        _characterSelectAvailability = _orchestrator.GetAccountLaunchCapabilities(LaunchMode.GuiSelect);
    }
}
