using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;

namespace KapePackRunner;

public partial class MainWindow : Window
{
    private string? _resultsDir;
    private string? _packageDir;
    private int _foundFiles;
    private int _copiedFiles;

    private static readonly Regex CopyProgress = new(
        @"Copied\s+([\d\s,\u00A0]+)\s+out\s+of\s+([\d\s,\u00A0]+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex FoundFiles = new(
        @"Found\s+([\d\s,\u00A0]+)\s+files?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex CopiedDeferred = new(
        @"^Copied\s+(deferred\s+)?file\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public MainWindow()
    {
        InitializeComponent();
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await RunAsync();
    }

    private async Task RunAsync()
    {
        try
        {
            var self = Environment.ProcessPath
                       ?? throw new InvalidOperationException("Не удалось определить путь к EXE.");

            Log($"EXE: {self}");
            TitleText.Text = Path.GetFileNameWithoutExtension(self);

            if (!PackPayload.TryReadPayload(self, out var zipStart, out var zipLen))
            {
                Fail("Это не автономный пакет KAPE (нет payload KAPEPACK).\nСоберите пакет через KAPE Pack Builder.");
                return;
            }

            _packageDir = Path.Combine(
                Path.GetDirectoryName(self)!,
                Path.GetFileNameWithoutExtension(self));

            SetStatus("Распаковка пакета…", indeterminate: true);
            await Task.Run(() => PackPayload.Extract(self, zipStart, zipLen, _packageDir, msg =>
                Dispatcher.Invoke(() => Log(msg))));

            var kape = Path.Combine(_packageDir, "kape.exe");
            if (!File.Exists(kape))
            {
                Fail($"В пакете нет kape.exe.\n{_packageDir}");
                return;
            }

            var cfg = PackPayload.ReadLaunchConfig(_packageDir);
            if (cfg is null || string.IsNullOrWhiteSpace(cfg.Target))
            {
                Fail("Нет package.json или не указан target_compound.");
                return;
            }

            Title = $"KAPE Pack — {cfg.Name}";
            TitleText.Text = cfg.Name;
            SubtitleText.Text = $"Target: {cfg.Target}" +
                                (string.IsNullOrWhiteSpace(cfg.Module) ? "" : $"  ·  Module: {cfg.Module}");

            var args = PackPayload.BuildKapeArgs(cfg);
            Log($"Команда: kape.exe {string.Join(" ", args.Select(Quote))}");
            SetStatus("Сбор артефактов (targets)…", indeterminate: true);
            PercentText.Text = "…";

            var exit = await RunKapeAsync(kape, args, _packageDir);
            _resultsDir = Path.Combine(_packageDir, "RESULTS", Environment.MachineName);

            if (Directory.Exists(_resultsDir))
            {
                SetStatus("Готово", indeterminate: false, percent: 100);
                ProgressBar.Foreground = new SolidColorBrush(Color.FromRgb(0x3F, 0xB9, 0x50));
                Log($"Результаты: {_resultsDir}");
                OpenResultsBtn.IsEnabled = true;
            }
            else
            {
                SetStatus(exit == 0 ? "Сбор не создал RESULTS" : $"Ошибка (код {exit})", indeterminate: false);
                ProgressBar.Foreground = new SolidColorBrush(Color.FromRgb(0xF8, 0x51, 0x49));
                ProgressBar.Value = 100;
                Log("Папка RESULTS не создана — сбор не выполнен или упал.");
            }
        }
        catch (Exception ex)
        {
            Fail(ex.Message);
        }
        finally
        {
            CloseBtn.IsEnabled = true;
            ProgressBar.IsIndeterminate = false;
        }
    }

    private Task<int> RunKapeAsync(string kape, List<string> args, string workDir)
    {
        // KAPE — консольное .NET-приложение: пишет в OEM code page (на RU обычно CP866), не UTF-8.
        var enc = GetConsoleEncoding();
        var tcs = new TaskCompletionSource<int>();
        var psi = new ProcessStartInfo
        {
            FileName = kape,
            WorkingDirectory = workDir,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = enc,
            StandardErrorEncoding = enc
        };
        foreach (var a in args)
            psi.ArgumentList.Add(a);

        var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
        proc.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            Dispatcher.Invoke(() =>
            {
                Log(e.Data);
                TryUpdateProgress(e.Data);
            });
        };
        proc.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            Dispatcher.Invoke(() =>
            {
                Log(e.Data);
                TryUpdateProgress(e.Data);
            });
        };
        proc.Exited += (_, _) =>
        {
            try { tcs.TrySetResult(proc.ExitCode); }
            finally { proc.Dispose(); }
        };

        if (!proc.Start())
        {
            tcs.TrySetResult(-1);
            return tcs.Task;
        }

        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();
        return tcs.Task;
    }

    private void TryUpdateProgress(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return;

        var found = FoundFiles.Match(line);
        if (found.Success && TryParseCount(found.Groups[1].Value, out var nFound) && nFound > 0)
        {
            _foundFiles = nFound;
            _copiedFiles = 0;
            ApplyProgress(0, $"Найдено файлов: {_foundFiles:N0}");
            return;
        }

        var copy = CopyProgress.Match(line);
        if (copy.Success
            && TryParseCount(copy.Groups[1].Value, out var done)
            && TryParseCount(copy.Groups[2].Value, out var total)
            && total > 0)
        {
            _copiedFiles = done;
            _foundFiles = total;
            // Targets = 0–75% общего прогресса
            var pct = 75.0 * done / total;
            ApplyProgress(pct, $"Копирование… {done:N0} / {total:N0}");
            return;
        }

        if (CopiedDeferred.IsMatch(line) && _foundFiles > 0)
        {
            _copiedFiles = Math.Min(_copiedFiles + 1, _foundFiles);
            var pct = 75.0 * _copiedFiles / _foundFiles;
            ApplyProgress(pct, $"Копирование… {_copiedFiles:N0} / {_foundFiles:N0}");
            return;
        }

        if (line.Contains("powershell.exe", StringComparison.OrdinalIgnoreCase)
            || line.Contains("Running module", StringComparison.OrdinalIgnoreCase)
            || (line.StartsWith("Running ", StringComparison.OrdinalIgnoreCase)
                && line.Contains(".exe", StringComparison.OrdinalIgnoreCase)))
        {
            var tip = line.Contains("powershell", StringComparison.OrdinalIgnoreCase)
                ? "Модуль PowerShell (может занять несколько минут)…"
                : "Выполнение модулей…";
            // Modules = 75–98%
            var basePct = Math.Max(ProgressBar.Value, 75);
            if (basePct < 75) basePct = 75;
            if (ProgressBar.IsIndeterminate || ProgressBar.Value < 75)
                ApplyProgress(Math.Min(basePct + 1, 95), tip);
            else
            {
                StatusText.Text = tip;
                PercentText.Text = $"{ProgressBar.Value:0}%";
                ProgressBar.IsIndeterminate = false;
            }
        }
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
        LogBox.AppendText($"[{stamp}] {message}\r\n");
        LogBox.ScrollToEnd();
    }

    private void Fail(string message)
    {
        SetStatus("Ошибка", indeterminate: false);
        ProgressBar.Value = 100;
        ProgressBar.Foreground = new SolidColorBrush(Color.FromRgb(0xF8, 0x51, 0x49));
        Log(message);
        MessageBox.Show(message, "KAPE Pack", MessageBoxButton.OK, MessageBoxImage.Error);
        CloseBtn.IsEnabled = true;
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

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private static string Quote(string s)
        => s.Contains(' ') || s.Contains('%') ? "\"" + s + "\"" : s;

    private static Encoding GetConsoleEncoding()
    {
        try
        {
            var oem = CultureInfo.CurrentCulture.TextInfo.OEMCodePage;
            return Encoding.GetEncoding(oem);
        }
        catch
        {
            try { return Encoding.GetEncoding(866); }
            catch { return Encoding.Default; }
        }
    }

    private static bool TryParseCount(string raw, out int value)
    {
        var digits = new string(raw.Where(char.IsDigit).ToArray());
        return int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }
}
