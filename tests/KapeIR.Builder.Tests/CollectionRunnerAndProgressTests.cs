using KapeIR.Core.Models;
using KapeIR.Core.Services;

namespace KapeIR.Builder.Tests;

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
        Assert.Equal(CollectionProgressBands.Phase2Floor, u.PhaseFloor);
        Assert.Equal(CollectionProgressBands.Phase2Ceil, u.PhaseCeil);
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
                runKape: (_, args, _, _) =>
                {
                    calls.Add(string.Join(' ', args));
                    return Task.FromResult(0);
                });

            Assert.Equal(0, result.ExitCode);
            Assert.Contains(result.PhaseSummaries, s => s.Contains("skipped — sim", StringComparison.Ordinal));
            Assert.Single(calls);
            Assert.DoesNotContain(calls, c =>
                c.Contains(PackageDefinition.DefaultPhase1Module, StringComparison.OrdinalIgnoreCase));
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
                runKape: (_, _, _, _) =>
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
                runKape: (_, _, _, _) =>
                {
                    called++;
                    return Task.FromResult(0);
                });

            Assert.Equal(0, result.ExitCode);
            // 1a (NoMemory) + phase 2 — no 1b when --skip-memory
            Assert.Equal(2, called);
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public async Task RunAsync_Phase1bFail_ContinuesPhase2_ExitFromPhase1aOnly()
    {
        var root = Path.Combine(Path.GetTempPath(), "kape_cr_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "Modules", "bin", "volatility3"));
        File.WriteAllText(Path.Combine(root, "Modules", "bin", "winpmem.exe"), "MZ");
        File.WriteAllText(Path.Combine(root, "Modules", "bin", "volatility3", "vol.exe"), "MZ");
        var modules = new List<string>();

        try
        {
            var hostResults = CollectionPlan.ResolveHostResultsDir(root, null);
            Directory.CreateDirectory(Path.Combine(hostResults, "Phase1_Volatile"));

            var result = await CollectionRunner.RunAsync(
                root,
                Path.Combine(root, "kape.exe"),
                TwoPhaseManifest(),
                new CollectionPlan.RuntimeOptions("C:", Simulate: false, SkipMemory: false),
                collectorExe: null,
                log: _ => { },
                runKape: (_, args, _, _) =>
                {
                    var line = string.Join(' ', args);
                    modules.Add(line);
                    // After phase 1a, plant a dump so 1b runs.
                    if (line.Contains(PackageDefinition.DefaultPhase1Module, StringComparison.OrdinalIgnoreCase))
                    {
                        File.WriteAllText(
                            Path.Combine(hostResults, "Phase1_Volatile", "memory.raw"),
                            "dump");
                        return Task.FromResult(0);
                    }

                    if (line.Contains(PackageDefinition.DefaultPhase1MemoryTriage, StringComparison.OrdinalIgnoreCase) ||
                        line.Contains(PackageDefinition.DefaultPhase1MemoryDeep, StringComparison.OrdinalIgnoreCase))
                        return Task.FromResult(7);

                    return Task.FromResult(0);
                });

            Assert.Equal(0, result.ExitCode);
            Assert.Equal(4, modules.Count);
            Assert.Contains(result.PhaseSummaries, s => s.Contains("1b-triage") && s.Contains("exit=7"));
            Assert.Contains(result.PhaseSummaries, s => s.StartsWith("2:", StringComparison.Ordinal));
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public async Task RunAsync_Phase1b_SkippedWithoutDump()
    {
        var root = Path.Combine(Path.GetTempPath(), "kape_cr_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "Modules", "bin", "volatility3"));
        File.WriteAllText(Path.Combine(root, "Modules", "bin", "winpmem.exe"), "MZ");
        File.WriteAllText(Path.Combine(root, "Modules", "bin", "volatility3", "vol.exe"), "MZ");
        var called = 0;

        try
        {
            var hostResults = CollectionPlan.ResolveHostResultsDir(root, null);
            Directory.CreateDirectory(hostResults);
            File.WriteAllText(Path.Combine(hostResults, "marker.txt"), "x");

            var result = await CollectionRunner.RunAsync(
                root,
                Path.Combine(root, "kape.exe"),
                TwoPhaseManifest(),
                new CollectionPlan.RuntimeOptions("C:", Simulate: false, SkipMemory: false),
                collectorExe: null,
                log: _ => { },
                runKape: (_, _, _, _) =>
                {
                    called++;
                    return Task.FromResult(0);
                });

            Assert.Equal(0, result.ExitCode);
            // 1a + phase2 only (1b skipped — no dump)
            Assert.Equal(2, called);
            Assert.Contains(result.PhaseSummaries, s => s.Contains("skipped", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { /* ignore */ }
        }
    }
}
