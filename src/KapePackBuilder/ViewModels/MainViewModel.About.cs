using System.Diagnostics;
using System.Reflection;
using CommunityToolkit.Mvvm.Input;
using KapePack.Core.Services;
using KapePackBuilder.Services;

namespace KapePackBuilder.ViewModels;

public partial class MainViewModel
{
    public const string ProductName = "Kape_IR";

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

        _dialogs.ShowMessage($"{ProductName}\nВерсия: {ver}", "О программе", DialogIcon.Info);
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
            _dialogs.ShowMessage(ex.Message, "Журнал", DialogIcon.Warning);
        }
    }
}
