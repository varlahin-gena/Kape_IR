using System.Text;

namespace KapePack.Core.Services;

/// <summary>collection_log, SHA256 manifest, chain-of-custody for IR packs.</summary>
public static class EvidenceWrapUp
{
    public sealed record Context(
        string ResultsDir,
        string CaseId,
        string PackageName,
        string CollectionMode,
        string CollectorExe,
        IReadOnlyList<string> PhaseSummaries,
        DateTimeOffset StartedUtc,
        DateTimeOffset EndedUtc);

    public static void WriteAll(Context ctx, Action<string>? log = null)
    {
        Directory.CreateDirectory(ctx.ResultsDir);
        WriteCollectionLog(ctx, log);
        var manifestPath = WriteManifest(ctx.ResultsDir, log);
        WriteChainOfCustody(ctx, manifestPath, log);
    }

    public static string WriteCollectionLog(Context ctx, Action<string>? log = null)
    {
        var path = Path.Combine(ctx.ResultsDir, "collection_log.txt");
        var sb = new StringBuilder();
        sb.AppendLine("KAPE Pack — collection log");
        sb.AppendLine("==========================");
        sb.AppendLine($"Case ID: {NullDash(ctx.CaseId)}");
        sb.AppendLine($"Package: {ctx.PackageName}");
        sb.AppendLine($"Mode: {ctx.CollectionMode}");
        sb.AppendLine($"Host: {Environment.MachineName}");
        sb.AppendLine($"User: {Environment.UserDomainName}\\{Environment.UserName}");
        sb.AppendLine($"Started (UTC): {ctx.StartedUtc:o}");
        sb.AppendLine($"Ended (UTC): {ctx.EndedUtc:o}");
        sb.AppendLine($"Duration: {ctx.EndedUtc - ctx.StartedUtc}");
        sb.AppendLine($"Collector EXE: {ctx.CollectorExe}");
        try
        {
            if (File.Exists(ctx.CollectorExe))
                sb.AppendLine($"Collector SHA256: {FileHash.Sha256Hex(ctx.CollectorExe)}");
        }
        catch { /* ignore */ }

        sb.AppendLine();
        sb.AppendLine("Phases:");
        foreach (var line in ctx.PhaseSummaries)
            sb.AppendLine("  " + line);

        sb.AppendLine();
        sb.AppendLine($"Local time at write: {DateTimeOffset.Now:o}");
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        log?.Invoke("Записан collection_log.txt");
        return path;
    }

    /// <summary>Memory dumps are hashed once via <see cref="HashMemoryDumps"/> → memory_hash.sha256.</summary>
    private static readonly HashSet<string> ManifestSkipExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".raw", ".aff4", ".lime", ".dmp", ".mem"
    };

    public static string WriteManifest(string resultsDir, Action<string>? log = null)
    {
        var path = Path.Combine(resultsDir, "evidence_manifest.sha256");
        var lines = new List<string>();
        var skippedDumps = 0;
        foreach (var file in Directory.EnumerateFiles(resultsDir, "*", SearchOption.AllDirectories)
                     .OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            var name = Path.GetFileName(file);
            if (name.Equals("evidence_manifest.sha256", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("chain_of_custody.txt", StringComparison.OrdinalIgnoreCase))
                continue;

            // Avoid re-hashing multi-GB RAM dumps; sidecar memory_hash.sha256 is the source of truth.
            var ext = Path.GetExtension(file);
            if (ManifestSkipExtensions.Contains(ext))
            {
                skippedDumps++;
                var relSkip = Path.GetRelativePath(resultsDir, file).Replace('\\', '/');
                lines.Add($"# skipped-dump (see memory_hash.sha256)  {relSkip}");
                continue;
            }

            try
            {
                var hash = FileHash.Sha256Hex(file);
                var rel = Path.GetRelativePath(resultsDir, file).Replace('\\', '/');
                lines.Add($"{hash}  {rel}");
            }
            catch (Exception ex)
            {
                log?.Invoke($"Не удалось захэшировать {file}: {ex.Message}");
            }
        }

        File.WriteAllLines(path, lines, new UTF8Encoding(false));
        var extra = skippedDumps > 0 ? $", пропущено дампов: {skippedDumps}" : "";
        log?.Invoke($"Записан evidence_manifest.sha256 ({lines.Count} записей{extra})");
        return path;
    }

    /// <summary>Hash memory dump(s) immediately after phase 1 when present.</summary>
    public static void HashMemoryDumps(string phase1Dir, Action<string>? log = null)
    {
        if (!Directory.Exists(phase1Dir)) return;
        var dumps = Directory.EnumerateFiles(phase1Dir, "*.raw", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(phase1Dir, "*.aff4", SearchOption.AllDirectories))
            .Concat(Directory.EnumerateFiles(phase1Dir, "*.lime", SearchOption.AllDirectories))
            .ToList();
        if (dumps.Count == 0) return;

        var outFile = Path.Combine(phase1Dir, "memory_hash.sha256");
        var lines = new List<string>();
        foreach (var d in dumps)
        {
            try
            {
                var hash = FileHash.Sha256Hex(d);
                var rel = Path.GetRelativePath(phase1Dir, d).Replace('\\', '/');
                lines.Add($"{hash}  {rel}");
                log?.Invoke($"SHA256 memdump: {rel}");
            }
            catch (Exception ex)
            {
                log?.Invoke($"Ошибка хеша дампа {d}: {ex.Message}");
            }
        }

        File.WriteAllLines(outFile, lines, new UTF8Encoding(false));
    }

    public static string WriteChainOfCustody(Context ctx, string manifestPath, Action<string>? log = null)
    {
        var path = Path.Combine(ctx.ResultsDir, "chain_of_custody.txt");
        var sb = new StringBuilder();
        sb.AppendLine("CHAIN OF CUSTODY RECORD");
        sb.AppendLine("========================");
        sb.AppendLine($"Case ID: {NullDash(ctx.CaseId)}");
        sb.AppendLine($"Collection Date (UTC): {ctx.StartedUtc:o} — {ctx.EndedUtc:o}");
        sb.AppendLine($"Collected By: {Environment.UserDomainName}\\{Environment.UserName}");
        sb.AppendLine($"System: {Environment.MachineName}");
        sb.AppendLine($"Collection Method: KAPE Pack CollectPack ({ctx.CollectionMode})");
        sb.AppendLine($"Package: {ctx.PackageName}");
        sb.AppendLine();
        sb.AppendLine("Evidence root:");
        sb.AppendLine($"  {ctx.ResultsDir}");
        sb.AppendLine();
        sb.AppendLine($"SHA256 Manifest: {Path.GetFileName(manifestPath)}");
        sb.AppendLine("Transfer: [TO BE COMPLETED]");
        sb.AppendLine("Storage Location: [TO BE COMPLETED]");
        sb.AppendLine();
        sb.AppendLine("Phase summary:");
        foreach (var line in ctx.PhaseSummaries)
            sb.AppendLine("  " + line);

        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        log?.Invoke("Записан chain_of_custody.txt");
        return path;
    }

    private static string NullDash(string? s) => string.IsNullOrWhiteSpace(s) ? "[NOT SET]" : s.Trim();
}
