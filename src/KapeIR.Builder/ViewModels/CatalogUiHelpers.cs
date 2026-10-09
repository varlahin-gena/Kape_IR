using System.Collections.ObjectModel;
using KapeIR.Core.Models;
using KapeIR.Core.Services;

namespace KapeIR.Builder.ViewModels;

/// <summary>Pure list/tree construction for catalog UI (keeps MainViewModel thinner).</summary>
public static class CatalogUiHelpers
{
    public static bool? ParseFilter(string filter) => filter switch
    {
        "Только compound" => true,
        "Только leaf" => false,
        _ => null
    };

    public static List<CatalogRowVm> BuildFilteredRows(
        KapeCatalog catalog,
        ItemKind kind,
        string search,
        string filter,
        IEnumerable<SelectionEntry> selection,
        string? colName = null,
        string? colOrigin = null,
        string? colMeta = null,
        ColumnSortState? sort = null)
    {
        var compoundsOnly = ParseFilter(filter);
        var selectedOnly = filter == "Только выбранные";
        var keys = KapeCatalog.BuildSelectionKeys(selection);
        var items = (kind == ItemKind.Target
                ? catalog.FilterTargets(search, selectedOnly ? null : compoundsOnly)
                : catalog.FilterModules(search, selectedOnly ? null : compoundsOnly))
            .ToList();
        if (selectedOnly)
            items = items.Where(i => KapeCatalog.IsSelected(i, keys)).ToList();

        IEnumerable<CatalogRowVm> rows = items.Select(i => new CatalogRowVm(i, KapeCatalog.IsSelected(i, keys)));
        rows = rows.Where(r =>
            ColumnListOps.Matches(r.Item.Name, colName) &&
            ColumnListOps.Matches(r.OriginLabel, colOrigin) &&
            ColumnListOps.Matches(r.Meta, colMeta));

        if (sort is { Dir: not ColumnSortDir.None, Key: not null })
        {
            rows = sort.Key switch
            {
                "Name" => ColumnListOps.SortBy(rows, sort, r => r.Item.Name),
                "Origin" => ColumnListOps.SortBy(rows, sort, r => r.OriginLabel),
                "Meta" => ColumnListOps.SortBy(rows, sort, r => r.Meta),
                _ => rows
            };
        }

        return rows.ToList();
    }

    public static List<CatalogRowVm> BuildExistingPackRows(
        KapeCatalog catalog,
        string? nameSearch = null,
        string? colName = null,
        string? colRole = null,
        string? colChildren = null,
        string? colUsedBy = null,
        string? colOrigin = null,
        string? colDescription = null,
        ColumnSortState? sort = null)
    {
        var rows = new List<CatalogRowVm>();
        foreach (var c in catalog.Compounds(ItemKind.Target))
        {
            var usedBy = catalog.IncludingCompounds(c.Name, ItemKind.Target);
            var direct = catalog.DirectParents(c.Name, ItemKind.Target);
            rows.Add(new CatalogRowVm(
                c,
                selected: false,
                childCount: c.Children.Count,
                usedBy: usedBy,
                directParentCount: direct.Count));
        }

        IEnumerable<CatalogRowVm> filtered = rows;
        if (!string.IsNullOrWhiteSpace(nameSearch))
            filtered = filtered.Where(r => ColumnListOps.Matches(r.Item.Name, nameSearch));

        filtered = filtered.Where(r =>
            ColumnListOps.Matches(r.Item.Name, colName) &&
            ColumnListOps.Matches(r.NestingRole, colRole) &&
            ColumnListOps.Matches(r.ChildCount.ToString(), colChildren) &&
            ColumnListOps.Matches(r.UsedByDisplay, colUsedBy) &&
            ColumnListOps.Matches(r.OriginLabel, colOrigin) &&
            ColumnListOps.Matches(r.Item.Description, colDescription));

        if (sort is { Dir: not ColumnSortDir.None, Key: not null })
        {
            filtered = sort.Key switch
            {
                "Name" => ColumnListOps.SortBy(filtered, sort, r => r.Item.Name),
                "Role" => ColumnListOps.SortBy(filtered, sort, r => r.NestingRole),
                "Children" => ColumnListOps.SortByComparable(filtered, sort, r => r.ChildCount),
                "UsedBy" => ColumnListOps.SortBy(filtered, sort, r => r.UsedByDisplay),
                "Origin" => ColumnListOps.SortBy(filtered, sort, r => r.OriginLabel),
                "Description" => ColumnListOps.SortBy(filtered, sort, r => r.Item.Description ?? ""),
                _ => filtered.OrderBy(r => r.Item.Name, StringComparer.OrdinalIgnoreCase)
            };
        }
        else
        {
            filtered = filtered.OrderBy(r => r.Item.Name, StringComparer.OrdinalIgnoreCase);
        }

        return filtered.ToList();
    }

    public static List<TreeNodeVm> BuildTreeRoots(
        KapeCatalog catalog,
        ItemKind kind,
        IEnumerable<SelectionEntry> selection,
        string treeSearch,
        bool sharedOnly,
        Func<CatalogItem, ItemKind, HashSet<string>, string, bool, int, HashSet<string>, TreeNodeVm?> buildNode)
    {
        var compounds = catalog.Compounds(kind).ToList();
        var childNames = compounds
            .SelectMany(c => c.Children)
            .Select(c => Path.GetFileNameWithoutExtension(c).ToLowerInvariant())
            .ToHashSet();

        var roots = compounds.Where(c => !childNames.Contains(c.Name.ToLowerInvariant())).ToList();
        foreach (var c in compounds.Where(c => c.Name.StartsWith('!') && !roots.Contains(c)))
            roots.Add(c);

        var keys = KapeCatalog.BuildSelectionKeys(selection);
        var query = treeSearch.Trim().ToLowerInvariant();
        var result = new List<TreeNodeVm>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots.OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase))
        {
            if (!seen.Add(root.AbsolutePath)) continue;
            var node = buildNode(root, kind, keys, query, sharedOnly, 0, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
            if (node is not null)
                result.Add(node);
        }

        return result;
    }

    public static void FillObservable<T>(ObservableCollection<T> target, IEnumerable<T> items)
    {
        target.Clear();
        foreach (var i in items)
            target.Add(i);
    }
}
