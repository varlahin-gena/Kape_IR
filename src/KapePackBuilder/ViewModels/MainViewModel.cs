using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using KapePack.Core.Models;
using KapePack.Core.Services;
using KapePackBuilder.Services;
using KapePackBuilder.Workspaces;

namespace KapePackBuilder.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IDialogService _dialogs;
    private readonly AppSettings _settings;
    private readonly CatalogWorkspace _catalogWs;
    private readonly ToolkitUpdateWorkspace _toolkitWs = new();
    private DispatcherTimer? _targetSearchTimer;
    private DispatcherTimer? _moduleSearchTimer;
    private DispatcherTimer? _treeSearchTimer;
    private bool _suppressSelectionEvents;
    private CancellationTokenSource? _syncCts;
    private CancellationTokenSource? _buildCts;
    private DispatcherTimer? _kapeRootReloadTimer;
    private bool _suppressKapeRootReload;

    private KapeCatalog _catalog => _catalogWs.Catalog;

    [ObservableProperty] private string _kapeRoot = "";
    [ObservableProperty] private string _statusText = "Готово";
    [ObservableProperty] private bool _isSyncing;
    [ObservableProperty] private bool _isBuilding;
    [ObservableProperty] private string _targetSearch = "";
    [ObservableProperty] private string _moduleSearch = "";
    [ObservableProperty] private string _treeSearch = "";
    [ObservableProperty] private string _targetFilter = "Все";
    [ObservableProperty] private string _moduleFilter = "Все";
    [ObservableProperty] private bool _treeSharedOnly;
    [ObservableProperty] private bool _treeIsTargets = true;

    [ObservableProperty] private string _packageName = "WindowsTriage";
    [ObservableProperty] private string _packageDescription = "Пакет Windows triage";
    [ObservableProperty] private string _packageAuthor = "";
    [ObservableProperty] private string _packageVersion = "1.0";
    [ObservableProperty] private string _tsource = "C:";
    [ObservableProperty] private string _notes = "";
    [ObservableProperty] private bool _zipOutput = true;
    [ObservableProperty] private bool _vss;
    [ObservableProperty] private bool _twoPhaseCollection;
    [ObservableProperty] private string _caseId = "";
    [ObservableProperty] private bool _makeZip;
    [ObservableProperty] private bool _copyDeps = true;

    [ObservableProperty] private string _selectedTargetsText = "";
    [ObservableProperty] private string _selectedModulesText = "";
    [ObservableProperty] private string _detailText = "Выберите элемент, чтобы увидеть сведения.";
    [ObservableProperty] private CatalogRowVm? _selectedExisting;
    [ObservableProperty] private string _treeStats = "";
    [ObservableProperty] private bool _hasDocumentationLinks;

    public ObservableCollection<CatalogRowVm> TargetRows { get; } = new();
    public ObservableCollection<CatalogRowVm> ModuleRows { get; } = new();
    public ObservableCollection<CatalogRowVm> ExistingPacks { get; } = new();
    public ObservableCollection<TreeNodeVm> TreeRoots { get; } = new();
    public ObservableCollection<string> DocumentationLinks { get; } = new();
    public List<string> FilterOptions { get; } = new() { "Все", "Только выбранные", "Только compound", "Только leaf" };

    public PackageDefinition Package { get; private set; } = new();

    public MainViewModel() : this(new WpfDialogService()) {}

    public MainViewModel(IDialogService dialogs)
    {
        _dialogs = dialogs;
        _settings = AppSettings.Load();
        KapeRoot = AppSettings.ResolveDefaultKapeRoot(_settings);
        // Catalog must track the UI root only — never Environment.CurrentDirectory.
        _catalogWs = new CatalogWorkspace(string.IsNullOrWhiteSpace(KapeRoot) ? "" : KapeRoot);
    }

    public async Task InitializeAsync()
    {
        await ReloadCatalogAsync();
    }

    /// <summary>Normalize UI root and ensure <see cref="_catalog"/> is bound to it.</summary>
    internal async Task<string?> EnsureCatalogBoundToUiRootAsync()
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

        if (!KapeRootPaths.LooksLikeKapeRoot(root))
        {
            _dialogs.ShowMessage(
                $"В указанной папке нет Targets:\n{root}\n\nВсе операции Builder идут только в этот корень.",
                "Корень KAPE",
                DialogIcon.Error);
            return null;
        }

        if (!string.Equals(KapeRoot, root, StringComparison.OrdinalIgnoreCase))
        {
            _suppressKapeRootReload = true;
            try { KapeRoot = root; }
            finally { _suppressKapeRootReload = false; }
        }

        if (!_catalogWs.IsBoundTo(root))
            await ReloadCatalogAsync();

        if (!_catalogWs.IsBoundTo(root))
        {
            _dialogs.ShowMessage(
                $"Каталог не привязан к выбранному корню.\nUI: {root}\nКаталог: {_catalog.KapeRoot}",
                "Корень KAPE",
                DialogIcon.Error);
            return null;
        }

        return root;
    }

    partial void OnKapeRootChanged(string value)
    {
        if (_suppressKapeRootReload) return;
        Debounce(ref _kapeRootReloadTimer, () => _ = ReloadCatalogAsync());
    }

    partial void OnTargetSearchChanged(string value) => Debounce(ref _targetSearchTimer, RefreshTargetRows);
    partial void OnModuleSearchChanged(string value) => Debounce(ref _moduleSearchTimer, RefreshModuleRows);
    partial void OnTreeSearchChanged(string value) => Debounce(ref _treeSearchTimer, RebuildTree);
    partial void OnTargetFilterChanged(string value) => RefreshTargetRows();
    partial void OnModuleFilterChanged(string value) => RefreshModuleRows();
    partial void OnTreeSharedOnlyChanged(bool value) => RebuildTree();
    partial void OnTreeIsTargetsChanged(bool value)
    {
        OnPropertyChanged(nameof(TreeIsModules));
        RebuildTree();
    }

    /// <summary>Inverse of TreeIsTargets for the Modules radio button.</summary>
    public bool TreeIsModules
    {
        get => !TreeIsTargets;
        set
        {
            if (value)
                TreeIsTargets = false;
        }
    }

    partial void OnTwoPhaseCollectionChanged(bool value)
    {
        if (value && string.IsNullOrWhiteSpace(Package.Phase1ModuleName))
            Package.Phase1ModuleName = PackageDefinition.DefaultPhase1Module;
    }

    private void Debounce(ref DispatcherTimer? timer, Action action)
    {
        timer?.Stop();
        var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(180) };
        timer = t;
        t.Tick += (_, _) =>
        {
            t.Stop();
            action();
        };
        t.Start();
    }

    private bool CanBuildPackage() => !IsBuilding && !IsSyncing;

    private bool CanCancelBuild() => IsBuilding;

    private bool CanUpdateFromGitHub() => !IsSyncing && !IsBuilding;

    partial void OnIsBuildingChanged(bool value)
    {
        BuildPackageCommand.NotifyCanExecuteChanged();
        CancelBuildCommand.NotifyCanExecuteChanged();
        UpdateFromGitHubCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsSyncingChanged(bool value)
    {
        BuildPackageCommand.NotifyCanExecuteChanged();
        UpdateFromGitHubCommand.NotifyCanExecuteChanged();
    }
}
