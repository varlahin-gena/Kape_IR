using KapeIR.Core.Services;
using KapeIR.Triage.ViewModels;
using KapeIR.Ui.Dialogs;
using KapeIR.Ui.Scheduling;
using Microsoft.Extensions.DependencyInjection;

namespace KapeIR.Triage.Hosting;

/// <summary>GUI composition root (silent mode does not use DI).</summary>
public static class AppComposition
{
    public static ServiceProvider BuildGuiServices()
    {
        var services = new ServiceCollection();

        services.AddSingleton<IDialogService, WpfDialogService>();
        services.AddSingleton<IUiScheduler, WpfUiScheduler>();
        services.AddSingleton<TriageRunCoordinator>();
        services.AddTransient<TriageViewModel>();
        services.AddTransient<MainWindow>();

        // ValidateOnBuild would construct MainWindow off the WPF STA thread in tests.
        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = false
        });
    }
}
