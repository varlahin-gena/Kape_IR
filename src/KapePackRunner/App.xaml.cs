using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Threading;

namespace KapePackRunner;

public partial class App : Application
{
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

        base.OnStartup(e);
        var window = new MainWindow();
        MainWindow = window;
        window.Show();
    }

    private static void ReportFatal(string source, Exception ex)
    {
        var text = $"[{source}] {ex}";
        try
        {
            var dir = Path.GetDirectoryName(Environment.ProcessPath)
                      ?? Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            var path = Path.Combine(dir, "KapePack_crash.log");
            File.AppendAllText(path, DateTime.Now.ToString("s") + " " + text + Environment.NewLine + Environment.NewLine,
                Encoding.UTF8);
            MessageBox.Show(
                ex.Message + "\n\nПодробности: " + path,
                "KAPE Pack — ошибка",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        catch
        {
            try
            {
                MessageBox.Show(ex.Message, "KAPE Pack — ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
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
