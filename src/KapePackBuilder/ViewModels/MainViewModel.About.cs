using System.Diagnostics;
using System.Reflection;
using CommunityToolkit.Mvvm.Input;
using KapePack.Core.Services;
using KapePackBuilder.Services;

namespace KapePackBuilder.ViewModels;

public partial class MainViewModel
{
    [RelayCommand]
    private void ShowAbout()
    {
        var ver = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "?";
        var path = Environment.ProcessPath ?? "(неизвестно)";
        var stub = StandaloneExeBuilder.TryGetEmbeddedStubInfo(out var stubSize, out _)
            ? $"Встроенный GUI-stub: да ({stubSize / (1024 * 1024)} МБ) — отдельный KapePackRunner.exe не нужен."
            : "Встроенный GUI-stub: нет (dev-сборка?). Для автономных пакетов нужен publish.ps1.";

        var logPath = AppLog.LogFilePath;
        var openLog = _dialogs.Confirm(
            $"KAPE Pack Builder {ver}\n\n" +
            $"EXE:\n{path}\n\n" +
            $"{stub}\n\n" +
            "Поставка: один файл KapePackBuilder.exe.\n" +
            "Полевые пакеты CollectPack.exe собираются из встроенного stub + payload.\n\n" +
            $"Журнал:\n{logPath}\n\nОткрыть папку логов?",
            "О программе",
            DialogIcon.Question);

        if (openLog)
            OpenAppLogFolder();
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
