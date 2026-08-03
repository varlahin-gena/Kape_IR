using KapePackBuilder.Models;

namespace KapePackBuilder.Services;

public sealed class KapeCatalog
{
    public string KapeRoot { get; }
    public string TargetsDir => Path.Combine(KapeRoot, "Targets");
    public string ModulesDir => Path.Combine(KapeRoot, "Modules");

    public List<CatalogItem> Targets { get; private set; } = new();
    public List<CatalogItem> Modules { get; private set; } = new();

    private Dictionary<string, CatalogItem> _targetIndex = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, CatalogItem> _moduleIndex = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, List<string>> _targetParents = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, List<string>> _moduleParents = new(StringComparer.OrdinalIgnoreCase);

    public KapeCatalog(string kapeRoot) => KapeRoot = kapeRoot;

    public void Refresh()
    {
        Targets = ScanTargets();
        Modules = ScanModules();
        _targetIndex = BuildIndex(Targets);
        _moduleIndex = BuildIndex(Modules);
        _targetParents = BuildParents(Targets);
        _moduleParents = BuildParents(Modules);
    }

    public CatalogItem? FindTarget(string nameOrFile) => Find(_targetIndex, nameOrFile, ".tkape");
    public CatalogItem? FindModule(string nameOrFile) => Find(_moduleIndex, nameOrFile, ".mkape");

    public IEnumerable<CatalogItem> FilterTargets(string query = "", bool? compoundsOnly = null)
        => Filter(Targets, query, compoundsOnly);

    public IEnumerable<CatalogItem> FilterModules(string query = "", bool? compoundsOnly = null)
        => Filter(Modules, query, compoundsOnly);

    public IEnumerable<CatalogItem> Compounds(ItemKind kind)
        => (kind == ItemKind.Target ? Targets : Modules).Where(i => i.IsCompound);

    public List<string> DirectParents(string nameOrFile, ItemKind kind)
    {
        var key = Path.GetFileNameWithoutExtension(nameOrFile).ToLowerInvariant();
        var parents = kind == ItemKind.Target ? _targetParents : _moduleParents;
        return parents.TryGetValue(key, out var list) ? list.ToList() : new List<string>();
    }

    public List<string> IncludingCompounds(string nameOrFile, ItemKind kind)
    {
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<string>(DirectParents(nameOrFile, kind));
        while (queue.Count > 0)
        {
            var name = queue.Dequeue();
            if (!found.Add(name)) continue;
            foreach (var parent in DirectParents(name, kind))
            {
                if (!found.Contains(parent))
                    queue.Enqueue(parent);
            }
        }
        return found.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public string SharedBadge(CatalogItem item)
    {
        var packs = IncludingCompounds(item.Name, item.Kind);
        if (packs.Count <= 1)
        {
            var direct = DirectParents(item.Name, item.Kind);
            return direct.Count == 1 ? $"в: {direct[0]}" : "";
        }
        var shown = string.Join(", ", packs.Take(5));
        var more = packs.Count > 5 ? $" +{packs.Count - 5}" : "";
        return $"⋆ в {packs.Count} пак.: {shown}{more}";
    }

    public List<CatalogItem> FlattenToLeaves(IEnumerable<string> refs, ItemKind kind)
    {
        var leaves = new List<CatalogItem>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Walk(string reference)
        {
            var item = kind == ItemKind.Target ? FindTarget(reference) : FindModule(reference);
            if (item is null) return;
            var key = item.AbsolutePath;
            if (!seen.Add(key)) return;
            if (item.IsCompound)
            {
                foreach (var child in item.Children)
                    Walk(child);
                return;
            }
            leaves.Add(item);
        }

        foreach (var r in refs) Walk(r);
        return leaves;
    }

    public List<CatalogItem> ResolveClosure(IEnumerable<string> names, ItemKind kind)
    {
        var result = new List<CatalogItem>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Walk(string reference)
        {
            var item = kind == ItemKind.Target ? FindTarget(reference) : FindModule(reference);
            if (item is null) return;
            if (!seen.Add(item.AbsolutePath)) return;
            result.Add(item);
            if (item.IsCompound)
            {
                foreach (var child in item.Children)
                    Walk(child);
            }
        }

        foreach (var n in names) Walk(n);
        return result;
    }

    public List<SelectionEntry> SelectionFromRefs(IEnumerable<string> refs, ItemKind kind, bool flatten = true)
    {
        var items = flatten ? FlattenToLeaves(refs, kind) : ResolveClosure(refs, kind);
        var entries = new List<SelectionEntry>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items)
        {
            if (flatten && item.IsCompound) continue;
            var filename = Path.GetFileName(item.RelativePath);
            if (!seen.Add(filename)) continue;
            entries.Add(new SelectionEntry
            {
                Name = item.Name,
                Category = string.IsNullOrWhiteSpace(item.Category) ? "General" : item.Category,
                Path = filename
            });
        }
        return entries;
    }

    public static List<SelectionEntry> MergeEntries(IEnumerable<SelectionEntry> existing, IEnumerable<SelectionEntry> incoming)
    {
        var ordered = new List<SelectionEntry>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in existing.Concat(incoming))
        {
            if (string.IsNullOrWhiteSpace(e.Path)) continue;
            if (!seen.Add(e.Path)) continue;
            ordered.Add(e);
        }
        return ordered;
    }

    public OverlapStats OverlapStats(IEnumerable<string> refs, ItemKind kind)
    {
        var refList = refs.ToList();
        var compounds = 0;
        foreach (var r in refList)
        {
            var item = kind == ItemKind.Target ? FindTarget(r) : FindModule(r);
            if (item?.IsCompound == true) compounds++;
        }
        var leaves = FlattenToLeaves(refList, kind);
        var shared = leaves.Count(l => IncludingCompounds(l.Name, kind).Count > 1);
        return new OverlapStats
        {
            Compounds = compounds,
            UniqueLeaves = leaves.Count,
            SharedLeaves = shared
        };
    }

    public bool IsSelected(CatalogItem item, IEnumerable<SelectionEntry> selection)
    {
        var keys = BuildSelectionKeys(selection);
        return IsSelected(item, keys);
    }

    public static HashSet<string> BuildSelectionKeys(IEnumerable<SelectionEntry> selection)
    {
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in selection)
        {
            if (string.IsNullOrWhiteSpace(e.Path)) continue;
            keys.Add(e.Path);
            keys.Add(Path.GetFileNameWithoutExtension(e.Path));
            if (!string.IsNullOrWhiteSpace(e.Name)) keys.Add(e.Name);
        }
        return keys;
    }

    public static bool IsSelected(CatalogItem item, HashSet<string> keys)
    {
        var filename = Path.GetFileName(item.RelativePath);
        var stem = Path.GetFileNameWithoutExtension(item.RelativePath);
        return keys.Contains(item.Name) || keys.Contains(filename) || keys.Contains(stem);
    }

    private static CatalogItem? Find(Dictionary<string, CatalogItem> index, string nameOrFile, string ext)
    {
        if (index.TryGetValue(nameOrFile, out var hit)) return hit;
        if (!nameOrFile.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
        {
            if (index.TryGetValue(nameOrFile + ext, out hit)) return hit;
        }
        return null;
    }

    private static IEnumerable<CatalogItem> Filter(IEnumerable<CatalogItem> items, string query, bool? compoundsOnly)
    {
        var q = query.Trim().ToLowerInvariant();
        foreach (var item in items)
        {
            if (compoundsOnly == true && !item.IsCompound) continue;
            if (compoundsOnly == false && item.IsCompound) continue;
            if (!string.IsNullOrEmpty(q) && !item.SearchBlob.Contains(q, StringComparison.Ordinal)) continue;
            yield return item;
        }
    }

    private static Dictionary<string, CatalogItem> BuildIndex(IEnumerable<CatalogItem> items)
    {
        var index = new Dictionary<string, CatalogItem>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items)
        {
            index[item.Name] = item;
            index[Path.GetFileName(item.RelativePath)] = item;
        }
        return index;
    }

    private static Dictionary<string, List<string>> BuildParents(IEnumerable<CatalogItem> items)
    {
        var parents = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items.Where(i => i.IsCompound))
        {
            foreach (var child in item.Children)
            {
                var stem = Path.GetFileNameWithoutExtension(child).ToLowerInvariant();
                if (!parents.TryGetValue(stem, out var list))
                {
                    list = new List<string>();
                    parents[stem] = list;
                }
                if (!list.Contains(item.Name, StringComparer.OrdinalIgnoreCase))
                    list.Add(item.Name);
            }
        }
        foreach (var key in parents.Keys.ToList())
            parents[key] = parents[key].OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
        return parents;
    }

    private List<CatalogItem> ScanTargets()
    {
        var items = new List<CatalogItem>();
        if (!Directory.Exists(TargetsDir)) return items;
        foreach (var path in Directory.EnumerateFiles(TargetsDir, "*.tkape", SearchOption.AllDirectories).OrderBy(p => p))
        {
            if (!KapeFileIo.TryLoadKapeFile(path, out var data)) continue;
            var compound = KapeFileIo.IsCompoundTarget(data);
            var rel = Path.GetRelativePath(KapeRoot, path).Replace('\\', '/');
            items.Add(new CatalogItem
            {
                Kind = ItemKind.Target,
                Name = Path.GetFileNameWithoutExtension(path),
                RelativePath = rel,
                Category = NullIfEmpty(KapeFileIo.GetString(data, "Category")) ?? new DirectoryInfo(Path.GetDirectoryName(path)!).Name,
                Description = KapeFileIo.GetString(data, "Description"),
                Author = KapeFileIo.GetString(data, "Author"),
                Version = KapeFileIo.GetString(data, "Version"),
                ItemId = KapeFileIo.GetString(data, "Id"),
                IsCompound = compound,
                Children = compound ? KapeFileIo.ExtractTargetChildren(data) : new List<string>(),
                FileMasks = compound ? new List<string>() : KapeFileIo.ExtractTargetFileMasks(data),
                AbsolutePath = path
            });
        }
        return items;
    }

    private List<CatalogItem> ScanModules()
    {
        var items = new List<CatalogItem>();
        if (!Directory.Exists(ModulesDir)) return items;
        foreach (var path in Directory.EnumerateFiles(ModulesDir, "*.mkape", SearchOption.AllDirectories).OrderBy(p => p))
        {
            if (!KapeFileIo.TryLoadKapeFile(path, out var data)) continue;
            var compound = KapeFileIo.IsCompoundModule(data);
            var rel = Path.GetRelativePath(KapeRoot, path).Replace('\\', '/');
            items.Add(new CatalogItem
            {
                Kind = ItemKind.Module,
                Name = Path.GetFileNameWithoutExtension(path),
                RelativePath = rel,
                Category = NullIfEmpty(KapeFileIo.GetString(data, "Category")) ?? new DirectoryInfo(Path.GetDirectoryName(path)!).Name,
                Description = KapeFileIo.GetString(data, "Description"),
                Author = KapeFileIo.GetString(data, "Author"),
                Version = KapeFileIo.GetString(data, "Version"),
                ItemId = KapeFileIo.GetString(data, "Id"),
                IsCompound = compound,
                Children = compound ? KapeFileIo.ExtractModuleChildren(data) : new List<string>(),
                FileMasks = compound ? new List<string>() : KapeFileIo.ExtractModuleFileMasks(data),
                AbsolutePath = path
            });
        }
        return items;
    }

    private static string? NullIfEmpty(string s) => string.IsNullOrWhiteSpace(s) ? null : s;
}
