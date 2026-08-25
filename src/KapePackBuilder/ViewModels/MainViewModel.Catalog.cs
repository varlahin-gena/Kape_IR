using System.Windows;
using CommunityToolkit.Mvvm.Input;
using KapePackBuilder.Models;
using KapePackBuilder.Services;

namespace KapePackBuilder.ViewModels;

public partial class MainViewModel
{
    [RelayCommand]
    private void BrowseKapeRoot()
    {
        var folder = _dialogs.PickFolder(
            "Выберите корень KAPE",
            !string.IsNullOrWhiteSpace(KapeRoot) && Directory.Exists(KapeRoot) ? KapeRoot : null);
        if (folder is null) return;
        KapeRoot = folder;
        _ = ReloadCatalogAsync();
    }

    [RelayCommand]
    private async Task ReloadCatalogAsync()
    {
        KapeRoot = (KapeRoot ?? "").Trim();
        if (!Directory.Exists(Path.Combine(KapeRoot, "Targets")))
        {
            if (_dialogs.Confirm(
                    $"Папка Targets не найдена в:\n{KapeRoot}\n\nВыбрать другую папку?",
                    "Корень KAPE",
                    DialogIcon.Error))
            {
                BrowseKapeRoot();
            }
            return;
        }

        StatusText = "Сканирование каталога…";
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
        var syncNote = last is not null && last.TryGetValue("synced_at", out var at) ? $" | синхронизация GitHub: {at}" : "";
        var tDup = NameCollisionFixer.FindCollisions(_catalog.Targets);
        var mDup = NameCollisionFixer.FindCollisions(_catalog.Modules);
        var dupNote = "";
        if (tDup.Count + mDup.Count > 0)
        {
            dupNote = $" | ⚠ дубликаты имён: Targets {tDup.Count}, Modules {mDup.Count} — KAPE упадёт при валидации. Нажмите «Убрать дубликаты».";
        }
        StatusText = $"Загружено: {_catalog.Targets.Count} таргетов, {_catalog.Modules.Count} модулей{syncNote}{dupNote}";
        RefreshTargetRows();
        RefreshModuleRows();
        RefreshExisting();
        RebuildTree();
        SyncSelectionTexts();
    }

    [RelayCommand]
    private async Task FixNameCollisionsAsync()
    {
        var tDup = NameCollisionFixer.FindCollisions(_catalog.Targets);
        var mDup = NameCollisionFixer.FindCollisions(_catalog.Modules);
        if (tDup.Count + mDup.Count == 0)
        {
            _dialogs.ShowMessage("Дубликатов имён не найдено.", "Дубликаты");
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
        if (!_dialogs.Confirm(
                "KAPE требует уникальные имена .tkape/.mkape во всём дереве.\n" +
                $"Найдено групп: Targets {tDup.Count}, Modules {mDup.Count}.\n\n" +
                $"{preview}{more}\n\n" +
                "Лишние файлы будут перенесены в PackBuilder\\name_collisions\\ (не удаляются навсегда).\nПродолжить?",
                "Убрать дубликаты имён",
                DialogIcon.Warning))
            return;

        StatusText = "Устранение дубликатов имён…";
        var root = KapeRoot;
        var targets = _catalog.Targets.ToList();
        var modules = _catalog.Modules.ToList();
        var (tFix, mFix) = await Task.Run(() =>
        {
            var tf = NameCollisionFixer.FixCollisions(root, targets);
            var mf = NameCollisionFixer.FixCollisions(root, modules);
            return (tf, mf);
        });

        var msg =
            $"Targets: убрано {tFix.Removed} из {tFix.Groups} групп.\n" +
            $"Modules: убрано {mFix.Removed} из {mFix.Groups} групп.\n" +
            $"Карантин: {tFix.QuarantineDir}";
        if (tFix.Errors.Count + mFix.Errors.Count > 0)
            msg += "\n\nОшибки:\n" + string.Join("\n", tFix.Errors.Concat(mFix.Errors).Take(10));
        _dialogs.ShowMessage(msg, "Дубликаты",
            tFix.Errors.Count + mFix.Errors.Count > 0 ? DialogIcon.Warning : DialogIcon.Info);

        await ReloadCatalogAsync();
    }

    public void RefreshTargetRows()
    {
        var compoundsOnly = ParseFilter(TargetFilter);
        var selectedOnly = TargetFilter == "Только выбранные";
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
        var selectedOnly = ModuleFilter == "Только выбранные";
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
        var expandedPaths = CaptureExpandedPaths();
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
        var packs = _catalog.IncludingCompounds(item.Name, kind);
        var isShared = packs.Count > 1;
        var matches = string.IsNullOrEmpty(query) || item.SearchBlob.Contains(query, StringComparison.Ordinal) ||
                      packs.Any(p => p.Contains(query, StringComparison.OrdinalIgnoreCase));

        if (sharedOnly && !item.IsCompound && !isShared) return null;
        if (!string.IsNullOrEmpty(query) && !matches && !item.IsCompound) return null;

        var selected = item.IsCompound
            ? AllLeavesSelected(item, kind, keys)
            : KapeCatalog.IsSelected(item, keys);

        // Always start collapsed; RebuildTree restores previously expanded paths.
        var node = new TreeNodeVm(item, _catalog.SharedBadge(item), selected, expanded: false);
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
        TreeStats = $"В пакете {(kind == ItemKind.Target ? "таргетов" : "модулей")}: {count}";
    }

    public void ToggleCatalogRow(CatalogRowVm row, ItemKind kind)
    {
        if (_suppressSelectionEvents) return;
        // Row.IsSelected already reflects desired state from checkbox binding.
        SetItemSelected(row.Item, kind, row.IsSelected);
    }

    /// <summary>Row click (not checkbox): flip inclusion without double-firing Checked handlers.</summary>
    public void ToggleRowFromListClick(CatalogRowVm row, ItemKind kind)
    {
        if (_suppressSelectionEvents) return;
        _suppressSelectionEvents = true;
        try
        {
            row.IsSelected = !row.IsSelected;
        }
        finally
        {
            _suppressSelectionEvents = false;
        }
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

        StatusText = $"{(selected ? "Добавлено" : "Убрано")}: {item.Name} → {(kind == ItemKind.Target ? Package.Targets.Count : Package.Modules.Count)} шт.";
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
            SyncVisibleRowChecks(ItemKind.Target);
            SyncVisibleRowChecks(ItemKind.Module);
            RebuildTree();
        }
        finally
        {
            _suppressSelectionEvents = false;
        }
    }

    /// <summary>Update checkmarks in place — avoid Clear()+rebuild (scroll/selection jump, flaky UI).</summary>
    private void SyncVisibleRowChecks(ItemKind kind)
    {
        var rows = kind == ItemKind.Target ? TargetRows : ModuleRows;
        var keys = KapeCatalog.BuildSelectionKeys(kind == ItemKind.Target ? Package.Targets : Package.Modules);
        foreach (var row in rows)
        {
            var should = row.Item.IsCompound
                ? AllLeavesSelected(row.Item, kind, keys)
                : KapeCatalog.IsSelected(row.Item, keys);
            if (row.IsSelected != should)
                row.IsSelected = should;
        }
    }

    private void SyncSelectionTexts()
    {
        SelectedTargetsText = string.Join('\n', Package.Targets.Select(t => $"{t.Path}  |  {t.Name}  |  {t.Category}"));
        SelectedModulesText = string.Join('\n', Package.Modules.Select(m => $"{m.Path}  |  {m.Name}  |  {m.Category}"));
    }

    private static bool? ParseFilter(string filter) => filter switch
    {
        "Только compound" => true,
        "Только leaf" => false,
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
    private void SuggestModules()
    {
        if (Package.Targets.Count == 0)
        {
            _dialogs.ShowMessage("Сначала выберите хотя бы один таргет.", "Подсказки модулей");
            return;
        }

        var advisor = new ModuleAdvisor(_catalog);
        var suggestions = advisor.Suggest(Package.Targets, Package.Modules);
        if (suggestions.Count == 0)
        {
            _dialogs.ShowMessage(
                "Нет подходящих модулей для текущих таргетов.\n" +
                "Попробуйте leaf-таргеты с FileMask (Prefetch, Amcache, $MFT, EventLogs, …).",
                "Подсказки модулей");
            return;
        }

        var dlg = new SuggestModulesWindow(suggestions) { Owner = Application.Current.MainWindow };
        if (dlg.ShowDialog() != true || !dlg.Applied || dlg.Chosen.Count == 0)
            return;

        var incoming = dlg.Chosen.Select(m => new SelectionEntry
        {
            Name = m.Name,
            Category = string.IsNullOrWhiteSpace(m.Category) ? "General" : m.Category,
            Path = Path.GetFileName(m.RelativePath)
        });
        Package.Modules = KapeCatalog.MergeEntries(Package.Modules, incoming);
        ModuleFilter = "Только выбранные";
        SyncAllViews();
        StatusText = $"Добавлено модулей из подсказок: {dlg.Chosen.Count} (в пакете {Package.Modules.Count})";
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
}
