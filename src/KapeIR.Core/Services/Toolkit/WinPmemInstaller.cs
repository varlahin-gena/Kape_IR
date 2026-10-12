using System.Net.Http;

namespace KapeIR.Core.Services;

/// <summary>
/// Installs signed go-winpmem as Modules\bin\winpmem.exe (Phase 1a RAM dump).
/// </summary>
public static class WinPmemInstaller
{
    /// <summary>Pinned signed artifact (Secure Boot). Matches Velocidex_WinPmem.mkape BinaryUrl.</summary>
    public const string BinaryUrl =
        "https://github.com/Velocidex/WinPmem/releases/download/v4.0.rc1/go-winpmem_amd64_1.0-rc2_signed.exe";

    public const string RelativeExe = @"Modules\bin\winpmem.exe";
    public const string ReleaseTag = "v4.0.rc1-signed";

    internal const string MarkerFileName = ".kapeir_winpmem_release";

    public static string GetExePath(string kapeRoot)
        => Path.Combine(kapeRoot, "Modules", "bin", "winpmem.exe");

    public static string GetMarkerPath(string kapeRoot)
        => Path.Combine(kapeRoot, "Modules", "bin", MarkerFileName);

    public static WinPmemStatus Check(string kapeRoot)
    {
        var exe = GetExePath(kapeRoot);
        var ok = File.Exists(exe) && new FileInfo(exe).Length > 1024;
        var tag = ReadTag(kapeRoot);
        return new WinPmemStatus
        {
            Ok = ok,
            NeedsInstall = !ok,
            ExePath = exe,
            InstalledTag = tag,
            Message = ok
                ? "WinPmem OK (" + exe + (string.IsNullOrEmpty(tag) ? ")" : $", {tag})")
                : "winpmem.exe отсутствует — Phase 1a не снимет дамп памяти"
        };
    }

    public static async Task<WinPmemInstallResult> InstallAsync(
        string kapeRoot,
        IProgress<string>? progress = null,
        CancellationToken ct = default,
        HttpMessageHandler? httpHandler = null,
        Stream? exeStreamOverride = null)
    {
        Directory.CreateDirectory(Path.Combine(kapeRoot, "Modules", "bin"));
        var dest = GetExePath(kapeRoot);
        using var tmp = ToolkitDownloadHelper.CreateTempWorkspace("kape_winpmem_", ct);
        var dlPath = Path.Combine(tmp.Path, "winpmem.exe");

        try
        {
            if (exeStreamOverride is not null)
            {
                progress?.Report("Установка WinPmem (тест)…");
                await using var fs = File.Create(dlPath);
                await exeStreamOverride.CopyToAsync(fs, ct).ConfigureAwait(false);
            }
            else
            {
                progress?.Report("Скачивание WinPmem (Velocidex signed)…");
                await ToolkitDownloadHelper.DownloadToFileAsync(
                        BinaryUrl,
                        dlPath,
                        progress,
                        total => $"WinPmem: скачано {total / (1024 * 1024):N0} МБ…",
                        httpHandler: httpHandler,
                        cancellationToken: ct)
                    .ConfigureAwait(false);
            }

            if (!File.Exists(dlPath) || new FileInfo(dlPath).Length < 1024)
            {
                return new WinPmemInstallResult
                {
                    Ok = false,
                    Message = "Скачанный winpmem.exe пустой или слишком маленький."
                };
            }

            File.Copy(dlPath, dest, overwrite: true);
            File.WriteAllText(GetMarkerPath(kapeRoot), ReleaseTag + Environment.NewLine);
            progress?.Report("WinPmem установлен.");
            return new WinPmemInstallResult
            {
                Ok = true,
                Message = $"WinPmem установлен: {dest} ({ReleaseTag})",
                ExePath = dest,
                ReleaseTag = ReleaseTag
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new WinPmemInstallResult
            {
                Ok = false,
                Message = "WinPmem: " + ex.Message
            };
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

public sealed class WinPmemStatus
{
    public bool Ok { get; init; }
    public bool NeedsInstall { get; init; }
    public string ExePath { get; init; } = "";
    public string? InstalledTag { get; init; }
    public string Message { get; init; } = "";
}

public sealed class WinPmemInstallResult
{
    public bool Ok { get; init; }
    public string Message { get; init; } = "";
    public string? ExePath { get; init; }
    public string? ReleaseTag { get; init; }
}
