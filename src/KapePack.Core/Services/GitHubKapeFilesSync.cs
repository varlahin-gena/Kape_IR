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

public static partial class GitHubKapeFilesSync
{
    public const string Repo = "EricZimmerman/KapeFiles";
    public const string Branch = "master";
    public static string ZipUrl => $"https://github.com/{Repo}/archive/refs/heads/{Branch}.zip";
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
                    zipBytes = await DownloadToFileAsync(ZipUrl, zipPath, progress, ct, httpHandler)
                        .ConfigureAwait(false);
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
                        ct).ConfigureAwait(false);
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
                            ct).ConfigureAwait(false);
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
}
