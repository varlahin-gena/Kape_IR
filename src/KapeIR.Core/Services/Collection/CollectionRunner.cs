using System.Diagnostics;
using KapeIR.Core.Models;

namespace KapeIR.Core.Services;

/// <summary>Runs single- or two-phase kape collection + evidence wrap-up.</summary>
public static class CollectionRunner
{
    public sealed record Result(int ExitCode, string? ResultsDir, IReadOnlyList<string> PhaseSummaries);

    public static async Task<Result> RunAsync(
        string packageDir,
        string kapeExe,
        CollectionPlan.LaunchManifest cfg,
        CollectionPlan.RuntimeOptions rt,
        string? collectorExe,
        Action<string> log,
        Func<string, List<string>, string, CancellationToken, Task<int>> runKape,
        CancellationToken cancellationToken = default)
    {
        var started = DateTimeOffset.UtcNow;
        var phases = CollectionPlan.BuildPhases(cfg, rt);
        if (phases.Count == 0)
        {
            log("Нет фаз для запуска. Проверьте параметр --phase.");
            return new Result(2, null, Array.Empty<string>());
        }

        if (!rt.Simulate &&
            cfg.CollectionMode == IrCollectionMode.TwoPhase &&
            !rt.SkipMemory &&
            phases.Any(p => p.Name == "1"))
        {
            var phase1Module = Path.GetFileNameWithoutExtension(
                phases.First(p => p.Name == "1").Options.Module ?? "");
            var needsRam = PackageDefinition.IsPhase1CollectWithMemory(phase1Module);
            if (needsRam)
            {
                var winpmem = WinPmemBinaryGuard.InspectUnderRoot(packageDir);
                foreach (var line in WinPmemBinaryGuard.FormatRunnerLogs(winpmem))
                    log(line);
                if (winpmem.Status == WinPmemBinaryStatus.Missing)
                    return new Result(2, null, Array.Empty<string>());
            }
        }

        var summaries = new List<string>();
        var worstExit = 0;
        var resultsDir = CollectionPlan.ResolveHostResultsDir(packageDir, rt.ResultsRoot);
        var phase1Dir = Path.Combine(resultsDir, "Phase1_Volatile");

        foreach (var phase in phases)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (rt.Simulate && phase.Options.ModuleOnly)
            {
                log("Фаза 1 (оперативный сбор / Vol3) пропущена при оценке объёма — считается только диск (targets).");
                summaries.Add($"{phase.Name}: skipped — sim (targets only)");
                continue;
            }

            if (!rt.Simulate && CollectionPlan.IsPhase1b(phase.Name))
            {
                if (!TryBeginPhase1b(packageDir, phase1Dir, phase.Name, log, summaries))
                    continue;
            }

            log(phase.Label);
            var args = KapeCliArgs.Build(phase.Options);
            log($"kape.exe {string.Join(" ", args)}");
            var exit = await runKape(kapeExe, args, packageDir, cancellationToken).ConfigureAwait(false);
            var summary = $"{phase.Name}: exit={exit} — {phase.Label}";
            summaries.Add(summary);

            if (CollectionPlan.IsPhase1b(phase.Name))
            {
                // 1b failures never block Phase 2 or inflate overall exit.
                if (exit != 0)
                    log($"Предупреждение: {phase.Name} завершилась с кодом {exit} — продолжаем.");
            }
            else if (exit != 0 && worstExit == 0)
            {
                worstExit = exit;
            }

            if (phase.Name == "1" && !rt.Simulate)
                EvidenceWrapUp.HashMemoryDumps(phase1Dir, log);

            // Log after phase bookkeeping so a UI-thread Log failure cannot skip wrap-up.
            log(summary);
        }

        if (!rt.Simulate)
        {
            Directory.CreateDirectory(resultsDir);
            var caseId = !string.IsNullOrWhiteSpace(rt.CaseIdOverride)
                ? rt.CaseIdOverride!
                : cfg.CaseId;
            var mode = cfg.CollectionMode == IrCollectionMode.TwoPhase ? "two_phase" : "single";
            // Write evidence files before any further UI log traffic.
            EvidenceWrapUp.WriteAll(new EvidenceWrapUp.Context(
                resultsDir,
                caseId,
                cfg.Name,
                mode,
                collectorExe ?? "",
                summaries,
                started,
                DateTimeOffset.UtcNow), msg =>
            {
                try { log(msg); }
                catch { /* never fail wrap-up because of UI logging */ }
            });
        }

        var hasResults = Directory.Exists(resultsDir) &&
                         Directory.EnumerateFileSystemEntries(resultsDir).Any();
        if (rt.Simulate)
            return new Result(worstExit, resultsDir, summaries);

        if (!hasResults && worstExit == 0)
            worstExit = 1;

        return new Result(worstExit == 0 ? 0 : worstExit, resultsDir, summaries);
    }

    /// <summary>
    /// Gate Phase 1b: require memory dump + vol.exe. Returns false when the phase should be skipped.
    /// </summary>
    internal static bool TryBeginPhase1b(
        string packageDir,
        string phase1Dir,
        string phaseName,
        Action<string> log,
        List<string> summaries)
    {
        if (!HasMemoryDump(phase1Dir))
        {
            var msg = $"{phaseName}: skipped — нет memory.raw/memory.dmp в Phase1_Volatile";
            log("Предупреждение: " + msg);
            summaries.Add(msg);
            return false;
        }

        if (!HasVolatility3Exe(packageDir))
        {
            var msg = $"{phaseName}: skipped — нет Modules\\bin\\volatility3\\vol.exe";
            log("Предупреждение: " + msg);
            summaries.Add(msg);
            return false;
        }

        return true;
    }

    internal static bool HasMemoryDump(string phase1Dir)
    {
        if (!Directory.Exists(phase1Dir))
            return false;
        try
        {
            return Directory.EnumerateFiles(phase1Dir, "memory.raw", SearchOption.AllDirectories).Any()
                   || Directory.EnumerateFiles(phase1Dir, "memory.dmp", SearchOption.AllDirectories).Any();
        }
        catch
        {
            return false;
        }
    }

    internal static bool HasVolatility3Exe(string packageDir)
    {
        var vol = Path.Combine(packageDir, "Modules", "bin", "volatility3", "vol.exe");
        return File.Exists(vol);
    }

    public static Task<int> StartKapeProcessAsync(
        string kape,
        List<string> args,
        string workDir,
        Action<string> onLine,
        Action<Process>? onStarted = null,
        CancellationToken cancellationToken = default)
        => KapeProcessHost.StartAsync(kape, args, workDir, onLine, onStarted, cancellationToken);
}
