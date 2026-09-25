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
        WriteFindingsTemplate(ctx.ResultsDir, log);
        var manifestPath = WriteManifest(ctx.ResultsDir, log);
        WriteChainOfCustody(ctx, manifestPath, log);
    }

    public const string FindingsTemplateFileName = "findings_template.csv";

    /// <summary>digital-forensics P2-9 — post-collection checklist (RU), shared with package README.</summary>
    public static IReadOnlyList<string> PostCollectionChecklistRu { get; } = new[]
    {
        "□ Case ID задан; есть collection_log.txt и chain_of_custody.txt",
        "□ TimeZone Id / Display / UTC offset / DST записаны (для таймлайна)",
        "□ evidence_manifest.sha256 сверен; для RAM — memory_hash.sha256 (дамп не дублируется в манифесте)",
        "□ CopyLog / SkipLog / ConsoleLog без критичных пропусков",
        "□ Phase1 (volatile) до тяжёлого диска; при --skip-memory это осознанно",
        "□ findings_template.csv → findings.csv; EXAMPLE-строки заменены",
        "□ Lab: Volatility3_Triage / Hayabusa_Offline / hayabusa_IocCandidates по необходимости (IOC = unverified)",
        "□ Передача evidence: заполнить Transfer / Storage в chain_of_custody.txt",
    };

    /// <summary>
    /// digital-forensics P2-7: analyst worksheet (header + EXAMPLE rows). Copy to findings.csv for the case.
    /// </summary>
    public static string FindingsTemplateCsv { get; } =
        "Time,Host,Artifact,Finding,Confidence,Evidence path\r\n" +
        "2026-09-24T00:00:00Z,EXAMPLE-HOST,Prefetch,\"EXAMPLE — replace me: suspicious binary executed\",unverified,Phase2_Disk\\C\\Windows\\Prefetch\\EXAMPLE.EXE-XXXXXXXX.pf\r\n" +
        "2026-09-24T00:00:00Z,EXAMPLE-HOST,EventLog,\"EXAMPLE — replace me: anomalous logon or PowerShell activity\",unverified,Phase2_Disk\\C\\Windows\\System32\\winevt\\Logs\\Security.evtx\r\n" +
        "2026-09-24T00:00:00Z,EXAMPLE-HOST,Netstat,\"EXAMPLE — replace me: unusual outbound connection\",unverified,Phase1_Volatile\\network_connections.txt\r\n";

    public static string WriteFindingsTemplate(string directory, Action<string>? log = null)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, FindingsTemplateFileName);
        File.WriteAllText(path, FindingsTemplateCsv, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        log?.Invoke($"Записан {FindingsTemplateFileName}");
        return path;
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
        AppendHostTimeZone(sb, ctx.EndedUtc);
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
        sb.AppendLine("Findings worksheet:");
        sb.AppendLine($"  {FindingsTemplateFileName} — copy to findings.csv; replace EXAMPLE rows (Confidence: unverified).");
        sb.AppendLine("  Columns: Time | Host | Artifact | Finding | Confidence | Evidence path");

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
        AppendHostTimeZone(sb, ctx.EndedUtc);
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

    /// <summary>
    /// Host timezone at collection (TimeZoneInfo.Local on the CollectPack machine).
    /// digital-forensics P0-2: Id, Display, UTC offset, DST — for defensible timelines.
    /// </summary>
    public static void AppendHostTimeZone(StringBuilder sb, DateTimeOffset at)
    {
        var tz = TimeZoneInfo.Local;
        var offset = tz.GetUtcOffset(at);
        var sign = offset < TimeSpan.Zero ? "-" : "+";
        var abs = offset.Duration();
        var offsetStr = $"{sign}{abs.Hours:D2}:{abs.Minutes:D2}";
        var localAt = TimeZoneInfo.ConvertTime(at, tz);

        sb.AppendLine($"TimeZone Id: {tz.Id}");
        sb.AppendLine($"TimeZone Display: {tz.DisplayName}");
        sb.AppendLine($"UTC offset at collection: {offsetStr}");
        sb.AppendLine($"Daylight saving: {tz.IsDaylightSavingTime(localAt.DateTime)}");
    }

    private static string NullDash(string? s) => string.IsNullOrWhiteSpace(s) ? "[NOT SET]" : s.Trim();
}
