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
            var (tCount, tErrors) = MergeTree(srcTargets, destTargets, progress);
            progress?.Report("Обновление Modules…");
            var (mCount, mErrors) = MergeTree(srcModules, destModules, progress);

            var syncedAt = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss") + " UTC";
            var meta = new
            {
                repo = Repo,
                branch = Branch,
                url = ZipUrl,
                synced_at = syncedAt,
                targets_copied = tCount,
                modules_copied = mCount
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

            var errors = tErrors.Concat(mErrors).ToList();
            var msg =
                $"Синхронизировано с {Repo}@{Branch}: обновлено/добавлено файлов Targets: {tCount}, Modules: {mCount}.";
            if (errors.Count > 0) msg += $" (ошибок файлов: {errors.Count})";
            progress?.Report(msg);

            return new SyncResult
            {
                Ok = errors.Count == 0 || tCount + mCount > 0,
                Message = msg,
                TargetsCopied = tCount,
                ModulesCopied = mCount,
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

    private static (int copied, List<string> errors) MergeTree(string src, string dest, IProgress<string>? progress)
    {
        var copied = 0;
        var errors = new List<string>();
        foreach (var path in Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories))
        {
            var name = Path.GetFileName(path);
            if (name.StartsWith('.') || name.Equals(".DS_Store", StringComparison.OrdinalIgnoreCase))
                continue;
            var rel = Path.GetRelativePath(src, path);
            var target = Path.Combine(dest, rel);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(path, target, true);
                copied++;
                if (copied % 50 == 0)
                    progress?.Report($"Копирование в {Path.GetFileName(dest)}… {copied} файлов");
            }
            catch (Exception ex)
            {
                errors.Add($"{rel.Replace('\\', '/')}: {ex.Message}");
            }
        }
        return (copied, errors);
    }
}
