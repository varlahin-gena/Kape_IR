using System.Windows;
using KapeIR.Core.Models;

namespace KapeIR.Builder.Services;

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
    /// <summary>Returns chosen catalog modules from suggestions, or null if cancelled.</summary>
    IReadOnlyList<CatalogItem>? PickModuleSuggestions(IReadOnlyList<ModuleSuggestion> suggestions);
}
