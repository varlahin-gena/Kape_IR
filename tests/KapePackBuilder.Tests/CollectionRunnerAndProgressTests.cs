using KapePack.Core.Models;
using KapePack.Core.Services;

namespace KapePackBuilder.Tests;

public class CollectPackProgressParserTests
{
    [Fact]
    public void TryParse_FoundFiles_SetsLocalProgress()
    {
        var p = new CollectPackProgressParser();
        var u = p.TryParse("Found 1,234 files");
        Assert.NotNull(u);
        Assert.NotNull(u);
        Assert.Equal(8, u!.Local0to100);
        Assert.Equal(1234, p.FoundFilesCount);
        Assert.Contains("234", u.Status);
    }

    [Fact]
    public void TryParse_CopiedOutOf_StopsAt48()
    {
        var p = new CollectPackProgressParser();
        p.TryParse("Found 100 files");
        var u = p.TryParse("Copied 100 out of 100");
        Assert.NotNull(u);
        Assert.Equal(48, u!.Local0to100);
        Assert.True(u.StopCopyPulse);
    }

    [Fact]
    public void TryParse_Phase2_ResetsRange()
    {
        var p = new CollectPackProgressParser();
        var u = p.TryParse("Фаза 2 — disk triage (targets)");
        Assert.NotNull(u);
        Assert.True(u!.ResetPhase);
        Assert.Equal(15, u.PhaseFloor);
        Assert.Equal(99, u.PhaseCeil);
    }

    [Theory]
    [InlineData("1 234", 1234)]
    [InlineData("12,345", 12345)]
    [InlineData("42", 42)]
    public void TryParseCount_StripsSeparators(string raw, int expected)
    {
        Assert.True(CollectPackProgressParser.TryParseCount(raw, out var n));
        Assert.Equal(expected, n);
    }
}

public class CollectionRunnerTests
{
    private static CollectionPlan.LaunchManifest TwoPhaseManifest() => new()
    {
        Name = "TestPack",
        Tsource = "C:",
        Target = "KapeTriage",
        CollectionMode = IrCollectionMode.TwoPhase,
        Phase1Module = PackageDefinition.DefaultPhase1Module,
        Phase2Module = null,
        CaseId = "IR-TEST"
    };

    [Fact]
    public async Task RunAsync_Simulate_SkipsPhase1Volatile()
    {
        var root = Path.Combine(Path.GetTempPath(), "kape_cr_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var calls = new List<string>();

        try
        {
            var result = await CollectionRunner.RunAsync(
                root,
                Path.Combine(root, "kape.exe"),
                TwoPhaseManifest(),
                new CollectionPlan.RuntimeOptions("C:", Simulate: true),
                collectorExe: null,
                log: _ => { },
                runKape: (_, args, _) =>
                {
                    calls.Add(string.Join(' ', args));
                    return Task.FromResult(0);
                });

            Assert.Equal(0, result.ExitCode);
            Assert.Contains(result.PhaseSummaries, s => s.Contains("skipped — sim", StringComparison.Ordinal));
            Assert.Single(calls);
            Assert.DoesNotContain(calls, c => c.Contains("VolatileFirst", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public async Task RunAsync_MissingWinpmem_ReturnsExit2()
    {
        var root = Path.Combine(Path.GetTempPath(), "kape_cr_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "Modules", "bin"));
        var kapeCalls = 0;

        try
        {
            var result = await CollectionRunner.RunAsync(
                root,
                Path.Combine(root, "kape.exe"),
                TwoPhaseManifest(),
                new CollectionPlan.RuntimeOptions("C:", Simulate: false, SkipMemory: false),
                collectorExe: null,
                log: _ => { },
                runKape: (_, _, _) =>
                {
                    kapeCalls++;
                    return Task.FromResult(0);
                });

            Assert.Equal(2, result.ExitCode);
            Assert.Equal(0, kapeCalls);
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public async Task RunAsync_SkipMemory_DoesNotRequireWinpmem()
    {
        var root = Path.Combine(Path.GetTempPath(), "kape_cr_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var called = 0;

        try
        {
            // Create a results entry so exit is not forced to 1 for empty RESULTS.
            var hostResults = CollectionPlan.ResolveHostResultsDir(root, null);
            Directory.CreateDirectory(hostResults);
            File.WriteAllText(Path.Combine(hostResults, "marker.txt"), "x");

            var result = await CollectionRunner.RunAsync(
                root,
                Path.Combine(root, "kape.exe"),
                TwoPhaseManifest(),
                new CollectionPlan.RuntimeOptions("C:", Simulate: false, SkipMemory: true),
                collectorExe: null,
                log: _ => { },
                runKape: (_, _, _) =>
                {
                    called++;
                    return Task.FromResult(0);
                });

            Assert.Equal(0, result.ExitCode);
            Assert.Equal(2, called);
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { /* ignore */ }
        }
    }
}
