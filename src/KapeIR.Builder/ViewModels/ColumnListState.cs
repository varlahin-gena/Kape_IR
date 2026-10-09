namespace KapeIR.Builder.ViewModels;

public enum ColumnSortDir
{
    None = 0,
    Asc = 1,
    Desc = 2
}

/// <summary>Sort key + direction for a GridView; toggles Asc ↔ Desc on repeat click.</summary>
public sealed class ColumnSortState
{
    public string? Key { get; private set; }
    public ColumnSortDir Dir { get; private set; } = ColumnSortDir.None;

    public void Toggle(string key)
    {
        if (!string.Equals(Key, key, StringComparison.Ordinal))
        {
            Key = key;
            Dir = ColumnSortDir.Asc;
            return;
        }

        Dir = Dir switch
        {
            ColumnSortDir.Asc => ColumnSortDir.Desc,
            ColumnSortDir.Desc => ColumnSortDir.Asc,
            _ => ColumnSortDir.Asc
        };
    }

    public string Label(string title, string key)
    {
        if (!string.Equals(Key, key, StringComparison.Ordinal) || Dir == ColumnSortDir.None)
            return title;
        return Dir == ColumnSortDir.Asc ? title + " ▴" : title + " ▾";
    }
}

public static class ColumnListOps
{
    public static bool Matches(string? haystack, string? filter)
    {
        var f = (filter ?? "").Trim();
        if (f.Length == 0) return true;
        return (haystack ?? "").Contains(f, StringComparison.OrdinalIgnoreCase);
    }

    public static IEnumerable<T> SortBy<T>(
        IEnumerable<T> rows,
        ColumnSortState sort,
        Func<T, string> keySelector)
    {
        if (sort.Dir == ColumnSortDir.None || string.IsNullOrEmpty(sort.Key))
            return rows;
        return sort.Dir == ColumnSortDir.Asc
            ? rows.OrderBy(keySelector, StringComparer.OrdinalIgnoreCase)
            : rows.OrderByDescending(keySelector, StringComparer.OrdinalIgnoreCase);
    }

    public static IEnumerable<T> SortByComparable<T, TKey>(
        IEnumerable<T> rows,
        ColumnSortState sort,
        Func<T, TKey> keySelector)
        where TKey : IComparable<TKey>
    {
        if (sort.Dir == ColumnSortDir.None || string.IsNullOrEmpty(sort.Key))
            return rows;
        return sort.Dir == ColumnSortDir.Asc
            ? rows.OrderBy(keySelector)
            : rows.OrderByDescending(keySelector);
    }
}
