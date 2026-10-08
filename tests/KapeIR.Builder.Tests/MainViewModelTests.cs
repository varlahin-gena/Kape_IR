using KapeIR.Builder.Services;
using KapeIR.Builder.ViewModels;
using KapeIR.Ui.Dialogs;

namespace KapeIR.Builder.Tests;

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
    public async Task EnsureCatalogBoundToUiRootAsync_RandomFolder_ShowsError()
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
                m.Message.Contains("не похожа", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            try { Directory.Delete(tmp, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public async Task EnsureCatalogBoundToUiRootAsync_KapeExeOnly_CreatesLayout()
    {
        var dialogs = new FakeDialogService();
        var tmp = Path.Combine(Path.GetTempPath(), "kape_vm_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        File.WriteAllBytes(Path.Combine(tmp, "kape.exe"), new byte[] { 1 });
        try
        {
            using var vm = new MainViewModel(dialogs) { KapeRoot = tmp };

            var root = await vm.EnsureCatalogBoundToUiRootAsync();

            Assert.NotNull(root);
            Assert.True(Directory.Exists(Path.Combine(tmp, "Targets")));
            Assert.True(Directory.Exists(Path.Combine(tmp, "Modules")));
            Assert.True(Directory.Exists(Path.Combine(tmp, "Modules", "bin")));
            Assert.Empty(dialogs.Messages);
        }
        finally
        {
            try { Directory.Delete(tmp, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public async Task InitializeAsync_EmptyRoot_DoesNotPrompt()
    {
        var dialogs = new FakeDialogService();
        dialogs.ConfirmResults.Enqueue(true);
        using var vm = new MainViewModel(dialogs) { KapeRoot = "" };

        await vm.InitializeAsync();

        Assert.Empty(dialogs.Confirms);
        Assert.Empty(dialogs.Messages);
        Assert.Contains("Обзор", vm.StatusText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CanExecute_BlocksBuildWhileSyncing()
    {
        var dialogs = new FakeDialogService();
        using var vm = new MainViewModel(dialogs);

        Assert.True(vm.BuildPackageCommand.CanExecute(null));
        Assert.True(vm.Sync.UpdateFromGitHubCommand.CanExecute(null));
        Assert.True(vm.Catalog.ReloadCatalogCommand.CanExecute(null));

        vm.IsSyncing = true;
        Assert.False(vm.BuildPackageCommand.CanExecute(null));
        Assert.False(vm.Sync.UpdateFromGitHubCommand.CanExecute(null));
        Assert.False(vm.Catalog.ReloadCatalogCommand.CanExecute(null));

        vm.IsSyncing = false;
        vm.IsBuilding = true;
        Assert.False(vm.BuildPackageCommand.CanExecute(null));
        Assert.False(vm.Sync.UpdateFromGitHubCommand.CanExecute(null));
        Assert.False(vm.Catalog.ReloadCatalogCommand.CanExecute(null));
        Assert.True(vm.CancelBuildCommand.CanExecute(null));
    }

    [Fact]
    public async Task ReloadCatalogAsync_WhileBuilding_DoesNotPromptForMissingRoot()
    {
        var dialogs = new FakeDialogService();
        dialogs.ConfirmResults.Enqueue(true);
        using var vm = new MainViewModel(dialogs) { KapeRoot = "", IsBuilding = true };

        await vm.Catalog.ReloadCatalogCommand.ExecuteAsync(null);

        Assert.Empty(dialogs.Confirms);
        Assert.Contains("сборк", vm.StatusText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TwoPhase_SetsDefaultPhase1Module()
    {
        using var vm = new MainViewModel(new FakeDialogService());
        vm.Package.Phase1ModuleName = "";
        vm.PackageEditor.TwoPhaseCollection = true;
        Assert.False(string.IsNullOrWhiteSpace(vm.Package.Phase1ModuleName));
    }
}
