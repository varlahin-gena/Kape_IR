using System.Windows;

namespace KapePackBuilder.Services;

public enum DialogIcon
{
    None,
    Info,
    Warning,
    Error,
    Question
}

public interface IDialogService
{
    void ShowMessage(string message, string title, DialogIcon icon = DialogIcon.Info);
    bool Confirm(string message, string title, DialogIcon icon = DialogIcon.Question);
    string? PickFolder(string title, string? initialDirectory = null);
    string? PickOpenFile(string title, string filter, string? initialDirectory = null);
}
