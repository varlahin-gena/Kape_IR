using System.Net.Http;

namespace KapePack.Core.Services;

public static partial class GitHubKapeFilesSync
{
    private static async Task<long> DownloadToFileAsync(
        string url,
        string destPath,
        IProgress<string>? progress,
        CancellationToken ct,
        HttpMessageHandler? httpHandler)
    {
        using var lease = SharedHttp.Acquire(httpHandler);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        linked.CancelAfter(TimeSpan.FromSeconds(180));
        using var resp = await lease.Client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, linked.Token)
            .ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        var total = resp.Content.Headers.ContentLength ?? 0;
        await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        await using var file = File.Create(destPath);
        var buffer = new byte[256 * 1024];
        long read = 0;
        int n;
        while ((n = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), ct).ConfigureAwait(false)) > 0)
        {
            await file.WriteAsync(buffer.AsMemory(0, n), ct).ConfigureAwait(false);
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
}
