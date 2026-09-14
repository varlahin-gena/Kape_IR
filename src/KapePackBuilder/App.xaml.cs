using System.Windows;
using KapePack.Core.Services;
using KapePackBuilder.Services;
using KapePackBuilder.ViewModels;

namespace KapePackBuilder;

public partial class App : Application
{
    /// <summary>Composition root — keep wiring here so ViewModels stay testable.</summary>
    public IDialogService Dialogs { get; } = new WpfDialogService();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        AppLog.Info($"KapePackBuilder start (pid={Environment.ProcessId})");
    }

    protected override void OnExit(ExitEventArgs e)
    {
        AppLog.Info($"KapePackBuilder exit code={e.ApplicationExitCode}");
        base.OnExit(e);
    }

    public MainViewModel CreateMainViewModel() => new(Dialogs);
}
