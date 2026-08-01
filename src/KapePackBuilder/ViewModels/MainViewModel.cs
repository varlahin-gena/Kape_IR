using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Data;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KapePackBuilder.Models;
using KapePackBuilder.Services;
using Microsoft.Win32;
using System.IO;

namespace KapePackBuilder.ViewModels;

public partial class CatalogRowVm : ObservableObject
{
    public CatalogItem Item { get; }
    [ObservableProperty] private bool _isSelected;
    public string DisplayName => Item.DisplayName;
    public string Meta => $"{Item.Category}  ·  {Item.RelativePath}";
    public CatalogRowVm(CatalogItem item, bool selected)
    {
        Item = item;
        _isSelected = selected;
    }
}

public partial class TreeNodeVm : ObservableObject
{
    public CatalogItem Item { get; }
    public string Badge { get; }
    public ObservableCollection<TreeNodeVm> Children { get; } = new();
    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private bool _isExpanded;
    public string DisplayName => Item.IsCompound
        ? $"{Item.DisplayName}  ({Item.Children.Count})"
        : Item.DisplayName;

    public TreeNodeVm(CatalogItem item, string badge, bool selected, bool expanded = false)
    {
        Item = item;
        Badge = badge;
        _isSelected = selected;
        _isExpanded = expanded;
    }
}

public partial class MainViewModel : ObservableObject
{
    private readonly AppSettings _settings;
    private KapeCatalog _catalog;
    private DispatcherTimer? _targetSearchTimer;
    private DispatcherTimer? _moduleSearchTimer;
    private DispatcherTimer? _treeSearchTimer;
    private bool _suppressSelectionEvents;

    [ObservableProperty] private string _kapeRoot = "";
    [ObservableProperty] private string _statusText = "Ready";
    [ObservableProperty] private string _targetSearch = "";
    [ObservableProperty] private string _moduleSearch = "";
    [ObservableProperty] private string _treeSearch = "";
    [ObservableProperty] private string _targetFilter = "All";
    [ObservableProperty] private string _moduleFilter = "All";
    [ObservableProperty] private bool _treeSharedOnly;
    [ObservableProperty] private bool _treeIsTargets = true;

    [ObservableProperty] private string _packageName = "!WindowsTriage";
    [ObservableProperty] private string _packageDescription = "Windows triage pack";
    [ObservableProperty] private string _packageAuthor = "";
    [ObservableProperty] private string _packageVersion = "1.0";
    [ObservableProperty] private string _tsource = "C:";
    [ObservableProperty] private string _notes = "";
    [ObservableProperty] private bool _zipOutput = true;
    [ObservableProperty] private bool _flush;
    [ObservableProperty] private bool _vss;
    [ObservableProperty] private bool _installIntoKape = true;
    [ObservableProperty] private bool _makeZip = true;
    [ObservableProperty] private bool _copyDeps = true;

    [ObservableProperty] private string _selectedTargetsText = "";
    [ObservableProperty] private string _selectedModulesText = "";
    [ObservableProperty] private string _detailText = "Select an item to see details.";
    [ObservableProperty] private CatalogRowVm? _selectedExisting;
    [ObservableProperty] private string _treeStats = "";

    public ObservableCollection<CatalogRowVm> TargetRows { get; } = new();
    public ObservableCollection<CatalogRowVm> ModuleRows { get; } = new();
    public ObservableCollection<CatalogRowVm> ExistingPacks { get; } = new();
    public ObservableCollection<TreeNodeVm> TreeRoots { get; } = new();
    public List<string> FilterOptions { get; } = new() { "All", "Selected only", "Compound only", "Leaf only" };

    public PackageDefinition Package { get; private set; } = new();

    public MainViewModel()
    {
        _settings = AppSettings.Load();
        KapeRoot = AppSettings.ResolveDefaultKapeRoot(_settings);
        _catalog = new KapeCatalog(KapeRoot);
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
    partial void OnTreeIsTargetsChanged(bool value) => RebuildTree();

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

    [RelayCommand]
    private void BrowseKapeRoot()
    {
        var dlg = new OpenFolderDialog { Title = "Select KAPE root" };
        if (!string.IsNullOrWhiteSpace(KapeRoot) && Directory.Exists(KapeRoot))
            dlg.InitialDirectory = KapeRoot;
        if (dlg.ShowDialog() == true)
        {
            KapeRoot = dlg.FolderName;
            _ = ReloadCatalogAsync();
        }
    }

    [RelayCommand]
    private async Task ReloadCatalogAsync()
    {
        if (!Directory.Exists(Path.Combine(KapeRoot, "Targets")))
        {
            MessageBox.Show($"Targets folder not found in:\n{KapeRoot}", "KAPE root", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        StatusText = "Scanning catalog…";
        var root = KapeRoot;
        await Task.Run(() =>
        {
            var cat = new KapeCatalog(root);
            cat.Refresh();
            Application.Current.Dispatcher.Invoke(() =>
            {
                _catalog = cat;
                _settings.LastKapeRoot = root;
                _settings.Save();
                OnCatalogLoaded();
            });
        });
    }

    private void OnCatalogLoaded()
    {
        var last = GitHubKapeFilesSync.ReadLastSync(KapeRoot);
        var syncNote = last is not null && last.TryGetValue("synced_at", out var at) ? $" | GitHub sync: {at}" : "";
        StatusText = $"Loaded {_catalog.Targets.Count} targets, {_catalog.Modules.Count} modules{syncNote}";
        RefreshTargetRows();
        RefreshModuleRows();
        RefreshExisting();
        RebuildTree();
        SyncSelectionTexts();
    }

    [RelayCommand]
    private async Task UpdateFromGitHubAsync()
    {
        var last = GitHubKapeFilesSync.ReadLastSync(KapeRoot);
        var lastLine = last is not null && last.TryGetValue("synced_at", out var at)
            ? $"\n\nLast sync: {at}"
            : "";
        var ok = MessageBox.Show(
            "Download latest Targets and Modules from:\n" +
            $"https://github.com/{GitHubKapeFilesSync.Repo}\n\n" +
            "Upstream files will be overwritten.\n" +
            "Local-only custom files and Modules\\bin are kept." +
            lastLine + "\n\nContinue?",
            "Update from GitHub",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (ok != MessageBoxResult.Yes) return;

        StatusText = "Updating from GitHub…";
        var progress = new Progress<string>(m => StatusText = m);
        var root = KapeRoot;
        var result = await Task.Run(() => GitHubKapeFilesSync.SyncAsync(root, progress).GetAwaiter().GetResult());
        if (result.Ok)
        {
            MessageBox.Show(result.Message, "GitHub sync", MessageBoxButton.OK, MessageBoxImage.Information);
            await ReloadCatalogAsync();
        }
        else
        {
            MessageBox.Show(result.Message, "GitHub sync", MessageBoxButton.OK, MessageBoxImage.Error);
            StatusText = "GitHub sync failed";
        }
    }

    public void RefreshTargetRows()
    {
        var compoundsOnly = ParseFilter(TargetFilter);
        var selectedOnly = TargetFilter == "Selected only";
        var keys = KapeCatalog.BuildSelectionKeys(Package.Targets);
        var items = _catalog.FilterTargets(TargetSearch, selectedOnly ? null : compoundsOnly).ToList();
        if (selectedOnly)
            items = items.Where(i => KapeCatalog.IsSelected(i, keys)).ToList();

        var wasSuppressing = _suppressSelectionEvents;
        _suppressSelectionEvents = true;
        TargetRows.Clear();
        foreach (var item in items)
            TargetRows.Add(new CatalogRowVm(item, KapeCatalog.IsSelected(item, keys)));
        if (!wasSuppressing)
            _suppressSelectionEvents = false;
    }

    public void RefreshModuleRows()
    {
        var compoundsOnly = ParseFilter(ModuleFilter);
        var selectedOnly = ModuleFilter == "Selected only";
        var keys = KapeCatalog.BuildSelectionKeys(Package.Modules);
        var items = _catalog.FilterModules(ModuleSearch, selectedOnly ? null : compoundsOnly).ToList();
        if (selectedOnly)
            items = items.Where(i => KapeCatalog.IsSelected(i, keys)).ToList();

        var wasSuppressing = _suppressSelectionEvents;
        _suppressSelectionEvents = true;
        ModuleRows.Clear();
        foreach (var item in items)
            ModuleRows.Add(new CatalogRowVm(item, KapeCatalog.IsSelected(item, keys)));
        if (!wasSuppressing)
            _suppressSelectionEvents = false;
    }

    private void RefreshExisting()
    {
        ExistingPacks.Clear();
        foreach (var c in _catalog.Compounds(ItemKind.Target).OrderBy(c => c.Name))
            ExistingPacks.Add(new CatalogRowVm(c, false));
    }

    public void RebuildTree()
    {
        var kind = TreeIsTargets ? ItemKind.Target : ItemKind.Module;
        var compounds = _catalog.Compounds(kind).ToList();
        var childNames = compounds
            .SelectMany(c => c.Children)
            .Select(c => Path.GetFileNameWithoutExtension(c).ToLowerInvariant())
            .ToHashSet();

        var roots = compounds.Where(c => !childNames.Contains(c.Name.ToLowerInvariant())).ToList();
        foreach (var c in compounds.Where(c => c.Name.StartsWith('!') && !roots.Contains(c)))
            roots.Add(c);

        var keys = KapeCatalog.BuildSelectionKeys(kind == ItemKind.Target ? Package.Targets : Package.Modules);
        var query = TreeSearch.Trim().ToLowerInvariant();

        TreeRoots.Clear();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots.OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase))
        {
            if (!seen.Add(root.AbsolutePath)) continue;
            var node = BuildTreeNode(root, kind, keys, query, TreeSharedOnly, 0, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
            if (node is not null)
                TreeRoots.Add(node);
        }

        UpdateTreeStats(kind, keys);
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
        var packs = _catalog.IncludingCompounds(item.Name, kind);
        var isShared = packs.Count > 1;
        var matches = string.IsNullOrEmpty(query) || item.SearchBlob.Contains(query, StringComparison.Ordinal) ||
                      packs.Any(p => p.Contains(query, StringComparison.OrdinalIgnoreCase));

        if (sharedOnly && !item.IsCompound && !isShared) return null;
        if (!string.IsNullOrEmpty(query) && !matches && !item.IsCompound) return null;

        var selected = item.IsCompound
            ? AllLeavesSelected(item, kind, keys)
            : KapeCatalog.IsSelected(item, keys);

        var node = new TreeNodeVm(item, _catalog.SharedBadge(item), selected, depth < 1);
        if (item.IsCompound)
        {
            var visible = 0;
            foreach (var childRef in item.Children)
            {
                var child = kind == ItemKind.Target ? _catalog.FindTarget(childRef) : _catalog.FindModule(childRef);
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

    private bool AllLeavesSelected(CatalogItem compound, ItemKind kind, HashSet<string> keys)
    {
        var leaves = _catalog.FlattenToLeaves(new[] { Path.GetFileName(compound.RelativePath) }, kind);
        return leaves.Count > 0 && leaves.All(l => KapeCatalog.IsSelected(l, keys));
    }

    private void UpdateTreeStats(ItemKind kind, HashSet<string> keys)
    {
        var count = kind == ItemKind.Target ? Package.Targets.Count : Package.Modules.Count;
        TreeStats = $"Package {(kind == ItemKind.Target ? "targets" : "modules")}: {count}";
    }

    public void ToggleCatalogRow(CatalogRowVm row, ItemKind kind)
    {
        if (_suppressSelectionEvents) return;
        // Row.IsSelected already reflects desired state from checkbox binding.
        SetItemSelected(row.Item, kind, row.IsSelected);
    }

    public void ToggleTreeNode(TreeNodeVm node)
    {
        if (_suppressSelectionEvents) return;
        var kind = TreeIsTargets ? ItemKind.Target : ItemKind.Module;
        SetItemSelected(node.Item, kind, node.IsSelected);
    }

    private void SetItemSelected(CatalogItem item, ItemKind kind, bool selected)
    {
        var refName = Path.GetFileName(item.RelativePath);
        var leaves = item.IsCompound
            ? _catalog.FlattenToLeaves(new[] { refName }, kind)
            : new List<CatalogItem> { item };
        if (leaves.Count == 0 && !item.IsCompound)
            leaves = new List<CatalogItem> { item };

        var list = kind == ItemKind.Target ? Package.Targets : Package.Modules;

        if (!selected)
        {
            var remove = leaves.Select(l => Path.GetFileName(l.RelativePath).ToLowerInvariant())
                .Concat(leaves.Select(l => l.Name.ToLowerInvariant()))
                .ToHashSet();
            var next = list.Where(e =>
                !remove.Contains(e.Path.ToLowerInvariant()) &&
                !remove.Contains(e.Name.ToLowerInvariant())).ToList();
            if (kind == ItemKind.Target) Package.Targets = next;
            else Package.Modules = next;
        }
        else
        {
            var incoming = leaves.Select(l => new SelectionEntry
            {
                Name = l.Name,
                Category = string.IsNullOrWhiteSpace(l.Category) ? "General" : l.Category,
                Path = Path.GetFileName(l.RelativePath)
            }).ToList();
            var merged = KapeCatalog.MergeEntries(list, incoming);
            if (kind == ItemKind.Target) Package.Targets = merged;
            else Package.Modules = merged;
        }

        StatusText = $"{(selected ? "Added" : "Removed")}: {item.Name} в†’ {(kind == ItemKind.Target ? Package.Targets.Count : Package.Modules.Count)} items";
        SyncAllViews();
    }

    private void ToggleItem(CatalogItem item, ItemKind kind)
    {
        var list = kind == ItemKind.Target ? Package.Targets : Package.Modules;
        var keys = KapeCatalog.BuildSelectionKeys(list);
        var refName = Path.GetFileName(item.RelativePath);
        var leaves = item.IsCompound
            ? _catalog.FlattenToLeaves(new[] { refName }, kind)
            : new List<CatalogItem> { item };
        var allOn = leaves.Count > 0 && leaves.All(l => KapeCatalog.IsSelected(l, keys));
        SetItemSelected(item, kind, !allOn);
    }

    private void SyncAllViews()
    {
        _suppressSelectionEvents = true;
        try
        {
            SyncSelectionTexts();
            RefreshTargetRows();
            RefreshModuleRows();
            RebuildTree();
        }
        finally
        {
            _suppressSelectionEvents = false;
        }
    }

    private void SyncSelectionTexts()
    {
        SelectedTargetsText = string.Join('\n', Package.Targets.Select(t => $"{t.Path}  |  {t.Name}  |  {t.Category}"));
        SelectedModulesText = string.Join('\n', Package.Modules.Select(m => $"{m.Path}  |  {m.Name}  |  {m.Category}"));
    }

    private static bool? ParseFilter(string filter) => filter switch
    {
        "Compound only" => true,
        "Leaf only" => false,
        _ => null
    };

    [RelayCommand]
    private void CheckVisibleTargets()
    {
        var incoming = TargetRows.Select(r => new SelectionEntry
        {
            Name = r.Item.Name,
            Category = r.Item.Category,
            Path = Path.GetFileName(r.Item.RelativePath)
        });
        Package.Targets = KapeCatalog.MergeEntries(Package.Targets, incoming);
        SyncAllViews();
    }

    [RelayCommand]
    private void CheckVisibleModules()
    {
        var incoming = ModuleRows.Select(r => new SelectionEntry
        {
            Name = r.Item.Name,
            Category = r.Item.Category,
            Path = Path.GetFileName(r.Item.RelativePath)
        });
        Package.Modules = KapeCatalog.MergeEntries(Package.Modules, incoming);
        SyncAllViews();
    }

    [RelayCommand]
    private void ClearTargets()
    {
        Package.Targets.Clear();
        SyncAllViews();
    }

    [RelayCommand]
    private void ClearModules()
    {
        Package.Modules.Clear();
        SyncAllViews();
    }

    [RelayCommand]
    private void NewPack()
    {
        Package = new PackageDefinition
        {
            Name = "!WindowsTriage",
            Description = "Windows triage pack",
            Author = PackageAuthor
        };
        PushPackageToForm();
        SyncAllViews();
        StatusText = "New empty pack";
    }

    [RelayCommand]
    private void LoadSelectedCompound()
    {
        if (SelectedExisting is null)
        {
            MessageBox.Show("Select a compound target first.", "Load");
            return;
        }
        var loaded = KapeFileIo.PackageFromCompoundTarget(SelectedExisting.Item.AbsolutePath);
        loaded.Modules = new List<SelectionEntry>();
        var guess = loaded.TargetCompoundName + "_Modules";
        var mod = _catalog.FindModule(guess) ?? _catalog.FindModule(guess.TrimStart('!'));
        if (mod?.IsCompound == true)
        {
            foreach (var child in mod.Children)
            {
                var childItem = _catalog.FindModule(child);
                loaded.Modules.Add(childItem is null
                    ? new SelectionEntry { Name = Path.GetFileNameWithoutExtension(child), Path = child.EndsWith(".mkape") ? child : child + ".mkape", Category = "General" }
                    : new SelectionEntry { Name = childItem.Name, Category = childItem.Category, Path = Path.GetFileName(childItem.RelativePath) });
            }
        }
        Package = loaded;
        PushPackageToForm();
        TargetFilter = "Selected only";
        if (Package.Modules.Count > 0) ModuleFilter = "Selected only";
        SyncAllViews();
        StatusText = $"Loaded {SelectedExisting.Item.Name}: {Package.Targets.Count} targets, {Package.Modules.Count} modules";
    }

    [RelayCommand]
    private void OpenPackageJson()
    {
        var dlg = new OpenFileDialog { Filter = "JSON|*.json|All|*.*", Title = "Open package.json" };
        if (dlg.ShowDialog() != true) return;
        Package = PackageExporter.LoadPackageJson(dlg.FileName);
        PushPackageToForm();
        TargetFilter = "Selected only";
        if (Package.Modules.Count > 0) ModuleFilter = "Selected only";
        SyncAllViews();
        StatusText = $"Loaded package.json: {Package.Targets.Count} targets, {Package.Modules.Count} modules";
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
            MessageBox.Show("Nothing checked in tree.", "Tree");
            return;
        }
        var incoming = _catalog.SelectionFromRefs(refs, kind, flatten: true);
        if (kind == ItemKind.Target)
            Package.Targets = replace ? incoming : KapeCatalog.MergeEntries(Package.Targets, incoming);
        else
            Package.Modules = replace ? incoming : KapeCatalog.MergeEntries(Package.Modules, incoming);
        var stats = _catalog.OverlapStats(refs, kind);
        StatusText = $"{(replace ? "Replaced" : "Merged")}: {refs.Count} refs в†’ {stats.UniqueLeaves} unique leaves";
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
    private void PreviewCommand()
    {
        PullFormToPackage();
        var text = KapeFileIo.RenderRunBat(Package);
        MessageBox.Show(text, "Launch script preview");
    }

    [RelayCommand]
    private void BuildPackage()
    {
        PullFormToPackage();
        if (Package.Targets.Count == 0)
        {
            MessageBox.Show("Add at least one target.", "Build", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        var dlg = new OpenFolderDialog { Title = "Select output folder for the package" };
        var initial = Path.Combine(KapeRoot, "PackBuilder", "exports");
        Directory.CreateDirectory(initial);
        dlg.InitialDirectory = initial;
        if (dlg.ShowDialog() != true) return;

        try
        {
            var exporter = new PackageExporter(_catalog);
            var result = exporter.Export(Package, dlg.FolderName, InstallIntoKape, MakeZip, CopyDeps);
            var msg = $"Package folder:\n{result.PackageDir}\n\nCompound target: {Path.GetFileName(result.TargetFile)}";
            if (result.ModuleFile is not null) msg += $"\nCompound module: {Path.GetFileName(result.ModuleFile)}";
            if (result.InstalledTarget is not null) msg += $"\nInstalled into KAPE: {result.InstalledTarget}";
            if (result.ZipFile is not null) msg += $"\nZIP: {result.ZipFile}";
            if (result.Warnings.Count > 0)
                msg += "\n\nWarnings:\n - " + string.Join("\n - ", result.Warnings.Take(12));
            MessageBox.Show(msg, "Build complete");
            StatusText = $"Built package: {Package.Name}";
            _ = ReloadCatalogAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Build failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    public void ShowItemInfo(CatalogItem item)
    {
        var text =
            $"{item.Name}\nPath: {item.RelativePath}\nCategory: {item.Category}\n" +
            $"Author: {item.Author} | Version: {item.Version}\nCompound: {item.IsCompound}\n" +
            $"Description: {item.Description}\n";
        if (item.Children.Count > 0)
            text += "Children: " + string.Join(", ", item.Children.Take(30));
        DetailText = text;
    }

    private void PushPackageToForm()
    {
        PackageName = Package.Name;
        PackageDescription = Package.Description;
        PackageAuthor = Package.Author;
        PackageVersion = Package.Version;
        Tsource = Package.Tsource;
        ZipOutput = Package.ZipOutput;
        Flush = Package.Flush;
        Vss = Package.Vss;
        Notes = Package.Notes;
    }

    private void PullFormToPackage()
    {
        Package.Name = string.IsNullOrWhiteSpace(PackageName) ? "!WindowsTriage" : PackageName.Trim();
        Package.Description = PackageDescription.Trim();
        Package.Author = PackageAuthor.Trim();
        Package.Version = string.IsNullOrWhiteSpace(PackageVersion) ? "1.0" : PackageVersion.Trim();
        Package.Tsource = string.IsNullOrWhiteSpace(Tsource) ? "C:" : Tsource.Trim();
        Package.ZipOutput = ZipOutput;
        Package.Flush = Flush;
        Package.Vss = Vss;
        Package.Notes = Notes.Trim();
        if (string.IsNullOrWhiteSpace(Package.PackageId))
            Package.PackageId = Guid.NewGuid().ToString();
    }
}
