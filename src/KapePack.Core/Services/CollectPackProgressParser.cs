using System.Globalization;
using System.Text.RegularExpressions;

namespace KapePack.Core.Services;

/// <summary>
/// Maps KAPE / CollectPack log lines to local phase progress (0–100) and status text.
/// Overall bar mapping (phase floor/ceil) stays in the UI host.
/// </summary>
public sealed class CollectPackProgressParser
{
    private static readonly Regex CopyProgress = new(
        @"Copied\s+([\d\s,\u00A0]+)\s+out\s+of\s+([\d\s,\u00A0]+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex FoundFiles = new(
        @"Found\s+([\d\s,\u00A0]+)\s+files?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex CopiedDeferred = new(
        @"^Copied\s+(deferred\s+)?file\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex DiscoveredProcessors = new(
        @"Discovered\s+([\d\s,\u00A0]+)\s+processors",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public int FoundFilesCount { get; private set; }
    public int CopiedFilesCount { get; private set; }
    public int ModuleProcessorsTotal { get; private set; }
    public int ModuleProcessorsDone { get; private set; }
    public double LocalProgress { get; private set; }

    public sealed record ProgressUpdate(
        double Local0to100,
        string Status,
        bool ResetPhase,
        double? PhaseFloor,
        double? PhaseCeil,
        bool StartCopyPulse,
        bool StopCopyPulse);

    public void ResetCounters()
    {
        FoundFilesCount = 0;
        CopiedFilesCount = 0;
        ModuleProcessorsTotal = 0;
        ModuleProcessorsDone = 0;
        LocalProgress = 0;
    }

    public void SetLocalProgress(double local0to100) =>
        LocalProgress = Math.Clamp(local0to100, 0, 100);

    /// <summary>Creep during silent KAPE copy (no per-file lines until the end).</summary>
    public ProgressUpdate? PulseCopy()
    {
        if (LocalProgress >= 45)
            return null;
        var next = LocalProgress + 0.7;
        LocalProgress = next;
        return new ProgressUpdate(next, "Копирование файлов…", false, null, null, false, false);
    }

    public ProgressUpdate? TryParse(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return null;

        if (line.StartsWith("Фаза 1", StringComparison.Ordinal))
        {
            ResetCounters();
            return new ProgressUpdate(0, "Фаза 1 — volatile…", true, 0, 15, false, true);
        }

        if (line.StartsWith("Фаза 2", StringComparison.Ordinal))
        {
            ResetCounters();
            return new ProgressUpdate(0, "Фаза 2 — disk triage…", true, 15, 99, false, true);
        }

        var found = FoundFiles.Match(line);
        if (found.Success && TryParseCount(found.Groups[1].Value, out var nFound) && nFound > 0)
        {
            FoundFilesCount = nFound;
            CopiedFilesCount = 0;
            ModuleProcessorsTotal = 0;
            ModuleProcessorsDone = 0;
            LocalProgress = 8;
            return new ProgressUpdate(8, $"Найдено файлов: {FoundFilesCount:N0}", false, null, null, false, true);
        }

        if (line.Contains("Beginning copy", StringComparison.OrdinalIgnoreCase))
        {
            LocalProgress = Math.Max(LocalProgress, 10);
            return new ProgressUpdate(LocalProgress, "Копирование файлов…", false, null, null, true, false);
        }

        var copy = CopyProgress.Match(line);
        if (copy.Success
            && TryParseCount(copy.Groups[1].Value, out var done)
            && TryParseCount(copy.Groups[2].Value, out var total)
            && total > 0)
        {
            CopiedFilesCount = done;
            FoundFilesCount = total;
            LocalProgress = 48;
            return new ProgressUpdate(48, $"Скопировано {done:N0} / {total:N0}", false, null, null, false, true);
        }

        if (CopiedDeferred.IsMatch(line) && FoundFilesCount > 0)
        {
            CopiedFilesCount = Math.Min(CopiedFilesCount + 1, FoundFilesCount);
            var frac = (double)CopiedFilesCount / FoundFilesCount;
            LocalProgress = 48 + 4.0 * frac;
            return new ProgressUpdate(
                LocalProgress,
                $"Докопирование… {CopiedFilesCount:N0} / {FoundFilesCount:N0}",
                false, null, null, false, false);
        }

        if (line.Contains("Using Module operations", StringComparison.OrdinalIgnoreCase)
            || line.Contains("Executing modules", StringComparison.OrdinalIgnoreCase))
        {
            LocalProgress = Math.Max(LocalProgress, 52);
            return new ProgressUpdate(LocalProgress, "Модули (парсеры)…", false, null, null, false, true);
        }

        var discovered = DiscoveredProcessors.Match(line);
        if (discovered.Success && TryParseCount(discovered.Groups[1].Value, out var nProc) && nProc > 0)
        {
            ModuleProcessorsTotal = nProc;
            ModuleProcessorsDone = 0;
            LocalProgress = 54;
            return new ProgressUpdate(54, $"Модули: 0 / {ModuleProcessorsTotal}", false, null, null, false, true);
        }

        if (line.Contains("Running ", StringComparison.OrdinalIgnoreCase)
            && (line.Contains(".exe", StringComparison.OrdinalIgnoreCase)
                || line.Contains("powershell", StringComparison.OrdinalIgnoreCase)
                || line.Contains("cmd.exe", StringComparison.OrdinalIgnoreCase)))
        {
            if (ModuleProcessorsTotal > 0)
            {
                ModuleProcessorsDone = Math.Min(ModuleProcessorsDone + 1, ModuleProcessorsTotal);
                var pct = 54 + 36.0 * ModuleProcessorsDone / ModuleProcessorsTotal;
                LocalProgress = pct;
                var tip = line.Contains("powershell", StringComparison.OrdinalIgnoreCase)
                    ? $"Модуль PowerShell… {ModuleProcessorsDone}/{ModuleProcessorsTotal}"
                    : $"Модули… {ModuleProcessorsDone}/{ModuleProcessorsTotal}";
                return new ProgressUpdate(pct, tip, false, null, null, false, true);
            }

            LocalProgress = Math.Min(Math.Max(LocalProgress, 55) + 0.4, 88);
            return new ProgressUpdate(LocalProgress, "Выполнение модулей…", false, null, null, false, true);
        }

        if (line.Contains("Executed ", StringComparison.OrdinalIgnoreCase)
            && line.Contains("processors", StringComparison.OrdinalIgnoreCase))
        {
            LocalProgress = 92;
            return new ProgressUpdate(92, "Модули завершены", false, null, null, false, true);
        }

        if (line.Contains("Compressing", StringComparison.OrdinalIgnoreCase))
        {
            LocalProgress = Math.Max(LocalProgress, 94);
            return new ProgressUpdate(LocalProgress, "Сжатие результатов…", false, null, null, false, true);
        }

        if (line.Contains("Total execution time", StringComparison.OrdinalIgnoreCase))
        {
            LocalProgress = 99;
            return new ProgressUpdate(99, "Фаза завершается…", false, null, null, false, true);
        }

        return null;
    }

    public static bool TryParseCount(string raw, out int value)
    {
        var digits = new string(raw.Where(char.IsDigit).ToArray());
        return int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }
}
