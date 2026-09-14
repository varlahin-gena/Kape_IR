namespace KapePack.Core.Services;

/// <summary>Best-effort cleanup of download/extract temp folders on cancel or finally.</summary>
public static class TempCleanup
{
    public static void TryDeleteDirectory(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch (Exception ex)
        {
            AppLog.Warn($"TempCleanup failed for {path}: {ex.Message}");
        }
    }

    public static void TryDeleteFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex)
        {
            AppLog.Warn($"TempCleanup file failed for {path}: {ex.Message}");
        }
    }

    /// <summary>Register deletion when <paramref name="ct"/> is cancelled (and still run in finally).</summary>
    public static IDisposable Register(string tempDir, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(tempDir))
            return EmptyDisposable.Instance;

        CancellationTokenRegistration reg = default;
        try
        {
            reg = ct.Register(() => TryDeleteDirectory(tempDir));
        }
        catch
        {
            /* ignore */
        }

        return new Reg(reg, tempDir);
    }

    private sealed class Reg : IDisposable
    {
        private CancellationTokenRegistration _reg;
        private readonly string _dir;
        private int _done;

        public Reg(CancellationTokenRegistration reg, string dir)
        {
            _reg = reg;
            _dir = dir;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _done, 1) != 0) return;
            try { _reg.Dispose(); } catch { /* ignore */ }
            TryDeleteDirectory(_dir);
        }
    }

    private sealed class EmptyDisposable : IDisposable
    {
        public static readonly EmptyDisposable Instance = new();
        public void Dispose() { }
    }
}
