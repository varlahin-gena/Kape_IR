using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace KapeIR.Core.Services;

/// <summary>
/// Accumulates KAPE --sim stdout into a short operator-facing summary for the Triage journal.
/// </summary>
public sealed class EstimateSummaryParser
{
    private static readonly Regex FoundFiles = new(
        @"Found\s+(\d+)\s+files\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex Deferred = new(
        @"Deferred\s+file\s+count:\s*(\d+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex Copied = new(
        @"Copied\s+(\d+)\s+\(Deduplicated:\s*(\d+)\)\s+out\s+of\s+(\d+)\s+files",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public int? FilesFound { get; private set; }
    public int? DeferredCount { get; private set; }
    public int? CopiedCount { get; private set; }
    public int? Deduplicated { get; private set; }
    public int? OutOf { get; private set; }
    public bool SawSimulateBanner { get; private set; }

    public bool HasUsefulData =>
        FilesFound is not null || CopiedCount is not null || DeferredCount is not null;

    public void Observe(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return;

        if (line.Contains("Simulate copy is True", StringComparison.OrdinalIgnoreCase))
            SawSimulateBanner = true;

        var m = FoundFiles.Match(line);
        if (m.Success &&
            int.TryParse(m.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var found))
            FilesFound = found;

        m = Deferred.Match(line);
        if (m.Success &&
            int.TryParse(m.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var def))
            DeferredCount = def;

        m = Copied.Match(line);
        if (m.Success &&
            int.TryParse(m.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var copied) &&
            int.TryParse(m.Groups[2].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var dedup) &&
            int.TryParse(m.Groups[3].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var outOf))
        {
            CopiedCount = copied;
            Deduplicated = dedup;
            OutOf = outOf;
            if (FilesFound is null)
                FilesFound = outOf;
        }
    }

    /// <summary>Multi-line block for the journal, or empty when nothing useful was seen.</summary>
    public string FormatLogBlock()
    {
        if (!HasUsefulData)
            return "";

        var sb = new StringBuilder();
        sb.AppendLine("=== Оценка объёма ===");
        if (FilesFound is int n)
            sb.AppendLine($"Файлов найдено: {n}");
        if (CopiedCount is int c && OutOf is int o)
        {
            var dedup = Deduplicated ?? 0;
            sb.AppendLine($"К копированию (sim): {c} (дедуп {dedup}, из {o})");
        }
        else if (CopiedCount is int cOnly)
        {
            sb.AppendLine($"К копированию (sim): {cOnly}" +
                          (Deduplicated is int d ? $" (дедуп {d})" : ""));
        }

        if (DeferredCount is int def)
            sb.AppendLine($"Отложено (locked): {def}");

        sb.Append("Файлы не копировались (--sim).");
        return sb.ToString();
    }
}
