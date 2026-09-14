namespace KapePack.Core.Services;

/// <summary>Minimal append-only file logger under LocalAppData (and optional Debug).</summary>
public static class AppLog
{
    private static readonly object Gate = new();
    private static string? _path;

    public static string LogDirectory =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "KapePackBuilder",
            "logs");

    public static string LogFilePath
    {
        get
        {
            if (_path is not null) return _path;
            Directory.CreateDirectory(LogDirectory);
            _path = Path.Combine(LogDirectory, $"kapepack-{DateTime.Now:yyyyMMdd}.log");
            return _path;
        }
    }

    public static void Info(string message) => Write("INF", message);
    public static void Warn(string message) => Write("WRN", message);
    public static void Error(string message, Exception? ex = null)
        => Write("ERR", ex is null ? message : $"{message}: {ex}");

    private static void Write(string level, string message)
    {
        var line = $"{DateTimeOffset.Now:o} [{level}] {message}";
        try
        {
            System.Diagnostics.Debug.WriteLine(line);
            lock (Gate)
            {
                File.AppendAllText(LogFilePath, line + Environment.NewLine);
            }
        }
        catch
        {
            // Never throw from logging.
        }
    }
}
