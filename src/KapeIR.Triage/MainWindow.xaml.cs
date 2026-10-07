using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using KapeIR.Core.Models;
using KapeIR.Core.Services;
using Microsoft.Win32;

namespace KapeIR.Triage;

public partial class MainWindow : Window
{
    private readonly TriageRunCoordinator _coord = new();
    private TriageRunCoordinator.Session? _session;
    private string? _resultsDir;
    private Process? _kapeProcess;
    private CancellationTokenSource? _runCts;
    private bool _running;
    private readonly StringBuilder _logBuffer = new();
    private readonly CollectPackProgressParser _progressParser = new();
    private readonly CollectionProgressState _progressState = new();
    private bool _copyPulseActive;
    private DispatcherTimer? _copyPulseTimer;
    private string _selectedTsource = "C:";
    private string? _resultsRoot;

    private string? PackageDir => _session?.PackageDir;
    private CollectionPlan.LaunchManifest? Cfg => _session?.Manifest;

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

            var prep = await Task.Run(() =>
                _coord.Prepare(
                    new TriageRunCoordinator.PrepareOptions(
                        self,
                        TsourceOverride: null,
                        RequireTsource: false,
                        RequireSha256: false,
                        VerifyIfSidecarPresent: true),
                    log: msg => Dispatcher.BeginInvoke(() => Log(msg))));

            if (prep.ExitCode != 0 || prep.Session is null)
            {
                var msg = prep.Message;
                if (prep.ExitCode == 2 && msg.Contains("sha256", StringComparison.OrdinalIgnoreCase))
                    msg += "\n\nПоложите корректный KapeIR.Triage.exe.sha256 рядом с EXE или пересоберите пакет.";
                Fail(msg);
                return;
            }

            _session = prep.Session;
            var cfg = _session.Manifest;

            Title = $"KapeIR.Triage — {cfg.Name}";
            TitleText.Text = cfg.Name;
            if (cfg.CollectionMode == IrCollectionMode.TwoPhase)
            {
                SubtitleText.Text =
                    $"two_phase: {cfg.Phase1Module} → {cfg.Target}" +
                    (string.IsNullOrWhiteSpace(cfg.Phase2Module) ? "" : $" + {cfg.Phase2Module}");
                TwoPhasePanel.Visibility = Visibility.Visible;
                AdvancedExpander.IsExpanded = true;
                CaseIdBox.Text = cfg.CaseId ?? "";
            }
            else
            {
                SubtitleText.Text = $"Target: {cfg.Target}" +
                                    (string.IsNullOrWhiteSpace(cfg.Module) ? "" : $"  ·  Module: {cfg.Module}");
                TwoPhasePanel.Visibility = Visibility.Collapsed;
                AdvancedExpander.IsExpanded = false;
            }

            var initialTs = string.IsNullOrWhiteSpace(cfg.Tsource) ? "C:" : cfg.Tsource;
            PopulateDrives(initialTs);
            ResultsBox.Text = PackageDir ?? "";
            SourcePanel.Visibility = Visibility.Visible;
            SetStatus("Выберите диск и папку результатов, затем «Начать сбор» (или «Оценить»)", indeterminate: false, percent: 0);
            PercentText.Text = "";
            SaveLogBtn.IsEnabled = true;
            CloseBtn.IsEnabled = true;
            Log("Пакет готов. Выберите диск и папку результатов. Можно сначала оценить объём (--sim).");
            if (cfg.CollectionMode == IrCollectionMode.TwoPhase)
                Log("Режим two_phase: сначала volatile (Phase1), затем disk triage (Phase2).");
            Log($"Результаты по умолчанию: {TriageRunCoordinator.DefaultResultsRoot(PackageDir!)}");
        }
        catch (Exception ex)
        {
            Fail(ex.Message);
        }
    }

    private async void Sim_Click(object sender, RoutedEventArgs e)
    {
        if (_running || _session is null) return;
        if (!TryApplyPaths()) return;

        await RunCollectionAsync(simulateOnly: true);
    }

    private async void Start_Click(object sender, RoutedEventArgs e)
    {
        if (_running || _session is null) return;
        if (!TryApplyPaths()) return;

        if (SimBeforeCheck.IsChecked == true)
        {
            var simOk = await RunCollectionAsync(simulateOnly: true);
            if (!simOk) return;
            var cont = MessageBox.Show(
                "Оценка (--sim) завершена — см. журнал.\n\nЗапустить полный сбор с копированием файлов?",
                "KapeIR.Triage",
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

    private bool TryApplyPaths()
    {
        var tsource = _selectedTsource.Trim();
        if (DriveCombo.SelectedItem is DriveInventory.DriveItem drive && !string.IsNullOrWhiteSpace(drive.Root))
            tsource = drive.Root.Trim();

        if (string.IsNullOrWhiteSpace(tsource))
        {
            MessageBox.Show("Выберите диск для сбора.", "KapeIR.Triage",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        var results = (ResultsBox.Text ?? "").Trim();
        if (string.IsNullOrWhiteSpace(results))
        {
            MessageBox.Show("Укажите папку для результатов (RESULTS).", "KapeIR.Triage",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        try
        {
            results = Path.GetFullPath(results);
        }
        catch (Exception ex)
        {
            MessageBox.Show("Некорректный путь результатов: " + ex.Message, "KapeIR.Triage",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        try
        {
            Directory.CreateDirectory(results);
        }
        catch (Exception ex)
        {
            MessageBox.Show("Не удалось создать папку результатов:\n" + ex.Message, "KapeIR.Triage",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        Cfg!.Tsource = tsource;
        _selectedTsource = tsource;
        _resultsRoot = results;
        ResultsBox.Text = results;
        return true;
    }

    private void BrowseResults_Click(object sender, RoutedEventArgs e)
    {
        var folder = PickFolder("Выберите папку для RESULTS",
            GuessInitialDir(ResultsBox.Text) ?? PackageDir);
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
        if (_running || _session is null) return false;

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
        var cfg = _session.Manifest;
        var twoPhase = cfg.CollectionMode == IrCollectionMode.TwoPhase
                       && Phase1OnlyCheck.IsChecked != true
                       && Phase2OnlyCheck.IsChecked != true;
        if (twoPhase)
            BeginPhaseRange(0, 15, "Фаза 1…");
        else
            BeginPhaseRange(0, 99, simulateOnly ? "Оценка…" : "Сбор…");

        try
        {
            int? phaseFilter = null;
            if (cfg.CollectionMode == IrCollectionMode.TwoPhase)
            {
                if (Phase1OnlyCheck.IsChecked == true) phaseFilter = 1;
                else if (Phase2OnlyCheck.IsChecked == true) phaseFilter = 2;
            }

            var rt = TriageRunCoordinator.BuildRuntime(
                cfg.Tsource,
                simulate: simulateOnly,
                phaseFilter: phaseFilter,
                skipMemory: SkipMemoryCheck.IsChecked == true,
                caseIdOverride: string.IsNullOrWhiteSpace(CaseIdBox.Text) ? null : CaseIdBox.Text.Trim(),
                resultsRoot: _resultsRoot);

            SetStatus(simulateOnly ? "Оценка объёма (--sim)…" : "Сбор артефактов…", indeterminate: true);
            PercentText.Text = "…";

            var result = await _coord.RunAsync(
                new TriageRunCoordinator.RunOptions(
                    _session,
                    rt,
                    RunKape: async (exe, args, wd, token) =>
                    {
                        Log(simulateOnly
                            ? $"Оценка: kape.exe {string.Join(" ", args.Select(Quote))}"
                            : $"Команда: kape.exe {string.Join(" ", args.Select(Quote))}");
                        return await RunKapeAsync(exe, args, wd, token);
                    }),
                Log,
                ct);

            _resultsDir = result.ResultsDir;

            if (simulateOnly)
            {
                SetStatus(result.ExitCode == 0 ? "Оценка завершена" : $"Оценка: код {result.ExitCode}",
                    indeterminate: false, percent: 100);
                ProgressBar.Foreground = new SolidColorBrush(
                    result.ExitCode == 0 ? Color.FromRgb(0x3F, 0xB9, 0x50) : Color.FromRgb(0xF8, 0x51, 0x49));
                Log(result.StatusMessage);
                return result.ExitCode == 0;
            }

            if (result.HasResultsDirectory)
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
                Log(result.StatusMessage);
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
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => SetStatus(text, indeterminate, percent));
            return;
        }

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

        // CollectionRunner continues after kape with ConfigureAwait(false) — off UI thread.
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => Log(message));
            return;
        }

        var stamp = DateTime.Now.ToString("HH:mm:ss");
        var line = $"[{stamp}] {message}";
        _logBuffer.AppendLine(line);
        LogBox.AppendText(line + "\r\n");
        LogBox.ScrollToEnd();
        SaveLogBtn.IsEnabled = true;
    }

    private void Fail(string message)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => Fail(message));
            return;
        }

        StopCopyPulse();
        SetStatus("Ошибка", indeterminate: false);
        ProgressBar.Value = 100;
        ProgressBar.Foreground = new SolidColorBrush(Color.FromRgb(0xF8, 0x51, 0x49));
        Log(message);
        MessageBox.Show(message, "KapeIR.Triage", MessageBoxButton.OK, MessageBoxImage.Error);
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
            InitialDirectory = PackageDir is not null && Directory.Exists(PackageDir)
                ? PackageDir
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
        if (MessageBox.Show("Остановить kape.exe? Сбор будет прерван.", "KapeIR.Triage",
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
