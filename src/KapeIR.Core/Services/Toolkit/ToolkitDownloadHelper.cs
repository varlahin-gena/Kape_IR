using System.Net.Http;
using KapeIR.Core.Shared;

namespace KapeIR.Core.Services;

/// <summary>
/// Shared temp / download / zip / copy helpers for toolkit installers
/// (Chainsaw, dfir_ntfs, EZ Tools). Layout-specific logic stays in each installer.
/// </summary>
public static class ToolkitDownloadHelper
{
    public static readonly TimeSpan DefaultDownloadTimeout = TimeSpan.FromMinutes(15);
    public const long DefaultProgressEveryBytes = 5L * 1024 * 1024;

    /// <summary>Temp folder under %TEMP% with cancel + dispose cleanup.</summary>
    public sealed class TempWorkspace : IDisposable
    {
        private readonly IDisposable _cleanup;
        private int _disposed;

        public TempWorkspace(string path, IDisposable cleanup)
        {
            Path = path;
            _cleanup = cleanup;
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            _cleanup.Dispose();
        }
    }

    public static TempWorkspace CreateTempWorkspace(string prefix, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);
        var tmp = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            prefix + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        return new TempWorkspace(tmp, TempCleanup.Register(tmp, ct));
    }

    /// <summary>
    /// Download URL → file via <see cref="SharedHttp"/> with linked timeout and optional byte progress.
    /// </summary>
    public static async Task DownloadToFileAsync(
        string url,
        string destPath,
        IProgress<string>? progress = null,
        Func<long, string>? formatProgress = null,
        TimeSpan? timeout = null,
        long progressEveryBytes = DefaultProgressEveryBytes,
        HttpMessageHandler? httpHandler = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);
        ArgumentException.ThrowIfNullOrWhiteSpace(destPath);

        using var lease = SharedHttp.Acquire(httpHandler);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linked.CancelAfter(timeout ?? DefaultDownloadTimeout);

        await using var remote = await lease.Client
            .GetStreamAsync(url, linked.Token)
            .ConfigureAwait(false);
        await using var fs = File.Create(destPath);

        var buffer = new byte[81920];
        long total = 0;
        var every = progressEveryBytes > 0 ? progressEveryBytes : DefaultProgressEveryBytes;
        int read;
        while ((read = await remote
                   .ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)
                   .ConfigureAwait(false)) > 0)
        {
            await fs.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            total += read;
            if (progress is not null && formatProgress is not null && total % every < buffer.Length)
                progress.Report(formatProgress(total));
        }
    }

    /// <summary>
    /// Write a ZIP to <paramref name="zipPath"/> from an in-memory override (tests) or by downloading <paramref name="url"/>.
    /// </summary>
    public static async Task MaterializeZipAsync(
        string zipPath,
        string url,
        Stream? zipStreamOverride,
        IProgress<string>? progress,
        string downloadMessage,
        string overrideMessage,
        Func<long, string>? formatDownloadProgress = null,
        TimeSpan? timeout = null,
        long progressEveryBytes = DefaultProgressEveryBytes,
        HttpMessageHandler? httpHandler = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(zipPath);

        if (zipStreamOverride is not null)
        {
            progress?.Report(overrideMessage);
            await using var fs = File.Create(zipPath);
            await zipStreamOverride.CopyToAsync(fs, cancellationToken).ConfigureAwait(false);
            return;
        }

        progress?.Report(downloadMessage);
        await DownloadToFileAsync(
                url,
                zipPath,
                progress,
                formatDownloadProgress,
                timeout,
                progressEveryBytes,
                httpHandler,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public static void ExtractZip(string zipPath, string extractDir, Action<double>? progress = null)
    {
        Directory.CreateDirectory(extractDir);
        SafeZip.ExtractToDirectory(zipPath, extractDir, progress: progress);
    }

    public static void CopyDirectory(string src, string dst, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(src);
        ArgumentException.ThrowIfNullOrWhiteSpace(dst);
        if (!Directory.Exists(src))
            throw new DirectoryNotFoundException(src);

        Directory.CreateDirectory(dst);
        foreach (var dir in Directory.EnumerateDirectories(src, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(System.IO.Path.Combine(dst, System.IO.Path.GetRelativePath(src, dir)));
        }

        foreach (var file in Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var dest = System.IO.Path.Combine(dst, System.IO.Path.GetRelativePath(src, file));
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(dest)!);
            File.Copy(file, dest, overwrite: true);
        }
    }
}
