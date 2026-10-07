using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace KapeIR.Core.Services;

/// <summary>
/// Process-wide logging façade for Core helpers that are not DI-aware.
/// Host apps call <see cref="Initialize"/> early; until then a simple file fallback is used.
/// </summary>
public static class AppLog
{
    private static readonly object Gate = new();
    private static ILogger _logger = NullLogger.Instance;
    private static bool _useFallback = true;
    private static string? _configuredFilePath;

    public static string LogDirectory =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            ProductIdentity.AppDataFolder,
            "logs");

    /// <summary>Current log file path (Serilog rolling file when configured, else local fallback).</summary>
    public static string LogFilePath
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(_configuredFilePath))
                return _configuredFilePath!;
            Directory.CreateDirectory(LogDirectory);
            return Path.Combine(LogDirectory, $"kapeir-{DateTime.Now:yyyyMMdd}.log");
        }
    }

    /// <summary>
    /// Wire MEL/Serilog from the composition root. Pass the active rolling file path for About/UI.
    /// </summary>
    public static void Initialize(ILoggerFactory factory, string? logFilePath = null)
    {
        ArgumentNullException.ThrowIfNull(factory);
        lock (Gate)
        {
            _logger = factory.CreateLogger(ProductIdentity.Builder);
            _configuredFilePath = string.IsNullOrWhiteSpace(logFilePath) ? null : logFilePath;
            _useFallback = false;
        }
    }

    /// <summary>Reset to null logger + file fallback (tests).</summary>
    public static void ResetForTests()
    {
        lock (Gate)
        {
            _logger = NullLogger.Instance;
            _configuredFilePath = null;
            _useFallback = true;
        }
    }

    public static void Info(string messageTemplate, params object?[] args)
        => Write(LogLevel.Information, null, messageTemplate, args);

    public static void Warn(string messageTemplate, params object?[] args)
        => Write(LogLevel.Warning, null, messageTemplate, args);

    public static void Error(string message, Exception? ex = null)
        => Write(LogLevel.Error, ex, message);

    public static void Error(Exception ex, string messageTemplate, params object?[] args)
        => Write(LogLevel.Error, ex, messageTemplate, args);

#pragma warning disable CA1848, CA2254 // façade forwards arbitrary templates from call sites
    private static void Write(LogLevel level, Exception? ex, string messageTemplate, params object?[] args)
    {
        try
        {
            if (args is { Length: > 0 })
                _logger.Log(level, ex, messageTemplate, args);
            else
                _logger.Log(level, ex, "{Message}", messageTemplate);

            if (!_useFallback)
                return;

            // Bootstrap / tests without a host logger: append a plain line.
            var rendered = args is { Length: > 0 }
                ? SafeFormat(messageTemplate, args)
                : messageTemplate;
            if (ex is not null)
                rendered = $"{rendered}: {ex}";

            var levelTag = level switch
            {
                LogLevel.Warning => "WRN",
                LogLevel.Error => "ERR",
                LogLevel.Critical => "CRT",
                _ => "INF"
            };
            var line = $"{DateTimeOffset.Now:o} [{levelTag}] {rendered}";
            System.Diagnostics.Debug.WriteLine(line);
            lock (Gate)
            {
                Directory.CreateDirectory(LogDirectory);
                File.AppendAllText(LogFilePath, line + Environment.NewLine);
            }
        }
        catch
        {
            // Never throw from logging.
        }
    }
#pragma warning restore CA1848, CA2254

    private static string SafeFormat(string template, object?[] args)
    {
        try
        {
            // MEL templates use {Name}; for fallback just append values.
            return template + " | " + string.Join(", ", args.Select(a => a?.ToString() ?? "null"));
        }
        catch
        {
            return template;
        }
    }
}
