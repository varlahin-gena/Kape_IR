using System.Windows;
using System.Windows.Controls;
using KapePackBuilder.ViewModels;

namespace KapePackBuilder;

public partial class MainWindow : Window
{
    private MainViewModel Vm => (MainViewModel)DataContext;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        await Vm.InitializeAsync();
    }

    private void TargetCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox { DataContext: CatalogRowVm row })
            Vm.ToggleCatalogRow(row, Models.ItemKind.Target);
    }

    private void ModuleCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox { DataContext: CatalogRowVm row })
            Vm.ToggleCatalogRow(row, Models.ItemKind.Module);
    }

    private void TreeCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox { DataContext: TreeNodeVm node })
            Vm.ToggleTreeNode(node);
    }

    private void Catalog_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is ListView { SelectedItem: CatalogRowVm row })
            Vm.ShowItemInfo(row.Item);
    }

    private void Module_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is ListView { SelectedItem: CatalogRowVm row })
            Vm.ShowItemInfo(row.Item);
    }

    private void TargetList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ListView { SelectedItem: CatalogRowVm row })
            Vm.ShowItemInfo(row.Item);
    }

    private void ModuleList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ListView { SelectedItem: CatalogRowVm row })
            Vm.ShowItemInfo(row.Item);
    }

    private void Tree_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is TreeView { SelectedItem: TreeNodeVm node })
            Vm.ShowItemInfo(node.Item);
    }
}
