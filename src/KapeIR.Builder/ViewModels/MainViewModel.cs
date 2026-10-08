using CommunityToolkit.Mvvm.ComponentModel;
using KapeIR.Core.Models;
using KapeIR.Core.Services;
using KapeIR.Builder.Services;
using KapeIR.Builder.Workspaces;
using KapeIR.Ui.Dialogs;
using KapeIR.Ui.Scheduling;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace KapeIR.Builder.ViewModels;

public partial class MainViewModel : ObservableObject, IBuilderShell, IDisposable
{
    private readonly IBuilderDialogService _dialogs;
    private readonly IPackageBuildFacade _build;
    private readonly ICatalogOpsFacade _catalogOps;
    private readonly AppSettings _settings;
    private readonly CatalogWorkspace _catalogWs;
    private readonly CatalogSelectionCoordinator _selection;
    private readonly IUiScheduler _ui;
    private readonly ILogger<MainViewModel> _logger;
    private readonly IUiDebounce _kapeRootDebounce;
    private CancellationTokenSource? _buildCts;
    private bool _suppressKapeRootReload;
    private bool _disposed;

    [ObservableProperty] private string _kapeRoot = "";
    [ObservableProperty] private string _statusText = "Готово";
    [ObservableProperty] private bool _isSyncing;
    [ObservableProperty] private bool _isBuilding;

    /// <summary>True while sync or build runs — drives compact status near the KAPE toolbar.</summary>
    public bool IsBusy => IsSyncing || IsBuilding;

    public PackageDefinition Package { get; set; } = new();

    public CatalogBrowserViewModel Catalog { get; }
    public PackageEditorViewModel PackageEditor { get; }
    public BinariesViewModel Binaries { get; }
    public SyncViewModel Sync { get; }

    CatalogWorkspace IBuilderShell.CatalogWorkspace => _catalogWs;
    CatalogSelectionCoordinator IBuilderShell.Selection => _selection;
    IBuilderDialogService IBuilderShell.Dialogs => _dialogs;
    ICatalogOpsFacade IBuilderShell.CatalogOps => _catalogOps;
    AppSettings IBuilderShell.Settings => _settings;
    IUiScheduler IBuilderShell.Ui => _ui;

    /// <summary>Test helper — dialogs only; other deps use defaults.</summary>
    public MainViewModel(IBuilderDialogService dialogs)
        : this(
            dialogs,
            new PackageBuildFacade(new PackageExporterFactory()),
            new CatalogOpsFacade(),
            new ToolkitUpdateWorkspace(),
            new CatalogSelectionCoordinator(),
            new CatalogWorkspace(),
            AppSettings.Load(),
            ImmediateUiScheduler.Instance,
            NullLogger<MainViewModel>.Instance)
    {
    }

    /// <summary>DI primary constructor.</summary>
    public MainViewModel(
        IBuilderDialogService dialogs,
        IPackageBuildFacade build,
        ICatalogOpsFacade catalogOps,
        ToolkitUpdateWorkspace toolkitWorkspace,
        CatalogSelectionCoordinator selection,
        CatalogWorkspace catalogWorkspace,
        AppSettings settings,
        IUiScheduler ui,
        ILogger<MainViewModel> logger)
    {
        _dialogs = dialogs;
        _build = build ?? throw new ArgumentNullException(nameof(build));
        _catalogOps = catalogOps ?? throw new ArgumentNullException(nameof(catalogOps));
        _selection = selection;
        _catalogWs = catalogWorkspace ?? throw new ArgumentNullException(nameof(catalogWorkspace));
        _settings = settings;
        _ui = ui ?? throw new ArgumentNullException(nameof(ui));
        _logger = logger;
        _kapeRootDebounce = _ui.CreateDebounce(IUiScheduler.DefaultDebounceDelay);

        Catalog = new CatalogBrowserViewModel(this, logger);
        PackageEditor = new PackageEditorViewModel(this);
        Binaries = new BinariesViewModel(this);
        Sync = new SyncViewModel(this, toolkitWorkspace);

        // Do not schedule catalog reload from the initial assignment — InitializeAsync owns first load.
        _suppressKapeRootReload = true;
        try
        {
            KapeRoot = AppSettings.ResolveDefaultKapeRoot(_settings);
        }
        finally
        {
            _suppressKapeRootReload = false;
        }
        // Catalog must track the UI root only — never Environment.CurrentDirectory.
        SyncCatalogWorkspaceToUiRoot();
    }

    /// <summary>Rebind catalog when the DI workspace started empty / on a different root.</summary>
    private void SyncCatalogWorkspaceToUiRoot()
    {
        if (string.IsNullOrWhiteSpace(KapeRoot))
            return;
        if (_catalogWs.IsBoundTo(KapeRoot))
            return;
        try
        {
            _catalogWs.Catalog.Rebind(KapeRootPaths.Normalize(KapeRoot));
        }
        catch
        {
            _catalogWs.Catalog.Rebind(KapeRoot);
        }
    }

    public async Task InitializeAsync()
    {
        // First launch / no known root: quiet status only — never prompt before the user can Browse.
        await Catalog.ReloadCatalogAsync(promptIfMissing: false);
    }

    public void SetKapeRootQuiet(string root)
    {
        _suppressKapeRootReload = true;
        try { KapeRoot = root; }
        finally { _suppressKapeRootReload = false; }
    }

    /// <summary>Normalize UI root and ensure catalog is bound to it.</summary>
    public async Task<string?> EnsureCatalogBoundToUiRootAsync()
    {
        var raw = (KapeRoot ?? "").Trim();
        if (string.IsNullOrEmpty(raw))
        {
            _dialogs.ShowMessage("Укажите корень KAPE в поле сверху.", "Корень KAPE", DialogIcon.Error);
            return null;
        }

        string root;
        try
        {
            root = KapeRootPaths.Normalize(raw);
        }
        catch (Exception ex)
        {
            _dialogs.ShowMessage("Некорректный путь корня KAPE:\n" + ex.Message, "Корень KAPE", DialogIcon.Error);
            return null;
        }

        if (!Directory.Exists(root) || !KapeRootPaths.LooksLikeKapeRoot(root))
        {
            _dialogs.ShowMessage(
                $"Папка не похожа на корень KAPE (нужны kape.exe и/или Targets/Modules):\n{root}\n\n" +
                "Выберите папку с kape.exe — Targets и Modules будут созданы при необходимости.",
                "Корень KAPE",
                DialogIcon.Error);
            return null;
        }

        try
        {
            KapeRootPaths.EnsureLayout(root);
        }
        catch (Exception ex)
        {
            _dialogs.ShowMessage(
                $"Не удалось создать Targets/Modules в:\n{root}\n\n{ex.Message}",
                "Корень KAPE",
                DialogIcon.Error);
            return null;
        }

        if (!string.Equals(KapeRoot, root, StringComparison.OrdinalIgnoreCase))
            SetKapeRootQuiet(root);

        if (!_catalogWs.IsBoundTo(root))
            await Catalog.ReloadCatalogAsync(promptIfMissing: false);

        if (!_catalogWs.IsBoundTo(root))
        {
            _dialogs.ShowMessage(
                $"Каталог не привязан к выбранному корню.\nUI: {root}\nКаталог: {_catalogWs.Catalog.KapeRoot}",
                "Корень KAPE",
                DialogIcon.Error);
            return null;
        }

        return root;
    }

    partial void OnKapeRootChanged(string value)
    {
        if (_suppressKapeRootReload) return;
        _kapeRootDebounce.Schedule(() => Catalog.ScheduleReloadCatalog());
    }

    public Task InvokeOnUiAsync(Action action) => _ui.InvokeAsync(action);

    private bool CanBuildPackage() => !IsBuilding && !IsSyncing;

    private bool CanCancelBuild() => IsBuilding;

    public void NotifyBusyCanExecute()
    {
        BuildPackageCommand.NotifyCanExecuteChanged();
        CancelBuildCommand.NotifyCanExecuteChanged();
        Sync.UpdateFromGitHubCommand.NotifyCanExecuteChanged();
        Catalog.ReloadCatalogCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsBuildingChanged(bool value)
    {
        OnPropertyChanged(nameof(IsBusy));
        NotifyBusyCanExecute();
    }

    partial void OnIsSyncingChanged(bool value)
    {
        OnPropertyChanged(nameof(IsBusy));
        NotifyBusyCanExecute();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _buildCts?.Cancel();
        _buildCts?.Dispose();
        _buildCts = null;
        _kapeRootDebounce.Dispose();
        Catalog.Dispose();
        Binaries.Dispose();
        Sync.Dispose();
        GC.SuppressFinalize(this);
    }
}
