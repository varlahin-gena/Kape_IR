using System.Windows;
using KapeIR.Core.Models;
using KapeIR.Ui.Dialogs;

namespace KapeIR.Builder.Services;

/// <summary>Builder WPF dialogs — extends shared <see cref="WpfDialogService"/>.</summary>
public sealed class WpfBuilderDialogService : WpfDialogService, IBuilderDialogService
{
    public IReadOnlyList<CatalogItem>? PickModuleSuggestions(IReadOnlyList<ModuleSuggestion> suggestions)
    {
        var dlg = new SuggestModulesWindow(suggestions) { Owner = Application.Current.MainWindow };
        if (dlg.ShowDialog() != true || !dlg.Applied || dlg.Chosen.Count == 0)
            return null;
        return dlg.Chosen;
    }

    public void ShowOperatorHelp()
    {
        var dlg = new HelpWindow { Owner = Application.Current.MainWindow };
        dlg.ShowDialog();
    }
}
