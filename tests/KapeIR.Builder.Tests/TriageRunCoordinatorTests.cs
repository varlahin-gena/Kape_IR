using KapeIR.Core.Models;
using KapeIR.Core.Services;

namespace KapeIR.Builder.Tests;

public class TriageRunCoordinatorTests
{
    [Fact]
    public void Prepare_MissingExe_Returns3()
    {
        var coord = new TriageRunCoordinator();
        var prep = coord.Prepare(new TriageRunCoordinator.PrepareOptions(
            Path.Combine(Path.GetTempPath(), "no-such-" + Guid.NewGuid().ToString("N") + ".exe")));

        Assert.Equal(3, prep.ExitCode);
        Assert.Null(prep.Session);
        Assert.Contains("EXE", prep.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Interpret_SimulateSuccess()
    {
        var r = TriageRunCoordinator.Interpret(
            new CollectionRunner.Result(0, @"C:\tmp\RESULTS\host", new[] { "1: ok" }),
            simulate: true);

        Assert.Equal(0, r.ExitCode);
        Assert.True(r.Simulate);
        Assert.Contains("Оценка объёма", r.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Interpret_NoResults_MapsMessage()
    {
        var missing = Path.Combine(Path.GetTempPath(), "no_results_" + Guid.NewGuid().ToString("N"));
        var r = TriageRunCoordinator.Interpret(
            new CollectionRunner.Result(0, missing, Array.Empty<string>()),
            simulate: false);

        Assert.False(r.HasResultsDirectory);
        Assert.Contains("RESULTS", r.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildRuntime_MapsFlags()
    {
        var rt = TriageRunCoordinator.BuildRuntime(
            "D:",
            simulate: true,
            phaseFilter: 2,
            skipMemory: true,
            caseIdOverride: "IR-9",
            resultsRoot: @"E:\out");

        Assert.Equal("D:", rt.Tsource);
        Assert.True(rt.Simulate);
        Assert.Equal(2, rt.PhaseFilter);
        Assert.True(rt.SkipMemory);
        Assert.Equal("IR-9", rt.CaseIdOverride);
        Assert.Equal(@"E:\out", rt.ResultsRoot);
    }

    [Fact]
    public async Task RunAsync_WithFakeKape_CompletesAndWraps()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "kape_triage_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        var kape = Path.Combine(tmp, "kape.exe");
        File.WriteAllBytes(kape, new byte[] { 0x4D, 0x5A });

        try
        {
            var cfg = new CollectionPlan.LaunchManifest
            {
                Name = "UnitPack",
                Tsource = "C:",
                Target = "UnitTarget",
                CollectionMode = IrCollectionMode.Single,
                CaseId = "CASE-UT"
            };
            var session = new TriageRunCoordinator.Session(
                ExePath: Path.Combine(tmp, "pack.exe"),
                PackageDir: tmp,
                KapeExe: kape,
                Manifest: cfg,
                LaunchDir: tmp);

            var logs = new List<string>();
            var coord = new TriageRunCoordinator();
            var result = await coord.RunAsync(
                new TriageRunCoordinator.RunOptions(
                    session,
                    TriageRunCoordinator.BuildRuntime("C:", simulate: true),
                    RunKape: (_, _, _, _) => Task.FromResult(0)),
                logs.Add);

            Assert.Equal(0, result.ExitCode);
            Assert.True(result.Simulate);
            Assert.Contains(logs, l => l.Contains("--sim", StringComparison.OrdinalIgnoreCase)
                                       || l.Contains("оценка", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            try { Directory.Delete(tmp, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void DefaultResultsRoot_IncludesMachineName()
    {
        var root = TriageRunCoordinator.DefaultResultsRoot(@"C:\pack");
        Assert.Contains(Environment.MachineName, root, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(Path.Combine("RESULTS", Environment.MachineName), root, StringComparison.OrdinalIgnoreCase);
    }
}
