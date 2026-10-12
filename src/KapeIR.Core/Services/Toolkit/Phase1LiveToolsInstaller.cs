using System.Net.Http;
using System.Text;

namespace KapeIR.Core.Services;

/// <summary>
/// Phase 1a live-response tools under Modules\bin:
/// Sysinternals (handle/pslist/PsService) + helper scripts used by Local\Phase1 leaves
/// and stock LiveResponse_ProcessDetails.
/// Scripts are preferably seeded from embedded Phase1 catalog; this installer fills gaps
/// and downloads Sysinternals from live.sysinternals.com.
/// </summary>
public static class Phase1LiveToolsInstaller
{
    public const string SysinternalsLiveBase = "https://live.sysinternals.com/";

    public static readonly string[] RequiredSysinternals =
    {
        "handle.exe",
        "pslist.exe",
        "PsService.exe"
    };

    public static readonly string[] RequiredScripts =
    {
        "Get-InjectedThread.ps1",
        "Get-LoggedOnUsers.ps1",
        "Get-Sessions.ps1",
        "Invoke-Utf8Capture.ps1"
    };

    /// <summary>Gist raw URL matching PowerShell_Get-InjectedThread.mkape BinaryUrl.</summary>
    public const string GetInjectedThreadUrl =
        "https://gist.githubusercontent.com/jaredcatkinson/23905d34537ce4b5b1818c3e6405c1d2/raw/104f630cc1dda91d4cb81cf32ef0d67ccd3e0735/Get-InjectedThread.ps1";

    public static string GetBinDir(string kapeRoot)
        => Path.Combine(kapeRoot, "Modules", "bin");

    public static Phase1LiveToolsStatus Check(string kapeRoot)
    {
        var bin = GetBinDir(kapeRoot);
        var missing = new List<string>();
        foreach (var name in RequiredSysinternals.Concat(RequiredScripts))
        {
            if (!File.Exists(Path.Combine(bin, name)))
                missing.Add(name);
        }

        var ok = missing.Count == 0;
        return new Phase1LiveToolsStatus
        {
            Ok = ok,
            NeedsInstall = !ok,
            Missing = missing,
            Message = ok
                ? $"Phase1 live tools OK ({bin})"
                : "Phase1 live tools неполны: " + string.Join(", ", missing)
        };
    }

    public static async Task<Phase1LiveToolsInstallResult> InstallAsync(
        string kapeRoot,
        IProgress<string>? progress = null,
        CancellationToken ct = default,
        HttpMessageHandler? httpHandler = null)
    {
        Directory.CreateDirectory(GetBinDir(kapeRoot));
        var bin = GetBinDir(kapeRoot);
        var installed = new List<string>();
        var warnings = new List<string>();

        // Prefer embedded Phase1 scripts (seeder), then download Get-InjectedThread if still missing.
        progress?.Report("Phase1: засев скриптов из встроенного каталога…");
        var seed = Phase1CatalogSeeder.Ensure(kapeRoot);
        if (!seed.Ok)
            warnings.Add(seed.Message);

        foreach (var script in RequiredScripts)
        {
            var dest = Path.Combine(bin, script);
            if (File.Exists(dest))
                continue;

            if (script.Equals("Get-InjectedThread.ps1", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    progress?.Report("Скачивание Get-InjectedThread.ps1…");
                    await ToolkitDownloadHelper.DownloadToFileAsync(
                            GetInjectedThreadUrl,
                            dest,
                            progress,
                            total => $"Get-InjectedThread: {total / 1024:N0} КБ…",
                            timeout: TimeSpan.FromMinutes(2),
                            httpHandler: httpHandler,
                            cancellationToken: ct)
                        .ConfigureAwait(false);
                    if (File.Exists(dest))
                        installed.Add(script);
                    else
                        warnings.Add(script + ": пустой файл после загрузки");
                }
                catch (Exception ex)
                {
                    warnings.Add(script + ": " + ex.Message);
                }
            }
            else
            {
                warnings.Add(script + ": нет во встроенном Phase1 и не скачивается автоматически");
            }
        }

        foreach (var exe in RequiredSysinternals)
        {
            var dest = Path.Combine(bin, exe);
            if (File.Exists(dest) && new FileInfo(dest).Length > 1024)
                continue;

            var url = SysinternalsLiveBase + exe;
            try
            {
                progress?.Report($"Скачивание Sysinternals {exe}…");
                await ToolkitDownloadHelper.DownloadToFileAsync(
                        url,
                        dest,
                        progress,
                        total => $"{exe}: {total / 1024:N0} КБ…",
                        timeout: TimeSpan.FromMinutes(5),
                        httpHandler: httpHandler,
                        cancellationToken: ct)
                    .ConfigureAwait(false);
                if (File.Exists(dest) && new FileInfo(dest).Length > 1024)
                    installed.Add(exe);
                else
                    warnings.Add(exe + ": пустой/маленький файл после загрузки");
            }
            catch (Exception ex)
            {
                warnings.Add(exe + ": " + ex.Message);
            }
        }

        var status = Check(kapeRoot);
        var sb = new StringBuilder();
        if (installed.Count > 0)
            sb.AppendLine("Установлено: " + string.Join(", ", installed));
        if (warnings.Count > 0)
            sb.AppendLine("Предупреждения: " + string.Join("; ", warnings));
        sb.Append(status.Message);

        return new Phase1LiveToolsInstallResult
        {
            Ok = status.Ok,
            Message = sb.ToString().TrimEnd(),
            Installed = installed,
            Missing = status.Missing
        };
    }
}

public sealed class Phase1LiveToolsStatus
{
    public bool Ok { get; init; }
    public bool NeedsInstall { get; init; }
    public IReadOnlyList<string> Missing { get; init; } = Array.Empty<string>();
    public string Message { get; init; } = "";
}

public sealed class Phase1LiveToolsInstallResult
{
    public bool Ok { get; init; }
    public string Message { get; init; } = "";
    public IReadOnlyList<string> Installed { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Missing { get; init; } = Array.Empty<string>();
}
