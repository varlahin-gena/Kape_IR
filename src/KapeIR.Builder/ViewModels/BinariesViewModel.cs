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

    [ObservableProperty] private string _colFilterName = "";
    [ObservableProperty] private string _colFilterCategory = "";
    [ObservableProperty] private string _colFilterGroup = "";
    [ObservableProperty] private string _colFilterVersion = "";
    [ObservableProperty] private string _colFilterSize = "";
    [ObservableProperty] private string _colFilterModified = "";
    [ObservableProperty] private string _colFilterPath = "";

    private readonly ColumnSortState _sort = new();
    private readonly IUiDebounce _colFilterDebounce;

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

    public string SortNameHeader => _sort.Label("Имя", "Name");
    public string SortCategoryHeader => _sort.Label("Категория", "Category");
    public string SortGroupHeader => _sort.Label("Группа", "Group");
    public string SortVersionHeader => _sort.Label("Версия", "Version");
    public string SortSizeHeader => _sort.Label("Размер", "Size");
    public string SortModifiedHeader => _sort.Label("Изменён", "Modified");
    public string SortPathHeader => _sort.Label("Путь", "Path");

    internal BinariesViewModel(IBuilderShell shell)
    {
        _shell = shell ?? throw new ArgumentNullException(nameof(shell));
        _binarySearchDebounce = _shell.Ui.CreateDebounce(IUiScheduler.DefaultDebounceDelay);
        _colFilterDebounce = _shell.Ui.CreateDebounce(IUiScheduler.DefaultDebounceDelay);
    }

    partial void OnBinarySearchChanged(string value) => _binarySearchDebounce.Schedule(RefreshBinaryRows);
    partial void OnBinaryFilterChanged(string value) => RefreshBinaryRows();
    partial void OnColFilterNameChanged(string value) => _colFilterDebounce.Schedule(RefreshBinaryRows);
    partial void OnColFilterCategoryChanged(string value) => _colFilterDebounce.Schedule(RefreshBinaryRows);
    partial void OnColFilterGroupChanged(string value) => _colFilterDebounce.Schedule(RefreshBinaryRows);
    partial void OnColFilterVersionChanged(string value) => _colFilterDebounce.Schedule(RefreshBinaryRows);
    partial void OnColFilterSizeChanged(string value) => _colFilterDebounce.Schedule(RefreshBinaryRows);
    partial void OnColFilterModifiedChanged(string value) => _colFilterDebounce.Schedule(RefreshBinaryRows);
    partial void OnColFilterPathChanged(string value) => _colFilterDebounce.Schedule(RefreshBinaryRows);

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

        // Top search: Name only.
        if (q.Length > 0)
            list = list.Where(i => i.Name.Contains(q, StringComparison.OrdinalIgnoreCase));

        IEnumerable<BinaryRowVm> rows = list.Select(i => new BinaryRowVm(i));
        rows = rows.Where(r =>
            ColumnListOps.Matches(r.Name, ColFilterName) &&
            ColumnListOps.Matches(r.Category, ColFilterCategory) &&
            ColumnListOps.Matches(r.Group, ColFilterGroup) &&
            ColumnListOps.Matches(r.VersionDisplay, ColFilterVersion) &&
            ColumnListOps.Matches(r.SizeDisplay, ColFilterSize) &&
            ColumnListOps.Matches(r.ModifiedDisplay, ColFilterModified) &&
            ColumnListOps.Matches(r.RelativePath, ColFilterPath));

        if (_sort is { Dir: not ColumnSortDir.None, Key: not null })
        {
            rows = _sort.Key switch
            {
                "Name" => ColumnListOps.SortBy(rows, _sort, r => r.Name),
                "Category" => ColumnListOps.SortBy(rows, _sort, r => r.Category),
                "Group" => ColumnListOps.SortBy(rows, _sort, r => r.Group),
                "Version" => ColumnListOps.SortBy(rows, _sort, r => r.VersionDisplay),
                "Size" => ColumnListOps.SortByComparable(rows, _sort, r => r.Item.SizeBytes),
                "Modified" => ColumnListOps.SortByComparable(rows, _sort, r => r.Item.ModifiedUtc),
                "Path" => ColumnListOps.SortBy(rows, _sort, r => r.RelativePath),
                _ => rows
            };
        }

        var selectedPath = SelectedBinary?.Item.AbsolutePath;
        BinaryRows.Clear();
        BinaryRowVm? reselect = null;
        foreach (var row in rows)
        {
            BinaryRows.Add(row);
            if (selectedPath is not null &&
                row.Item.AbsolutePath.Equals(selectedPath, StringComparison.OrdinalIgnoreCase))
                reselect = row;
        }

        SelectedBinary = reselect;
        if (BinaryRows.Count == 0 && _allBinaries.Count > 0)
            BinaryDetailText = "Нет строк по текущему фильтру/поиску.";
    }

    [RelayCommand]
    private void SortBinaries(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return;
        _sort.Toggle(key);
        OnPropertyChanged(nameof(SortNameHeader));
        OnPropertyChanged(nameof(SortCategoryHeader));
        OnPropertyChanged(nameof(SortGroupHeader));
        OnPropertyChanged(nameof(SortVersionHeader));
        OnPropertyChanged(nameof(SortSizeHeader));
        OnPropertyChanged(nameof(SortModifiedHeader));
        OnPropertyChanged(nameof(SortPathHeader));
        RefreshBinaryRows();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _binarySearchDebounce.Dispose();
        _colFilterDebounce.Dispose();
        GC.SuppressFinalize(this);
    }
}
