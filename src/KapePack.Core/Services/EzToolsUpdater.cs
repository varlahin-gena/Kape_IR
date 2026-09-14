using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using KapePack.Core.Shared;

namespace KapePack.Core.Services;

/// <summary>Checks / updates Eric Zimmerman tools under Modules\bin via Get-ZimmermanTools.</summary>
public static class EzToolsUpdater
{
    public const string GetZimmermanToolsZipUrl =
        "https://download.ericzimmermanstools.com/Get-ZimmermanTools.zip";

    private const string UserAgent = "Kape_IR/1.8.4 (+EZ Tools via Get-ZimmermanTools)";

    /// <summary>Common parsers expected for !EZParser-style workflows.</summary>
    public static readonly string[] KeyBinaries =
    {
        "PECmd.exe",
        "MFTECmd.exe",
        "RECmd.exe",
        "EvtxECmd.exe",
        "LECmd.exe",
        "JLECmd.exe",
        "AmcacheParser.exe",
        "AppCompatCacheParser.exe",
        "SBECmd.exe",
        "SQLECmd.exe",
        "WxTCmd.exe",
        "RBCmd.exe"
    };

    public static string GetModulesBin(string kapeRoot)
        => Path.Combine(kapeRoot, "Modules", "bin");

    public static EzToolsStatus Check(string kapeRoot)
    {
        var bin = GetModulesBin(kapeRoot);
        var present = new List<string>();
        var missing = new List<string>();
        foreach (var name in KeyBinaries)
        {
            // KAPE only sees Modules\bin root (not net9\) — Check must match that.
            if (EzToolsLayout.FindKapeVisibleBinary(bin, name) is not null)
                present.Add(name);
            else
                missing.Add(name);
        }

        var ok = missing.Count == 0;
        var nestedOnly = 0;
        if (!ok)
        {
            foreach (var name in missing)
            {
                if (FindBinary(bin, name) is not null)
                    nestedOnly++;
            }
        }

        var msg = ok
            ? $"EZ Tools OK ({present.Count}/{KeyBinaries.Length} ключевых в корне {bin})"
            : nestedOnly > 0
                ? $"EZ Tools: {nestedOnly} в netN, но не в корне bin (KAPE их не видит). " +
                  $"Нет в корне: {string.Join(", ", missing.Take(6))}{(missing.Count > 6 ? "…" : "")}"
                : $"EZ Tools: нет {missing.Count} из {KeyBinaries.Length} " +
                  $"({string.Join(", ", missing.Take(6))}{(missing.Count > 6 ? "…" : "")})";

        return new EzToolsStatus
        {
            Ok = ok,
            NeedsUpdate = !ok,
            ModulesBin = bin,
            Present = present,
            Missing = missing,
            Message = msg
        };
    }

    public static async Task<EzToolsUpdateResult> UpdateAsync(
        string kapeRoot,
        IProgress<string>? progress = null,
        CancellationToken ct = default,
        HttpMessageHandler? httpHandler = null,
        int netVersion = 9)
    {
        var bin = GetModulesBin(kapeRoot);
        Directory.CreateDirectory(bin);
            var tmp = Path.Combine(Path.GetTempPath(), "kape_eztools_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        using var _ = TempCleanup.Register(tmp, ct);

        try
        {
            progress?.Report("Скачивание Get-ZimmermanTools…");
            var zipPath = Path.Combine(tmp, "Get-ZimmermanTools.zip");
            await DownloadAsync(GetZimmermanToolsZipUrl, zipPath, progress, ct, httpHandler);

            progress?.Report("Распаковка Get-ZimmermanTools…");
            var scriptDir = Path.Combine(tmp, "script");
            Directory.CreateDirectory(scriptDir);
            SafeZip.ExtractToDirectory(zipPath, scriptDir);

            var ps1 = Directory.EnumerateFiles(scriptDir, "Get-ZimmermanTools.ps1", SearchOption.AllDirectories)
                .FirstOrDefault();
            if (ps1 is null)
            {
                return new EzToolsUpdateResult
                {
                    Ok = false,
                    Message = "В архиве Get-ZimmermanTools.zip нет Get-ZimmermanTools.ps1"
                };
            }

            progress?.Report($"Запуск Get-ZimmermanTools.ps1 → {bin} (net{netVersion})…");
            // Force UTF-8 so ru-RU NBSP thousands separators are not mojibake'd as «я».
            var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                ArgumentList =
                {
                    "-NoProfile",
                    "-ExecutionPolicy", "Bypass",
                    "-Command",
                    "$OutputEncoding = [Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false); " +
                    $"& '{ps1.Replace("'", "''")}' -Dest '{bin.Replace("'", "''")}' -NetVersion {netVersion}"
                },
                WorkingDirectory = Path.GetDirectoryName(ps1)!,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = utf8,
                StandardErrorEncoding = utf8
            };

            using var proc = new Process { StartInfo = psi };
            await using var killReg = ct.Register(() =>
            {
                try
                {
                    if (!proc.HasExited)
                        proc.Kill(entireProcessTree: true);
                }
                catch { /* ignore */ }
            });

            proc.OutputDataReceived += (_, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data))
                    progress?.Report("EZ: " + SanitizeToolOutput(e.Data.Trim()));
            };
            proc.ErrorDataReceived += (_, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data))
                    progress?.Report("EZ: " + SanitizeToolOutput(e.Data.Trim()));
            };

            if (!proc.Start())
                return new EzToolsUpdateResult { Ok = false, Message = "Не удалось запустить powershell.exe" };

            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();
            await proc.WaitForExitAsync(ct);

            var promoted = EzToolsLayout.PromoteNetFolderToBinRoot(bin, progress);
            // Keep netN as Get-ZimmermanTools cache; packages strip it on export.
            var status = Check(kapeRoot);
            var ok = proc.ExitCode == 0 || status.Present.Count > 0 || promoted > 0;
            var promoteNote = promoted > 0 ? $" Раскладка net→bin: {promoted} файлов." : "";
            return new EzToolsUpdateResult
            {
                Ok = ok,
                ExitCode = proc.ExitCode,
                Message = ok
                    ? $"Get-ZimmermanTools завершён (код {proc.ExitCode}).{promoteNote} {status.Message}"
                    : $"Get-ZimmermanTools код {proc.ExitCode}.{promoteNote} {status.Message}",
                Status = status
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new EzToolsUpdateResult { Ok = false, Message = "EZ Tools: " + ex.Message };
        }
        finally
        {
            TempCleanup.TryDeleteDirectory(tmp);
        }
    }

    private static string? FindBinary(string modulesBin, string fileName)
    {
        if (!Directory.Exists(modulesBin)) return null;
        var direct = Path.Combine(modulesBin, fileName);
        if (File.Exists(direct)) return direct;
        try
        {
            return Directory.EnumerateFiles(modulesBin, fileName, SearchOption.AllDirectories).FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Get-ZimmermanTools prints sizes with culture group separators (often U+00A0).
    /// On mis-decoded pipes that becomes «я» between digits — normalize for UI.
    /// </summary>
    public static string SanitizeToolOutput(string line)
    {
        if (string.IsNullOrEmpty(line)) return line;
        var s = line
            .Replace('\u00A0', ' ')
            .Replace('\u202F', ' ')
            .Replace('\u2007', ' ')
            .Replace('\u2008', ' ')
            .Replace('\u2009', ' ');
        // Already-corrupted NBSP on typical ru OEM/ANSI capture
        s = Regex.Replace(s, @"(?<=\d)я(?=\d)", " ");
        return s;
    }

    private static async Task DownloadAsync(
        string url, string destPath, IProgress<string>? progress, CancellationToken ct, HttpMessageHandler? handler)
    {
        using var client = handler is null
            ? new HttpClient(new HttpClientHandler { AllowAutoRedirect = true })
            : new HttpClient(handler, disposeHandler: false);
        client.Timeout = TimeSpan.FromMinutes(30);
        client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);

        await using var resp = await client.GetStreamAsync(url, ct);
        await using var fs = File.Create(destPath);
        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = await resp.ReadAsync(buffer.AsMemory(0, buffer.Length), ct)) > 0)
        {
            await fs.WriteAsync(buffer.AsMemory(0, read), ct);
            total += read;
            if (total % (2 * 1024 * 1024) < buffer.Length)
                progress?.Report($"Get-ZimmermanTools: {total / 1024} КБ…");
        }
    }
}

public sealed class EzToolsStatus
{
    public bool Ok { get; init; }
    public bool NeedsUpdate { get; init; }
    public string ModulesBin { get; init; } = "";
    public List<string> Present { get; init; } = new();
    public List<string> Missing { get; init; } = new();
    public string Message { get; init; } = "";
}

public sealed class EzToolsUpdateResult
{
    public bool Ok { get; init; }
    public string Message { get; init; } = "";
    public int ExitCode { get; init; }
    public EzToolsStatus? Status { get; init; }
}
