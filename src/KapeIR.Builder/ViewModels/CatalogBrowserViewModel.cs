using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KapeIR.Core.Models;
using KapeIR.Core.Services;
using KapeIR.Builder.Services;
using KapeIR.Ui.Dialogs;
using KapeIR.Ui.Scheduling;
using Microsoft.Extensions.Logging;

namespace KapeIR.Builder.ViewModels;

public sealed partial class CatalogBrowserViewModel : ObservableObject, IDisposable
{
    private readonly IBuilderShell _shell;
    private readonly ILogger _logger;
    private readonly IUiDebounce _targetSearchDebounce;
    private readonly IUiDebounce _moduleSearchDebounce;
    private readonly IUiDebounce _treeSearchDebounce;
    private readonly IUiDebounce _packSearchDebounce;
    private readonly IUiDebounce _colFilterDebounce;
    private CancellationTokenSource? _catalogReloadCts;
    private bool _disposed;

    private KapeCatalog Catalog => _shell.CatalogWorkspace.Catalog;

    [ObservableProperty] private string _targetSearch = "";
    [ObservableProperty] private string _moduleSearch = "";
    [ObservableProperty] private string _packSearch = "";
    [ObservableProperty] private string _treeSearch = "";
    [ObservableProperty] private string _targetFilter = "Все";
    [ObservableProperty] private string _moduleFilter = "Все";
    [ObservableProperty] private bool _treeSharedOnly;
    [ObservableProperty] private bool _treeIsTargets = true;
    [ObservableProperty] private string _detailText = "Выберите элемент, чтобы увидеть сведения.";
    [ObservableProperty] private string _treeStats = "";
    [ObservableProperty] private bool _hasDocumentationLinks;

    [ObservableProperty] private string _targetColFilterName = "";
    [ObservableProperty] private string _targetColFilterOrigin = "";
    [ObservableProperty] private string _targetColFilterMeta = "";
    [ObservableProperty] private string _moduleColFilterName = "";
    [ObservableProperty] private string _moduleColFilterOrigin = "";
    [ObservableProperty] private string _moduleColFilterMeta = "";
    [ObservableProperty] private string _packColFilterName = "";
    [ObservableProperty] private string _packColFilterRole = "";
    [ObservableProperty] private string _packColFilterChildren = "";
    [ObservableProperty] private string _packColFilterUsedBy = "";
    [ObservableProperty] private string _packColFilterOrigin = "";
    [ObservableProperty] private string _packColFilterDescription = "";

    private readonly ColumnSortState _targetSort = new();
    private readonly ColumnSortState _moduleSort = new();
    private readonly ColumnSortState _packSort = new();

    public ObservableCollection<CatalogRowVm> TargetRows { get; } = new();
    public ObservableCollection<CatalogRowVm> ModuleRows { get; } = new();
    public ObservableCollection<CatalogRowVm> ExistingPacks { get; } = new();
    public ObservableCollection<TreeNodeVm> TreeRoots { get; } = new();
    public ObservableCollection<string> DocumentationLinks { get; } = new();
    public List<string> FilterOptions { get; } = new() { "Все", "Только выбранные", "Только compound", "Только leaf" };

    public string TargetSortNameHeader => _targetSort.Label("Имя", "Name");
    public string TargetSortOriginHeader => _targetSort.Label("Источник", "Origin");
    public string TargetSortMetaHeader => _targetSort.Label("Категория / путь", "Meta");
    public string ModuleSortNameHeader => _moduleSort.Label("Имя", "Name");
    public string ModuleSortOriginHeader => _moduleSort.Label("Источник", "Origin");
    public string ModuleSortMetaHeader => _moduleSort.Label("Категория / путь", "Meta");
    public string PackSortNameHeader => _packSort.Label("Имя", "Name");
    public string PackSortRoleHeader => _packSort.Label("Роль", "Role");
    public string PackSortChildrenHeader => _packSort.Label("Детей", "Children");
    public string PackSortUsedByHeader => _packSort.Label("Используется в", "UsedBy");
    public string PackSortOriginHeader => _packSort.Label("Источник", "Origin");
    public string PackSortDescriptionHeader => _packSort.Label("Описание", "Description");

    internal CatalogBrowserViewModel(IBuilderShell shell, ILogger logger)
    {
        _shell = shell ?? throw new ArgumentNullException(nameof(shell));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        var delay = IUiScheduler.DefaultDebounceDelay;
        _targetSearchDebounce = _shell.Ui.CreateDebounce(delay);
        _moduleSearchDebounce = _shell.Ui.CreateDebounce(delay);
        _packSearchDebounce = _shell.Ui.CreateDebounce(delay);
        _treeSearchDebounce = _shell.Ui.CreateDebounce(delay);
        _colFilterDebounce = _shell.Ui.CreateDebounce(delay);
    }

    partial void OnTargetSearchChanged(string value) => _targetSearchDebounce.Schedule(RefreshTargetRows);
    partial void OnModuleSearchChanged(string value) => _moduleSearchDebounce.Schedule(RefreshModuleRows);
    partial void OnPackSearchChanged(string value) => _packSearchDebounce.Schedule(RefreshExisting);
    partial void OnTreeSearchChanged(string value) => _treeSearchDebounce.Schedule(RebuildTree);
    partial void OnTargetFilterChanged(string value) => RefreshTargetRows();
    partial void OnModuleFilterChanged(string value) => RefreshModuleRows();
    partial void OnTargetColFilterNameChanged(string value) => _colFilterDebounce.Schedule(RefreshTargetRows);
    partial void OnTargetColFilterOriginChanged(string value) => _colFilterDebounce.Schedule(RefreshTargetRows);
    partial void OnTargetColFilterMetaChanged(string value) => _colFilterDebounce.Schedule(RefreshTargetRows);
    partial void OnModuleColFilterNameChanged(string value) => _colFilterDebounce.Schedule(RefreshModuleRows);
    partial void OnModuleColFilterOriginChanged(string value) => _colFilterDebounce.Schedule(RefreshModuleRows);
    partial void OnModuleColFilterMetaChanged(string value) => _colFilterDebounce.Schedule(RefreshModuleRows);
    partial void OnPackColFilterNameChanged(string value) => _colFilterDebounce.Schedule(RefreshExisting);
    partial void OnPackColFilterRoleChanged(string value) => _colFilterDebounce.Schedule(RefreshExisting);
    partial void OnPackColFilterChildrenChanged(string value) => _colFilterDebounce.Schedule(RefreshExisting);
    partial void OnPackColFilterUsedByChanged(string value) => _colFilterDebounce.Schedule(RefreshExisting);
    partial void OnPackColFilterOriginChanged(string value) => _colFilterDebounce.Schedule(RefreshExisting);
    partial void OnPackColFilterDescriptionChanged(string value) => _colFilterDebounce.Schedule(RefreshExisting);
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

    private bool CanReloadCatalog() => !_shell.IsBuilding && !_shell.IsSyncing;

    [RelayCommand]
    private void BrowseKapeRoot()
    {
        var folder = _shell.Dialogs.PickFolder(
            "Выберите корень KAPE",
            !string.IsNullOrWhiteSpace(_shell.KapeRoot) && Directory.Exists(_shell.KapeRoot) ? _shell.KapeRoot : null);
        if (folder is null) return;
        try { _shell.KapeRoot = KapeRootPaths.Normalize(folder); }
        catch { _shell.KapeRoot = folder; }
        ScheduleReloadCatalog();
    }

    [RelayCommand(CanExecute = nameof(CanReloadCatalog))]
    private Task ReloadCatalogAsync() => ReloadCatalogAsync(promptIfMissing: true);

    /// <param name="promptIfMissing">
    /// When false (startup), missing/unknown root only updates StatusText — no dialogs.
    /// When true (Browse / «Перечитать»), offer to pick a folder.
    /// </param>
    internal async Task ReloadCatalogAsync(bool promptIfMissing)
    {
        if (_shell.IsBuilding || _shell.IsSyncing)
        {
            _shell.StatusText = _shell.IsBuilding
                ? "Каталог не перезагружается во время сборки"
                : "Каталог не перезагружается во время обновления";
            return;
        }

        _shell.KapeRoot = (_shell.KapeRoot ?? "").Trim();
        if (string.IsNullOrEmpty(_shell.KapeRoot))
        {
            _shell.StatusText = "Укажите корень KAPE (кнопка «Обзор…»).";
            if (promptIfMissing &&
                _shell.Dialogs.Confirm("Корень KAPE не задан.\n\nВыбрать папку?", "Корень KAPE", DialogIcon.Warning))
            {
                BrowseKapeRoot();
            }
            return;
        }

        string root;
        try { root = KapeRootPaths.Normalize(_shell.KapeRoot); }
        catch
        {
            _shell.StatusText = "Некорректный путь корня KAPE.";
            if (promptIfMissing)
                _shell.Dialogs.ShowMessage("Некорректный путь корня KAPE.", "Корень KAPE", DialogIcon.Error);
            return;
        }

        if (!Directory.Exists(root) || !KapeRootPaths.LooksLikeKapeRoot(root))
        {
            _shell.StatusText = "Укажите корень KAPE (папка с kape.exe).";
            if (promptIfMissing &&
                _shell.Dialogs.Confirm(
                    $"Папка не похожа на корень KAPE (нет kape.exe / Targets / Modules):\n{root}\n\n" +
                    "Выбрать другую папку?",
                    "Корень KAPE",
                    DialogIcon.Warning))
            {
                BrowseKapeRoot();
            }
            return;
        }

        try
        {
            KapeRootPaths.EnsureLayout(root);
        }
        catch (Exception ex)
        {
            _shell.StatusText = "Не удалось создать Targets/Modules.";
            if (promptIfMissing)
            {
                _shell.Dialogs.ShowMessage(
                    $"Не удалось создать Targets/Modules в:\n{root}\n\n{ex.Message}",
                    "Корень KAPE",
                    DialogIcon.Error);
            }
            return;
        }

        if (!string.Equals(_shell.KapeRoot, root, StringComparison.OrdinalIgnoreCase))
            _shell.SetKapeRootQuiet(root);

        _catalogReloadCts?.Cancel();
        _catalogReloadCts?.Dispose();
        _catalogReloadCts = new CancellationTokenSource();
        var ct = _catalogReloadCts.Token;

        _shell.StatusText = "Сканирование каталога…";
        try
        {
            PackageAssemblyStore.MigrateSessionsIfNeeded(root);
            var stats = await Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();
                return _shell.CatalogWorkspace.Reload(root);
            }, ct);

            if (ct.IsCancellationRequested)
                return;

            await _shell.InvokeOnUiAsync(() =>
            {
                if (ct.IsCancellationRequested) return;
                _shell.Settings.LastKapeRoot = root;
                _shell.Settings.Save();
                OnCatalogLoaded(stats);
            });
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer reload or Dispose.
        }
    }

    /// <summary>Fire-and-forget reload with exception logging (UI triggers).</summary>
    internal void ScheduleReloadCatalog()
    {
        _ = ReloadCatalogSafeAsync();
    }

    internal async Task ReloadCatalogSafeAsync()
    {
        try
        {
            // Quiet: debounced KapeRoot changes / auto-reload must not spawn dialogs.
            // Explicit ReloadCatalogCommand still uses promptIfMissing: true.
            await ReloadCatalogAsync(promptIfMissing: false);
        }
        catch (Exception ex)
        {
            LogCatalogReloadFailed(ex);
            AppLog.Error(ex, "Catalog reload failed");
            _shell.StatusText = "Ошибка загрузки каталога";
        }
    }

    [LoggerMessage(EventId = 1001, Level = LogLevel.Error, Message = "Catalog reload failed")]
    private partial void LogCatalogReloadFailed(Exception ex);

    internal void CancelPendingReload()
    {
        _catalogReloadCts?.Cancel();
    }

    private void OnCatalogLoaded(CatalogRefreshStats? stats = null)
    {
        var last = _shell.CatalogOps.ReadLastSync(_shell.KapeRoot);
        var syncNote = last is not null && last.TryGetValue("synced_at", out var at) ? $" | синхронизация GitHub: {at}" : "";
        var localN = Catalog.Targets.Concat(Catalog.Modules).Count(i => i.Origin == CatalogOrigin.Local);
        var ghN = Catalog.Targets.Concat(Catalog.Modules).Count(i => i.Origin == CatalogOrigin.GitHub);
        var unkN = Catalog.Targets.Concat(Catalog.Modules).Count(i => i.Origin == CatalogOrigin.Unknown);
        var originNote = Catalog.HasUpstreamInventory
            ? $" | источник: GitHub {ghN}, локальных {localN}"
            : unkN > 0
                ? $" | источник: локальных (по Author) {localN}, ? {unkN} — нажмите «Обновить с GitHub…» для меток GitHub"
                : $" | источник: локальных {localN}";
        var tDup = _shell.CatalogOps.FindCollisions(Catalog.Targets);
        var mDup = _shell.CatalogOps.FindCollisions(Catalog.Modules);
        var dupNote = "";
        if (tDup.Count + mDup.Count > 0)
        {
            dupNote = $" | ⚠ дубликаты имён: Targets {tDup.Count}, Modules {mDup.Count} — KAPE упадёт при валидации. Нажмите «Убрать дубликаты».";
        }
        var cacheNote = stats is { FilesSeen: > 0 }
            ? $" | кеш файлов: {stats.CacheHits}/{stats.FilesSeen}"
            : "";
        _shell.StatusText = $"Загружено: {Catalog.Targets.Count} таргетов, {Catalog.Modules.Count} модулей{syncNote}{originNote}{dupNote}{cacheNote}";
        RefreshTargetRows();
        RefreshModuleRows();
        RefreshExisting();
        RebuildTree();
        _shell.PackageEditor.SyncSelectionTexts();
        _ = _shell.Binaries.LoadBinariesAsync(updateStatus: false);
    }

    [RelayCommand]
    private async Task FixNameCollisionsAsync()
    {
        var root = await _shell.EnsureCatalogBoundToUiRootAsync();
        if (root is null) return;

        var tDup = _shell.CatalogOps.FindCollisions(Catalog.Targets);
        var mDup = _shell.CatalogOps.FindCollisions(Catalog.Modules);
        if (tDup.Count + mDup.Count == 0)
        {
            _shell.Dialogs.ShowMessage("Дубликатов имён не найдено.", "Дубликаты");
            return;
        }

        var preview = string.Join("\n",
            tDup.Take(8).Select(g =>
                $"• {g.FileName}: оставить {g.Items[0].RelativePath}; убрать " +
                string.Join(", ", g.Items.Skip(1).Select(i => i.RelativePath)))
            .Concat(mDup.Take(4).Select(g =>
                $"• {g.FileName}: оставить {g.Items[0].RelativePath}; убрать " +
                string.Join(", ", g.Items.Skip(1).Select(i => i.RelativePath)))));
        var more = tDup.Count + mDup.Count > 12 ? "\n…" : "";
        if (!_shell.Dialogs.Confirm(
                "KAPE требует уникальные имена .tkape/.mkape во всём дереве.\n" +
                $"Найдено групп: Targets {tDup.Count}, Modules {mDup.Count}.\n\n" +
                $"{preview}{more}\n\n" +
                "Лишние файлы будут перенесены в PackBuilder\\name_collisions\\ (не удаляются навсегда).\nПродолжить?",
                "Убрать дубликаты имён",
                DialogIcon.Warning))
            return;

        _shell.StatusText = "Устранение дубликатов имён…";
        var (tFix, mFix) = await _shell.CatalogOps.FixCollisionsAsync(
            root, Catalog.Targets.ToList(), Catalog.Modules.ToList());

        var msg =
            $"Targets: убрано {tFix.Removed} из {tFix.Groups} групп.\n" +
            $"Modules: убрано {mFix.Removed} из {mFix.Groups} групп.\n" +
            $"Карантин: {tFix.QuarantineDir}";
        if (tFix.Errors.Count + mFix.Errors.Count > 0)
            msg += "\n\nОшибки:\n" + string.Join("\n", tFix.Errors.Concat(mFix.Errors).Take(10));
        _shell.Dialogs.ShowMessage(msg, "Дубликаты",
            tFix.Errors.Count + mFix.Errors.Count > 0 ? DialogIcon.Warning : DialogIcon.Info);

        await ReloadCatalogAsync();
    }

    public void RefreshTargetRows()
    {
        using (_shell.Selection.SuppressEvents())
        {
            CatalogUiHelpers.FillObservable(
                TargetRows,
                CatalogUiHelpers.BuildFilteredRows(
                    Catalog, ItemKind.Target, TargetSearch, TargetFilter, _shell.Package.Targets,
                    TargetColFilterName, TargetColFilterOrigin, TargetColFilterMeta, _targetSort));
        }
    }

    public void RefreshModuleRows()
    {
        using (_shell.Selection.SuppressEvents())
        {
            CatalogUiHelpers.FillObservable(
                ModuleRows,
                CatalogUiHelpers.BuildFilteredRows(
                    Catalog, ItemKind.Module, ModuleSearch, ModuleFilter, _shell.Package.Modules,
                    ModuleColFilterName, ModuleColFilterOrigin, ModuleColFilterMeta, _moduleSort));
        }
    }

    private void RefreshExisting()
    {
        CatalogUiHelpers.FillObservable(
            ExistingPacks,
            CatalogUiHelpers.BuildExistingPackRows(
                Catalog,
                PackSearch,
                PackColFilterName,
                PackColFilterRole,
                PackColFilterChildren,
                PackColFilterUsedBy,
                PackColFilterOrigin,
                PackColFilterDescription,
                _packSort));
    }

    [RelayCommand]
    private void SortTargets(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return;
        _targetSort.Toggle(key);
        NotifyTargetSortHeaders();
        RefreshTargetRows();
    }

    [RelayCommand]
    private void SortModules(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return;
        _moduleSort.Toggle(key);
        NotifyModuleSortHeaders();
        RefreshModuleRows();
    }

    [RelayCommand]
    private void SortPacks(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return;
        _packSort.Toggle(key);
        NotifyPackSortHeaders();
        RefreshExisting();
    }

    private void NotifyTargetSortHeaders()
    {
        OnPropertyChanged(nameof(TargetSortNameHeader));
        OnPropertyChanged(nameof(TargetSortOriginHeader));
        OnPropertyChanged(nameof(TargetSortMetaHeader));
    }

    private void NotifyModuleSortHeaders()
    {
        OnPropertyChanged(nameof(ModuleSortNameHeader));
        OnPropertyChanged(nameof(ModuleSortOriginHeader));
        OnPropertyChanged(nameof(ModuleSortMetaHeader));
    }

    private void NotifyPackSortHeaders()
    {
        OnPropertyChanged(nameof(PackSortNameHeader));
        OnPropertyChanged(nameof(PackSortRoleHeader));
        OnPropertyChanged(nameof(PackSortChildrenHeader));
        OnPropertyChanged(nameof(PackSortUsedByHeader));
        OnPropertyChanged(nameof(PackSortOriginHeader));
        OnPropertyChanged(nameof(PackSortDescriptionHeader));
    }

    public void RebuildTree()
    {
        var expandedPaths = CaptureExpandedPaths();
        var kind = TreeIsTargets ? ItemKind.Target : ItemKind.Module;
        var keys = KapeCatalog.BuildSelectionKeys(kind == ItemKind.Target ? _shell.Package.Targets : _shell.Package.Modules);
        var roots = CatalogUiHelpers.BuildTreeRoots(
            Catalog,
            kind,
            kind == ItemKind.Target ? _shell.Package.Targets : _shell.Package.Modules,
            TreeSearch,
            TreeSharedOnly,
            BuildTreeNode);
        CatalogUiHelpers.FillObservable(TreeRoots, roots);
        RestoreExpandedPaths(expandedPaths);
        UpdateTreeStats(kind, keys);
    }

    private HashSet<string> CaptureExpandedPaths()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Walk(IEnumerable<TreeNodeVm> nodes)
        {
            foreach (var n in nodes)
            {
                if (n.IsExpanded)
                    set.Add(n.Item.AbsolutePath);
                if (n.Children.Count > 0)
                    Walk(n.Children);
            }
        }
        Walk(TreeRoots);
        return set;
    }

    private void RestoreExpandedPaths(HashSet<string> expandedPaths)
    {
        if (expandedPaths.Count == 0) return;
        void Walk(IEnumerable<TreeNodeVm> nodes)
        {
            foreach (var n in nodes)
            {
                if (expandedPaths.Contains(n.Item.AbsolutePath))
                    n.IsExpanded = true;
                if (n.Children.Count > 0)
                    Walk(n.Children);
            }
        }
        Walk(TreeRoots);
    }

    private TreeNodeVm? BuildTreeNode(
        CatalogItem item,
        ItemKind kind,
        HashSet<string> keys,
        string query,
        bool sharedOnly,
        int depth,
        HashSet<string> stack)
    {
        if (depth > 12 || !stack.Add(item.AbsolutePath)) return null;
        var packs = Catalog.IncludingCompounds(item.Name, kind);
        var isShared = packs.Count > 1;
        var matches = string.IsNullOrEmpty(query) || item.SearchBlob.Contains(query, StringComparison.Ordinal) ||
                      packs.Any(p => p.Contains(query, StringComparison.OrdinalIgnoreCase));

        if (sharedOnly && !item.IsCompound && !isShared) return null;
        if (!string.IsNullOrEmpty(query) && !matches && !item.IsCompound) return null;

        var selected = _shell.Selection.IsItemEffectivelySelected(Catalog, item, kind, keys);

        // Always start collapsed; RebuildTree restores previously expanded paths.
        var node = new TreeNodeVm(item, Catalog.SharedBadge(item), selected, expanded: false);
        if (item.IsCompound)
        {
            var visible = 0;
            foreach (var childRef in item.Children)
            {
                var child = kind == ItemKind.Target ? Catalog.FindTarget(childRef) : Catalog.FindModule(childRef);
                if (child is null) continue;
                var childNode = BuildTreeNode(child, kind, keys, query, sharedOnly, depth + 1, new HashSet<string>(stack, StringComparer.OrdinalIgnoreCase));
                if (childNode is not null)
                {
                    node.Children.Add(childNode);
                    visible++;
                }
            }
            if (!string.IsNullOrEmpty(query) && !matches && visible == 0) return null;
            if (sharedOnly && !matches && visible == 0 && !isShared) return null;
        }
        return node;
    }

    private void UpdateTreeStats(ItemKind kind, HashSet<string> keys)
    {
        var count = kind == ItemKind.Target ? _shell.Package.Targets.Count : _shell.Package.Modules.Count;
        TreeStats = $"В сборке {(kind == ItemKind.Target ? "таргетов" : "модулей")}: {count}";
    }

    public void ToggleCatalogRow(CatalogRowVm row, ItemKind kind)
    {
        if (_shell.Selection.IsSuppressed) return;
        // Row.IsSelected already reflects desired state from checkbox binding.
        _shell.StatusText = _shell.Selection.SetItemSelected(Catalog, _shell.Package, row.Item, kind, row.IsSelected);
        SyncAllViews();
    }

    /// <summary>Row click (not checkbox): flip inclusion without double-firing Checked handlers.</summary>
    public void ToggleRowFromListClick(CatalogRowVm row, ItemKind kind)
    {
        if (_shell.Selection.IsSuppressed) return;
        using (_shell.Selection.SuppressEvents())
            row.IsSelected = !row.IsSelected;
        _shell.StatusText = _shell.Selection.SetItemSelected(Catalog, _shell.Package, row.Item, kind, row.IsSelected);
        SyncAllViews();
    }

    public void ToggleTreeNode(TreeNodeVm node)
    {
        if (_shell.Selection.IsSuppressed) return;
        var kind = TreeIsTargets ? ItemKind.Target : ItemKind.Module;
        _shell.StatusText = _shell.Selection.SetItemSelected(Catalog, _shell.Package, node.Item, kind, node.IsSelected);
        SyncAllViews();
    }

    internal void SyncAllViews()
    {
        using (_shell.Selection.SuppressEvents())
        {
            _shell.PackageEditor.SyncSelectionTexts();
            SyncVisibleRowChecks(ItemKind.Target);
            SyncVisibleRowChecks(ItemKind.Module);
            RebuildTree();
        }
    }

    /// <summary>Update checkmarks in place — avoid Clear()+rebuild (scroll/selection jump, flaky UI).</summary>
    private void SyncVisibleRowChecks(ItemKind kind)
    {
        var rows = kind == ItemKind.Target ? TargetRows : ModuleRows;
        var keys = KapeCatalog.BuildSelectionKeys(kind == ItemKind.Target ? _shell.Package.Targets : _shell.Package.Modules);
        foreach (var row in rows)
        {
            var should = _shell.Selection.IsItemEffectivelySelected(Catalog, row.Item, kind, keys);
            if (row.IsSelected != should)
                row.IsSelected = should;
        }
    }

    [RelayCommand]
    private void CheckVisibleTargets()
    {
        _shell.Selection.MergeEntries(_shell.Package, ItemKind.Target, TargetRows.Select(r => _shell.Selection.ToSelectionEntry(r.Item)));
        SyncAllViews();
    }

    [RelayCommand]
    private void CheckVisibleModules()
    {
        _shell.Selection.MergeEntries(_shell.Package, ItemKind.Module, ModuleRows.Select(r => _shell.Selection.ToSelectionEntry(r.Item)));
        SyncAllViews();
    }

    [RelayCommand]
    private void ClearTargets()
    {
        _shell.Selection.Clear(_shell.Package, ItemKind.Target);
        SyncAllViews();
    }

    [RelayCommand]
    private void ClearModules()
    {
        _shell.Selection.Clear(_shell.Package, ItemKind.Module);
        SyncAllViews();
    }

    [RelayCommand]
    private void SuggestModules()
    {
        if (_shell.Package.Targets.Count == 0)
        {
            _shell.Dialogs.ShowMessage("Сначала выберите хотя бы один таргет.", "Подсказки модулей");
            return;
        }

        var advisor = new ModuleAdvisor(Catalog);
        var suggestions = advisor.Suggest(_shell.Package.Targets, _shell.Package.Modules);
        if (suggestions.Count == 0)
        {
            _shell.Dialogs.ShowMessage(
                "Нет подходящих модулей для текущих таргетов.\n" +
                "Попробуйте leaf-таргеты с FileMask (Prefetch, Amcache, $MFT, EventLogs, …).",
                "Подсказки модулей");
            return;
        }

        var chosen = _shell.Dialogs.PickModuleSuggestions(suggestions);
        if (chosen is null || chosen.Count == 0)
            return;

        _shell.Selection.MergeEntries(_shell.Package, ItemKind.Module, chosen.Select(_shell.Selection.ToSelectionEntry));
        ModuleFilter = "Только выбранные";
        SyncAllViews();
        _shell.StatusText = $"Добавлено модулей из подсказок: {chosen.Count} (в сборке {_shell.Package.Modules.Count})";
    }

    [RelayCommand]
    private void ExpandTree() => SetExpanded(TreeRoots, true);

    [RelayCommand]
    private void CollapseTree() => SetExpanded(TreeRoots, false);

    private static void SetExpanded(IEnumerable<TreeNodeVm> nodes, bool expanded)
    {
        foreach (var n in nodes)
        {
            n.IsExpanded = expanded;
            SetExpanded(n.Children, expanded);
        }
    }

    [RelayCommand]
    private void LoadSelectedCompound()
    {
        if (_shell.PackageEditor.SelectedExisting is null)
        {
            _shell.Dialogs.ShowMessage("Сначала выберите готовую сборку.", "Загрузка");
            return;
        }
        var selected = _shell.PackageEditor.SelectedExisting;
        var loaded = _shell.CatalogOps.LoadPackageFromCompound(
            selected.Item.AbsolutePath,
            Catalog);
        _shell.Package = loaded;
        _shell.PackageEditor.PushPackageToForm();
        TargetFilter = "Только выбранные";
        if (_shell.Package.Modules.Count > 0) ModuleFilter = "Только выбранные";
        SyncAllViews();
        var origin = selected.Item.OriginLabel;
        _shell.StatusText =
            $"Загружен {selected.Item.Name} ({origin}): " +
            $"{_shell.Package.Targets.Count} таргетов, {_shell.Package.Modules.Count} модулей";
    }

    [RelayCommand]
    private void MergeTreeIntoPackage()
    {
        ApplyCheckedTree(replace: false);
    }

    [RelayCommand]
    private void ReplacePackageFromTree()
    {
        ApplyCheckedTree(replace: true);
    }

    private void ApplyCheckedTree(bool replace)
    {
        var kind = TreeIsTargets ? ItemKind.Target : ItemKind.Module;
        var refs = CollectCheckedRefs(TreeRoots).ToList();
        if (refs.Count == 0)
        {
            _shell.Dialogs.ShowMessage("В дереве ничего не отмечено.", "Дерево");
            return;
        }
        var incoming = Catalog.SelectionFromRefs(refs, kind, flatten: true);
        if (replace)
            _shell.Selection.ReplaceEntries(_shell.Package, kind, incoming);
        else
            _shell.Selection.MergeEntries(_shell.Package, kind, incoming);
        var stats = Catalog.OverlapStats(refs, kind);
        _shell.StatusText = $"{(replace ? "Заменено" : "Добавлено")}: {refs.Count} ссылок → {stats.UniqueLeaves} уникальных leaf";
        SyncAllViews();
    }

    private static IEnumerable<string> CollectCheckedRefs(IEnumerable<TreeNodeVm> nodes)
    {
        foreach (var n in nodes)
        {
            if (n.IsSelected)
                yield return Path.GetFileName(n.Item.RelativePath);
            foreach (var c in CollectCheckedRefs(n.Children))
                yield return c;
        }
    }

    public void ShowItemInfo(CatalogItem item)
    {
        var text =
            $"{item.Name}\nПуть: {item.RelativePath}\nКатегория: {item.Category}\n" +
            $"Источник: {item.OriginLabel} ({item.OriginTag})\n" +
            $"Автор: {item.Author} | Версия: {item.Version}\nCompound: {item.IsCompound}\n" +
            $"Описание: {item.Description}\n";
        if (item.FileMasks.Count > 0)
            text += "FileMask: " + string.Join(", ", item.FileMasks.Take(12)) + "\n";
        if (item.Children.Count > 0)
            text += "Дочерние: " + string.Join(", ", item.Children.Take(30)) + "\n";
        if (item.DocumentationUrls.Count > 0)
            text += $"Документация ({item.DocumentationUrls.Count}): см. ссылки ниже\n";
        else
            text += "Документация: нет ссылок в файле\n";
        if (item.Origin == CatalogOrigin.Unknown)
            text += "Подсказка: «Обновить с GitHub…» запишет список путей KapeFiles — метки GitHub/локальный станут точными.\n";
        DetailText = text;

        DocumentationLinks.Clear();
        foreach (var url in item.DocumentationUrls)
            DocumentationLinks.Add(url);
        HasDocumentationLinks = DocumentationLinks.Count > 0;
    }

    [RelayCommand]
    private void OpenDocumentationLink(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            _shell.Dialogs.ShowMessage(
                "Разрешены только ссылки http/https.\nОтклонено: " + url,
                "Небезопасная ссылка",
                DialogIcon.Warning);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _shell.Dialogs.ShowMessage(ex.Message, "Не удалось открыть ссылку", DialogIcon.Warning);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _catalogReloadCts?.Cancel();
        _catalogReloadCts?.Dispose();
        _catalogReloadCts = null;
        _targetSearchDebounce.Dispose();
        _moduleSearchDebounce.Dispose();
        _packSearchDebounce.Dispose();
        _treeSearchDebounce.Dispose();
        _colFilterDebounce.Dispose();
        GC.SuppressFinalize(this);
    }
}
