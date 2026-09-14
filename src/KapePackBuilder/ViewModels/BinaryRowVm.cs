using CommunityToolkit.Mvvm.ComponentModel;
using KapePack.Core.Services;

namespace KapePackBuilder.ViewModels;

public partial class BinaryRowVm : ObservableObject
{
    public ModulesBinItem Item { get; }

    public string Name => Item.Name;
    public string Group => Item.Group;
    public string Category => Item.Category;
    public string SizeDisplay => Item.SizeDisplay;
    public string ModifiedDisplay => Item.ModifiedLocalDisplay;
    public string VersionDisplay => Item.VersionDisplay;
    public string RelativePath => Item.RelativePath;
    public string KeyMark => Item.IsKeyTool ? "★" : "";

    public BinaryRowVm(ModulesBinItem item) => Item = item;
}
