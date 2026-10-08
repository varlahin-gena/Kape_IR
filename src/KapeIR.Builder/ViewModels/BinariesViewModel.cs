using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KapeIR.Core.Services;
using KapeIR.Builder.Services;
using KapeIR.Ui.Dialogs;
using KapeIR.Ui.Scheduling;

namespace KapeIR.Builder.ViewModels;

public sealed partial class BinariesViewModel : ObservableObject, IDisposable
{
    private readonly IBuilderShell _shell;
    private readonly IUiDebounce _binarySearchDebounce;
    private List<ModulesBinItem> _allBinaries = new();
    private bool _disposed;

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
        "Hayabusa",
        "Sysinternals",
        "Скрипты"
    };

    internal BinariesViewModel(IBuilderShell shell)
    {
        _shell = shell ?? throw new ArgumentNullException(nameof(shell));
        _binarySearchDebounce = _shell.Ui.CreateDebounce(IUiScheduler.DefaultDebounceDelay);
    }

    partial void OnBinarySearchChanged(string value) => _binarySearchDebounce.Schedule(RefreshBinaryRows);
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
    private void OpenModulesBinFolder()
    {
        var bin = Path.Combine(_shell.KapeRoot ?? "", "Modules", "bin");
        if (!Directory.Exists(bin))
        {
            _shell.Dialogs.ShowMessage($"Папка не найдена:\n{bin}", "Modules\\bin", DialogIcon.Warning);
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = "\"" + bin + "\"",
            UseShellExecute = true
        });
    }

    internal async Task LoadBinariesAsync(bool updateStatus)
    {
        var root = (_shell.KapeRoot ?? "").Trim();
        if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
        {
            _allBinaries = new();
            BinaryRows.Clear();
            BinariesSummary = "Укажите корень KAPE.";
            return;
        }

        IsLoadingBinaries = true;
        if (updateStatus)
            _shell.StatusText = "Сканирование Modules\\bin…";

        try
        {
            var report = await Task.Run(() => ModulesBinInventory.Scan(root));
            await _shell.InvokeOnUiAsync(() =>
            {
                _allBinaries = report.Items;
                BinariesSummary = report.Message;
                RefreshBinaryRows();
                if (updateStatus)
                    _shell.StatusText = report.Message;
            });
        }
        catch (Exception ex)
        {
            BinariesSummary = "Ошибка чтения Modules\\bin: " + ex.Message;
            if (updateStatus)
                _shell.StatusText = BinariesSummary;
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
            "Hayabusa" => list.Where(i => i.Category.Equals("Hayabusa", StringComparison.OrdinalIgnoreCase)),
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

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _binarySearchDebounce.Dispose();
        GC.SuppressFinalize(this);
    }
}
