using System.Text;

namespace KapeIR.Core.Services;

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
        "□ Часовой пояс хоста записан (Id / Display / UTC offset / DST) — для таймлайна",
        "□ evidence_manifest.sha256 сверен; для дампа памяти — memory_hash.sha256 (дамп в манифесте не дублируется)",
        "□ CopyLog / SkipLog / ConsoleLog без критичных пропусков",
        "□ Фаза 1 (оперативный сбор) до тяжёлого диска; --skip-memory — осознанный выбор",
        "□ findings_template.csv → findings.csv; строки EXAMPLE заменены",
        "□ Lab при необходимости: Volatility3_Triage / Hayabusa_Offline / hayabusa_IocCandidates (IOC = unverified)",
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
        log?.Invoke($"Создан файл {FindingsTemplateFileName}");
        return path;
    }

    public static string WriteCollectionLog(Context ctx, Action<string>? log = null)
    {
        var path = Path.Combine(ctx.ResultsDir, "collection_log.txt");
        var sb = new StringBuilder();
        sb.AppendLine("KapeIR.Triage — collection log");
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
        log?.Invoke("Создан файл collection_log.txt");
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
        var skippedBulk = 0;
        var hashed = 0;
        log?.Invoke("Подсчёт SHA256 для evidence_manifest.sha256…");
        foreach (var file in Directory.EnumerateFiles(resultsDir, "*", SearchOption.AllDirectories)
                     .OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            var name = Path.GetFileName(file);
            if (name.Equals("evidence_manifest.sha256", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("chain_of_custody.txt", StringComparison.OrdinalIgnoreCase))
                continue;

            var rel = Path.GetRelativePath(resultsDir, file).Replace('\\', '/');

            // MFTECmd --mp dumps 100k+ Resident\*.bin; hashing them freezes wrap-up for hours.
            if (IsBulkModuleArtifact(rel))
            {
                skippedBulk++;
                if (skippedBulk == 1)
                    lines.Add("# skipped-bulk (MFTECmd Resident / similar) — see ModuleOutput\\FileSystem\\Resident");
                continue;
            }

            // Avoid re-hashing multi-GB RAM dumps; sidecar memory_hash.sha256 is the source of truth.
            var ext = Path.GetExtension(file);
            if (ManifestSkipExtensions.Contains(ext))
            {
                skippedDumps++;
                lines.Add($"# skipped-dump (see memory_hash.sha256)  {rel}");
                continue;
            }

            try
            {
                var hash = FileHash.Sha256Hex(file);
                lines.Add($"{hash}  {rel}");
                hashed++;
                if (hashed % 500 == 0)
                    log?.Invoke($"  evidence_manifest: обработано {hashed:N0} файлов…");
            }
            catch (Exception ex)
            {
                log?.Invoke($"Не удалось посчитать SHA256 для {file}: {ex.Message}");
            }
        }

        File.WriteAllLines(path, lines, new UTF8Encoding(false));
        var extras = new List<string>();
        if (skippedDumps > 0) extras.Add($"дампов памяти: {skippedDumps}");
        if (skippedBulk > 0) extras.Add($"массовых Resident: {skippedBulk:N0}");
        var extra = extras.Count > 0 ? $", пропущено {string.Join(", ", extras)}" : "";
        log?.Invoke($"Создан файл evidence_manifest.sha256 ({lines.Count} записей{extra})");
        return path;
    }

    /// <summary>
    /// High-cardinality module dumps that must not be per-file SHA256'd in the wrap-up manifest.
    /// MFTECmd <c>-mp</c> writes 100k+ files under ModuleOutput/FileSystem/Resident/.
    /// </summary>
    internal static bool IsBulkModuleArtifact(string relativePath)
    {
        if (string.IsNullOrEmpty(relativePath))
            return false;
        var norm = relativePath.Replace('\\', '/');
        return norm.Contains("/FileSystem/Resident/", StringComparison.OrdinalIgnoreCase);
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
                log?.Invoke($"SHA256 дампа памяти: {rel}");
            }
            catch (Exception ex)
            {
                log?.Invoke($"Ошибка SHA256 дампа памяти {d}: {ex.Message}");
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
        sb.AppendLine($"Collection Method: KapeIR.Triage ({ctx.CollectionMode})");
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
        log?.Invoke("Создан файл chain_of_custody.txt");
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
