using KapeIR.Core.Models;
using KapeIR.Core.Services;
using KapeIR.Builder.Services;
using KapeIR.Builder.Workspaces;
using KapeIR.Ui.Scheduling;

namespace KapeIR.Builder.ViewModels;

/// <summary>Shared host surface for Builder child ViewModels.</summary>
internal interface IBuilderShell
{
    string KapeRoot { get; set; }
    string StatusText { get; set; }
    bool IsBuilding { get; set; }
    bool IsSyncing { get; set; }
    bool IsBusy { get; }

    /// <summary>Assign KapeRoot without scheduling a catalog reload (normalize-in-place).</summary>
    void SetKapeRootQuiet(string root);

    PackageDefinition Package { get; set; }

    CatalogWorkspace CatalogWorkspace { get; }
    CatalogSelectionCoordinator Selection { get; }
    IBuilderDialogService Dialogs { get; }
    ICatalogOpsFacade CatalogOps { get; }
    AppSettings Settings { get; }
    IUiScheduler Ui { get; }

    Task<string?> EnsureCatalogBoundToUiRootAsync();
    Task InvokeOnUiAsync(Action action);
    void NotifyBusyCanExecute();

    CatalogBrowserViewModel Catalog { get; }
    PackageEditorViewModel PackageEditor { get; }
    BinariesViewModel Binaries { get; }
    SyncViewModel Sync { get; }
}
