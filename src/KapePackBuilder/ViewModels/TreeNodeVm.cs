using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using KapePackBuilder.Models;

namespace KapePackBuilder.ViewModels;

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
