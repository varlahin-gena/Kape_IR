using KapePack.Core.Models;
using KapePackBuilder.Services;

namespace KapePackBuilder.Tests;

internal sealed class FakeDialogService : IDialogService
{
    public List<(string Message, string Title, DialogIcon Icon)> Messages { get; } = new();
    public List<(string Message, string Title, DialogIcon Icon)> Confirms { get; } = new();
    public Queue<bool> ConfirmResults { get; } = new();
    public Queue<string?> FolderResults { get; } = new();
    public Queue<string?> OpenFileResults { get; } = new();
    public Queue<IReadOnlyList<CatalogItem>?> SuggestionResults { get; } = new();

    public void ShowMessage(string message, string title, DialogIcon icon = DialogIcon.Info)
        => Messages.Add((message, title, icon));

    public bool Confirm(string message, string title, DialogIcon icon = DialogIcon.Question)
    {
        Confirms.Add((message, title, icon));
        return ConfirmResults.Count > 0 && ConfirmResults.Dequeue();
    }

    public string? PickFolder(string title, string? initialDirectory = null)
        => FolderResults.Count > 0 ? FolderResults.Dequeue() : null;

    public string? PickOpenFile(string title, string filter, string? initialDirectory = null)
        => OpenFileResults.Count > 0 ? OpenFileResults.Dequeue() : null;

    public IReadOnlyList<CatalogItem>? PickModuleSuggestions(IReadOnlyList<ModuleSuggestion> suggestions)
        => SuggestionResults.Count > 0 ? SuggestionResults.Dequeue() : null;
}
