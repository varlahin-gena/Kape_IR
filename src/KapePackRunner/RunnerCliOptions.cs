using KapePack.Core.Services;

namespace KapePackRunner;

/// <summary>Thin alias kept for Runner call sites; parsing lives in Core.</summary>
internal sealed class RunnerCliOptions
{
    public bool Silent { get; init; }
    public bool ShowHelp { get; init; }
    public bool SimOnly { get; init; }
    public string? Tsource { get; init; }
    public string? LogPath { get; init; }
    public string? CaseId { get; init; }
    public int? Phase { get; init; }
    public bool SkipMemory { get; init; }
    public bool VerifySha256 { get; init; }
    public List<string> Errors { get; init; } = new();

    public static RunnerCliOptions Parse(string[] args)
    {
        var o = CollectPackCliOptions.Parse(args);
        return new RunnerCliOptions
        {
            Silent = o.Silent,
            ShowHelp = o.ShowHelp,
            SimOnly = o.SimOnly,
            Tsource = o.Tsource,
            LogPath = o.LogPath,
            CaseId = o.CaseId,
            Phase = o.Phase,
            SkipMemory = o.SkipMemory,
            VerifySha256 = o.VerifySha256,
            Errors = o.Errors
        };
    }

    public static string HelpText => CollectPackCliOptions.HelpText;
}
