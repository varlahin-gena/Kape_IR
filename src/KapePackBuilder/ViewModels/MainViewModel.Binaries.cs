using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KapePack.Core.Services;
using KapePackBuilder.Services;

namespace KapePackBuilder.ViewModels;

public partial class MainViewModel
{
    private DispatcherTimer? _binarySearchTimer;
    private List<ModulesBinItem> _allBinaries = new();

    [ObservableProperty] private string _binarySearch = "";
    [ObservableProperty] private string _binaryFilter = "Все";
    [ObservableProperty] private string _binariesSummary = "Modules\\bin не загружен.";
    [ObservableProperty] private string _binaryDetailText = "Выберите утилиту, чтобы увидеть сведения.";
    [ObservableProperty] private BinaryRowVm? _selectedBinary;
    [ObservableProperty] private bool _isLoadingBinaries;

    public ObservableCollection<BinaryRowVm> BinaryRows { get; } = new();
    public List<string> BinaryFilterOptions { get; } = new()
    {
        "Все",
        "Только EXE",
        "Ключевые EZ",
        "EZ Tools",
        "Chainsaw",
        "Sysinternals",
        "Скрипты"
    };

    partial void OnBinarySearchChanged(string value) => Debounce(ref _binarySearchTimer, RefreshBinaryRows);
    partial void OnBinaryFilterChanged(string value) => RefreshBinaryRows();

    partial void OnSelectedBinaryChanged(BinaryRowVm? value)
    {
        if (value is null)
        {
            BinaryDetailText = "Выберите утилиту, чтобы увидеть сведения.";
            return;
        }

        var i = value.Item;
        var sb = new StringBuilder();
        sb.AppendLine(i.Name);
        sb.AppendLine($"Путь: {i.RelativePath}");
        sb.AppendLine($"Группа: {i.Group}");
        sb.AppendLine($"Категория: {i.Category}");
        sb.AppendLine($"Размер: {i.SizeDisplay} ({i.SizeBytes} байт)");
        sb.AppendLine($"Изменён: {i.ModifiedLocalDisplay} (локальное время)");
        if (i.IsKeyTool) sb.AppendLine("Ключевой парсер для !EZParser-сценариев: да");
        if (!string.IsNullOrEmpty(i.ProductName)) sb.AppendLine($"Product: {i.ProductName}");
        if (!string.IsNullOrEmpty(i.Description)) sb.AppendLine($"Description: {i.Description}");
        if (!string.IsNullOrEmpty(i.Company)) sb.AppendLine($"Company: {i.Company}");
        if (!string.IsNullOrEmpty(i.FileVersion)) sb.AppendLine($"FileVersion: {i.FileVersion}");
        if (!string.IsNullOrEmpty(i.ProductVersion)) sb.AppendLine($"ProductVersion: {i.ProductVersion}");
        sb.AppendLine($"Полный путь: {i.AbsolutePath}");
        BinaryDetailText = sb.ToString().TrimEnd();
    }

    [RelayCommand]
    private async Task ReloadBinariesAsync()
    {
        await LoadBinariesAsync(updateStatus: true);
    }

    [RelayCommand]
    private void OpenModulesBinFolder()
    {
        var bin = Path.Combine(KapeRoot ?? "", "Modules", "bin");
        if (!Directory.Exists(bin))
        {
            _dialogs.ShowMessage($"Папка не найдена:\n{bin}", "Modules\\bin", DialogIcon.Warning);
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = "\"" + bin + "\"",
            UseShellExecute = true
        });
    }

    private async Task LoadBinariesAsync(bool updateStatus)
    {
        var root = (KapeRoot ?? "").Trim();
        if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
        {
            _allBinaries = new();
            BinaryRows.Clear();
            BinariesSummary = "Укажите корень KAPE.";
            return;
        }

        IsLoadingBinaries = true;
        if (updateStatus)
            StatusText = "Сканирование Modules\\bin…";

        try
        {
            var report = await Task.Run(() => ModulesBinInventory.Scan(root));
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                _allBinaries = report.Items;
                BinariesSummary = report.Message;
                RefreshBinaryRows();
                if (updateStatus)
                    StatusText = report.Message;
            });
        }
        catch (Exception ex)
        {
            BinariesSummary = "Ошибка чтения Modules\\bin: " + ex.Message;
            if (updateStatus)
                StatusText = BinariesSummary;
        }
        finally
        {
            IsLoadingBinaries = false;
        }
    }

    private void RefreshBinaryRows()
    {
        var q = (BinarySearch ?? "").Trim();
        IEnumerable<ModulesBinItem> list = _allBinaries;

        list = BinaryFilter switch
        {
            "Только EXE" => list.Where(i => i.Kind == ModulesBinKind.Executable),
            "Ключевые EZ" => list.Where(i => i.IsKeyTool),
            "EZ Tools" => list.Where(i => i.Category.Equals("EZ Tools", StringComparison.OrdinalIgnoreCase)),
            "Chainsaw" => list.Where(i => i.Category.Equals("Chainsaw", StringComparison.OrdinalIgnoreCase)),
            "Sysinternals" => list.Where(i => i.Category.Equals("Sysinternals", StringComparison.OrdinalIgnoreCase)),
            "Скрипты" => list.Where(i => i.Kind == ModulesBinKind.Script),
            _ => list
        };

        if (q.Length > 0)
        {
            list = list.Where(i =>
                i.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                i.RelativePath.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                i.Category.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                (i.ProductName?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (i.Description?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (i.FileVersion?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        var selectedPath = SelectedBinary?.Item.AbsolutePath;
        BinaryRows.Clear();
        BinaryRowVm? reselect = null;
        foreach (var item in list)
        {
            var row = new BinaryRowVm(item);
            BinaryRows.Add(row);
            if (selectedPath is not null &&
                item.AbsolutePath.Equals(selectedPath, StringComparison.OrdinalIgnoreCase))
                reselect = row;
        }

        SelectedBinary = reselect;
        if (BinaryRows.Count == 0 && _allBinaries.Count > 0)
            BinaryDetailText = "Нет строк по текущему фильтру/поиску.";
    }
}
