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
            var needsRam = phase1Module.Equals(
                PackageDefinition.DefaultPhase1Module, StringComparison.OrdinalIgnoreCase);
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

        foreach (var phase in phases)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (rt.Simulate && phase.Options.ModuleOnly)
            {
                log("Фаза 1 (оперативный сбор) пропущена при оценке объёма — считается только диск (targets).");
                summaries.Add($"{phase.Name}: skipped — sim (targets only)");
                continue;
            }

            log(phase.Label);
            var args = KapeCliArgs.Build(phase.Options);
            log($"kape.exe {string.Join(" ", args)}");
            var exit = await runKape(kapeExe, args, packageDir, cancellationToken).ConfigureAwait(false);
            var summary = $"{phase.Name}: exit={exit} — {phase.Label}";
            summaries.Add(summary);
            if (exit != 0 && worstExit == 0)
                worstExit = exit;

            if (phase.Name == "1" && !rt.Simulate)
            {
                var phase1 = Path.Combine(resultsDir, "Phase1_Volatile");
                EvidenceWrapUp.HashMemoryDumps(phase1, log);
            }

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

    public static Task<int> StartKapeProcessAsync(
        string kape,
        List<string> args,
        string workDir,
        Action<string> onLine,
        Action<Process>? onStarted = null,
        CancellationToken cancellationToken = default)
        => KapeProcessHost.StartAsync(kape, args, workDir, onLine, onStarted, cancellationToken);
}
