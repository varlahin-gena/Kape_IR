using KapeIR.Core.Models;
using KapeIR.Core.Services;

namespace KapeIR.Builder.Workspaces;

/// <summary>
/// Mutates <see cref="PackageDefinition"/> target/module selection and guards UI re-entrancy
/// while list/tree checkboxes sync. ViewModels own ObservableCollections; this owns the rules.
/// </summary>
public sealed class CatalogSelectionCoordinator
{
    private int _suppressDepth;

    public bool IsSuppressed => _suppressDepth > 0;

    /// <summary>Suppress checkbox side-effects while programmatically updating IsSelected.</summary>
    public IDisposable SuppressEvents()
    {
        _suppressDepth++;
        return new SuppressScope(this);
    }

    public string FormatSelectionText(IEnumerable<SelectionEntry> entries)
        => string.Join('\n', entries.Select(e => $"{e.Path}  |  {e.Name}  |  {e.Category}"));

    public bool AllLeavesSelected(KapeCatalog catalog, CatalogItem compound, ItemKind kind, HashSet<string> keys)
    {
        var leaves = catalog.FlattenToLeaves(new[] { Path.GetFileName(compound.RelativePath) }, kind);
        return leaves.Count > 0 && leaves.All(l => KapeCatalog.IsSelected(l, keys));
    }

    public bool IsItemEffectivelySelected(KapeCatalog catalog, CatalogItem item, ItemKind kind, HashSet<string> keys)
        => item.IsCompound
            ? AllLeavesSelected(catalog, item, kind, keys)
            : KapeCatalog.IsSelected(item, keys);

    /// <summary>Add or remove leaf entries for a row/tree toggle. Returns operator status line.</summary>
    public string SetItemSelected(
        KapeCatalog catalog,
        PackageDefinition package,
        CatalogItem item,
        ItemKind kind,
        bool selected)
    {
        var refName = Path.GetFileName(item.RelativePath);
        var leaves = item.IsCompound
            ? catalog.FlattenToLeaves(new[] { refName }, kind)
            : new List<CatalogItem> { item };
        if (leaves.Count == 0 && !item.IsCompound)
            leaves = new List<CatalogItem> { item };

        var list = kind == ItemKind.Target ? package.Targets : package.Modules;

        if (!selected)
        {
            var remove = leaves.Select(l => Path.GetFileName(l.RelativePath).ToLowerInvariant())
                .Concat(leaves.Select(l => l.Name.ToLowerInvariant()))
                .ToHashSet();
            var next = list.Where(e =>
                !remove.Contains(e.Path.ToLowerInvariant()) &&
                !remove.Contains(e.Name.ToLowerInvariant())).ToList();
            if (kind == ItemKind.Target) package.Targets = next;
            else package.Modules = next;
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
            if (kind == ItemKind.Target) package.Targets = merged;
            else package.Modules = merged;
        }

        var count = kind == ItemKind.Target ? package.Targets.Count : package.Modules.Count;
        return $"{(selected ? "Добавлено" : "Убрано")}: {item.Name} → {count} шт.";
    }

    /// <summary>Flip selection for an item based on whether all its leaves are currently on.</summary>
    public string ToggleItem(KapeCatalog catalog, PackageDefinition package, CatalogItem item, ItemKind kind)
    {
        var list = kind == ItemKind.Target ? package.Targets : package.Modules;
        var keys = KapeCatalog.BuildSelectionKeys(list);
        var refName = Path.GetFileName(item.RelativePath);
        var leaves = item.IsCompound
            ? catalog.FlattenToLeaves(new[] { refName }, kind)
            : new List<CatalogItem> { item };
        var allOn = leaves.Count > 0 && leaves.All(l => KapeCatalog.IsSelected(l, keys));
        return SetItemSelected(catalog, package, item, kind, !allOn);
    }

    public void MergeEntries(PackageDefinition package, ItemKind kind, IEnumerable<SelectionEntry> incoming)
    {
        if (kind == ItemKind.Target)
            package.Targets = KapeCatalog.MergeEntries(package.Targets, incoming);
        else
            package.Modules = KapeCatalog.MergeEntries(package.Modules, incoming);
    }

    public void ReplaceEntries(PackageDefinition package, ItemKind kind, List<SelectionEntry> entries)
    {
        if (kind == ItemKind.Target) package.Targets = entries;
        else package.Modules = entries;
    }

    public void Clear(PackageDefinition package, ItemKind kind)
    {
        if (kind == ItemKind.Target) package.Targets.Clear();
        else package.Modules.Clear();
    }

    public SelectionEntry ToSelectionEntry(CatalogItem item) => new()
    {
        Name = item.Name,
        Category = string.IsNullOrWhiteSpace(item.Category) ? "General" : item.Category,
        Path = Path.GetFileName(item.RelativePath)
    };

    private sealed class SuppressScope : IDisposable
    {
        private CatalogSelectionCoordinator? _owner;

        public SuppressScope(CatalogSelectionCoordinator owner) => _owner = owner;

        public void Dispose()
        {
            var o = Interlocked.Exchange(ref _owner, null);
            if (o is null) return;
            if (o._suppressDepth > 0)
                o._suppressDepth--;
        }
    }
}
