using System.IO;
using KapeIR.Core.Models;

namespace KapeIR.Core.Services;

/// <summary>Builds ordered kape.exe invocations for single or two-phase IR packs.</summary>
public static class CollectionPlan
{
    public sealed record Phase(
        string Name,
        string Label,
        KapeCliArgs.Options Options);

    public sealed record RuntimeOptions(
        string Tsource,
        bool Simulate = false,
        /// <summary>1, 2, or null for all phases.</summary>
        int? PhaseFilter = null,
        bool SkipMemory = false,
        /// <summary>
        /// Skip Phase 1b (Volatility3 triage/deep) while still collecting a RAM dump in 1a.
        /// Implied when <see cref="SkipMemory"/> is true.
        /// </summary>
        bool SkipMemoryAnalysis = false,
        string? CaseIdOverride = null,
        /// <summary>
        /// Parent folder for RESULTS\&lt;host&gt;\… Absolute paths are passed to kape.
        /// Null/empty → relative RESULTS under the package working directory.
        /// </summary>
        string? ResultsRoot = null);

    /// <summary>Launch config shape shared by Runner (package.json).</summary>
    public sealed class LaunchManifest
    {
        public string Name { get; init; } = "";
        public string Tsource { get; set; } = "C:";
        public string Target { get; init; } = "";
        public string? Module { get; init; }
        public bool ZipOutput { get; init; } = true;
        public bool Flush { get; init; }
        public bool Vss { get; init; }
        public IrCollectionMode CollectionMode { get; init; } = IrCollectionMode.Single;
        public string CaseId { get; init; } = "";
        public string Phase1Module { get; init; } = PackageDefinition.DefaultPhase1Module;
        public string? Phase2Module { get; init; }
    }

    public static List<Phase> BuildPhases(LaunchManifest cfg, RuntimeOptions rt)
    {
        cfg.Tsource = string.IsNullOrWhiteSpace(rt.Tsource) ? cfg.Tsource : rt.Tsource.Trim();
        var phases = new List<Phase>();
        var resultsPrefix = ResolveResultsPrefix(rt.ResultsRoot);

        if (cfg.CollectionMode == IrCollectionMode.TwoPhase)
        {
            var phase1Name = rt.SkipMemory
                ? PackageDefinition.DefaultPhase1ModuleNoMemory
                : (string.IsNullOrWhiteSpace(cfg.Phase1Module)
                    ? PackageDefinition.DefaultPhase1Module
                    : cfg.Phase1Module.Trim());

            // Prefer NoMemory compound when skip requested but Phase1 was collect-with-memory.
            if (rt.SkipMemory && PackageDefinition.IsPhase1CollectWithMemory(phase1Name))
                phase1Name = PackageDefinition.DefaultPhase1ModuleNoMemory;

            var phase1Volatile = resultsPrefix + @"RESULTS\%m\Phase1_Volatile";

            phases.Add(new Phase(
                "1",
                "Фаза 1a — оперативный сбор (память, сеть, процессы)",
                new KapeCliArgs.Options(
                    cfg.Tsource,
                    Target: null,
                    Module: phase1Name,
                    ZipOutput: false,
                    Flush: cfg.Flush,
                    Vss: false,
                    Simulate: rt.Simulate,
                    Mdest: phase1Volatile,
                    ModuleOnly: true)));

            // Phase 1b — Vol3 post against collected dump (skipped when no dump or analysis opted out).
            if (!rt.SkipMemory && !rt.SkipMemoryAnalysis)
            {
                phases.Add(new Phase(
                    "1b-triage",
                    "Фаза 1b — Volatility3 triage (MemoryAnalysis)",
                    new KapeCliArgs.Options(
                        cfg.Tsource,
                        Target: null,
                        Module: PackageDefinition.DefaultPhase1MemoryTriage,
                        ZipOutput: false,
                        Flush: false,
                        Vss: false,
                        Simulate: rt.Simulate,
                        Mdest: phase1Volatile + @"\MemoryAnalysis",
                        Msource: phase1Volatile,
                        ModuleOnly: true)));

                phases.Add(new Phase(
                    "1b-deep",
                    "Фаза 1b — Volatility3 deep (MemoryDeep)",
                    new KapeCliArgs.Options(
                        cfg.Tsource,
                        Target: null,
                        Module: PackageDefinition.DefaultPhase1MemoryDeep,
                        ZipOutput: false,
                        Flush: false,
                        Vss: false,
                        Simulate: rt.Simulate,
                        Mdest: phase1Volatile + @"\MemoryDeep",
                        Msource: phase1Volatile,
                        ModuleOnly: true)));
            }

            var phase2Module = string.IsNullOrWhiteSpace(cfg.Phase2Module) ? null : cfg.Phase2Module.Trim();
            phases.Add(new Phase(
                "2",
                "Фаза 2 — дисковый сбор (targets)",
                new KapeCliArgs.Options(
                    cfg.Tsource,
                    Target: cfg.Target,
                    Module: phase2Module,
                    ZipOutput: cfg.ZipOutput,
                    Flush: false,
                    Vss: cfg.Vss,
                    Simulate: rt.Simulate,
                    Tdest: resultsPrefix + @"RESULTS\%m\Phase2_Disk",
                    Mdest: resultsPrefix + @"RESULTS\%m\Phase2_Disk\ModuleOutput",
                    ModuleOnly: false)));
        }
        else
        {
            string? tdest = null;
            string? mdest = null;
            if (!string.IsNullOrEmpty(resultsPrefix))
            {
                tdest = resultsPrefix + @"RESULTS\%m";
                mdest = resultsPrefix + @"RESULTS\%m\ModuleOutput";
            }

            phases.Add(new Phase(
                "all",
                "Сбор (одна фаза)",
                new KapeCliArgs.Options(
                    cfg.Tsource,
                    Target: cfg.Target,
                    Module: string.IsNullOrWhiteSpace(cfg.Module) ? null : cfg.Module,
                    ZipOutput: cfg.ZipOutput,
                    Flush: cfg.Flush,
                    Vss: cfg.Vss,
                    Simulate: rt.Simulate,
                    Tdest: tdest,
                    Mdest: mdest)));
        }

        if (rt.PhaseFilter is 1 or 2 && cfg.CollectionMode == IrCollectionMode.TwoPhase)
        {
            return phases.Where(p => MatchesPhaseFilter(p.Name, rt.PhaseFilter.Value)).ToList();
        }

        return phases;
    }

    /// <summary>--phase 1 includes 1a + 1b-*; --phase 2 is disk only.</summary>
    public static bool MatchesPhaseFilter(string phaseName, int filter)
    {
        if (filter == 2)
            return phaseName.Equals("2", StringComparison.Ordinal);
        if (filter == 1)
            return phaseName.Equals("1", StringComparison.Ordinal) ||
                   phaseName.StartsWith("1b", StringComparison.OrdinalIgnoreCase);
        return true;
    }

    public static bool IsPhase1b(string phaseName)
        => phaseName.StartsWith("1b", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Absolute prefix ending with '\' for tdest/mdest, or empty for package-relative RESULTS.
    /// </summary>
    public static string ResolveResultsPrefix(string? resultsRoot)
    {
        if (string.IsNullOrWhiteSpace(resultsRoot))
            return "";
        try
        {
            var full = Path.GetFullPath(resultsRoot.Trim());
            return full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                   + Path.DirectorySeparatorChar;
        }
        catch
        {
            return "";
        }
    }

    public static string ResolveHostResultsDir(string packageDir, string? resultsRoot)
    {
        var baseDir = string.IsNullOrWhiteSpace(resultsRoot)
            ? packageDir
            : Path.GetFullPath(resultsRoot.Trim());
        return Path.Combine(baseDir, "RESULTS", Environment.MachineName);
    }

    public static LaunchManifest FromPackage(PackageDefinition pkg) => new()
    {
        Name = pkg.Name,
        Tsource = pkg.Tsource,
        Target = pkg.TargetCompoundName,
        Module = pkg.ModuleCompoundName,
        ZipOutput = pkg.ZipOutput,
        Flush = pkg.Flush,
        Vss = pkg.Vss,
        CollectionMode = pkg.CollectionMode,
        CaseId = pkg.CaseId ?? "",
        Phase1Module = string.IsNullOrWhiteSpace(pkg.Phase1ModuleName)
            ? PackageDefinition.DefaultPhase1Module
            : pkg.Phase1ModuleName.Trim(),
        Phase2Module = pkg.ResolvePhase2ModuleName()
    };
}
