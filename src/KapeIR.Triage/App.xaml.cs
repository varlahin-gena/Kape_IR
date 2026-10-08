using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using KapeIR.Triage.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace KapeIR.Triage;

public partial class App : Application
{
    private ServiceProvider? _services;

    /// <summary>GUI DI container (null in silent / help paths).</summary>
    public IServiceProvider? Services => _services;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(int dwProcessId);

    private const int AttachParentProcess = -1;

    protected override async void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += (_, args) =>
        {
            ReportFatal("UI", args.Exception);
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
                ReportFatal("AppDomain", ex);
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            ReportFatal("Task", args.Exception);
            args.SetObserved();
        };

        var opt = RunnerCliOptions.Parse(e.Args);

        if (opt.ShowHelp || (opt.Errors.Count > 0 && !opt.Silent))
        {
            TryAttachConsole();
            Console.WriteLine(RunnerCliOptions.HelpText);
            if (opt.Errors.Count > 0)
            {
                Console.Error.WriteLine();
                foreach (var err in opt.Errors)
                    Console.Error.WriteLine(err);
            }
            Shutdown(opt.Errors.Count > 0 ? 2 : 0);
            return;
        }

        if (opt.Silent)
        {
            TryAttachConsole();
            if (opt.Errors.Count > 0)
            {
                foreach (var err in opt.Errors)
                    Console.Error.WriteLine(err);
                Shutdown(2);
                return;
            }

            var code = await SilentCollectionHost.RunAsync(opt);
            Shutdown(code);
            return;
        }

        _services = AppComposition.BuildGuiServices();
        base.OnStartup(e);
        var window = _services.GetRequiredService<MainWindow>();
        MainWindow = window;
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            _services?.Dispose();
        }
        catch
        {
            /* ignore */
        }
        finally
        {
            _services = null;
        }

        base.OnExit(e);
    }

    private static void ReportFatal(string source, Exception ex)
    {
        var text = $"[{source}] {ex}";
        try
        {
            var dir = Path.GetDirectoryName(Environment.ProcessPath)
                      ?? Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            var path = Path.Combine(dir, "KapeIR.Triage_crash.log");
            File.AppendAllText(path, DateTime.Now.ToString("s") + " " + text + Environment.NewLine + Environment.NewLine,
                Encoding.UTF8);
            MessageBox.Show(
                ex.Message + "\n\nПодробности записаны в:\n" + path,
                "KapeIR.Triage — ошибка",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        catch
        {
            try
            {
                MessageBox.Show(ex.Message, "KapeIR.Triage — ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch
            {
                /* ignore */
            }
        }
    }

    private static void TryAttachConsole()
    {
        try { AttachConsole(AttachParentProcess); }
        catch { /* GUI subsystem — ignore */ }
    }
}
