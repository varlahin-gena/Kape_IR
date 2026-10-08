using KapeIR.Triage;
using KapeIR.Triage.Hosting;
using KapeIR.Triage.ViewModels;
using KapeIR.Ui.Dialogs;
using KapeIR.Ui.Scheduling;
using Microsoft.Extensions.DependencyInjection;

namespace KapeIR.Builder.Tests;

public sealed class TriageAppCompositionTests
{
    [Fact]
    public void BuildGuiServices_ResolvesMainWindowGraph()
    {
        using var sp = AppComposition.BuildGuiServices();

        var dialogs = sp.GetRequiredService<IDialogService>();
        var vm = sp.GetRequiredService<TriageViewModel>();
        Assert.NotNull(dialogs);
        Assert.NotNull(vm);
        Assert.IsType<WpfDialogService>(dialogs);
        Assert.IsType<WpfUiScheduler>(sp.GetRequiredService<IUiScheduler>());
    }
}
