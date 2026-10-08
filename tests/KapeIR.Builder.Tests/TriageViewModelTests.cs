using KapeIR.Triage.ViewModels;

namespace KapeIR.Builder.Tests;

public sealed class TriageViewModelTests
{
    [Fact]
    public void Initial_state_blocks_start_until_prepared()
    {
        var dlg = new FakeTriageDialogService();
        var vm = new TriageViewModel(dlg);

        Assert.False(vm.IsPrepared);
        Assert.False(vm.IsRunning);
        Assert.True(vm.IsSourceEnabled);
        Assert.False(vm.StartCommand.CanExecute(null));
        Assert.False(vm.SimulateCommand.CanExecute(null));
        Assert.False(vm.CancelRunCommand.CanExecute(null));
    }

    [Fact]
    public void Phase_flags_are_mutually_exclusive()
    {
        var vm = new TriageViewModel(new FakeTriageDialogService());

        vm.Phase1Only = true;
        Assert.True(vm.Phase1Only);
        Assert.False(vm.Phase2Only);

        vm.Phase2Only = true;
        Assert.True(vm.Phase2Only);
        Assert.False(vm.Phase1Only);
    }

    [Fact]
    public void IsRunning_disables_close_and_source_panel()
    {
        var vm = new TriageViewModel(new FakeTriageDialogService())
        {
            IsPrepared = true,
            CanClose = true
        };

        vm.IsRunning = true;

        Assert.False(vm.CanClose);
        Assert.False(vm.IsSourceEnabled);
        Assert.True(vm.CancelRunCommand.CanExecute(null));
        Assert.False(vm.StartCommand.CanExecute(null));

        vm.IsRunning = false;

        Assert.True(vm.CanClose);
        Assert.True(vm.IsSourceEnabled);
        Assert.True(vm.StartCommand.CanExecute(null));
    }

    [Fact]
    public void PresentError_shows_banner_without_message_box()
    {
        var dlg = new FakeTriageDialogService();
        var vm = new TriageViewModel(dlg);

        vm.PresentError("Подготовка не удалась", "Нет sidecar sha256.\nВторая строка.");

        Assert.True(vm.IsErrorVisible);
        Assert.Equal("Подготовка не удалась", vm.ErrorTitle);
        Assert.Contains("sidecar", vm.ErrorDetail, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Вторая строка", vm.ErrorDetail, StringComparison.Ordinal);
        Assert.True(vm.CanClose);
        Assert.True(vm.CanSaveLog);
        Assert.Contains("Ошибка", vm.StatusText, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(dlg.Messages);
    }
}
