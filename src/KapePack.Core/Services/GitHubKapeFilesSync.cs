using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;
using KapePackBuilder.Models;
using KapePackShared;

namespace KapePackBuilder.Services;

public sealed class SyncOptions
{
    public bool DryRun { get; init; }
    /// <summary>When not dry-run, copy overwritten files under PackBuilder/sync_backup/…</summary>
    public bool BackupBeforeOverwrite { get; init; } = true;
    /// <summary>If set, fail before extract when downloaded zip SHA-256 differs.</summary>
    public string? ExpectedZipSha256 { get; init; }
    /// <summary>After successful non-dry sync, write hash to PackBuilder/last_kapefiles_zip.sha256.</summary>
    public bool RememberZipSha256 { get; init; } = true;
}

public static class GitHubKapeFilesSync
{
    public const string Repo = "EricZimmerman/KapeFiles";
    public const string Branch = "master";
    public static string ZipUrl => $"https://github.com/{Repo}/archive/refs/heads/{Branch}.zip";
    private const string UserAgent = "KapePackBuilder/1.1 (+https://github.com/EricZimmerman/KapeFiles)";
    private const int SampleCap = 30;
    public const string LastZipSha256FileName = "last_kapefiles_zip.sha256";

    public static async Task<SyncResult> SyncAsync(
        string kapeRoot,
        IProgress<string>? progress = null,
        CancellationToken ct = default,
        HttpMessageHandler? httpHandler = null,
        SyncOptions? options = null)
    {
        options ??= new SyncOptions();
        if (!Directory.Exists(kapeRoot))
            return new SyncResult { Ok = false, Message = $"Корень KAPE не найден: {kapeRoot}", IsDryRun = options.DryRun };

        progress?.Report(options.DryRun
            ? "Скачивание KapeFiles с GitHub (проверка, без записи)…"
            : "Скачивание KapeFiles с GitHub…");
        var tmp = Path.Combine(Path.GetTempPath(), "kapefiles_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        var zipPath = Path.Combine(tmp, "kapefiles.zip");
        long zipBytes;
        try
        {
            zipBytes = await DownloadToFileAsync(ZipUrl, zipPath, progress, ct, httpHandler);
        }
        catch (OperationCanceledException)
        {
            try { Directory.Delete(tmp, true); } catch { /* ignore */ }
            throw;
        }
        catch (Exception ex)
        {
            try { Directory.Delete(tmp, true); } catch { /* ignore */ }
            return new SyncResult { Ok = false, Message = $"Ошибка загрузки: {ex.Message}", IsDryRun = options.DryRun };
        }

        string zipSha;
        try
        {
            zipSha = ComputeFileSha256(zipPath);
        }
        catch (Exception ex)
        {
            try { Directory.Delete(tmp, true); } catch { /* ignore */ }
            return new SyncResult { Ok = false, Message = $"Ошибка хеша архива: {ex.Message}", IsDryRun = options.DryRun };
        }

        if (!string.IsNullOrWhiteSpace(options.ExpectedZipSha256))
        {
            var expected = options.ExpectedZipSha256.Trim().ToLowerInvariant();
            if (!string.Equals(expected, zipSha, StringComparison.Ordinal))
            {
                try { Directory.Delete(tmp, true); } catch { /* ignore */ }
                return new SyncResult
                {
                    Ok = false,
                    Message =
                        $"SHA-256 архива не совпадает.\nОжидалось: {expected}\nПолучено: {zipSha}",
                    ZipBytes = zipBytes,
                    ZipSha256 = zipSha,
                    IsDryRun = options.DryRun
                };
            }
        }

        progress?.Report($"Скачано {zipBytes:N0} байт, распаковка…");
        try
        {
            try
            {
                SafeZip.ExtractToDirectory(zipPath, tmp);
            }
            catch (InvalidDataException ex)
            {
                return new SyncResult
                {
                    Ok = false,
                    Message = $"Архив отклонён (zip-slip/повреждён): {ex.Message}",
                    ZipBytes = zipBytes,
                    ZipSha256 = zipSha,
                    IsDryRun = options.DryRun
                };
            }

            var extracted = FindExtractedRoot(tmp);
            if (extracted is null)
                return new SyncResult
                {
                    Ok = false,
                    Message = "В архиве не найдена папка KapeFiles",
                    ZipBytes = zipBytes,
                    ZipSha256 = zipSha,
                    IsDryRun = options.DryRun
                };

            var srcTargets = Path.Combine(extracted, "Targets");
            var srcModules = Path.Combine(extracted, "Modules");
            if (!Directory.Exists(srcTargets) || !Directory.Exists(srcModules))
                return new SyncResult
                {
                    Ok = false,
                    Message = "В архиве нет Targets/Modules",
                    ZipBytes = zipBytes,
                    ZipSha256 = zipSha,
                    IsDryRun = options.DryRun
                };

            var destTargets = Path.Combine(kapeRoot, "Targets");
            var destModules = Path.Combine(kapeRoot, "Modules");
            if (!options.DryRun)
            {
                Directory.CreateDirectory(destTargets);
                Directory.CreateDirectory(destModules);
            }

            string? backupRoot = null;
            if (!options.DryRun && options.BackupBeforeOverwrite)
            {
                backupRoot = Path.Combine(
                    kapeRoot,
                    "PackBuilder",
                    "sync_backup",
                    DateTime.Now.ToString("yyyyMMdd_HHmmss"));
            }

            progress?.Report(options.DryRun ? "Проверка Targets…" : "Обновление Targets…");
            var t = MergeTree(
                srcTargets,
                destTargets,
                progress,
                ct,
                dryRun: options.DryRun,
                backupRoot: backupRoot is null ? null : Path.Combine(backupRoot, "Targets"),
                samplePrefix: "Targets");
            progress?.Report(options.DryRun ? "Проверка Modules…" : "Обновление Modules…");
            var m = MergeTree(
                srcModules,
                destModules,
                progress,
                ct,
                dryRun: options.DryRun,
                backupRoot: backupRoot is null ? null : Path.Combine(backupRoot, "Modules"),
                samplePrefix: "Modules");

            // Only keep backup dir in result if something was actually backed up.
            if (backupRoot is not null && !Directory.Exists(backupRoot))
                backupRoot = null;

            var syncedAt = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss") + " UTC";
            if (!options.DryRun)
            {
                var meta = new
                {
                    repo = Repo,
                    branch = Branch,
                    url = ZipUrl,
                    synced_at = syncedAt,
                    zip_sha256 = zipSha,
                    targets_added = t.Added,
                    targets_updated = t.Updated,
                    targets_unchanged = t.Unchanged,
                    modules_added = m.Added,
                    modules_updated = m.Updated,
                    modules_unchanged = m.Unchanged
                };
                try
                {
                    var metaDir = Path.Combine(kapeRoot, "PackBuilder");
                    Directory.CreateDirectory(metaDir);
                    await File.WriteAllTextAsync(
                        Path.Combine(metaDir, "last_kapefiles_sync.json"),
                        JsonSerializer.Serialize(meta, new JsonSerializerOptions { WriteIndented = true }),
                        ct);
                }
                catch { /* ignore */ }

                if (options.RememberZipSha256)
                {
                    try
                    {
                        var metaDir = Path.Combine(kapeRoot, "PackBuilder");
                        Directory.CreateDirectory(metaDir);
                        await File.WriteAllTextAsync(
                            Path.Combine(metaDir, LastZipSha256FileName),
                            zipSha + "\n",
                            ct);
                    }
                    catch { /* ignore */ }
                }
            }

            var errors = t.Errors.Concat(m.Errors).ToList();
            var skippedDup = t.SkippedDuplicates + m.SkippedDuplicates;
            var changed = t.Added + t.Updated + m.Added + m.Updated;
            var prefix = options.DryRun ? "Проверка (dry-run) " : "Синхронизировано ";
            var msg = changed == 0
                ? $"{prefix}с {Repo}@{Branch}: изменений нет " +
                  $"(Targets: {t.Unchanged}, Modules: {m.Unchanged} уже актуальны)."
                : $"{prefix}с {Repo}@{Branch}.\n" +
                  $"Targets — добавлено: {t.Added}, обновлено: {t.Updated}, без изменений: {t.Unchanged}.\n" +
                  $"Modules — добавлено: {m.Added}, обновлено: {m.Updated}, без изменений: {m.Unchanged}.";
            if (skippedDup > 0)
                msg += $"\nПропущено дубликатов имён (KAPE требует уникальные имена): {skippedDup}.";
            if (errors.Count > 0) msg += $"\nОшибок файлов: {errors.Count}";
            progress?.Report(msg.Replace('\n', ' '));

            var addedSamples = t.AddedSamples.Concat(m.AddedSamples).Take(SampleCap).ToList();
            var updatedSamples = t.UpdatedSamples.Concat(m.UpdatedSamples).Take(SampleCap).ToList();

            return new SyncResult
            {
                Ok = errors.Count == 0,
                Message = msg,
                TargetsCopied = t.Added + t.Updated,
                ModulesCopied = m.Added + m.Updated,
                TargetsAdded = t.Added,
                TargetsUpdated = t.Updated,
                TargetsUnchanged = t.Unchanged,
                ModulesAdded = m.Added,
                ModulesUpdated = m.Updated,
                ModulesUnchanged = m.Unchanged,
                ZipBytes = zipBytes,
                Errors = errors,
                SyncedAt = syncedAt,
                BackupDir = backupRoot,
                ZipSha256 = zipSha,
                IsDryRun = options.DryRun,
                AddedSamples = addedSamples,
                UpdatedSamples = updatedSamples
            };
        }
        finally
        {
            try { Directory.Delete(tmp, true); } catch { /* ignore */ }
        }
    }

    public static string ComputeFileSha256(string path) => FileHash.Sha256Hex(path);

    public static string? ReadLastZipSha256(string kapeRoot)
    {
        var path = Path.Combine(kapeRoot, "PackBuilder", LastZipSha256FileName);
        if (!File.Exists(path)) return null;
        try
        {
            var text = File.ReadAllText(path).Trim();
            if (string.IsNullOrEmpty(text)) return null;
            // Allow "hash" or "hash  filename" lines
            var first = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)[0];
            return first.ToLowerInvariant();
        }
        catch
        {
            return null;
        }
    }

    public static Dictionary<string, string>? ReadLastSync(string kapeRoot)
    {
        var path = Path.Combine(kapeRoot, "PackBuilder", "last_kapefiles_sync.json");
        if (!File.Exists(path)) return null;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in doc.RootElement.EnumerateObject())
                dict[p.Name] = p.Value.ToString();
            return dict;
        }
        catch
        {
            return null;
        }
    }

    private static async Task<long> DownloadToFileAsync(
        string url,
        string destPath,
        IProgress<string>? progress,
        CancellationToken ct,
        HttpMessageHandler? httpHandler)
    {
        using var client = httpHandler is null
            ? new HttpClient()
            : new HttpClient(httpHandler, disposeHandler: false);
        client.Timeout = TimeSpan.FromSeconds(180);
        client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        using var resp = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();
        var total = resp.Content.Headers.ContentLength ?? 0;
        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        await using var file = File.Create(destPath);
        var buffer = new byte[256 * 1024];
        long read = 0;
        int n;
        while ((n = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), ct)) > 0)
        {
            await file.WriteAsync(buffer.AsMemory(0, n), ct);
            read += n;
            if (total > 0)
            {
                var pct = (int)(read * 100 / total);
                progress?.Report($"Скачивание KapeFiles… {pct}% ({read:N0}/{total:N0} байт)");
            }
            else
            {
                progress?.Report($"Скачивание KapeFiles… {read:N0} байт");
            }
        }
        return read;
    }

    private static string? FindExtractedRoot(string tmp)
    {
        foreach (var child in Directory.EnumerateDirectories(tmp))
        {
            if (Directory.Exists(Path.Combine(child, "Targets")) &&
                Directory.Exists(Path.Combine(child, "Modules")))
                return child;
        }
        foreach (var child in Directory.EnumerateDirectories(tmp))
        {
            foreach (var nested in Directory.EnumerateDirectories(child))
            {
                if (Directory.Exists(Path.Combine(nested, "Targets")))
                    return nested;
            }
        }
        return null;
    }

    private static MergeStats MergeTree(
        string src,
        string dest,
        IProgress<string>? progress,
        CancellationToken ct,
        bool dryRun,
        string? backupRoot,
        string samplePrefix)
    {
        var stats = new MergeStats();
        var processed = 0;
        foreach (var path in Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();
            var name = Path.GetFileName(path);
            if (name.StartsWith('.') || name.Equals(".DS_Store", StringComparison.OrdinalIgnoreCase))
                continue;
            var rel = Path.GetRelativePath(src, path);
            var target = Path.Combine(dest, rel);
            var samplePath = (samplePrefix + "/" + rel.Replace('\\', '/')).TrimStart('/');
            try
            {
                if (!dryRun)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    if (!NameCollisionFixer.ShouldWriteUniqueBasename(dest, target, path, out _))
                    {
                        stats.SkippedDuplicates++;
                        continue;
                    }
                }
                else if (ShouldSkipDuplicateDryRun(dest, target, path))
                {
                    stats.SkippedDuplicates++;
                    continue;
                }

                if (File.Exists(target) && FilesContentEqual(path, target))
                {
                    stats.Unchanged++;
                }
                else
                {
                    var isNew = !File.Exists(target);
                    if (!dryRun)
                    {
                        if (!isNew && backupRoot is not null)
                        {
                            var backupPath = Path.Combine(backupRoot, rel);
                            Directory.CreateDirectory(Path.GetDirectoryName(backupPath)!);
                            File.Copy(target, backupPath, overwrite: true);
                        }
                        File.Copy(path, target, overwrite: true);
                    }

                    if (isNew)
                    {
                        stats.Added++;
                        if (stats.AddedSamples.Count < SampleCap)
                            stats.AddedSamples.Add(samplePath);
                    }
                    else
                    {
                        stats.Updated++;
                        if (stats.UpdatedSamples.Count < SampleCap)
                            stats.UpdatedSamples.Add(samplePath);
                    }
                }

                processed++;
                if (processed % 50 == 0)
                    progress?.Report(
                        $"Сверка {Path.GetFileName(dest)}… +{stats.Added} / ~{stats.Updated} / ={stats.Unchanged}");
            }
            catch (Exception ex)
            {
                stats.Errors.Add($"{rel.Replace('\\', '/')}: {ex.Message}");
            }
        }
        return stats;
    }

    /// <summary>Read-only duplicate-basename check for dry-run (never quarantines/moves).</summary>
    private static bool ShouldSkipDuplicateDryRun(string destRoot, string destPath, string srcPath)
    {
        if (!Directory.Exists(destRoot)) return false;
        var name = Path.GetFileName(destPath);
        if (string.IsNullOrEmpty(name)) return false;

        string? existingOther = null;
        foreach (var hit in Directory.EnumerateFiles(destRoot, name, SearchOption.AllDirectories))
        {
            if (!PathsEqual(hit, destPath))
            {
                existingOther = hit;
                break;
            }
        }

        if (existingOther is null) return false;

        if (File.Exists(existingOther) && FilesContentEqual(srcPath, existingOther))
            return true;

        var newScore = NameCollisionFixer.PathPreferScore(RelHint(destRoot, destPath));
        var oldScore = NameCollisionFixer.PathPreferScore(RelHint(destRoot, existingOther));
        // Prefer incoming → would write (do not skip). Else skip as occupied.
        return newScore <= oldScore;
    }

    private static bool PathsEqual(string a, string b)
        => string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);

    private static string RelHint(string root, string path)
    {
        try { return Path.GetRelativePath(root, path).Replace('\\', '/'); }
        catch { return Path.GetFileName(path); }
    }

    private static bool FilesContentEqual(string a, string b)
    {
        var fa = new FileInfo(a);
        var fb = new FileInfo(b);
        if (fa.Length != fb.Length) return false;
        if (fa.Length == 0) return true;

        using var sa = File.OpenRead(a);
        using var sb = File.OpenRead(b);
        var bufA = new byte[64 * 1024];
        var bufB = new byte[64 * 1024];
        while (true)
        {
            var na = sa.Read(bufA, 0, bufA.Length);
            var nb = sb.Read(bufB, 0, bufB.Length);
            if (na != nb) return false;
            if (na == 0) return true;
            if (!bufA.AsSpan(0, na).SequenceEqual(bufB.AsSpan(0, nb)))
                return false;
        }
    }

    private sealed class MergeStats
    {
        public int Added { get; set; }
        public int Updated { get; set; }
        public int Unchanged { get; set; }
        public int SkippedDuplicates { get; set; }
        public List<string> Errors { get; } = new();
        public List<string> AddedSamples { get; } = new();
        public List<string> UpdatedSamples { get; } = new();
    }
}
