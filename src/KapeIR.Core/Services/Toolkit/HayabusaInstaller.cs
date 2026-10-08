using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace KapeIR.Core.Services;

/// <summary>
/// Installs Yamato-Security Hayabusa into Modules\bin\hayabusa\ as required by hayabusa_*.mkape:
/// hayabusa.exe + config\ + rules\ (~4.5k Sigma/Hayabusa rules from the release ZIP).
/// </summary>
public static class HayabusaInstaller
{
    public const string LatestApiUrl =
        "https://api.github.com/repos/Yamato-Security/hayabusa/releases/latest";

    public const string RelativeExe = @"Modules\bin\hayabusa\hayabusa.exe";

    internal const string ReleaseMarkerFileName = ".kapeir_release";

    public static string GetInstallDir(string kapeRoot)
        => Path.Combine(kapeRoot, "Modules", "bin", "hayabusa");

    public static string GetExePath(string kapeRoot)
        => Path.Combine(GetInstallDir(kapeRoot), "hayabusa.exe");

    public static string GetRulesDir(string kapeRoot)
        => Path.Combine(GetInstallDir(kapeRoot), "rules");

    public static string GetConfigDir(string kapeRoot)
        => Path.Combine(GetInstallDir(kapeRoot), "config");

    /// <summary>Local layout check (no network). <see cref="HayabusaStatus.NeedsUpdate"/> is true when incomplete.</summary>
    public static HayabusaStatus Check(string kapeRoot)
    {
        var dir = GetInstallDir(kapeRoot);
        var exe = GetExePath(kapeRoot);
        var rules = GetRulesDir(kapeRoot);
        var config = GetConfigDir(kapeRoot);

        var hasExe = File.Exists(exe);
        var hasRules = Directory.Exists(rules) &&
                       Directory.EnumerateFiles(rules, "*", SearchOption.AllDirectories).Any();
        var hasConfig = Directory.Exists(config) &&
                        Directory.EnumerateFileSystemEntries(config).Any();

        var ok = hasExe && hasRules && hasConfig;
        var missing = new List<string>();
        if (!hasExe) missing.Add("hayabusa.exe");
        if (!hasRules) missing.Add("rules\\");
        if (!hasConfig) missing.Add("config\\");

        DateTimeOffset? localWrite = null;
        if (hasExe)
            localWrite = File.GetLastWriteTimeUtc(exe);

        var installedTag = ReadInstalledTag(kapeRoot);

        return new HayabusaStatus
        {
            Ok = ok,
            NeedsUpdate = !ok,
            InstallDir = dir,
            MissingParts = missing,
            LocalExeWriteUtc = localWrite,
            InstalledTag = installedTag,
            Message = ok
                ? $"Hayabusa OK ({dir}" +
                  (string.IsNullOrEmpty(installedTag) ? ")" : $", {installedTag})")
                : missing.Count == 3
                    ? "Hayabusa отсутствует (нужна вложенность Modules\\bin\\hayabusa\\)"
                    : "Hayabusa неполный: " + string.Join(", ", missing)
        };
    }

    /// <summary>
    /// Local check plus GitHub latest release comparison when the tree is already complete.
    /// </summary>
    public static async Task<HayabusaStatus> CheckAsync(
        string kapeRoot,
        IProgress<string>? progress = null,
        CancellationToken ct = default,
        HttpMessageHandler? httpHandler = null)
    {
        var local = Check(kapeRoot);
        if (!local.Ok)
            return local;

        try
        {
            progress?.Report("Проверка Hayabusa (GitHub releases)…");
            var latest = await ResolveLatestReleaseAsync(httpHandler, ct).ConfigureAwait(false);
            if (latest is null)
                return local;

            var installed = local.InstalledTag;
            if (string.IsNullOrEmpty(installed))
            {
                return CloneStatus(local,
                    needsUpdate: true,
                    latestTag: latest.Tag,
                    downloadUrl: latest.DownloadUrl,
                    message: $"Hayabusa установлен без метки версии → доступен {latest.Tag}");
            }

            if (!TagsEqual(installed, latest.Tag))
            {
                return CloneStatus(local,
                    needsUpdate: true,
                    latestTag: latest.Tag,
                    downloadUrl: latest.DownloadUrl,
                    message: $"Hayabusa {NormalizeTag(installed)} → доступен {NormalizeTag(latest.Tag)}");
            }

            return CloneStatus(local,
                needsUpdate: false,
                latestTag: latest.Tag,
                downloadUrl: latest.DownloadUrl,
                message: $"Hayabusa OK ({NormalizeTag(installed)})");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return CloneStatus(local,
                message: local.Message + " (проверка GitHub: " + ex.Message + ")");
        }
    }

    private static HayabusaStatus CloneStatus(
        HayabusaStatus src,
        bool? needsUpdate = null,
        string? latestTag = null,
        string? downloadUrl = null,
        string? message = null)
        => new()
        {
            Ok = src.Ok,
            NeedsUpdate = needsUpdate ?? src.NeedsUpdate,
            InstallDir = src.InstallDir,
            MissingParts = src.MissingParts,
            LocalExeWriteUtc = src.LocalExeWriteUtc,
            InstalledTag = src.InstalledTag,
            LatestTag = latestTag ?? src.LatestTag,
            DownloadUrl = downloadUrl ?? src.DownloadUrl,
            Message = message ?? src.Message
        };

    public static async Task<HayabusaInstallResult> InstallAsync(
        string kapeRoot,
        IProgress<string>? progress = null,
        CancellationToken ct = default,
        HttpMessageHandler? httpHandler = null,
        Stream? zipStreamOverride = null,
        string? downloadUrlOverride = null,
        string? releaseTagOverride = null)
    {
        Directory.CreateDirectory(Path.Combine(kapeRoot, "Modules", "bin"));
        var dest = GetInstallDir(kapeRoot);
        using var tmp = ToolkitDownloadHelper.CreateTempWorkspace("kape_hayabusa_", ct);
        var zipPath = Path.Combine(tmp.Path, "hayabusa.zip");

        try
        {
            string? tag = releaseTagOverride;
            string? url = downloadUrlOverride;

            if (zipStreamOverride is null)
            {
                if (string.IsNullOrWhiteSpace(url))
                {
                    progress?.Report("Поиск последнего релиза Hayabusa…");
                    var latest = await ResolveLatestReleaseAsync(httpHandler, ct).ConfigureAwait(false);
                    if (latest is null)
                    {
                        return new HayabusaInstallResult
                        {
                            Ok = false,
                            Message =
                                "Не найден asset hayabusa-*-win-x64.zip в последнем релизе Yamato-Security/hayabusa."
                        };
                    }

                    url = latest.DownloadUrl;
                    tag ??= latest.Tag;
                }
            }

            await ToolkitDownloadHelper.MaterializeZipAsync(
                    zipPath,
                    url ?? "https://example.invalid/hayabusa.zip",
                    zipStreamOverride,
                    progress,
                    downloadMessage: "Скачивание Hayabusa (Yamato-Security)…",
                    overrideMessage: "Распаковка Hayabusa (тест)…",
                    formatDownloadProgress: total =>
                        $"Hayabusa: скачано {total / (1024 * 1024):N0} МБ…",
                    httpHandler: httpHandler,
                    cancellationToken: ct)
                .ConfigureAwait(false);

            progress?.Report("Распаковка Hayabusa…");
            var extractDir = Path.Combine(tmp.Path, "extract");
            ToolkitDownloadHelper.ExtractZip(zipPath, extractDir);

            var winExe = FindWindowsExecutable(extractDir);
            if (winExe is null)
            {
                return new HayabusaInstallResult
                {
                    Ok = false,
                    Message = "В архиве Hayabusa не найден Windows-бинарник (*win*x64*.exe / hayabusa*.exe)."
                };
            }

            var rulesSrc = FindNamedDirectory(extractDir, "rules");
            var configSrc = FindNamedDirectory(extractDir, "config");
            if (rulesSrc is null || configSrc is null)
            {
                return new HayabusaInstallResult
                {
                    Ok = false,
                    Message =
                        "В архиве нет папок rules / config (нужен полный win-x64 ZIP, не live-response)."
                };
            }

            progress?.Report("Установка в Modules\\bin\\hayabusa\\…");
            if (Directory.Exists(dest))
                Directory.Delete(dest, true);
            Directory.CreateDirectory(dest);

            File.Copy(winExe, Path.Combine(dest, "hayabusa.exe"), true);
            ToolkitDownloadHelper.CopyDirectory(rulesSrc, Path.Combine(dest, "rules"), ct);
            ToolkitDownloadHelper.CopyDirectory(configSrc, Path.Combine(dest, "config"), ct);

            if (!string.IsNullOrWhiteSpace(tag))
                WriteInstalledTag(kapeRoot, tag);

            var status = Check(kapeRoot);
            var ruleCount = CountRuleFiles(GetRulesDir(kapeRoot));
            return new HayabusaInstallResult
            {
                Ok = status.Ok,
                InstallDir = dest,
                ReleaseTag = tag,
                RuleFileCount = ruleCount,
                Message = status.Ok
                    ? $"Hayabusa установлен: {dest}" +
                      (string.IsNullOrEmpty(tag) ? "" : $" ({NormalizeTag(tag)})") +
                      $" · rules: {ruleCount}"
                    : "Установка завершена, но проверка не прошла: " + status.Message
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new HayabusaInstallResult { Ok = false, Message = "Hayabusa: " + ex.Message };
        }
    }

    public static async Task<HayabusaReleaseInfo?> ResolveLatestReleaseAsync(
        HttpMessageHandler? httpHandler = null,
        CancellationToken ct = default)
    {
        using var lease = SharedHttp.Acquire(httpHandler);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        linked.CancelAfter(TimeSpan.FromMinutes(2));

        using var req = new HttpRequestMessage(HttpMethod.Get, LatestApiUrl);
        req.Headers.TryAddWithoutValidation("Accept", "application/vnd.github+json");

        using var resp = await lease.Client.SendAsync(req, linked.Token).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        await using var stream = await resp.Content.ReadAsStreamAsync(linked.Token).ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: linked.Token)
            .ConfigureAwait(false);

        var root = doc.RootElement;
        var tag = root.TryGetProperty("tag_name", out var tagEl) ? tagEl.GetString() : null;
        if (string.IsNullOrWhiteSpace(tag) || !root.TryGetProperty("assets", out var assets))
            return null;

        var preferArm = RuntimeInformation.OSArchitecture == Architecture.Arm64;
        var asset = PickWindowsAsset(assets, preferArm)
                    ?? PickWindowsAsset(assets, preferArm: false);
        if (asset is null)
            return null;

        return new HayabusaReleaseInfo
        {
            Tag = tag,
            AssetName = asset.Value.Name,
            DownloadUrl = asset.Value.Url
        };
    }

    internal static (string Name, string Url)? PickWindowsAsset(JsonElement assets, bool preferArm)
    {
        var needle = preferArm ? "win-aarch64.zip" : "win-x64.zip";
        foreach (var a in assets.EnumerateArray())
        {
            var name = a.TryGetProperty("name", out var n) ? n.GetString() : null;
            var url = a.TryGetProperty("browser_download_url", out var u) ? u.GetString() : null;
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(url))
                continue;
            if (name.Contains("live-response", StringComparison.OrdinalIgnoreCase))
                continue;
            if (name.EndsWith(needle, StringComparison.OrdinalIgnoreCase))
                return (name, url);
        }

        return null;
    }

    internal static string? FindWindowsExecutable(string root)
    {
        var exes = Directory.EnumerateFiles(root, "*.exe", SearchOption.AllDirectories).ToList();
        string? Pick(Func<string, bool> pred) =>
            exes.FirstOrDefault(p => pred(Path.GetFileName(p)));

        return Pick(n => n.Contains("win", StringComparison.OrdinalIgnoreCase)
                         && n.Contains("x64", StringComparison.OrdinalIgnoreCase)
                         && !n.Contains("live", StringComparison.OrdinalIgnoreCase))
               ?? Pick(n => n.Contains("win", StringComparison.OrdinalIgnoreCase)
                            && n.Contains("aarch64", StringComparison.OrdinalIgnoreCase))
               ?? Pick(n => n.Equals("hayabusa.exe", StringComparison.OrdinalIgnoreCase))
               ?? Pick(n => n.StartsWith("hayabusa", StringComparison.OrdinalIgnoreCase)
                            && !n.Contains("linux", StringComparison.OrdinalIgnoreCase)
                            && !n.Contains("mac", StringComparison.OrdinalIgnoreCase)
                            && !n.Contains("lin-", StringComparison.OrdinalIgnoreCase));
    }

    private static string? FindNamedDirectory(string root, string name)
        => Directory.EnumerateDirectories(root, name, SearchOption.AllDirectories)
            .OrderBy(d => d.Length)
            .FirstOrDefault();

    private static int CountRuleFiles(string rulesDir)
    {
        if (!Directory.Exists(rulesDir)) return 0;
        return Directory.EnumerateFiles(rulesDir, "*.yml", SearchOption.AllDirectories).Count()
               + Directory.EnumerateFiles(rulesDir, "*.yaml", SearchOption.AllDirectories).Count();
    }

    internal static string? ReadInstalledTag(string kapeRoot)
    {
        var path = Path.Combine(GetInstallDir(kapeRoot), ReleaseMarkerFileName);
        if (!File.Exists(path)) return null;
        try
        {
            var text = File.ReadAllText(path).Trim();
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }
        catch
        {
            return null;
        }
    }

    internal static void WriteInstalledTag(string kapeRoot, string tag)
    {
        var dir = GetInstallDir(kapeRoot);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, ReleaseMarkerFileName), NormalizeTag(tag) + Environment.NewLine);
    }

    internal static string NormalizeTag(string tag)
    {
        tag = tag.Trim();
        if (tag.StartsWith("v", StringComparison.OrdinalIgnoreCase))
            tag = tag[1..].TrimStart();
        return "v" + tag;
    }

    internal static bool TagsEqual(string a, string b)
        => string.Equals(NormalizeTag(a), NormalizeTag(b), StringComparison.OrdinalIgnoreCase);
}

public sealed class HayabusaReleaseInfo
{
    public string Tag { get; init; } = "";
    public string AssetName { get; init; } = "";
    public string DownloadUrl { get; init; } = "";
}

public sealed class HayabusaStatus
{
    public bool Ok { get; init; }
    public bool NeedsUpdate { get; init; }
    public string InstallDir { get; init; } = "";
    public List<string> MissingParts { get; init; } = new();
    public DateTimeOffset? LocalExeWriteUtc { get; init; }
    public string? InstalledTag { get; init; }
    public string? LatestTag { get; init; }
    public string? DownloadUrl { get; init; }
    public string Message { get; init; } = "";
}

public sealed class HayabusaInstallResult
{
    public bool Ok { get; init; }
    public string Message { get; init; } = "";
    public string? InstallDir { get; init; }
    public string? ReleaseTag { get; init; }
    public int RuleFileCount { get; init; }
}
