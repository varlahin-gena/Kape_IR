using KapeIR.Ui.Dialogs;

namespace KapeIR.Builder.Tests;

public sealed class FakeTriageDialogService : IDialogService
{
    public List<string> Messages { get; } = new();
    public bool NextConfirm { get; set; } = true;
    public string? NextFolder { get; set; }
    public string? NextSavePath { get; set; }

    public void ShowMessage(string message, string title, DialogIcon icon = DialogIcon.Info)
        => Messages.Add($"{icon}:{title}:{message}");

    public bool Confirm(string message, string title, DialogIcon icon = DialogIcon.Question)
    {
        Messages.Add($"confirm:{title}:{message}");
        return NextConfirm;
    }

    public string? PickFolder(string title, string? initialDirectory = null) => NextFolder;

    public string? PickOpenFile(string title, string filter, string? initialDirectory = null) => null;

    public string? PickSaveFile(string title, string filter, string defaultFileName, string? initialDirectory = null)
        => NextSavePath;
}
