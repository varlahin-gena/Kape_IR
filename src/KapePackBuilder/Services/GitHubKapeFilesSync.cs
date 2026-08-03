using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;
using KapePackBuilder.Models;

namespace KapePackBuilder.Services;

public static class GitHubKapeFilesSync
{
    public const string Repo = "EricZimmerman/KapeFiles";
    public const string Branch = "master";
    public static string ZipUrl => $"https://github.com/{Repo}/archive/refs/heads/{Branch}.zip";
    private const string UserAgent = "KapePackBuilder/1.0 (+https://github.com/EricZimmerman/KapeFiles)";

    public static async Task<SyncResult> SyncAsync(
        string kapeRoot,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        if (!Directory.Exists(kapeRoot))
            return new SyncResult { Ok = false, Message = $"Корень KAPE не найден: {kapeRoot}" };

        progress?.Report("Скачивание KapeFiles с GitHub…");
        byte[] data;
        try
        {
            data = await DownloadAsync(ZipUrl, progress, ct);
        }
        catch (Exception ex)
        {
            return new SyncResult { Ok = false, Message = $"Ошибка загрузки: {ex.Message}" };
        }

        progress?.Report($"Скачано {data.Length:N0} байт, распаковка…");
        var tmp = Path.Combine(Path.GetTempPath(), "kapefiles_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var zipPath = Path.Combine(tmp, "kapefiles.zip");
            await File.WriteAllBytesAsync(zipPath, data, ct);
            ZipFile.ExtractToDirectory(zipPath, tmp);

            var extracted = FindExtractedRoot(tmp);
            if (extracted is null)
                return new SyncResult { Ok = false, Message = "В архиве не найдена папка KapeFiles" };

            var srcTargets = Path.Combine(extracted, "Targets");
            var srcModules = Path.Combine(extracted, "Modules");
            if (!Directory.Exists(srcTargets) || !Directory.Exists(srcModules))
                return new SyncResult { Ok = false, Message = "В архиве нет Targets/Modules" };

            var destTargets = Path.Combine(kapeRoot, "Targets");
            var destModules = Path.Combine(kapeRoot, "Modules");
            Directory.CreateDirectory(destTargets);
            Directory.CreateDirectory(destModules);

            progress?.Report("Обновление Targets…");
            var t = MergeTree(srcTargets, destTargets, progress);
            progress?.Report("Обновление Modules…");
            var m = MergeTree(srcModules, destModules, progress);

            var syncedAt = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss") + " UTC";
            var meta = new
            {
                repo = Repo,
                branch = Branch,
                url = ZipUrl,
                synced_at = syncedAt,
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

            var errors = t.Errors.Concat(m.Errors).ToList();
            var changed = t.Added + t.Updated + m.Added + m.Updated;
            var msg = changed == 0
                ? $"Синхронизировано с {Repo}@{Branch}: изменений нет " +
                  $"(Targets: {t.Unchanged}, Modules: {m.Unchanged} уже актуальны)."
                : $"Синхронизировано с {Repo}@{Branch}.\n" +
                  $"Targets — добавлено: {t.Added}, обновлено: {t.Updated}, без изменений: {t.Unchanged}.\n" +
                  $"Modules — добавлено: {m.Added}, обновлено: {m.Updated}, без изменений: {m.Unchanged}.";
            if (errors.Count > 0) msg += $"\nОшибок файлов: {errors.Count}";
            progress?.Report(msg.Replace('\n', ' '));

            return new SyncResult
            {
                Ok = errors.Count == 0 || changed > 0,
                Message = msg,
                TargetsCopied = t.Added + t.Updated,
                ModulesCopied = m.Added + m.Updated,
                TargetsAdded = t.Added,
                TargetsUpdated = t.Updated,
                TargetsUnchanged = t.Unchanged,
                ModulesAdded = m.Added,
                ModulesUpdated = m.Updated,
                ModulesUnchanged = m.Unchanged,
                ZipBytes = data.Length,
                Errors = errors,
                SyncedAt = syncedAt
            };
        }
        finally
        {
            try { Directory.Delete(tmp, true); } catch { /* ignore */ }
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

    private static async Task<byte[]> DownloadAsync(string url, IProgress<string>? progress, CancellationToken ct)
    {
        using var client = new HttpClient();
        client.Timeout = TimeSpan.FromSeconds(120);
        client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        using var resp = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();
        var total = resp.Content.Headers.ContentLength ?? 0;
        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var ms = new MemoryStream();
        var buffer = new byte[256 * 1024];
        long read = 0;
        int n;
        while ((n = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), ct)) > 0)
        {
            ms.Write(buffer, 0, n);
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
        return ms.ToArray();
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

    private static MergeStats MergeTree(string src, string dest, IProgress<string>? progress)
    {
        var stats = new MergeStats();
        var processed = 0;
        foreach (var path in Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories))
        {
            var name = Path.GetFileName(path);
            if (name.StartsWith('.') || name.Equals(".DS_Store", StringComparison.OrdinalIgnoreCase))
                continue;
            var rel = Path.GetRelativePath(src, path);
            var target = Path.Combine(dest, rel);
            try
            {
                if (File.Exists(target) && FilesContentEqual(path, target))
                {
                    stats.Unchanged++;
                }
                else
                {
                    var isNew = !File.Exists(target);
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    File.Copy(path, target, overwrite: true);
                    if (isNew) stats.Added++;
                    else stats.Updated++;
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

    /// <summary>Compare by size then bytes — zip timestamps differ from local copies.</summary>
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
        public List<string> Errors { get; } = new();
    }
}
