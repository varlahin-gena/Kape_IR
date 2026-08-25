using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using KapePackBuilder.Models;
using KapePackBuilder.Services;

namespace KapePackBuilder.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IDialogService _dialogs;
    private readonly AppSettings _settings;
    private KapeCatalog _catalog;
    private DispatcherTimer? _targetSearchTimer;
    private DispatcherTimer? _moduleSearchTimer;
    private DispatcherTimer? _treeSearchTimer;
    private bool _suppressSelectionEvents;
    private CancellationTokenSource? _syncCts;
    private CancellationTokenSource? _buildCts;

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
    [ObservableProperty] private bool _flush;
    [ObservableProperty] private bool _vss;
    [ObservableProperty] private bool _installIntoKape;
    [ObservableProperty] private bool _makeZip;
    [ObservableProperty] private bool _copyDeps = true;
    [ObservableProperty] private bool _includeModuleBin = true;

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
        _catalog = new KapeCatalog(string.IsNullOrWhiteSpace(KapeRoot) ? Environment.CurrentDirectory : KapeRoot);
    }

    public async Task InitializeAsync()
    {
        await ReloadCatalogAsync();
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
