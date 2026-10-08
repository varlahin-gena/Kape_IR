using System.Net.Http;

namespace KapeIR.Core.Services;

/// <summary>
/// Installs msuhanov/dfir_ntfs into Modules\bin\dfir_ntfs\ so KAPE modules can run
/// <c>python.exe %kapeDirectory%\Modules\bin\dfir_ntfs\ntfs_parser …</c>.
/// Requires Python on the triage host (module Executable is python.exe / powershell → python).
/// </summary>
public static class DfirNtfsInstaller
{
    public const string BinaryUrl =
        "https://github.com/msuhanov/dfir_ntfs/archive/refs/tags/1.1.19.zip";

    public const string TagFolderName = "dfir_ntfs-1.1.19";

    public static string GetInstallDir(string kapeRoot)
        => Path.Combine(kapeRoot, "Modules", "bin", "dfir_ntfs");

    public static string GetParserPath(string kapeRoot)
        => Path.Combine(GetInstallDir(kapeRoot), "ntfs_parser");

    public static DfirNtfsStatus Check(string kapeRoot)
    {
        var dir = GetInstallDir(kapeRoot);
        var parser = GetParserPath(kapeRoot);
        var pkg = Path.Combine(dir, "dfir_ntfs");
        var hasParser = File.Exists(parser);
        var hasPkg = Directory.Exists(pkg) &&
                     Directory.EnumerateFiles(pkg, "*.py", SearchOption.AllDirectories).Any();
        var ok = hasParser && hasPkg;
        var missing = new List<string>();
        if (!hasParser) missing.Add("ntfs_parser");
        if (!hasPkg) missing.Add("dfir_ntfs\\*.py");

        return new DfirNtfsStatus
        {
            Ok = ok,
            NeedsInstall = !ok,
            InstallDir = dir,
            MissingParts = missing,
            Message = ok
                ? $"dfir_ntfs OK ({dir})"
                : missing.Count == 0
                    ? "dfir_ntfs отсутствует"
                    : "dfir_ntfs неполный: " + string.Join(", ", missing)
        };
    }

    public static async Task<DfirNtfsInstallResult> InstallAsync(
        string kapeRoot,
        IProgress<string>? progress = null,
        CancellationToken ct = default,
        HttpMessageHandler? httpHandler = null,
        Stream? zipStreamOverride = null)
    {
        Directory.CreateDirectory(Path.Combine(kapeRoot, "Modules", "bin"));
        var dest = GetInstallDir(kapeRoot);
        using var tmp = ToolkitDownloadHelper.CreateTempWorkspace("kape_dfir_ntfs_", ct);
        var zipPath = Path.Combine(tmp.Path, "dfir_ntfs.zip");

        try
        {
            await ToolkitDownloadHelper.MaterializeZipAsync(
                    zipPath,
                    BinaryUrl,
                    zipStreamOverride,
                    progress,
                    downloadMessage: "Скачивание dfir_ntfs (msuhanov)…",
                    overrideMessage: "Распаковка dfir_ntfs (тест)…",
                    formatDownloadProgress: total =>
                        $"dfir_ntfs: скачано {total / (1024 * 1024):N0} МБ…",
                    httpHandler: httpHandler,
                    cancellationToken: ct)
                .ConfigureAwait(false);

            progress?.Report("Распаковка dfir_ntfs…");
            var extractDir = Path.Combine(tmp.Path, "extract");
            ToolkitDownloadHelper.ExtractZip(zipPath, extractDir);

            var root = FindPackageRoot(extractDir);
            if (root is null || !File.Exists(Path.Combine(root, "ntfs_parser")))
            {
                return new DfirNtfsInstallResult
                {
                    Ok = false,
                    Message = "В ZIP нет ntfs_parser (ожидался архив msuhanov/dfir_ntfs)."
                };
            }

            if (Directory.Exists(dest))
                Directory.Delete(dest, true);
            Directory.CreateDirectory(dest);

            // Layout expected by dfir_ntfs_*.mkape: Modules\bin\dfir_ntfs\ntfs_parser + package folder.
            foreach (var entry in Directory.EnumerateFileSystemEntries(root))
            {
                ct.ThrowIfCancellationRequested();
                var name = Path.GetFileName(entry);
                if (name.Equals("test_data", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("test_cases.py", StringComparison.OrdinalIgnoreCase))
                    continue;

                var target = Path.Combine(dest, name);
                if (Directory.Exists(entry))
                    ToolkitDownloadHelper.CopyDirectory(entry, target, ct);
                else
                    File.Copy(entry, target, true);
            }

            var status = Check(kapeRoot);
            return new DfirNtfsInstallResult
            {
                Ok = status.Ok,
                InstallDir = dest,
                Message = status.Ok
                    ? $"dfir_ntfs установлен: {dest}"
                    : "Установка dfir_ntfs неполная: " + status.Message
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new DfirNtfsInstallResult
            {
                Ok = false,
                Message = "Ошибка установки dfir_ntfs: " + ex.Message
            };
        }
    }

    private static string? FindPackageRoot(string extractDir)
    {
        var direct = Path.Combine(extractDir, TagFolderName);
        if (File.Exists(Path.Combine(direct, "ntfs_parser")))
            return direct;

        foreach (var dir in Directory.EnumerateDirectories(extractDir, "*", SearchOption.AllDirectories))
        {
            if (File.Exists(Path.Combine(dir, "ntfs_parser")) &&
                Directory.Exists(Path.Combine(dir, "dfir_ntfs")))
                return dir;
        }

        return null;
    }
}

public sealed class DfirNtfsStatus
{
    public bool Ok { get; init; }
    public bool NeedsInstall { get; init; }
    public string InstallDir { get; init; } = "";
    public IReadOnlyList<string> MissingParts { get; init; } = Array.Empty<string>();
    public string Message { get; init; } = "";
}

public sealed class DfirNtfsInstallResult
{
    public bool Ok { get; init; }
    public string? InstallDir { get; init; }
    public string Message { get; init; } = "";
}
