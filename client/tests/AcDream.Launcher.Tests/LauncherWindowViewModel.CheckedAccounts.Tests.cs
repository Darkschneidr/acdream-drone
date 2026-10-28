using AcDream.Launcher.Core.Profiles;

namespace AcDream.Launcher.Tests;

public sealed partial class LauncherWindowViewModelTests
{
    [Fact]
    public void CheckedAccountsReopenOnTheirOwnServerWithoutLaunching()
    {
        using var selection = new CheckedAccountsFixture();
        using (var core = BatchOrchestrator())
        using (var vm = CreateInitialized(core, checkedAccountStore: selection.NewStore()))
        {
            Assert.All(vm.Accounts.SelectMany(group => group.Rows), row => Assert.False(row.IsChecked));
            vm.Accounts[0].Rows[1].IsChecked = true;
            Assert.Null(vm.LastError);
        }

        using var reopenedCore = BatchOrchestrator();
        using var reopened = CreateInitialized(reopenedCore, checkedAccountStore: selection.NewStore());
        var selected = Assert.Single(reopened.Accounts.SelectMany(group => group.Rows), row => row.IsChecked);
        Assert.Equal("Alice", selected.AccountName);
        Assert.Equal("Two", selected.ServerName);
        Assert.False(reopened.Accounts[0].Rows[0].IsChecked);
        Assert.False(reopened.RelaunchCheckedClientsAutomatically);
        Assert.Empty(reopenedCore.LaunchRequests);
    }

    [Fact]
    public void SavingChecksKeepsHiddenSelectionsAndPersistsUnchecking()
    {
        using var selection = new CheckedAccountsFixture();
        using (var core = ProfiledOrchestrator())
        using (var vm = CreateInitialized(core, checkedAccountStore: selection.NewStore()))
        {
            var alice = vm.Accounts[0];
            alice.Rows[0].IsChecked = true;
            vm.ProfileFilter = "bots";
            Assert.False(alice.Rows[0].IsVisible);
            alice.Rows[1].IsChecked = true;
            vm.ShowOnlyCheckedAccounts = true;
            vm.RefreshProfiles();
            Assert.True(alice.Rows[0].IsChecked);

            var saved = selection.NewStore();
            saved.Load();
            Assert.True(saved.Contains(alice.Rows[0].ServerName, alice.AccountName));
            Assert.True(saved.Contains(alice.Rows[1].ServerName, alice.AccountName));
            alice.Rows[1].IsChecked = false;
            Assert.Null(vm.LastError);
        }

        using var reopenedCore = ProfiledOrchestrator();
        using var reopened = CreateInitialized(reopenedCore, checkedAccountStore: selection.NewStore());
        Assert.True(reopened.Accounts[0].Rows[0].IsChecked);
        Assert.False(reopened.Accounts[0].Rows[1].IsChecked);
    }

    [Fact]
    public void RestoringAndRefreshingChecksDoesNotRewriteSelectionFile()
    {
        using var selection = new CheckedAccountsFixture();
        selection.NewStore().Save([new LauncherCheckedAccount("Two", "Alice")]);
        byte[] original = File.ReadAllBytes(selection.FilePath);
        using var core = BatchOrchestrator();
        using var vm = CreateInitialized(core, checkedAccountStore: selection.NewStore());
        vm.RefreshProfiles();
        vm.PollStatus();
        Assert.Equal(original, File.ReadAllBytes(selection.FilePath));
        Assert.Null(core.SavedRowSelection);
    }

    [Fact]
    public void InvalidSelectionFileIsReportedAndPreserved()
    {
        using var selection = new CheckedAccountsFixture();
        File.WriteAllText(selection.FilePath, "[{\"server\":null,\"account\":\"Alice\"}]");
        string original = File.ReadAllText(selection.FilePath);
        using var core = BatchOrchestrator();
        using var vm = CreateInitialized(core, checkedAccountStore: selection.NewStore());
        Assert.NotNull(vm.LastError);
        vm.Accounts[0].Rows[0].IsChecked = true;
        Assert.Contains("selections cannot be saved", vm.LastError!);
        Assert.Equal(original, File.ReadAllText(selection.FilePath));
    }

    private sealed class CheckedAccountsFixture : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "launcher-checks-" + Guid.NewGuid().ToString("N"));

        public CheckedAccountsFixture() => Directory.CreateDirectory(_directory);
        public string FilePath => Path.Combine(_directory, LauncherCheckedAccountStore.FileName);
        public LauncherCheckedAccountStore NewStore() => new(FilePath);
        public void Dispose() => Directory.Delete(_directory, recursive: true);
    }
}
