using CommunityToolkit.Mvvm.ComponentModel;
using KapePack.Core.Models;

namespace KapePackBuilder.ViewModels;

public partial class CatalogRowVm : ObservableObject
{
    public CatalogItem Item { get; }
    [ObservableProperty] private bool _isSelected;
    public string DisplayName => Item.DisplayName;
    public string OriginLabel => Item.OriginLabel;
    public string Meta => $"{Item.Category}  ·  {Item.OriginLabel}  ·  {Item.RelativePath}";

    /// <summary>Direct child refs count (compound nesting inward).</summary>
    public int ChildCount { get; }

    /// <summary>Parent compounds that include this item (direct + transitive).</summary>
    public string UsedByDisplay { get; }

    /// <summary>корневой / вложенный — based on direct parents.</summary>
    public string NestingRole { get; }

    public CatalogRowVm(CatalogItem item, bool selected)
        : this(item, selected, childCount: item.Children.Count, usedBy: Array.Empty<string>(), directParentCount: 0)
    {
    }

    public CatalogRowVm(
        CatalogItem item,
        bool selected,
        int childCount,
        IReadOnlyList<string> usedBy,
        int directParentCount)
    {
        Item = item;
        _isSelected = selected;
        ChildCount = childCount;
        NestingRole = directParentCount == 0 ? "корневой" : "вложенный";
        UsedByDisplay = usedBy.Count == 0
            ? "—"
            : usedBy.Count <= 4
                ? string.Join(", ", usedBy)
                : string.Join(", ", usedBy.Take(4)) + $" +{usedBy.Count - 4}";
    }
}
