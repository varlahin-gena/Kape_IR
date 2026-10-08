using System.Diagnostics;
using System.Reflection;
using CommunityToolkit.Mvvm.Input;
using KapeIR.Core.Services;
using KapeIR.Builder.Services;
using KapeIR.Ui.Dialogs;

namespace KapeIR.Builder.ViewModels;

public partial class MainViewModel
{
    public const string ProductName = ProductIdentity.Builder;

    [RelayCommand]
    private void ShowOperatorHelp() => _dialogs.ShowOperatorHelp();

    [RelayCommand]
    private void ShowAbout()
    {
        var asm = Assembly.GetExecutingAssembly();
        var ver = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                  ?? asm.GetName().Version?.ToString()
                  ?? "?";
        // Strip any "+git" / build metadata for a clean About line.
        var plus = ver.IndexOf('+');
        if (plus >= 0)
            ver = ver[..plus];

        var logPath = AppLog.LogFilePath;
        _dialogs.ShowMessage(
            $"{ProductName}\nВерсия: {ver}\n\nЖурнал:\n{logPath}",
            "О программе",
            DialogIcon.Info);
    }

    [RelayCommand]
    private void OpenAppLogFolder()
    {
        try
        {
            Directory.CreateDirectory(AppLog.LogDirectory);
            Process.Start(new ProcessStartInfo
            {
                FileName = AppLog.LogDirectory,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            _dialogs.ShowMessage(
                ex.Message + "\n\n" + AppLog.LogFilePath,
                "Журнал",
                DialogIcon.Warning);
        }
    }
}
