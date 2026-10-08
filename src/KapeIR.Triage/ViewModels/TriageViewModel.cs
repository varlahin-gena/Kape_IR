using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KapeIR.Core.Models;
using KapeIR.Core.Services;
using KapeIR.Ui.Dialogs;
using KapeIR.Ui.Scheduling;

namespace KapeIR.Triage.ViewModels;

public partial class TriageViewModel : ObservableObject
{
    private readonly IDialogService _dialogs;
    private readonly TriageRunCoordinator _coord;
    private readonly IUiScheduler _ui;
    private readonly TriageLogJournal _journal = new();
    private readonly TriageProgressBinder _progress = new();

    private TriageRunCoordinator.Session? _session;
    private string? _resultsDir;
    private Process? _kapeProcess;
    private CancellationTokenSource? _runCts;
    private string _selectedTsource = "C:";
    private string? _resultsRoot;
    private EstimateSummaryParser? _estimateParser;

    public TriageViewModel(
        IDialogService dialogs,
        TriageRunCoordinator? coord = null,
        IUiScheduler? ui = null)
    {
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
        _coord = coord ?? new TriageRunCoordinator();
        _ui = ui ?? ImmediateUiScheduler.Instance;
    }

    public ObservableCollection<DriveInventory.DriveItem> Drives { get; } = new();

    [ObservableProperty] private string _windowTitle = "KapeIR.Triage";
    [ObservableProperty] private string _packageTitle = "KapeIR.Triage";
    [ObservableProperty] private string _subtitle = "Автономный пакет сбора артефактов";
    [ObservableProperty] private string _statusText = "Подготовка…";
    [ObservableProperty] private string _percentText = "";
    [ObservableProperty] private double _progressValue;
    [ObservableProperty] private bool _isProgressIndeterminate = true;
    [ObservableProperty] private ProgressTone _progressTone = ProgressTone.Accent;
    [ObservableProperty] private string _resultsPath = "";
    [ObservableProperty] private string _caseId = "";
    [ObservableProperty] private bool _simBeforeCollect;
    [ObservableProperty] private bool _skipMemory;
    [ObservableProperty] private bool _phase1Only;
    [ObservableProperty] private bool _phase2Only;
    [ObservableProperty] private bool _isTwoPhase;
    [ObservableProperty] private bool _isSourceVisible;
    [ObservableProperty] private bool _isAdvancedExpanded;
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private bool _isSourceEnabled = true;
    [ObservableProperty] private bool _canClose;
    [ObservableProperty] private bool _canSaveLog;
    [ObservableProperty] private bool _canOpenResults;
    [ObservableProperty] private bool _isPrepared;
    [ObservableProperty] private bool _isErrorVisible;
    [ObservableProperty] private string _errorTitle = "Ошибка";
    [ObservableProperty] private string _errorDetail = "";
    [ObservableProperty] private DriveInventory.DriveItem? _selectedDrive;

    /// <summary>Raised on the UI thread with the stamped CRLF fragment to append to the journal TextBox.</summary>
    public event Action<string>? LogFragmentAppended;

    private string? PackageDir => _session?.PackageDir;
    private CollectionPlan.LaunchManifest? Cfg => _session?.Manifest;

    partial void OnIsRunningChanged(bool value)
    {
        IsSourceEnabled = !value;
        // Policy B: Close stays disabled for the whole run (including unpack).
        RefreshCanClose();
        SimulateCommand.NotifyCanExecuteChanged();
        StartCommand.NotifyCanExecuteChanged();
        CancelRunCommand.NotifyCanExecuteChanged();
        BrowseResultsCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsPreparedChanged(bool value)
    {
        RefreshCanClose();
        SimulateCommand.NotifyCanExecuteChanged();
        StartCommand.NotifyCanExecuteChanged();
        BrowseResultsCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsErrorVisibleChanged(bool value) => RefreshCanClose();

    private void RefreshCanClose()
        => CanClose = !IsRunning && (IsPrepared || IsErrorVisible || CanSaveLog);

    partial void OnSelectedDriveChanged(DriveInventory.DriveItem? value)
    {
        if (value is null || string.IsNullOrWhiteSpace(value.Root)) return;
        _selectedTsource = value.Root;
    }

    partial void OnPhase1OnlyChanged(bool value)
    {
        if (value) Phase2Only = false;
    }

    partial void OnPhase2OnlyChanged(bool value)
    {
        if (value) Phase1Only = false;
    }

    [RelayCommand]
    public async Task InitializeAsync()
    {
        try
        {
            var self = Environment.ProcessPath
                       ?? throw new InvalidOperationException("Не удалось определить путь к EXE.");

            PackageTitle = Path.GetFileNameWithoutExtension(self);
            // Policy B: Close disabled for the whole prepare/unpack phase.
            IsRunning = true;
            SetStatus("Подготовка пакета…", indeterminate: true);
            ProgressTone = ProgressTone.Accent;

            // Log on the UI thread before background work so the journal is never blank
            // while SHA256 / extract runs (and so a marshal bug is obvious immediately).
            AppendLog($"EXE: {self}");
            var sidecar = self + ".sha256";
            if (File.Exists(sidecar))
                AppendLog("Найден файл SHA256 рядом с EXE — проверка на больших пакетах может занять несколько минут.");
            else
                AppendLog("Файл SHA256 рядом с EXE не найден — сразу распаковка пакета.");

            var prep = await Task.Run(() =>
                _coord.Prepare(
                    new TriageRunCoordinator.PrepareOptions(
                        self,
                        TsourceOverride: null,
                        RequireTsource: false,
                        RequireSha256: false,
                        VerifyIfSidecarPresent: true),
                    log: msg => RunOnUi(() => OnPrepareLog(msg)))).ConfigureAwait(true);

            if (prep.ExitCode != 0 || prep.Session is null)
            {
                var msg = prep.Message;
                if (prep.ExitCode == 2 && msg.Contains("sha256", StringComparison.OrdinalIgnoreCase))
                    msg += "\n\nПоложите корректный файл KapeIR.Triage.exe.sha256 рядом с EXE или пересоберите пакет.";
                Fail(msg);
                return;
            }

            _session = prep.Session;
            var cfg = _session.Manifest;

            WindowTitle = $"KapeIR.Triage — {cfg.Name}";
            PackageTitle = cfg.Name;
            IsTwoPhase = cfg.CollectionMode == IrCollectionMode.TwoPhase;
            if (IsTwoPhase)
            {
                Subtitle =
                    $"Двухфазный сбор: {cfg.Phase1Module} → {cfg.Target}" +
                    (string.IsNullOrWhiteSpace(cfg.Phase2Module) ? "" : $" + {cfg.Phase2Module}");
                IsAdvancedExpanded = true;
                CaseId = cfg.CaseId ?? "";
            }
            else
            {
                Subtitle = $"Цель: {cfg.Target}" +
                           (string.IsNullOrWhiteSpace(cfg.Module) ? "" : $"  ·  Модуль: {cfg.Module}");
                IsAdvancedExpanded = false;
            }

            var initialTs = string.IsNullOrWhiteSpace(cfg.Tsource) ? "C:" : cfg.Tsource;
            PopulateDrives(initialTs);
            ResultsPath = PackageDir ?? "";
            IsSourceVisible = true;
            IsPrepared = true;
            CanSaveLog = true;
            IsRunning = false;
            CanClose = true;
            SetStatus("Выберите диск и папку результатов, затем нажмите «Начать сбор»",
                indeterminate: false, percent: 0);
            PercentText = "";
            AppendLog("Пакет готов. Выберите диск и папку результатов. При необходимости сначала оцените объём без копирования.");
            if (IsTwoPhase)
                AppendLog("Двухфазный режим: сначала оперативный сбор (память, сеть, процессы), затем дисковый.");
            AppendLog($"Папка результатов по умолчанию: {TriageRunCoordinator.DefaultResultsRoot(PackageDir!)}");
        }
        catch (Exception ex)
        {
            Fail(ex.Message);
        }
    }

    [RelayCommand(CanExecute = nameof(CanStartOrSim))]
    private async Task SimulateAsync()
    {
        if (!TryApplyPaths()) return;
        await RunCollectionAsync(simulateOnly: true).ConfigureAwait(true);
    }

    [RelayCommand(CanExecute = nameof(CanStartOrSim))]
    private async Task StartAsync()
    {
        if (!TryApplyPaths()) return;

        if (SimBeforeCollect)
        {
            var simOk = await RunCollectionAsync(simulateOnly: true).ConfigureAwait(true);
            if (!simOk) return;
            var cont = _dialogs.Confirm(
                "Оценка объёма завершена — подробности в журнале.\n\nЗапустить полный сбор с копированием файлов?",
                "KapeIR.Triage",
                DialogIcon.Question);
            if (!cont)
            {
                AppendLog("Полный сбор после оценки отменён.");
                return;
            }
        }

        await RunCollectionAsync(simulateOnly: false).ConfigureAwait(true);
    }

    private bool CanStartOrSim() => IsPrepared && !IsRunning;

    [RelayCommand(CanExecute = nameof(CanBrowse))]
    private void BrowseResults()
    {
        var folder = _dialogs.PickFolder(
            "Папка для результатов сбора",
            TriagePathGuards.GuessInitialDir(ResultsPath) ?? PackageDir);
        if (folder is null) return;
        ResultsPath = folder;
    }

    private bool CanBrowse() => IsPrepared && !IsRunning;

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void CancelRun()
    {
        if (_runCts is null || _runCts.IsCancellationRequested) return;
        if (!_dialogs.Confirm(
                "Остановить сбор? Текущая операция будет прервана.",
                "KapeIR.Triage",
                DialogIcon.Warning))
            return;
        try
        {
            _runCts.Cancel();
            AppendLog("Остановка по запросу пользователя…");
            SetStatus("Остановка…", indeterminate: true);
        }
        catch (Exception ex)
        {
            AppendLog("Не удалось остановить процесс: " + ex.Message);
        }
    }

    private bool CanCancel() => IsRunning;

    [RelayCommand(CanExecute = nameof(CanOpenResultsExec))]
    private void OpenResults()
    {
        if (_resultsDir is null || !Directory.Exists(_resultsDir)) return;
        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = "\"" + _resultsDir + "\"",
            UseShellExecute = true
        });
    }

    private bool CanOpenResultsExec() => CanOpenResults;

    [RelayCommand(CanExecute = nameof(CanSaveLogExec))]
    private void SaveLog()
    {
        var path = _dialogs.PickSaveFile(
            "Сохранить журнал",
            "Текст (*.txt)|*.txt|Все файлы (*.*)|*.*",
            $"kape_run_{DateTime.Now:yyyyMMdd_HHmmss}.txt",
            PackageDir is not null && Directory.Exists(PackageDir)
                ? PackageDir
                : Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
        if (path is null) return;
        try
        {
            File.WriteAllText(path, _journal.FullText, Encoding.UTF8);
            AppendLog($"Журнал сохранён: {path}");
        }
        catch (Exception ex)
        {
            _dialogs.ShowMessage(ex.Message, "Сохранение журнала", DialogIcon.Error);
        }
    }

    private bool CanSaveLogExec() => CanSaveLog;

    /// <summary>Called by the view's DispatcherTimer while copy-wait is active (no bar creep).</summary>
    public void OnCopyPulseTick()
    {
        if (!IsRunning) return;
        var snap = _progress.TickCopyWait();
        if (snap is not null)
            ApplySnapshot(snap);
    }

    public bool IsCopyPulseActive => _progress.IsCopyWaiting;

    private async Task<bool> RunCollectionAsync(bool simulateOnly)
    {
        if (IsRunning || _session is null) return false;

        IsRunning = true;
        CanClose = false;
        _runCts?.Dispose();
        _runCts = new CancellationTokenSource();
        var ct = _runCts.Token;
        _progress.Reset();
        ProgressTone = ProgressTone.Accent;
        _estimateParser = simulateOnly ? new EstimateSummaryParser() : null;

        var cfg = _session.Manifest;
        var twoPhase = cfg.CollectionMode == IrCollectionMode.TwoPhase
                       && !Phase1Only
                       && !Phase2Only;
        if (twoPhase)
            ApplySnapshot(_progress.BeginPhase(0, 15, "Фаза 1…"));
        else
            ApplySnapshot(_progress.BeginPhase(0, 99, simulateOnly ? "Оценка объёма…" : "Сбор…"));

        try
        {
            int? phaseFilter = null;
            if (cfg.CollectionMode == IrCollectionMode.TwoPhase)
            {
                if (Phase1Only) phaseFilter = 1;
                else if (Phase2Only) phaseFilter = 2;
            }

            var rt = TriageRunCoordinator.BuildRuntime(
                cfg.Tsource,
                simulate: simulateOnly,
                phaseFilter: phaseFilter,
                skipMemory: SkipMemory,
                caseIdOverride: string.IsNullOrWhiteSpace(CaseId) ? null : CaseId.Trim(),
                resultsRoot: _resultsRoot);

            SetStatus(simulateOnly ? "Оценка объёма…" : "Сбор артефактов…", indeterminate: true);
            PercentText = "…";

            var result = await _coord.RunAsync(
                new TriageRunCoordinator.RunOptions(
                    _session,
                    rt,
                    RunKape: async (exe, args, wd, token) =>
                    {
                        AppendLog(simulateOnly
                            ? $"Оценка: kape.exe {string.Join(" ", args.Select(TriagePathGuards.QuoteCliArg))}"
                            : $"Команда: kape.exe {string.Join(" ", args.Select(TriagePathGuards.QuoteCliArg))}");
                        return await RunKapeAsync(exe, args, wd, token).ConfigureAwait(false);
                    }),
                msg => AppendLog(msg),
                ct).ConfigureAwait(true);

            _resultsDir = result.ResultsDir;

            if (simulateOnly)
            {
                SetStatus(result.ExitCode == 0 ? "Оценка завершена" : $"Оценка завершилась с кодом {result.ExitCode}",
                    indeterminate: false, percent: 100);
                ProgressTone = result.ExitCode == 0 ? ProgressTone.Success : ProgressTone.Danger;
                AppendLog(result.StatusMessage);
                var estimateBlock = _estimateParser?.FormatLogBlock();
                if (!string.IsNullOrEmpty(estimateBlock))
                    AppendLog(estimateBlock);
                return result.ExitCode == 0;
            }

            if (result.HasResultsDirectory)
            {
                SetStatus(result.ExitCode == 0 ? "Готово" : $"Готово с ошибками (код {result.ExitCode})",
                    indeterminate: false, percent: 100);
                ProgressTone = result.ExitCode == 0 ? ProgressTone.Success : ProgressTone.Danger;
                AppendLog($"Папка результатов: {_resultsDir}");
                CanOpenResults = true;
                OpenResultsCommand.NotifyCanExecuteChanged();
            }
            else
            {
                SetStatus(result.ExitCode == 0 ? "Папка RESULTS не создана" : $"Ошибка (код {result.ExitCode})",
                    indeterminate: false);
                ProgressTone = ProgressTone.Danger;
                ProgressValue = 100;
                AppendLog(result.StatusMessage);
            }

            return true;
        }
        catch (OperationCanceledException)
        {
            AppendLog("Сбор остановлен пользователем.");
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
            IsRunning = false;
            _kapeProcess = null;
            _runCts?.Dispose();
            _runCts = null;
            _estimateParser = null;
            _progress.EndCopyWait();
            IsProgressIndeterminate = false;
            CanClose = true;
        }
    }

    private bool TryApplyPaths()
    {
        var resolved = TriagePathGuards.TryResolve(
            _selectedTsource,
            SelectedDrive?.Root,
            ResultsPath);
        if (!resolved.Ok)
        {
            _dialogs.ShowMessage(resolved.Error!, "KapeIR.Triage", DialogIcon.Warning);
            return false;
        }

        Cfg!.Tsource = resolved.Tsource;
        _selectedTsource = resolved.Tsource;
        _resultsRoot = resolved.ResultsRoot;
        ResultsPath = resolved.ResultsRoot;
        return true;
    }

    private void PopulateDrives(string preferred)
    {
        Drives.Clear();
        var drives = DriveInventory.ListReady(preferred);
        foreach (var d in drives)
            Drives.Add(d);

        var idx = DriveInventory.IndexOfPreferred(drives, preferred);
        if (idx >= 0 && idx < Drives.Count)
            SelectedDrive = Drives[idx];
    }

    private Task<int> RunKapeAsync(string kape, List<string> args, string workDir, CancellationToken ct)
    {
        return KapeProcessHost.StartAsync(
            kape,
            args,
            workDir,
            onLine: line =>
            {
                RunOnUi(() =>
                {
                    AppendLog(line);
                    TryUpdateProgress(line);
                });
            },
            onStarted: proc => _kapeProcess = proc,
            cancellationToken: ct);
    }

    private void TryUpdateProgress(string line)
    {
        var snap = _progress.ApplyLine(line);
        if (snap is not null)
            ApplySnapshot(snap);
    }

    private void ApplySnapshot(CollectionProgressFacade.Snapshot snap)
    {
        StatusText = snap.Status;
        IsProgressIndeterminate = snap.IsIndeterminate;
        if (snap.IsIndeterminate)
        {
            PercentText = "…";
            return;
        }

        ProgressValue = snap.OverallPercent;
        PercentText = $"{snap.OverallPercent:0}%";
    }

    private void SetStatus(string text, bool indeterminate, double? percent = null)
    {
        StatusText = text;
        IsProgressIndeterminate = indeterminate;
        if (percent is { } p)
        {
            ProgressValue = p;
            PercentText = $"{p:0}%";
        }
        else if (indeterminate)
        {
            PercentText = "…";
        }
    }

    /// <summary>Prepare-phase log: keep journal + surface SHA256/extract % in the status bar.</summary>
    private void OnPrepareLog(string message)
    {
        var effect = TriageProgressBinder.InterpretPrepareLog(message);
        if (effect.StatusText is not null)
            SetStatus(effect.StatusText, effect.Indeterminate ?? false, effect.Percent);
        if (effect.AppendToJournal && effect.JournalMessage is not null)
            AppendLog(effect.JournalMessage);
    }

    private void AppendLog(string message)
    {
        if (string.IsNullOrEmpty(message)) return;

        if (!IsOnUiThread())
        {
            RunOnUi(() => AppendLog(message));
            return;
        }

        _estimateParser?.Observe(message);

        var fragment = _journal.Append(message);
        if (fragment is null) return;
        LogFragmentAppended?.Invoke(fragment);
        CanSaveLog = true;
        SaveLogCommand.NotifyCanExecuteChanged();
    }

    private void Fail(string message)
    {
        if (!IsOnUiThread())
        {
            RunOnUi(() => Fail(message));
            return;
        }

        // In-window error (no MessageBox): alerts.md — avoid interrupting with a modal on failure
        // when the window can show status + detail + save log.
        PresentError(
            IsPrepared ? "Сбор завершился с ошибкой" : "Не удалось подготовить пакет",
            message);
    }

    /// <summary>Shows a non-modal error banner; details also go to the journal.</summary>
    internal void PresentError(string title, string message)
    {
        _progress.EndCopyWait();
        SetStatus("Ошибка", indeterminate: false);
        ProgressValue = 100;
        ProgressTone = ProgressTone.Danger;
        ErrorTitle = title;
        ErrorDetail = TriageLogJournal.TrimBanner(message);
        IsErrorVisible = true;
        AppendLog(message);
        CanSaveLog = true;
        IsRunning = false;
        RefreshCanClose();
        SimulateCommand.NotifyCanExecuteChanged();
        StartCommand.NotifyCanExecuteChanged();
        CancelRunCommand.NotifyCanExecuteChanged();
        SaveLogCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Marshal via <see cref="IUiScheduler"/>. Do not capture <see cref="SynchronizationContext"/> in the
    /// ctor — DI may construct the VM before a context is installed.
    /// </summary>
    private void RunOnUi(Action action) => _ui.Post(action);

    private bool IsOnUiThread() => _ui.CheckAccess();
}
