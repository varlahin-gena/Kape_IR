using KapePack.Core.Models;

namespace KapePack.Core.Services;

/// <summary>
/// Prepare CollectPack for run: unpack KAPEPACK next to EXE, validate kape.exe + package.json.
/// Exit codes align with SilentCollectionHost: 2 = args/payload/config, 3 = prep failure.
/// </summary>
public static class CollectPackPrepare
{
    public sealed record Result(
        int ExitCode,
        string Message,
        string? LaunchDir = null,
        string? PackageDir = null,
        string? KapeExe = null,
        CollectionPlan.LaunchManifest? Manifest = null);

    public static Result Prepare(
        string exePath,
        string? tsourceOverride = null,
        bool requireTsource = true,
        Action<string>? log = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
            return new Result(3, "Не удалось определить путь к EXE.");

        var launchDir = CollectPackPaths.ResolveLaunchDirectory(exePath);
        CollectPackPaths.UseAsWorkingDirectory(launchDir);
        log?.Invoke($"EXE: {exePath}");
        log?.Invoke($"Каталог запуска: {launchDir}");

        if (!KapepackPayload.TryRead(exePath, out var zipStart, out var zipLen))
            return new Result(2, "Нет payload KAPEPACK — это не автономный пакет.", launchDir);

        var packageDir = CollectPackPaths.ResolvePackageDirectory(launchDir, exePath);
        log?.Invoke($"Распаковка → {packageDir}");
        try
        {
            KapepackPayload.Extract(exePath, zipStart, zipLen, packageDir, log, ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new Result(3, "Ошибка распаковки: " + ex.Message, launchDir, packageDir);
        }

        var kape = Path.Combine(packageDir, "kape.exe");
        if (!File.Exists(kape))
            return new Result(3, "В пакете нет kape.exe: " + packageDir, launchDir, packageDir);

        var cfg = LaunchManifestIo.TryReadFromPackageDir(packageDir);
        if (cfg is null || string.IsNullOrWhiteSpace(cfg.Target))
            return new Result(2, "Нет package.json или target_compound.", launchDir, packageDir, kape);

        if (requireTsource)
        {
            var tsource = (tsourceOverride ?? cfg.Tsource ?? "").Trim();
            if (string.IsNullOrWhiteSpace(tsource))
                return new Result(2, "В silent-режиме укажите --tsource (например --tsource C:).", launchDir, packageDir, kape, cfg);

            cfg.Tsource = tsource;
        }

        return new Result(0, "OK", launchDir, packageDir, kape, cfg);
    }
}
