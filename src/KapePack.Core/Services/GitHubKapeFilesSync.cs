using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;
using KapePack.Core.Models;
using KapePack.Core.Shared;

namespace KapePack.Core.Services;

public sealed class SyncOptions
{
    public bool DryRun { get; init; }
    /// <summary>When not dry-run, copy overwritten files under PackBuilder/sync_backup/…</summary>
    public bool BackupBeforeOverwrite { get; init; } = true;
    /// <summary>If set, fail before extract when downloaded zip SHA-256 differs.</summary>
    public string? ExpectedZipSha256 { get; init; }
    /// <summary>After successful non-dry sync, write hash to PackBuilder/last_kapefiles_zip.sha256.</summary>
    public bool RememberZipSha256 { get; init; } = true;
    /// <summary>Reuse an already-downloaded zip (skip HTTP). File must exist.</summary>
    public string? ExistingZipPath { get; init; }
    /// <summary>Copy the zip used for this sync to this path (e.g. dry-run → apply reuse).</summary>
    public string? PersistZipTo { get; init; }
}

public static class GitHubKapeFilesSync
{
    public const string Repo = "EricZimmerman/KapeFiles";
    public const string Branch = "master";
    public static string ZipUrl => $"https://github.com/{Repo}/archive/refs/heads/{Branch}.zip";
    private const string UserAgent = "Kape_IR/1.8.4 (+https://github.com/EricZimmerman/KapeFiles)";
    private const int SampleCap = 30;
    public const string LastZipSha256FileName = "last_kapefiles_zip.sha256";
    /// <summary>Relative paths (Targets/…, Modules/…) present in the last successfully read KapeFiles zip.</summary>
    public const string LastUpstreamPathsFileName = "last_kapefiles_paths.txt";

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

        var tmp = Path.Combine(Path.GetTempPath(), "kapefiles_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        using var cleanup = TempCleanup.Register(tmp, ct);
        var zipPath = Path.Combine(tmp, "kapefiles.zip");
        long zipBytes;
        string? cachedZipPath = null;
        try
        {
            if (!string.IsNullOrWhiteSpace(options.ExistingZipPath) && File.Exists(options.ExistingZipPath))
            {
                progress?.Report(options.DryRun
                    ? "Повторное использование скачанного KapeFiles ZIP (проверка)…"
                    : "Повторное использование скачанного KapeFiles ZIP…");
                File.Copy(options.ExistingZipPath, zipPath, overwrite: true);
                zipBytes = new FileInfo(zipPath).Length;
            }
            else
            {
                progress?.Report(options.DryRun
                    ? "Скачивание KapeFiles с GitHub (проверка, без записи)…"
                    : "Скачивание KapeFiles с GitHub…");
                try
                {
                    zipBytes = await DownloadToFileAsync(ZipUrl, zipPath, progress, ct, httpHandler);
                }
                catch (OperationCanceledException)
                {
                    TempCleanup.TryDeleteDirectory(tmp);
                    throw;
                }
                catch (Exception ex)
                {
                    TempCleanup.TryDeleteDirectory(tmp);
                    return new SyncResult { Ok = false, Message = $"Ошибка загрузки: {ex.Message}", IsDryRun = options.DryRun };
                }
            }
        }
        catch (OperationCanceledException)
        {
            TempCleanup.TryDeleteDirectory(tmp);
            throw;
        }
        catch (Exception ex) when (options.ExistingZipPath is not null)
        {
            TempCleanup.TryDeleteDirectory(tmp);
            return new SyncResult
            {
                Ok = false,
                Message = $"Ошибка чтения кешированного ZIP: {ex.Message}",
                IsDryRun = options.DryRun
            };
        }

        string zipSha;
        try
        {
            zipSha = ComputeFileSha256(zipPath);
        }
        catch (Exception ex)
        {
            TempCleanup.TryDeleteDirectory(tmp);
            return new SyncResult { Ok = false, Message = $"Ошибка хеша архива: {ex.Message}", IsDryRun = options.DryRun };
        }

        if (!string.IsNullOrWhiteSpace(options.ExpectedZipSha256))
        {
            var expected = options.ExpectedZipSha256.Trim().ToLowerInvariant();
            if (!string.Equals(expected, zipSha, StringComparison.Ordinal))
            {
                TempCleanup.TryDeleteDirectory(tmp);
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

            // Inventory of upstream paths (dry-run + apply) so catalog can tag local vs GitHub.
            var upstreamPaths = CollectUpstreamRelativePaths(srcTargets, srcModules);
            try
            {
                WriteUpstreamPaths(kapeRoot, upstreamPaths);
            }
            catch { /* ignore */ }

            progress?.Report(options.DryRun ? "Проверка Targets…" : "Обновление Targets…");
            var t = MergeTree(
                srcTargets,
                destTargets,
                kapeRoot,
                writingModules: false,
                progress,
                ct,
                dryRun: options.DryRun,
                backupRoot: backupRoot is null ? null : Path.Combine(backupRoot, "Targets"),
                samplePrefix: "Targets");
            progress?.Report(options.DryRun ? "Проверка Modules…" : "Обновление Modules…");
            var m = MergeTree(
                srcModules,
                destModules,
                kapeRoot,
                writingModules: true,
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
                    upstream_paths = upstreamPaths.Count,
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
            var skippedMisplaced = t.SkippedMisplaced + m.SkippedMisplaced;
            // Redirected writes from the wrong tree land in the correct tree.
            var targetsAdded = t.Added + m.RedirectedAdded;
            var targetsUpdated = t.Updated + m.RedirectedUpdated;
            var modulesAdded = m.Added + t.RedirectedAdded;
            var modulesUpdated = m.Updated + t.RedirectedUpdated;
            var changed = targetsAdded + targetsUpdated + modulesAdded + modulesUpdated;
            var prefix = options.DryRun ? "Проверка (dry-run) " : "Синхронизировано ";
            var msg = changed == 0
                ? $"{prefix}с {Repo}@{Branch}: изменений нет " +
                  $"(Targets: {t.Unchanged}, Modules: {m.Unchanged} уже актуальны)."
                : $"{prefix}с {Repo}@{Branch}.\n" +
                  $"Targets — добавлено: {targetsAdded}, обновлено: {targetsUpdated}, без изменений: {t.Unchanged}.\n" +
                  $"Modules — добавлено: {modulesAdded}, обновлено: {modulesUpdated}, без изменений: {m.Unchanged}.";
            if (skippedDup > 0)
                msg += $"\nПропущено дубликатов имён (KAPE требует уникальные имена): {skippedDup}.";
            if (skippedMisplaced > 0)
                msg += $"\nИсправлено расположение (.tkape→Targets / .mkape→Modules): {skippedMisplaced}.";
            if (errors.Count > 0) msg += $"\nОшибок файлов: {errors.Count}";
            progress?.Report(msg.Replace('\n', ' '));

            var addedSamples = t.AddedSamples.Concat(m.AddedSamples).Take(SampleCap).ToList();
            var updatedSamples = t.UpdatedSamples.Concat(m.UpdatedSamples).Take(SampleCap).ToList();
            var misplacedSamples = t.MisplacedSamples.Concat(m.MisplacedSamples).Take(SampleCap).ToList();

            if (!string.IsNullOrWhiteSpace(options.PersistZipTo))
            {
                try
                {
                    var dest = options.PersistZipTo;
                    var destDir = Path.GetDirectoryName(dest);
                    if (!string.IsNullOrEmpty(destDir))
                        Directory.CreateDirectory(destDir);
                    if (!string.Equals(
                            Path.GetFullPath(zipPath),
                            Path.GetFullPath(dest),
                            StringComparison.OrdinalIgnoreCase))
                        File.Copy(zipPath, dest, overwrite: true);
                    cachedZipPath = dest;
                }
                catch
                {
                    cachedZipPath = null;
                }
            }
            else if (!string.IsNullOrWhiteSpace(options.ExistingZipPath) && File.Exists(options.ExistingZipPath))
            {
                cachedZipPath = options.ExistingZipPath;
            }

            return new SyncResult
            {
                Ok = errors.Count == 0,
                Message = msg,
                TargetsCopied = targetsAdded + targetsUpdated,
                ModulesCopied = modulesAdded + modulesUpdated,
                TargetsAdded = targetsAdded,
                TargetsUpdated = targetsUpdated,
                TargetsUnchanged = t.Unchanged,
                ModulesAdded = modulesAdded,
                ModulesUpdated = modulesUpdated,
                ModulesUnchanged = m.Unchanged,
                ZipBytes = zipBytes,
                Errors = errors,
                SyncedAt = syncedAt,
                BackupDir = backupRoot,
                ZipSha256 = zipSha,
                IsDryRun = options.DryRun,
                CachedZipPath = cachedZipPath,
                AddedSamples = addedSamples,
                UpdatedSamples = updatedSamples,
                MisplacedSamples = misplacedSamples
            };
        }
        finally
        {
            TempCleanup.TryDeleteDirectory(tmp);
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

    /// <summary>
    /// Paths from the last KapeFiles zip (<c>Targets/…</c>, <c>Modules/…</c>).
    /// Null if the inventory file is missing (run «Обновить с GitHub…» once).
    /// </summary>
    public static HashSet<string>? ReadUpstreamPathSet(string kapeRoot)
    {
        var path = Path.Combine(kapeRoot, "PackBuilder", LastUpstreamPathsFileName);
        if (!File.Exists(path)) return null;
        try
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var line in File.ReadLines(path))
            {
                var s = CatalogOriginLabels.NormalizeRelativePath(line.Trim());
                if (s.Length == 0 || s.StartsWith('#')) continue;
                set.Add(s);
            }
            return set.Count == 0 ? null : set;
        }
        catch
        {
            return null;
        }
    }

    public static void WriteUpstreamPaths(string kapeRoot, IEnumerable<string> relativePaths)
    {
        var metaDir = Path.Combine(kapeRoot, "PackBuilder");
        Directory.CreateDirectory(metaDir);
        var path = Path.Combine(metaDir, LastUpstreamPathsFileName);
        var lines = relativePaths
            .Select(CatalogOriginLabels.NormalizeRelativePath)
            .Where(s => s.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(s => s, StringComparer.OrdinalIgnoreCase);
        File.WriteAllLines(path, lines);
    }

    /// <summary>Build relative path set from extracted KapeFiles Targets/ and Modules/ trees.</summary>
    public static HashSet<string> CollectUpstreamRelativePaths(string srcTargets, string srcModules)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        CollectTreePaths(srcTargets, writingModules: false, set);
        CollectTreePaths(srcModules, writingModules: true, set);
        return set;
    }

    private static void CollectTreePaths(string src, bool writingModules, HashSet<string> set)
    {
        if (!Directory.Exists(src)) return;
        foreach (var path in Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories))
        {
            var name = Path.GetFileName(path);
            if (name.StartsWith('.') || name.Equals(".DS_Store", StringComparison.OrdinalIgnoreCase))
                continue;
            var rel = Path.GetRelativePath(src, path).Replace('\\', '/');
            var tree = writingModules ? "Modules" : "Targets";
            if (NameCollisionFixer.IsWrongTreeExtension(name, writingModules))
                tree = writingModules ? "Targets" : "Modules";
            set.Add(tree + "/" + rel);
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
        string kapeRoot,
        bool writingModules,
        IProgress<string>? progress,
        CancellationToken ct,
        bool dryRun,
        string? backupRoot,
        string samplePrefix)
    {
        var stats = new MergeStats();
        var processed = 0;
        var uniquenessRoots = new[]
        {
            Path.Combine(kapeRoot, "Targets"),
            Path.Combine(kapeRoot, "Modules")
        };
        foreach (var path in Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();
            var name = Path.GetFileName(path);
            if (name.StartsWith('.') || name.Equals(".DS_Store", StringComparison.OrdinalIgnoreCase))
                continue;
            var rel = Path.GetRelativePath(src, path);
            var target = Path.Combine(dest, rel);
            var samplePath = (samplePrefix + "/" + rel.Replace('\\', '/')).TrimStart('/');
            string? wrongTreePath = null;
            try
            {
                if (NameCollisionFixer.IsWrongTreeExtension(name, writingModules))
                {
                    var otherTree = writingModules ? "Targets" : "Modules";
                    wrongTreePath = target;
                    var redirected = Path.Combine(kapeRoot, otherTree, rel);
                    if (stats.MisplacedSamples.Count < SampleCap)
                        stats.MisplacedSamples.Add($"{samplePath} → {otherTree}/{rel.Replace('\\', '/')}");
                    stats.SkippedMisplaced++;
                    target = redirected;
                    samplePath = otherTree + "/" + rel.Replace('\\', '/');
                }

                if (!dryRun)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    if (!NameCollisionFixer.ShouldWriteUniqueBasename(uniquenessRoots, target, path, out _))
                    {
                        stats.SkippedDuplicates++;
                        continue;
                    }
                }
                else if (ShouldSkipDuplicateDryRun(uniquenessRoots, target, path))
                {
                    stats.SkippedDuplicates++;
                    continue;
                }

                if (File.Exists(target) && FilesContentEqual(path, target))
                {
                    stats.Unchanged++;
                    // Still remove a leftover copy in the wrong tree from an earlier bad sync.
                    if (!dryRun && wrongTreePath is not null && File.Exists(wrongTreePath) &&
                        !PathsEqual(wrongTreePath, target))
                    {
                        try { File.Delete(wrongTreePath); } catch { /* ignore */ }
                    }
                }
                else
                {
                    var isNew = !File.Exists(target);
                    if (!dryRun)
                    {
                        if (!isNew && backupRoot is not null)
                        {
                            var backupTree = samplePath.StartsWith("Modules/", StringComparison.OrdinalIgnoreCase)
                                ? "Modules"
                                : "Targets";
                            var backupPath = Path.Combine(
                                Path.GetDirectoryName(backupRoot) ?? backupRoot,
                                backupTree,
                                rel);
                            Directory.CreateDirectory(Path.GetDirectoryName(backupPath)!);
                            File.Copy(target, backupPath, overwrite: true);
                        }
                        File.Copy(path, target, overwrite: true);
                        if (wrongTreePath is not null && File.Exists(wrongTreePath) &&
                            !PathsEqual(wrongTreePath, target))
                        {
                            try { File.Delete(wrongTreePath); } catch { /* ignore */ }
                        }
                    }

                    if (isNew)
                    {
                        if (wrongTreePath is not null) stats.RedirectedAdded++;
                        else stats.Added++;
                        if (stats.AddedSamples.Count < SampleCap)
                            stats.AddedSamples.Add(samplePath);
                    }
                    else
                    {
                        if (wrongTreePath is not null) stats.RedirectedUpdated++;
                        else stats.Updated++;
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
    private static bool ShouldSkipDuplicateDryRun(
        IReadOnlyList<string> searchRoots,
        string destPath,
        string srcPath)
    {
        var name = Path.GetFileName(destPath);
        if (string.IsNullOrEmpty(name)) return false;

        string? existingOther = null;
        foreach (var destRoot in searchRoots)
        {
            if (!Directory.Exists(destRoot)) continue;
            foreach (var hit in Directory.EnumerateFiles(destRoot, name, SearchOption.AllDirectories))
            {
                if (NameCollisionFixer.IsUnderDisabledFolder(hit)) continue;
                if (!PathsEqual(hit, destPath))
                {
                    existingOther = hit;
                    break;
                }
            }
            if (existingOther is not null) break;
        }

        if (existingOther is null) return false;

        var newScore = NameCollisionFixer.PathPreferScore(RelHintAny(searchRoots, destPath));
        var oldScore = NameCollisionFixer.PathPreferScore(RelHintAny(searchRoots, existingOther));

        if (File.Exists(existingOther) && FilesContentEqual(srcPath, existingOther))
            return newScore <= oldScore;

        // Prefer incoming → would write (do not skip). Else skip as occupied.
        return newScore <= oldScore;
    }

    private static string RelHintAny(IReadOnlyList<string> roots, string path)
    {
        foreach (var root in roots)
        {
            try
            {
                var rel = Path.GetRelativePath(root, path);
                if (!rel.StartsWith("..", StringComparison.Ordinal) && !Path.IsPathRooted(rel))
                {
                    var tree = Path.GetFileName(root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                    return string.IsNullOrEmpty(tree)
                        ? rel.Replace('\\', '/')
                        : tree + "/" + rel.Replace('\\', '/');
                }
            }
            catch
            {
                /* next */
            }
        }
        return RelHint(roots.Count > 0 ? roots[0] : Path.GetDirectoryName(path) ?? ".", path);
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
        public int SkippedMisplaced { get; set; }
        public int RedirectedAdded { get; set; }
        public int RedirectedUpdated { get; set; }
        public List<string> Errors { get; } = new();
        public List<string> AddedSamples { get; } = new();
        public List<string> UpdatedSamples { get; } = new();
        public List<string> MisplacedSamples { get; } = new();
    }
}
