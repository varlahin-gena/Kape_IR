using CommunityToolkit.Mvvm.ComponentModel;
using KapePackBuilder.Models;

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
