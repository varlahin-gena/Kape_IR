using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
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

    private void TargetList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        => TryToggleRowFromClick(e, Models.ItemKind.Target);

    private void ModuleList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        => TryToggleRowFromClick(e, Models.ItemKind.Module);

    private void TryToggleRowFromClick(MouseButtonEventArgs e, Models.ItemKind kind)
    {
        // Checkbox handles itself via Checked/Unchecked.
        if (FindVisualParent<CheckBox>(e.OriginalSource as DependencyObject) is not null)
            return;

        var row = FindRowVm(e.OriginalSource as DependencyObject);
        if (row is null) return;

        Vm.ToggleRowFromListClick(row, kind);
        Vm.ShowItemInfo(row.Item);
    }

    private void Catalog_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListView { SelectedItem: CatalogRowVm row })
            Vm.ShowItemInfo(row.Item);
    }

    private void Module_MouseDoubleClick(object sender, MouseButtonEventArgs e)
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

    private void Tree_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is TreeView { SelectedItem: TreeNodeVm node })
            Vm.ShowItemInfo(node.Item);
    }

    private static CatalogRowVm? FindRowVm(DependencyObject? start)
    {
        for (var d = start; d is not null; d = VisualTreeHelper.GetParent(d))
        {
            if (d is FrameworkElement { DataContext: CatalogRowVm row })
                return row;
        }
        return null;
    }

    private static T? FindVisualParent<T>(DependencyObject? child) where T : DependencyObject
    {
        for (var d = child; d is not null; d = VisualTreeHelper.GetParent(d))
        {
            if (d is T match) return match;
        }
        return null;
    }
}
