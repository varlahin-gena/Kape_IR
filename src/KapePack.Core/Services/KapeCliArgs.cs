using KapePack.Core.Models;

namespace KapePack.Core.Services;

/// <summary>Shared argv / _kape.cli line builder for packages and PackRunner.</summary>
public static class KapeCliArgs
{
    public sealed record Options(
        string Tsource,
        string? Target = null,
        string? Module = null,
        bool ZipOutput = true,
        bool Flush = false,
        bool Vss = false,
        /// <summary>When true, use %%d / %%m for native KAPE _kape.cli batch files.</summary>
        bool FleetCliVars = false,
        /// <summary>KAPE --sim: enumerate without copying.</summary>
        bool Simulate = false,
        /// <summary>Override --tdest (relative to package / fleet root).</summary>
        string? Tdest = null,
        /// <summary>Override --mdest.</summary>
        string? Mdest = null,
        /// <summary>
        /// Module-only live response: use --msource instead of requiring --target.
        /// </summary>
        bool ModuleOnly = false);

    public static Options FromPackage(PackageDefinition pkg, bool fleetCliVars = false) => new(
        pkg.Tsource,
        pkg.TargetCompoundName,
        pkg.ModuleCompoundName,
        pkg.ZipOutput,
        pkg.Flush,
        pkg.Vss,
        fleetCliVars);

    public static List<string> Build(Options opt)
    {
        var machine = opt.FleetCliVars ? "%%m" : "%m";
        var defaultTdest = opt.FleetCliVars ? @"%%d\RESULTS\%%m" : @"RESULTS\%m";
        var defaultMdest = opt.FleetCliVars ? @"%%d\RESULTS\%%m\ModuleOutput" : @"RESULTS\%m\ModuleOutput";

        var tdest = string.IsNullOrWhiteSpace(opt.Tdest) ? defaultTdest : opt.Tdest!;
        var mdest = string.IsNullOrWhiteSpace(opt.Mdest) ? defaultMdest : opt.Mdest!;

        // --sim is for target volume estimate; parsers add little and often hang the UI at ~99%.
        var runModules = !opt.Simulate && !string.IsNullOrWhiteSpace(opt.Module);
        var zipOutput = opt.ZipOutput && !opt.Simulate;

        var args = new List<string>();

        if (opt.ModuleOnly || string.IsNullOrWhiteSpace(opt.Target))
        {
            // Live modules: --msource + --mdest + --module
            args.AddRange(new[] { "--msource", opt.Tsource, "--mdest", mdest });
            if (runModules)
            {
                args.AddRange(new[] { "--module", opt.Module! });
                args.AddRange(new[] { "--zm", "true" });
            }
        }
        else
        {
            args.AddRange(new[]
            {
                "--tsource", opt.Tsource,
                "--tdest", tdest,
                "--target", opt.Target!
            });
            if (zipOutput)
                args.AddRange(new[] { "--zip", machine });
            if (runModules)
            {
                args.AddRange(new[]
                {
                    "--mdest", mdest,
                    "--zm", "true",
                    "--module", opt.Module!
                });
            }
        }

        if (opt.Flush) args.Add("--flush");
        if (opt.Vss) args.Add("--vss");
        if (opt.Simulate) args.Add("--sim");
        return args;
    }

    /// <summary>One line for _kape.cli (space-separated, no exe name).</summary>
    public static string RenderCliLine(Options opt)
        => string.Join(' ', Build(opt).Select(QuoteCliToken));

    private static string QuoteCliToken(string s)
        => s.Contains(' ') ? "\"" + s.Replace("\"", "\\\"") + "\"" : s;
}
