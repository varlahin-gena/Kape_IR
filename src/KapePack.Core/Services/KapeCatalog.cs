using System.Collections.Concurrent;
using KapePack.Core.Models;

namespace KapePack.Core.Services;

public sealed class KapeCatalog
{
    public string KapeRoot { get; private set; }
    public string TargetsDir => Path.Combine(KapeRoot, "Targets");
    public string ModulesDir => Path.Combine(KapeRoot, "Modules");

    public List<CatalogItem> Targets { get; private set; } = new();
    public List<CatalogItem> Modules { get; private set; } = new();

    /// <summary>Hits/misses from the last <see cref="Refresh"/> (file parse cache).</summary>
    public CatalogRefreshStats LastRefreshStats { get; private set; } = new();

    private Dictionary<string, CatalogItem> _targetIndex = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, CatalogItem> _moduleIndex = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, List<string>> _targetParents = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, List<string>> _moduleParents = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, List<string>> _targetAncestors = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, List<string>> _moduleAncestors = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Process-wide parse cache keyed by absolute path (mtime + size).</summary>
    private static readonly ConcurrentDictionary<string, CachedKapeFile> FileCache =
        new(StringComparer.OrdinalIgnoreCase);

    public KapeCatalog(string kapeRoot) => KapeRoot = kapeRoot ?? "";

    /// <summary>Rebind to another KAPE root without discarding the shared file cache.</summary>
    public void Rebind(string kapeRoot) => KapeRoot = kapeRoot ?? "";

    public void Refresh()
    {
        var upstream = GitHubKapeFilesSync.ReadUpstreamPathSet(KapeRoot);
        HasUpstreamInventory = upstream is not null;
        var stats = new CatalogRefreshStats();
        Targets = ScanTargets(upstream, stats);
        Modules = ScanModules(upstream, stats);
        LastRefreshStats = stats;
        _targetIndex = BuildIndex(Targets);
        _moduleIndex = BuildIndex(Modules);
        _targetParents = BuildParents(Targets);
        _moduleParents = BuildParents(Modules);
        _targetAncestors = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        _moduleAncestors = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Drop cached parses (tests / forced full rescan).</summary>
    public static void ClearFileCache() => FileCache.Clear();

    /// <summary>True when PackBuilder/last_kapefiles_paths.txt was loaded.</summary>
    public bool HasUpstreamInventory { get; private set; }

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
        var key = Path.GetFileNameWithoutExtension(nameOrFile).ToLowerInvariant();
        var cache = kind == ItemKind.Target ? _targetAncestors : _moduleAncestors;
        if (cache.TryGetValue(key, out var cached))
            return cached.ToList();

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

        var result = found.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
        cache[key] = result;
        return result.ToList();
    }

    public string SharedBadge(CatalogItem item)
    {
        var packs = IncludingCompounds(item.Name, item.Kind);
        string shared;
        if (packs.Count <= 1)
        {
            var direct = DirectParents(item.Name, item.Kind);
            shared = direct.Count == 1 ? $"в: {direct[0]}" : "";
        }
        else
        {
            var shown = string.Join(", ", packs.Take(5));
            var more = packs.Count > 5 ? $" +{packs.Count - 5}" : "";
            shared = $"⋆ в {packs.Count} пак.: {shown}{more}";
        }

        var origin = item.OriginTag;
        return string.IsNullOrEmpty(shared) ? origin : $"{origin}  {shared}";
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
        if (string.IsNullOrWhiteSpace(nameOrFile))
            return null;

        if (index.TryGetValue(nameOrFile, out var hit))
            return hit;

        // SelectionEntry.Path may be "Modules\EZTools\Foo.mkape" — index keys are basename / stem.
        var fileName = Path.GetFileName(nameOrFile);
        if (!string.IsNullOrEmpty(fileName) &&
            !fileName.Equals(nameOrFile, StringComparison.OrdinalIgnoreCase) &&
            index.TryGetValue(fileName, out hit))
            return hit;

        var stem = Path.GetFileNameWithoutExtension(nameOrFile);
        if (!string.IsNullOrEmpty(stem) && index.TryGetValue(stem, out hit))
            return hit;

        if (!nameOrFile.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
        {
            if (index.TryGetValue(nameOrFile + ext, out hit))
                return hit;
            if (!string.IsNullOrEmpty(stem) && index.TryGetValue(stem + ext, out hit))
                return hit;
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

    private List<CatalogItem> ScanTargets(IReadOnlySet<string>? upstream, CatalogRefreshStats stats)
    {
        var items = new List<CatalogItem>();
        if (!Directory.Exists(TargetsDir)) return items;
        foreach (var path in Directory.EnumerateFiles(TargetsDir, "*.tkape", SearchOption.AllDirectories).OrderBy(p => p))
        {
            if (NameCollisionFixer.IsUnderDisabledFolder(path)) continue;
            var item = LoadOrCacheItem(path, ItemKind.Target, upstream, stats);
            if (item is not null) items.Add(item);
        }
        return items;
    }

    private List<CatalogItem> ScanModules(IReadOnlySet<string>? upstream, CatalogRefreshStats stats)
    {
        var items = new List<CatalogItem>();
        if (!Directory.Exists(ModulesDir)) return items;
        foreach (var path in Directory.EnumerateFiles(ModulesDir, "*.mkape", SearchOption.AllDirectories).OrderBy(p => p))
        {
            if (NameCollisionFixer.IsUnderDisabledFolder(path)) continue;
            var item = LoadOrCacheItem(path, ItemKind.Module, upstream, stats);
            if (item is not null) items.Add(item);
        }
        return items;
    }

    private CatalogItem? LoadOrCacheItem(
        string path,
        ItemKind kind,
        IReadOnlySet<string>? upstream,
        CatalogRefreshStats stats)
    {
        FileInfo fi;
        try { fi = new FileInfo(path); }
        catch { return null; }
        if (!fi.Exists) return null;

        var full = fi.FullName;
        var ticks = fi.LastWriteTimeUtc.Ticks;
        var len = fi.Length;

        if (FileCache.TryGetValue(full, out var cached) &&
            cached.Length == len &&
            cached.LastWriteUtcTicks == ticks &&
            cached.Kind == kind)
        {
            stats.CacheHits++;
            var relCached = Path.GetRelativePath(KapeRoot, path).Replace('\\', '/');
            return cached.ToCatalogItem(relCached, path, upstream);
        }

        stats.CacheMisses++;
        if (!KapeFileIo.TryReadKapeFile(path, out var data, out var rawText))
            return null;

        var compound = kind == ItemKind.Target
            ? KapeFileIo.IsCompoundTarget(data)
            : KapeFileIo.IsCompoundModule(data);
        var author = KapeFileIo.GetString(data, "Author");
        var category = NullIfEmpty(KapeFileIo.GetString(data, "Category"))
                       ?? new DirectoryInfo(Path.GetDirectoryName(path)!).Name;
        var parsed = new CachedKapeFile
        {
            Kind = kind,
            Length = len,
            LastWriteUtcTicks = ticks,
            Name = Path.GetFileNameWithoutExtension(path),
            Category = category,
            Description = KapeFileIo.GetString(data, "Description"),
            Author = author,
            Version = KapeFileIo.GetString(data, "Version"),
            ItemId = KapeFileIo.GetString(data, "Id"),
            IsCompound = compound,
            Children = compound
                ? (kind == ItemKind.Target
                    ? KapeFileIo.ExtractTargetChildren(data)
                    : KapeFileIo.ExtractModuleChildren(data))
                : new List<string>(),
            FileMasks = compound
                ? new List<string>()
                : (kind == ItemKind.Target
                    ? KapeFileIo.ExtractTargetFileMasks(data)
                    : KapeFileIo.ExtractModuleFileMasks(data)),
            DocumentationUrls = KapeFileIo.ExtractDocumentationLinksFromText(rawText)
        };
        FileCache[full] = parsed;
        var rel = Path.GetRelativePath(KapeRoot, path).Replace('\\', '/');
        return parsed.ToCatalogItem(rel, path, upstream);
    }

    private static string? NullIfEmpty(string s) => string.IsNullOrWhiteSpace(s) ? null : s;

    private sealed class CachedKapeFile
    {
        public ItemKind Kind { get; init; }
        public long Length { get; init; }
        public long LastWriteUtcTicks { get; init; }
        public string Name { get; init; } = "";
        public string Category { get; init; } = "";
        public string Description { get; init; } = "";
        public string Author { get; init; } = "";
        public string Version { get; init; } = "";
        public string ItemId { get; init; } = "";
        public bool IsCompound { get; init; }
        public List<string> Children { get; init; } = new();
        public List<string> FileMasks { get; init; } = new();
        public List<string> DocumentationUrls { get; init; } = new();

        public CatalogItem ToCatalogItem(string relativePath, string absolutePath, IReadOnlySet<string>? upstream)
            => new()
            {
                Kind = Kind,
                Name = Name,
                RelativePath = relativePath,
                Category = Category,
                Description = Description,
                Author = Author,
                Version = Version,
                ItemId = ItemId,
                IsCompound = IsCompound,
                Children = Children,
                FileMasks = FileMasks,
                DocumentationUrls = DocumentationUrls,
                AbsolutePath = absolutePath,
                Origin = CatalogOriginLabels.Resolve(relativePath, Author, upstream)
            };
    }
}

public sealed class CatalogRefreshStats
{
    public int CacheHits { get; set; }
    public int CacheMisses { get; set; }
    public int FilesSeen => CacheHits + CacheMisses;
}
