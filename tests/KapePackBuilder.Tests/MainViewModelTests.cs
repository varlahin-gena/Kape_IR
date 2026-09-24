using KapePackBuilder.Services;
using KapePackBuilder.ViewModels;

namespace KapePackBuilder.Tests;

public class MainViewModelTests
{
    [Fact]
    public async Task EnsureCatalogBoundToUiRootAsync_EmptyRoot_ShowsError()
    {
        var dialogs = new FakeDialogService();
        using var vm = new MainViewModel(dialogs) { KapeRoot = "" };

        var root = await vm.EnsureCatalogBoundToUiRootAsync();

        Assert.Null(root);
        Assert.Contains(dialogs.Messages, m => m.Title == "Корень KAPE" && m.Icon == DialogIcon.Error);
    }

    [Fact]
    public async Task EnsureCatalogBoundToUiRootAsync_MissingTargets_ShowsError()
    {
        var dialogs = new FakeDialogService();
        var tmp = Path.Combine(Path.GetTempPath(), "kape_vm_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            using var vm = new MainViewModel(dialogs) { KapeRoot = tmp };

            var root = await vm.EnsureCatalogBoundToUiRootAsync();

            Assert.Null(root);
            Assert.Contains(dialogs.Messages, m =>
                m.Title == "Корень KAPE" &&
                m.Message.Contains("нет Targets", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            try { Directory.Delete(tmp, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void CanExecute_BlocksBuildWhileSyncing()
    {
        var dialogs = new FakeDialogService();
        using var vm = new MainViewModel(dialogs);

        Assert.True(vm.BuildPackageCommand.CanExecute(null));
        Assert.True(vm.UpdateFromGitHubCommand.CanExecute(null));
        Assert.True(vm.ReloadCatalogCommand.CanExecute(null));

        vm.IsSyncing = true;
        Assert.False(vm.BuildPackageCommand.CanExecute(null));
        Assert.False(vm.UpdateFromGitHubCommand.CanExecute(null));
        Assert.False(vm.ReloadCatalogCommand.CanExecute(null));

        vm.IsSyncing = false;
        vm.IsBuilding = true;
        Assert.False(vm.BuildPackageCommand.CanExecute(null));
        Assert.False(vm.UpdateFromGitHubCommand.CanExecute(null));
        Assert.False(vm.ReloadCatalogCommand.CanExecute(null));
        Assert.True(vm.CancelBuildCommand.CanExecute(null));
    }

    [Fact]
    public async Task ReloadCatalogAsync_WhileBuilding_DoesNotPromptForMissingRoot()
    {
        var dialogs = new FakeDialogService();
        dialogs.ConfirmResults.Enqueue(true);
        using var vm = new MainViewModel(dialogs) { KapeRoot = "", IsBuilding = true };

        await vm.ReloadCatalogCommand.ExecuteAsync(null);

        Assert.Empty(dialogs.Confirms);
        Assert.Contains("сборк", vm.StatusText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TwoPhase_SetsDefaultPhase1Module()
    {
        using var vm = new MainViewModel(new FakeDialogService());
        vm.Package.Phase1ModuleName = "";
        vm.TwoPhaseCollection = true;
        Assert.False(string.IsNullOrWhiteSpace(vm.Package.Phase1ModuleName));
    }
}
