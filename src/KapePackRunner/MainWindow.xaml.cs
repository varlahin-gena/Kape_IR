using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using KapePack.Core.Models;
using KapePack.Core.Services;
using Microsoft.Win32;

namespace KapePackRunner;

public partial class MainWindow : Window
{
    private string? _resultsDir;
    private string? _packageDir;
    private CollectionPlan.LaunchManifest? _cfg;
    private Process? _kapeProcess;
    private CancellationTokenSource? _runCts;
    private bool _running;
    private readonly StringBuilder _logBuffer = new();
    private readonly CollectPackProgressParser _progressParser = new();
    private readonly CollectionProgressState _progressState = new();
    private bool _copyPulseActive;
    private DispatcherTimer? _copyPulseTimer;
    private string _selectedTsource = "C:";

    public MainWindow()
    {
        InitializeComponent();
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await PrepareAsync();
    }

    private async Task PrepareAsync()
    {
        try
        {
            var self = Environment.ProcessPath
                       ?? throw new InvalidOperationException("Не удалось определить путь к EXE.");

            TitleText.Text = Path.GetFileNameWithoutExtension(self);
            SetStatus("Распаковка пакета…", indeterminate: true);

            if (File.Exists(self + ".sha256"))
            {
                if (!FileHash.TryVerifySidecar(self, out var verifyMsg, requireSidecar: false))
                {
                    Fail(verifyMsg + "\n\nПоложите корректный CollectPack.exe.sha256 рядом с EXE или пересоберите пакет.");
                    return;
                }

                Log(verifyMsg);
            }

            var prep = await Task.Run(() =>
                CollectPackPrepare.Prepare(
                    self,
                    tsourceOverride: null,
                    requireTsource: false,
                    log: msg => Dispatcher.BeginInvoke(() => Log(msg))));

            if (prep.ExitCode != 0)
            {
                Fail(prep.Message);
                return;
            }

            _packageDir = prep.PackageDir;
            _cfg = prep.Manifest;

            Title = $"KAPE Pack — {_cfg!.Name}";
            TitleText.Text = _cfg.Name;
            if (_cfg.CollectionMode == IrCollectionMode.TwoPhase)
            {
                SubtitleText.Text =
                    $"two_phase: {_cfg.Phase1Module} → {_cfg.Target}" +
                    (string.IsNullOrWhiteSpace(_cfg.Phase2Module) ? "" : $" + {_cfg.Phase2Module}");
                TwoPhasePanel.Visibility = Visibility.Visible;
                CaseIdBox.Text = _cfg.CaseId ?? "";
            }
            else
            {
                SubtitleText.Text = $"Target: {_cfg.Target}" +
                                    (string.IsNullOrWhiteSpace(_cfg.Module) ? "" : $"  ·  Module: {_cfg.Module}");
                TwoPhasePanel.Visibility = Visibility.Collapsed;
            }

            var initialTs = string.IsNullOrWhiteSpace(_cfg.Tsource) ? "C:" : _cfg.Tsource;
            PopulateDrives(initialTs);
            ResultsBox.Text = _packageDir ?? "";
            SourcePanel.Visibility = Visibility.Visible;
            SetStatus("Выберите диск и папку результатов, затем «Начать сбор» (или «Оценить»)", indeterminate: false, percent: 0);
            PercentText.Text = "";
            SaveLogBtn.IsEnabled = true;
            CloseBtn.IsEnabled = true;
            Log("Пакет готов. Выберите диск и папку результатов. Можно сначала оценить объём (--sim).");
            if (_cfg.CollectionMode == IrCollectionMode.TwoPhase)
                Log("Режим two_phase: сначала volatile (Phase1), затем disk triage (Phase2).");
            Log($"Результаты по умолчанию: {Path.Combine(_packageDir!, "RESULTS", Environment.MachineName)}");
        }
        catch (Exception ex)
        {
            Fail(ex.Message);
        }
    }

    private async void Sim_Click(object sender, RoutedEventArgs e)
    {
        if (_running || _cfg is null || _packageDir is null) return;
        if (!TryApplyPaths()) return;

        await RunCollectionAsync(simulateOnly: true);
    }

    private async void Start_Click(object sender, RoutedEventArgs e)
    {
        if (_running || _cfg is null || _packageDir is null) return;
        if (!TryApplyPaths()) return;

        if (SimBeforeCheck.IsChecked == true)
        {
            var simOk = await RunCollectionAsync(simulateOnly: true);
            if (!simOk) return;
            var cont = MessageBox.Show(
                "Оценка (--sim) завершена — см. журнал.\n\nЗапустить полный сбор с копированием файлов?",
                "KAPE Pack",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (cont != MessageBoxResult.Yes)
            {
                Log("Полный сбор отменён после оценки.");
                return;
            }
        }

        await RunCollectionAsync(simulateOnly: false);
    }

    private string? _resultsRoot;

    private bool TryApplyPaths()
    {
        var tsource = _selectedTsource.Trim();
        if (DriveCombo.SelectedItem is DriveInventory.DriveItem drive && !string.IsNullOrWhiteSpace(drive.Root))
            tsource = drive.Root.Trim();

        if (string.IsNullOrWhiteSpace(tsource))
        {
            MessageBox.Show("Выберите диск для сбора.", "KAPE Pack",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        var results = (ResultsBox.Text ?? "").Trim();
        if (string.IsNullOrWhiteSpace(results))
        {
            MessageBox.Show("Укажите папку для результатов (RESULTS).", "KAPE Pack",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        try
        {
            results = Path.GetFullPath(results);
        }
        catch (Exception ex)
        {
            MessageBox.Show("Некорректный путь результатов: " + ex.Message, "KAPE Pack",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        try
        {
            Directory.CreateDirectory(results);
        }
        catch (Exception ex)
        {
            MessageBox.Show("Не удалось создать папку результатов:\n" + ex.Message, "KAPE Pack",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        _cfg!.Tsource = tsource;
        _selectedTsource = tsource;
        _resultsRoot = results;
        ResultsBox.Text = results;
        return true;
    }

    private void BrowseResults_Click(object sender, RoutedEventArgs e)
    {
        var folder = PickFolder("Выберите папку для RESULTS",
            GuessInitialDir(ResultsBox.Text) ?? _packageDir);
        if (folder is null) return;
        ResultsBox.Text = folder;
    }

    private static string? GuessInitialDir(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        try
        {
            var full = Path.GetFullPath(path.Trim());
            if (Directory.Exists(full)) return full;
            var parent = Path.GetDirectoryName(full);
            return Directory.Exists(parent) ? parent : null;
        }
        catch
        {
            return null;
        }
    }

    private static string? PickFolder(string title, string? initialDirectory)
    {
        var dlg = new OpenFolderDialog { Title = title };
        if (!string.IsNullOrWhiteSpace(initialDirectory) && Directory.Exists(initialDirectory))
            dlg.InitialDirectory = initialDirectory;
        return dlg.ShowDialog() == true ? dlg.FolderName : null;
    }

    /// <returns>False if failed or cancelled mid-run setup; true if process finished (any exit code).</returns>
    private async Task<bool> RunCollectionAsync(bool simulateOnly)
    {
        if (_running || _cfg is null || _packageDir is null) return false;

        SourcePanel.IsEnabled = false;
        StartBtn.IsEnabled = false;
        SimBtn.IsEnabled = false;
        CancelRunBtn.IsEnabled = true;
        _running = true;
        _runCts?.Dispose();
        _runCts = new CancellationTokenSource();
        var ct = _runCts.Token;
        _progressParser.ResetCounters();
        StopCopyPulse();
        var twoPhase = _cfg.CollectionMode == IrCollectionMode.TwoPhase
                       && Phase1OnlyCheck.IsChecked != true
                       && Phase2OnlyCheck.IsChecked != true;
        if (twoPhase)
            BeginPhaseRange(0, 15, "Фаза 1…");
        else
            BeginPhaseRange(0, 99, simulateOnly ? "Оценка…" : "Сбор…");

        try
        {
            var kape = Path.Combine(_packageDir, "kape.exe");
            int? phaseFilter = null;
            if (_cfg.CollectionMode == IrCollectionMode.TwoPhase)
            {
                if (Phase1OnlyCheck.IsChecked == true) phaseFilter = 1;
                else if (Phase2OnlyCheck.IsChecked == true) phaseFilter = 2;
            }

            var rt = new CollectionPlan.RuntimeOptions(
                _cfg.Tsource,
                Simulate: simulateOnly,
                PhaseFilter: phaseFilter,
                SkipMemory: SkipMemoryCheck.IsChecked == true,
                CaseIdOverride: string.IsNullOrWhiteSpace(CaseIdBox.Text) ? null : CaseIdBox.Text.Trim(),
                ResultsRoot: _resultsRoot);

            SetStatus(simulateOnly ? "Оценка объёма (--sim)…" : "Сбор артефактов…", indeterminate: true);
            PercentText.Text = "…";

            var self = Environment.ProcessPath;
            var result = await CollectionRunner.RunAsync(
                _packageDir,
                kape,
                _cfg,
                rt,
                self,
                Log,
                async (exe, args, wd, token) =>
                {
                    Log(simulateOnly
                        ? $"Оценка: kape.exe {string.Join(" ", args.Select(Quote))}"
                        : $"Команда: kape.exe {string.Join(" ", args.Select(Quote))}");
                    return await RunKapeAsync(exe, args, wd, token);
                },
                ct);

            _resultsDir = result.ResultsDir;

            if (simulateOnly)
            {
                SetStatus(result.ExitCode == 0 ? "Оценка завершена" : $"Оценка: код {result.ExitCode}",
                    indeterminate: false, percent: 100);
                ProgressBar.Foreground = new SolidColorBrush(
                    result.ExitCode == 0 ? Color.FromRgb(0x3F, 0xB9, 0x50) : Color.FromRgb(0xF8, 0x51, 0x49));
                Log(result.ExitCode == 0
                    ? "Оценка (--sim) завершена. Файлы не копировались."
                    : $"Оценка завершилась с кодом {result.ExitCode}.");
                return result.ExitCode == 0;
            }

            if (_resultsDir is not null && Directory.Exists(_resultsDir))
            {
                SetStatus(result.ExitCode == 0 ? "Готово" : $"Готово с ошибками (код {result.ExitCode})",
                    indeterminate: false, percent: 100);
                ProgressBar.Foreground = new SolidColorBrush(
                    result.ExitCode == 0 ? Color.FromRgb(0x3F, 0xB9, 0x50) : Color.FromRgb(0xF8, 0x51, 0x49));
                Log($"Результаты: {_resultsDir}");
                OpenResultsBtn.IsEnabled = true;
            }
            else
            {
                SetStatus(result.ExitCode == 0 ? "Сбор не создал RESULTS" : $"Ошибка (код {result.ExitCode})",
                    indeterminate: false);
                ProgressBar.Foreground = new SolidColorBrush(Color.FromRgb(0xF8, 0x51, 0x49));
                ProgressBar.Value = 100;
                Log("Папка RESULTS не создана — сбор не выполнен или упал.");
            }

            return true;
        }
        catch (OperationCanceledException)
        {
            Log("Сбор остановлен пользователем.");
            SetStatus("Остановлено", indeterminate: false);
            return false;
        }
        catch (Exception ex)
        {
            Fail(ex.Message);
            return false;
        }
        finally
        {
            _running = false;
            _kapeProcess = null;
            _runCts?.Dispose();
            _runCts = null;
            StopCopyPulse();
            CancelRunBtn.IsEnabled = false;
            CloseBtn.IsEnabled = true;
            ProgressBar.IsIndeterminate = false;
            SourcePanel.IsEnabled = true;
            StartBtn.IsEnabled = true;
            SimBtn.IsEnabled = true;
        }
    }

    private void PopulateDrives(string preferred)
    {
        DriveCombo.SelectionChanged -= DriveCombo_SelectionChanged;
        try
        {
            DriveCombo.Items.Clear();
            var drives = DriveInventory.ListReady(preferred);
            foreach (var d in drives)
                DriveCombo.Items.Add(d);

            DriveCombo.SelectedIndex = DriveInventory.IndexOfPreferred(drives, preferred);
            ApplySelectedDriveToTsource();
        }
        finally
        {
            DriveCombo.SelectionChanged += DriveCombo_SelectionChanged;
        }
    }

    private void DriveCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        try
        {
            ApplySelectedDriveToTsource();
        }
        catch
        {
            /* never crash the pack UI on drive pick */
        }
    }

    private void ApplySelectedDriveToTsource()
    {
        if (DriveCombo.SelectedItem is not DriveInventory.DriveItem drive || string.IsNullOrWhiteSpace(drive.Root))
            return;
        _selectedTsource = drive.Root;
        // Editable ComboBox: keep selection text explicit (Win11 theme sometimes leaves the box blank).
        if (!string.Equals(DriveCombo.Text, drive.Label, StringComparison.Ordinal))
            DriveCombo.Text = drive.Label;
    }

    private void PhaseOnly_Checked(object sender, RoutedEventArgs e)
    {
        if (sender == Phase1OnlyCheck && Phase1OnlyCheck.IsChecked == true)
            Phase2OnlyCheck.IsChecked = false;
        else if (sender == Phase2OnlyCheck && Phase2OnlyCheck.IsChecked == true)
            Phase1OnlyCheck.IsChecked = false;
    }

    private Task<int> RunKapeAsync(string kape, List<string> args, string workDir, CancellationToken ct)
    {
        return KapeProcessHost.StartAsync(
            kape,
            args,
            workDir,
            onLine: line =>
            {
                Dispatcher.BeginInvoke(() =>
                {
                    Log(line);
                    TryUpdateProgress(line);
                });
            },
            onStarted: proc => _kapeProcess = proc,
            cancellationToken: ct);
    }

    private void TryUpdateProgress(string line)
    {
        var update = _progressParser.TryParse(line);
        if (update is null) return;

        if (update.ResetPhase || update.StopCopyPulse)
            StopCopyPulse();
        if (update.StartCopyPulse)
            StartCopyPulse();

        _progressState.ApplyUpdate(update);
        _progressParser.SetLocalProgress(update.Local0to100);
        ApplyProgress(_progressState.OverallPercent, update.Status);
    }

    private void BeginPhaseRange(double floor, double ceil, string status)
    {
        StopCopyPulse();
        _progressParser.ResetCounters();
        _progressState.BeginPhase(floor, ceil);
        ApplyProgress(_progressState.OverallPercent, status);
    }

    private void SetLocalProgress(double local0to100, string status)
    {
        _progressParser.SetLocalProgress(local0to100);
        _progressState.SetLocal(local0to100);
        ApplyProgress(_progressState.OverallPercent, status);
    }

    private void StartCopyPulse()
    {
        _copyPulseActive = true;
        _copyPulseTimer ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
        _copyPulseTimer.Tick -= CopyPulse_Tick;
        _copyPulseTimer.Tick += CopyPulse_Tick;
        _copyPulseTimer.Start();
    }

    private void StopCopyPulse()
    {
        _copyPulseActive = false;
        if (_copyPulseTimer is null) return;
        _copyPulseTimer.Stop();
        _copyPulseTimer.Tick -= CopyPulse_Tick;
    }

    private void CopyPulse_Tick(object? sender, EventArgs e)
    {
        if (!_copyPulseActive || !_running) return;
        var pulse = _progressParser.PulseCopy();
        if (pulse is not null)
            SetLocalProgress(pulse.Local0to100, pulse.Status);
    }

    private void ApplyProgress(double percent, string status)
    {
        percent = Math.Clamp(percent, 0, 99);
        ProgressBar.IsIndeterminate = false;
        ProgressBar.Value = percent;
        PercentText.Text = $"{percent:0}%";
        StatusText.Text = status;
    }

    private void SetStatus(string text, bool indeterminate, double? percent = null)
    {
        StatusText.Text = text;
        ProgressBar.IsIndeterminate = indeterminate;
        if (percent is { } p)
        {
            ProgressBar.Value = p;
            PercentText.Text = $"{p:0}%";
        }
        else if (indeterminate)
        {
            PercentText.Text = "…";
        }
    }

    private void Log(string message)
    {
        if (string.IsNullOrEmpty(message)) return;
        var stamp = DateTime.Now.ToString("HH:mm:ss");
        var line = $"[{stamp}] {message}";
        _logBuffer.AppendLine(line);
        LogBox.AppendText(line + "\r\n");
        LogBox.ScrollToEnd();
        SaveLogBtn.IsEnabled = true;
    }

    private void Fail(string message)
    {
        StopCopyPulse();
        SetStatus("Ошибка", indeterminate: false);
        ProgressBar.Value = 100;
        ProgressBar.Foreground = new SolidColorBrush(Color.FromRgb(0xF8, 0x51, 0x49));
        Log(message);
        MessageBox.Show(message, "KAPE Pack", MessageBoxButton.OK, MessageBoxImage.Error);
        CloseBtn.IsEnabled = true;
        SaveLogBtn.IsEnabled = true;
        CancelRunBtn.IsEnabled = false;
        StartBtn.IsEnabled = true;
        SimBtn.IsEnabled = true;
        SourcePanel.IsEnabled = true;
    }

    private void OpenResults_Click(object sender, RoutedEventArgs e)
    {
        if (_resultsDir is null || !Directory.Exists(_resultsDir)) return;
        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = "\"" + _resultsDir + "\"",
            UseShellExecute = true
        });
    }

    private void SaveLog_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog
        {
            Title = "Сохранить журнал",
            Filter = "Текст (*.txt)|*.txt|Все файлы (*.*)|*.*",
            FileName = $"kape_run_{DateTime.Now:yyyyMMdd_HHmmss}.txt",
            InitialDirectory = _packageDir is not null && Directory.Exists(_packageDir)
                ? _packageDir
                : Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            File.WriteAllText(dlg.FileName, _logBuffer.ToString(), Encoding.UTF8);
            Log($"Лог сохранён: {dlg.FileName}");
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Сохранение лога", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void CancelRun_Click(object sender, RoutedEventArgs e)
    {
        if (_runCts is null || _runCts.IsCancellationRequested) return;
        if (MessageBox.Show("Остановить kape.exe? Сбор будет прерван.", "KAPE Pack",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;
        try
        {
            _runCts.Cancel();
            Log("Остановка…");
            SetStatus("Остановка…", indeterminate: true);
        }
        catch (Exception ex)
        {
            Log("Не удалось остановить процесс: " + ex.Message);
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private static string Quote(string s)
        => s.Contains(' ') || s.Contains('%') ? "\"" + s + "\"" : s;
}
