using System.Net.Http;

namespace KapeIR.Core.Services;

/// <summary>
/// Installs WithSecure Chainsaw into Modules\bin\chainsaw\ as required by Chainsaw.mkape:
/// Chainsaw.exe + rules\ + sigma\ + mappings\.
/// </summary>
public static class ChainsawInstaller
{
    public const string BinaryUrl =
        "https://github.com/WithSecureLabs/chainsaw/releases/latest/download/chainsaw_all_platforms+rules+examples.zip";

    public const string RelativeExe = @"Modules\bin\chainsaw\Chainsaw.exe";

    public static string GetInstallDir(string kapeRoot)
        => Path.Combine(kapeRoot, "Modules", "bin", "chainsaw");

    public static string GetExePath(string kapeRoot)
        => Path.Combine(GetInstallDir(kapeRoot), "Chainsaw.exe");

    public static ChainsawStatus Check(string kapeRoot)
    {
        var dir = GetInstallDir(kapeRoot);
        var exe = GetExePath(kapeRoot);
        var rules = Path.Combine(dir, "rules");
        var sigma = Path.Combine(dir, "sigma");
        var mappings = Path.Combine(dir, "mappings");
        var mappingFile = Path.Combine(mappings, "sigma-event-logs-all.yml");

        var hasExe = File.Exists(exe);
        var hasRules = Directory.Exists(rules) && Directory.EnumerateFileSystemEntries(rules).Any();
        var hasSigma = Directory.Exists(sigma) && Directory.EnumerateFileSystemEntries(sigma).Any();
        var hasMappings = File.Exists(mappingFile) ||
                          (Directory.Exists(mappings) && Directory.EnumerateFiles(mappings, "*.yml").Any());

        var ok = hasExe && hasRules && hasSigma && hasMappings;
        var missing = new List<string>();
        if (!hasExe) missing.Add("Chainsaw.exe");
        if (!hasRules) missing.Add("rules\\");
        if (!hasSigma) missing.Add("sigma\\");
        if (!hasMappings) missing.Add("mappings\\sigma-event-logs-all.yml");

        DateTimeOffset? localWrite = null;
        if (hasExe)
            localWrite = File.GetLastWriteTimeUtc(exe);

        return new ChainsawStatus
        {
            Ok = ok,
            NeedsInstall = !ok,
            InstallDir = dir,
            MissingParts = missing,
            LocalExeWriteUtc = localWrite,
            Message = ok
                ? $"Chainsaw OK ({dir})"
                : missing.Count == 4
                    ? "Chainsaw отсутствует (нужна вложенность Modules\\bin\\chainsaw\\)"
                    : "Chainsaw неполный: " + string.Join(", ", missing)
        };
    }

    public static async Task<ChainsawInstallResult> InstallAsync(
        string kapeRoot,
        IProgress<string>? progress = null,
        CancellationToken ct = default,
        HttpMessageHandler? httpHandler = null,
        Stream? zipStreamOverride = null)
    {
        Directory.CreateDirectory(Path.Combine(kapeRoot, "Modules", "bin"));
        var dest = GetInstallDir(kapeRoot);
        using var tmp = ToolkitDownloadHelper.CreateTempWorkspace("kape_chainsaw_", ct);
        var zipPath = Path.Combine(tmp.Path, "chainsaw.zip");

        try
        {
            await ToolkitDownloadHelper.MaterializeZipAsync(
                    zipPath,
                    BinaryUrl,
                    zipStreamOverride,
                    progress,
                    downloadMessage: "Скачивание Chainsaw (WithSecureLabs)…",
                    overrideMessage: "Распаковка Chainsaw (тест)…",
                    formatDownloadProgress: total =>
                        $"Chainsaw: скачано {total / (1024 * 1024):N0} МБ…",
                    httpHandler: httpHandler,
                    cancellationToken: ct)
                .ConfigureAwait(false);

            progress?.Report("Распаковка Chainsaw…");
            var extractDir = Path.Combine(tmp.Path, "extract");
            ToolkitDownloadHelper.ExtractZip(zipPath, extractDir);

            var winExe = FindWindowsExecutable(extractDir);
            if (winExe is null)
            {
                return new ChainsawInstallResult
                {
                    Ok = false,
                    Message = "В архиве Chainsaw не найден Windows-бинарник (*windows*.exe / chainsaw*.exe)."
                };
            }

            var rulesSrc = FindNamedDirectory(extractDir, "rules");
            var sigmaSrc = FindNamedDirectory(extractDir, "sigma");
            var mappingsSrc = FindNamedDirectory(extractDir, "mappings")
                              ?? FindNamedDirectory(extractDir, "mapping_files");

            if (rulesSrc is null || sigmaSrc is null || mappingsSrc is null)
            {
                return new ChainsawInstallResult
                {
                    Ok = false,
                    Message =
                        "В архиве нет ожидаемых папок rules / sigma / mappings (структура Chainsaw 2.x)."
                };
            }

            progress?.Report("Установка в Modules\\bin\\chainsaw\\…");
            if (Directory.Exists(dest))
                Directory.Delete(dest, true);
            Directory.CreateDirectory(dest);

            File.Copy(winExe, Path.Combine(dest, "Chainsaw.exe"), true);
            ToolkitDownloadHelper.CopyDirectory(rulesSrc, Path.Combine(dest, "rules"), ct);
            ToolkitDownloadHelper.CopyDirectory(sigmaSrc, Path.Combine(dest, "sigma"), ct);
            ToolkitDownloadHelper.CopyDirectory(mappingsSrc, Path.Combine(dest, "mappings"), ct);

            var status = Check(kapeRoot);
            return new ChainsawInstallResult
            {
                Ok = status.Ok,
                Message = status.Ok
                    ? $"Chainsaw установлен: {dest}"
                    : "Установка завершена, но проверка не прошла: " + status.Message,
                InstallDir = dest
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new ChainsawInstallResult { Ok = false, Message = "Chainsaw: " + ex.Message };
        }
    }

    internal static string? FindWindowsExecutable(string root)
    {
        var exes = Directory.EnumerateFiles(root, "*.exe", SearchOption.AllDirectories).ToList();
        string? Pick(Func<string, bool> pred) =>
            exes.FirstOrDefault(p => pred(Path.GetFileName(p)));

        return Pick(n => n.Contains("windows", StringComparison.OrdinalIgnoreCase)
                         && n.Contains("x86_64", StringComparison.OrdinalIgnoreCase))
               ?? Pick(n => n.Contains("windows", StringComparison.OrdinalIgnoreCase)
                            && n.Contains("msvc", StringComparison.OrdinalIgnoreCase))
               ?? Pick(n => n.Contains("windows", StringComparison.OrdinalIgnoreCase))
               ?? Pick(n => n.Equals("chainsaw.exe", StringComparison.OrdinalIgnoreCase))
               ?? Pick(n => n.StartsWith("chainsaw", StringComparison.OrdinalIgnoreCase)
                            && !n.Contains("linux", StringComparison.OrdinalIgnoreCase)
                            && !n.Contains("darwin", StringComparison.OrdinalIgnoreCase)
                            && !n.Contains("apple", StringComparison.OrdinalIgnoreCase));
    }

    private static string? FindNamedDirectory(string root, string name)
        => Directory.EnumerateDirectories(root, name, SearchOption.AllDirectories)
            .OrderBy(d => d.Length)
            .FirstOrDefault();
}

public sealed class ChainsawStatus
{
    public bool Ok { get; init; }
    public bool NeedsInstall { get; init; }
    public string InstallDir { get; init; } = "";
    public List<string> MissingParts { get; init; } = new();
    public DateTimeOffset? LocalExeWriteUtc { get; init; }
    public string Message { get; init; } = "";
}

public sealed class ChainsawInstallResult
{
    public bool Ok { get; init; }
    public string Message { get; init; } = "";
    public string? InstallDir { get; init; }
}
