using KapeIR.Core.Models;
using KapeIR.Ui.Dialogs;

namespace KapeIR.Builder.Services;

/// <summary>Builder dialogs: shared <see cref="IDialogService"/> plus module-suggestion picker.</summary>
public interface IBuilderDialogService : IDialogService
{
    /// <summary>Returns chosen catalog modules from suggestions, or null if cancelled.</summary>
    IReadOnlyList<CatalogItem>? PickModuleSuggestions(IReadOnlyList<ModuleSuggestion> suggestions);

    /// <summary>Operator help window (Markdown sections + search). Builder only.</summary>
    void ShowOperatorHelp();
}
