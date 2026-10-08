namespace KapeIR.Ui.Dialogs;

/// <summary>
/// Shared dialog surface for Builder and Triage ViewModels — no WPF types in the contract.
/// </summary>
public interface IDialogService
{
    void ShowMessage(string message, string title, DialogIcon icon = DialogIcon.Info);
    bool Confirm(string message, string title, DialogIcon icon = DialogIcon.Question);
    string? PickFolder(string title, string? initialDirectory = null);
    string? PickOpenFile(string title, string filter, string? initialDirectory = null);
    string? PickSaveFile(string title, string filter, string defaultFileName, string? initialDirectory = null);
}
