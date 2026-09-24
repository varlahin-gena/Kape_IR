using System.Net.Http;
using System.Text;
using KapePack.Core.Models;

namespace KapePack.Core.Services;

/// <summary>
/// One-shot check/apply: KapeFiles (Targets/Modules) + EZ Tools + Chainsaw nest.
/// </summary>
public static class ToolkitUpdateCoordinator
{
    /// <summary>Stable cache under the selected KapeRoot (no global TEMP collision).</summary>
    public static string KapeFilesZipCachePath(string kapeRoot)
        => KapeRootPaths.KapeFilesZipCachePath(kapeRoot);

    public static async Task<ToolkitCheckReport> CheckAsync(
        string kapeRoot,
        IProgress<string>? progress = null,
        CancellationToken ct = default,
        HttpMessageHandler? httpHandler = null)
    {
        var cacheZip = KapeFilesZipCachePath(kapeRoot);
        progress?.Report("Проверка KapeFiles (Targets/Modules)…");
        var kapefiles = await GitHubKapeFilesSync.SyncAsync(
            kapeRoot, progress, ct, httpHandler,
            new SyncOptions { DryRun = true, PersistZipTo = cacheZip }).ConfigureAwait(false);

        progress?.Report("Проверка EZ Tools (Modules\\bin)…");
        var ez = EzToolsUpdater.Check(kapeRoot);

        progress?.Report("Проверка Chainsaw (Modules\\bin\\chainsaw)…");
        var chainsaw = ChainsawInstaller.Check(kapeRoot);

        var sb = new StringBuilder();
        sb.AppendLine("=== Возможность обновления ===");
        sb.AppendLine();
        sb.AppendLine("1) Targets / Modules (EricZimmerman/KapeFiles)");
        sb.AppendLine(kapefiles.Ok ? kapefiles.Message : "Ошибка: " + kapefiles.Message);
        var cfgChanges = kapefiles.TargetsAdded + kapefiles.TargetsUpdated +
                         kapefiles.ModulesAdded + kapefiles.ModulesUpdated;
        sb.AppendLine(cfgChanges > 0
            ? $"   → доступно изменений: {cfgChanges}"
            : "   → конфиги актуальны (или проверка не удалась)");
        if (!string.IsNullOrEmpty(kapefiles.CachedZipPath))
            sb.AppendLine("   → ZIP сохранён для применения без повторной загрузки");
        sb.AppendLine();
        sb.AppendLine("2) EZ Tools (Get-ZimmermanTools → Modules\\bin)");
        sb.AppendLine("   " + ez.Message);
        sb.AppendLine(ez.NeedsUpdate ? "   → рекомендуется обновление" : "   → обновление не требуется");
        sb.AppendLine();
        sb.AppendLine("3) Chainsaw (вложенность для Chainsaw.mkape)");
        sb.AppendLine("   " + chainsaw.Message);
        sb.AppendLine(chainsaw.NeedsInstall
            ? "   → нужно скачать и разложить в Modules\\bin\\chainsaw\\"
            : "   → структура на месте");

        var anything = (kapefiles.Ok && cfgChanges > 0) || ez.NeedsUpdate || chainsaw.NeedsInstall;
        return new ToolkitCheckReport
        {
            Ok = kapefiles.Ok || ez.Present.Count > 0 || chainsaw.Ok,
            AnythingToUpdate = anything,
            KapeFiles = kapefiles,
            EzTools = ez,
            Chainsaw = chainsaw,
            CachedKapeFilesZipPath = kapefiles.CachedZipPath,
            Summary = sb.ToString().TrimEnd()
        };
    }

    public static async Task<ToolkitApplyResult> ApplyAsync(
        string kapeRoot,
        ToolkitApplyOptions options,
        IProgress<string>? progress = null,
        CancellationToken ct = default,
        HttpMessageHandler? httpHandler = null)
    {
        var messages = new List<string>();
        SyncResult? sync = null;
        EzToolsUpdateResult? ez = null;
        ChainsawInstallResult? chainsaw = null;
        var ok = true;

        if (options.UpdateKapeFiles)
        {
            progress?.Report("Применение KapeFiles…");
            var existingZip = options.CachedKapeFilesZipPath;
            if (string.IsNullOrWhiteSpace(existingZip) || !File.Exists(existingZip))
                existingZip = null;

            sync = await GitHubKapeFilesSync.SyncAsync(
                kapeRoot, progress, ct, httpHandler,
                new SyncOptions
                {
                    DryRun = false,
                    BackupBeforeOverwrite = true,
                    RememberZipSha256 = true,
                    ExistingZipPath = existingZip,
                    ExpectedZipSha256 = options.ExpectedZipSha256
                }).ConfigureAwait(false);
            messages.Add(sync.Message);
            if (!string.IsNullOrEmpty(sync.BackupDir))
                messages.Add("Backup: " + sync.BackupDir);
            if (existingZip is not null && sync.Ok)
                messages.Add("KapeFiles ZIP: повторно использован кеш (без повторной загрузки).");
            ok &= sync.Ok;

            // Best-effort cleanup of check cache after successful apply.
            if (sync.Ok && existingZip is not null)
            {
                try { File.Delete(existingZip); } catch { /* ignore */ }
            }
        }

        if (options.UpdateEzTools)
        {
            progress?.Report("Обновление EZ Tools…");
            ez = await EzToolsUpdater.UpdateAsync(kapeRoot, progress, ct, httpHandler, options.EzNetVersion)
                .ConfigureAwait(false);
            messages.Add(ez.Message);
            ok &= ez.Ok;
        }

        if (options.UpdateChainsaw)
        {
            progress?.Report("Установка Chainsaw…");
            chainsaw = await ChainsawInstaller.InstallAsync(kapeRoot, progress, ct, httpHandler)
                .ConfigureAwait(false);
            messages.Add(chainsaw.Message);
            ok &= chainsaw.Ok;
        }

        return new ToolkitApplyResult
        {
            Ok = ok,
            Message = string.Join("\n\n", messages),
            KapeFiles = sync,
            EzTools = ez,
            Chainsaw = chainsaw
        };
    }
}

public sealed class ToolkitCheckReport
{
    public bool Ok { get; init; }
    public bool AnythingToUpdate { get; init; }
    public SyncResult KapeFiles { get; init; } = new();
    public EzToolsStatus EzTools { get; init; } = new();
    public ChainsawStatus Chainsaw { get; init; } = new();
    public string? CachedKapeFilesZipPath { get; init; }
    public string Summary { get; init; } = "";
}

public sealed class ToolkitApplyOptions
{
    public bool UpdateKapeFiles { get; init; } = true;
    public bool UpdateEzTools { get; init; } = true;
    public bool UpdateChainsaw { get; init; } = true;
    public int EzNetVersion { get; init; } = 9;
    /// <summary>ZIP path from <see cref="ToolkitCheckReport.CachedKapeFilesZipPath"/>.</summary>
    public string? CachedKapeFilesZipPath { get; init; }
    /// <summary>Optional pin (e.g. SHA from the preceding check).</summary>
    public string? ExpectedZipSha256 { get; init; }
}

public sealed class ToolkitApplyResult
{
    public bool Ok { get; init; }
    public string Message { get; init; } = "";
    public SyncResult? KapeFiles { get; init; }
    public EzToolsUpdateResult? EzTools { get; init; }
    public ChainsawInstallResult? Chainsaw { get; init; }
}
