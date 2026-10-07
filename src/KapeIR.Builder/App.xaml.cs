using System.Windows;
using System.Windows.Threading;
using KapeIR.Core.Services;
using KapeIR.Builder.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;

namespace KapeIR.Builder;

public partial class App : Application
{
    private ServiceProvider? _services;
    private ILoggerFactory? _loggerFactory;

    /// <summary>Root DI container (available after <see cref="OnStartup"/>).</summary>
    public IServiceProvider Services =>
        _services ?? throw new InvalidOperationException("DI container is not built yet.");

    protected override void OnStartup(StartupEventArgs e)
    {
        var logging = AppComposition.CreateLogging();
        _loggerFactory = logging.Factory;
        AppLog.Initialize(logging.Factory, logging.LogFilePath);
        _services = AppComposition.BuildServices(logging.Factory);

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        base.OnStartup(e);

        AppLog.Info(
            "{App} start (pid={Pid}, log={LogFile})",
            ProductIdentity.Builder,
            Environment.ProcessId,
            AppLog.LogFilePath);

        var window = Services.GetRequiredService<MainWindow>();
        MainWindow = window;
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        AppLog.Info("{App} exit code={Code}", ProductIdentity.Builder, e.ApplicationExitCode);
        try
        {
            _services?.Dispose();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(ex);
        }
        finally
        {
            _services = null;
            _loggerFactory?.Dispose();
            _loggerFactory = null;
            Log.CloseAndFlush();
        }

        base.OnExit(e);
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        AppLog.Error(e.Exception, "Unhandled UI exception");
        e.Handled = true;
        try
        {
            MessageBox.Show(
                e.Exception.Message + "\n\nПодробности: " + AppLog.LogFilePath,
                ProductIdentity.Builder + " — ошибка",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        catch
        {
            /* ignore */
        }
    }

    private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
            AppLog.Error(ex, "Unhandled AppDomain exception");
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        AppLog.Error(e.Exception, "Unobserved task exception");
        e.SetObserved();
    }
}
