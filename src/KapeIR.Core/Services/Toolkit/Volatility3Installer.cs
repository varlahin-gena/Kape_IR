using System.Net.Http;

namespace KapeIR.Core.Services;

/// <summary>
/// Installs Volatility 3 Windows EXE pack into Modules\bin\volatility3\ for Phase 1b.
/// </summary>
public static class Volatility3Installer
{
    /// <summary>Pinned win-exes ZIP (matches Phase1_MemoryTriage BinaryUrl).</summary>
    public const string BinaryUrl =
        "https://github.com/volatilityfoundation/volatility3/releases/download/v2.28.0/volatility3-win-exes-2.28.0.zip";

    public const string ReleaseTag = "v2.28.0";
    public const string RelativeExe = @"Modules\bin\volatility3\vol.exe";

    internal const string MarkerFileName = ".kapeir_volatility3_release";

    public static string GetInstallDir(string kapeRoot)
        => Path.Combine(kapeRoot, "Modules", "bin", "volatility3");

    public static string GetExePath(string kapeRoot)
        => Path.Combine(GetInstallDir(kapeRoot), "vol.exe");

    public static string GetMarkerPath(string kapeRoot)
        => Path.Combine(GetInstallDir(kapeRoot), MarkerFileName);

    public static Volatility3Status Check(string kapeRoot)
    {
        var dir = GetInstallDir(kapeRoot);
        var exe = GetExePath(kapeRoot);
        var ok = File.Exists(exe) && new FileInfo(exe).Length > 1024;
        var tag = ReadTag(kapeRoot);
        return new Volatility3Status
        {
            Ok = ok,
            NeedsInstall = !ok,
            InstallDir = dir,
            ExePath = exe,
            InstalledTag = tag,
            Message = ok
                ? "Volatility3 OK (" + dir + (string.IsNullOrEmpty(tag) ? ")" : $", {tag})")
                : "volatility3\\vol.exe отсутствует — Phase 1b (Vol3) будет пропущен"
        };
    }

    public static async Task<Volatility3InstallResult> InstallAsync(
        string kapeRoot,
        IProgress<string>? progress = null,
        CancellationToken ct = default,
        HttpMessageHandler? httpHandler = null,
        Stream? zipStreamOverride = null)
    {
        Directory.CreateDirectory(Path.Combine(kapeRoot, "Modules", "bin"));
        var destDir = GetInstallDir(kapeRoot);
        using var tmp = ToolkitDownloadHelper.CreateTempWorkspace("kape_vol3_", ct);
        var zipPath = Path.Combine(tmp.Path, "volatility3.zip");

        try
        {
            await ToolkitDownloadHelper.MaterializeZipAsync(
                    zipPath,
                    BinaryUrl,
                    zipStreamOverride,
                    progress,
                    downloadMessage: "Скачивание Volatility 3 (win-exes)…",
                    overrideMessage: "Распаковка Volatility 3 (тест)…",
                    formatDownloadProgress: total =>
                        $"Volatility3: скачано {total / (1024 * 1024):N0} МБ…",
                    httpHandler: httpHandler,
                    cancellationToken: ct)
                .ConfigureAwait(false);

            progress?.Report("Распаковка Volatility 3…");
            var extractDir = Path.Combine(tmp.Path, "extract");
            ToolkitDownloadHelper.ExtractZip(zipPath, extractDir);

            var volExe = FindVolExe(extractDir);
            if (volExe is null)
            {
                return new Volatility3InstallResult
                {
                    Ok = false,
                    Message = "В архиве Volatility3 не найден vol.exe."
                };
            }

            if (Directory.Exists(destDir))
            {
                try { Directory.Delete(destDir, recursive: true); }
                catch
                {
                    // Fall through — overwrite files below.
                }
            }

            Directory.CreateDirectory(destDir);
            var sourceRoot = Path.GetDirectoryName(volExe)!;
            // Prefer copying the folder that contains vol.exe (may be nested in the ZIP).
            CopyDirectory(sourceRoot, destDir);

            if (!File.Exists(GetExePath(kapeRoot)))
            {
                File.Copy(volExe, GetExePath(kapeRoot), overwrite: true);
            }

            File.WriteAllText(GetMarkerPath(kapeRoot), ReleaseTag + Environment.NewLine);
            progress?.Report("Volatility 3 установлен.");
            return new Volatility3InstallResult
            {
                Ok = true,
                Message = $"Volatility3 установлен: {GetExePath(kapeRoot)} ({ReleaseTag})",
                ExePath = GetExePath(kapeRoot),
                ReleaseTag = ReleaseTag
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new Volatility3InstallResult
            {
                Ok = false,
                Message = "Volatility3: " + ex.Message
            };
        }
    }

    private static string? FindVolExe(string root)
    {
        foreach (var path in Directory.EnumerateFiles(root, "vol.exe", SearchOption.AllDirectories))
            return path;
        return null;
    }

    private static void CopyDirectory(string sourceDir, string destDir)
    {
        Directory.CreateDirectory(destDir);
        foreach (var file in Directory.EnumerateFiles(sourceDir, "*", SearchOption.TopDirectoryOnly))
        {
            var name = Path.GetFileName(file);
            File.Copy(file, Path.Combine(destDir, name), overwrite: true);
        }

        foreach (var dir in Directory.EnumerateDirectories(sourceDir, "*", SearchOption.TopDirectoryOnly))
        {
            var name = Path.GetFileName(dir);
            CopyDirectory(dir, Path.Combine(destDir, name));
        }
    }

    private static string? ReadTag(string kapeRoot)
    {
        var p = GetMarkerPath(kapeRoot);
        if (!File.Exists(p)) return null;
        try { return File.ReadAllText(p).Trim(); }
        catch { return null; }
    }
}

public sealed class Volatility3Status
{
    public bool Ok { get; init; }
    public bool NeedsInstall { get; init; }
    public string InstallDir { get; init; } = "";
    public string ExePath { get; init; } = "";
    public string? InstalledTag { get; init; }
    public string Message { get; init; } = "";
}

public sealed class Volatility3InstallResult
{
    public bool Ok { get; init; }
    public string Message { get; init; } = "";
    public string? ExePath { get; init; }
    public string? ReleaseTag { get; init; }
}
