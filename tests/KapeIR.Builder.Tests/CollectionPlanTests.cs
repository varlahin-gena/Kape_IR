using KapeIR.Core.Models;
using KapeIR.Core.Services;

namespace KapeIR.Builder.Tests;

public class CollectionPlanTests
{
    [Fact]
    public void TwoPhase_BuildsCollectThenMemoryPostThenDisk()
    {
        var cfg = new CollectionPlan.LaunchManifest
        {
            Name = "IR",
            Tsource = "C:",
            Target = "KapeTriage",
            CollectionMode = IrCollectionMode.TwoPhase,
            Phase1Module = PackageDefinition.DefaultPhase1Module,
            ZipOutput = true
        };

        var phases = CollectionPlan.BuildPhases(cfg, new CollectionPlan.RuntimeOptions("C:"));
        Assert.Equal(4, phases.Count);
        Assert.Equal("1", phases[0].Name);
        Assert.Equal("1b-triage", phases[1].Name);
        Assert.Equal("1b-deep", phases[2].Name);
        Assert.Equal("2", phases[3].Name);

        var p1 = KapeCliArgs.Build(phases[0].Options);
        Assert.Contains("--msource", p1);
        Assert.Contains("--module", p1);
        Assert.Contains(PackageDefinition.DefaultPhase1Module, p1);
        Assert.Contains(@"RESULTS\%m\Phase1_Volatile", p1);
        Assert.DoesNotContain("--target", p1);

        var p1b = KapeCliArgs.Build(phases[1].Options);
        Assert.Contains("--msource", p1b);
        Assert.Contains(@"RESULTS\%m\Phase1_Volatile", p1b);
        Assert.Contains(@"MemoryAnalysis", string.Join(' ', p1b));
        Assert.Contains(PackageDefinition.DefaultPhase1MemoryTriage, p1b);

        var p1bDeep = KapeCliArgs.Build(phases[2].Options);
        Assert.Contains(PackageDefinition.DefaultPhase1MemoryDeep, p1bDeep);
        Assert.Contains(@"MemoryDeep", string.Join(' ', p1bDeep));

        var p2 = KapeCliArgs.Build(phases[3].Options);
        Assert.Contains("--tsource", p2);
        Assert.Contains("--target", p2);
        Assert.Contains("KapeTriage", p2);
        Assert.Contains(@"RESULTS\%m\Phase2_Disk", p2);
        Assert.Contains("--zip", p2);
    }

    [Fact]
    public void TwoPhase_SkipMemory_UsesNoMemoryAndOmitsPhase1b()
    {
        var cfg = new CollectionPlan.LaunchManifest
        {
            Target = "T",
            CollectionMode = IrCollectionMode.TwoPhase,
            Phase1Module = PackageDefinition.DefaultPhase1Module
        };
        var phases = CollectionPlan.BuildPhases(cfg,
            new CollectionPlan.RuntimeOptions("C:", SkipMemory: true));
        Assert.Equal(2, phases.Count);
        Assert.Equal("1", phases[0].Name);
        Assert.Equal("2", phases[1].Name);
        Assert.Contains(PackageDefinition.DefaultPhase1ModuleNoMemory, KapeCliArgs.Build(phases[0].Options));
        Assert.DoesNotContain(phases, p => CollectionPlan.IsPhase1b(p.Name));
    }

    [Fact]
    public void TwoPhase_SkipMemoryAnalysis_KeepsDumpModuleOmitsPhase1b()
    {
        var cfg = new CollectionPlan.LaunchManifest
        {
            Target = "T",
            CollectionMode = IrCollectionMode.TwoPhase,
            Phase1Module = PackageDefinition.DefaultPhase1Module
        };
        var phases = CollectionPlan.BuildPhases(cfg,
            new CollectionPlan.RuntimeOptions("C:", SkipMemoryAnalysis: true));
        Assert.Equal(2, phases.Count);
        Assert.Equal("1", phases[0].Name);
        Assert.Equal("2", phases[1].Name);
        Assert.Contains(PackageDefinition.DefaultPhase1Module, KapeCliArgs.Build(phases[0].Options));
        Assert.DoesNotContain(PackageDefinition.DefaultPhase1ModuleNoMemory, KapeCliArgs.Build(phases[0].Options));
        Assert.DoesNotContain(phases, p => CollectionPlan.IsPhase1b(p.Name));
    }

    [Fact]
    public void TwoPhase_SkipMemory_LegacyVolatileFirstName_MapsToNoMemory()
    {
        var cfg = new CollectionPlan.LaunchManifest
        {
            Target = "T",
            CollectionMode = IrCollectionMode.TwoPhase,
            Phase1Module = PackageDefinition.LegacyPhase1Module
        };
        var phases = CollectionPlan.BuildPhases(cfg,
            new CollectionPlan.RuntimeOptions("C:", SkipMemory: true));
        Assert.Contains(PackageDefinition.DefaultPhase1ModuleNoMemory, KapeCliArgs.Build(phases[0].Options));
    }

    [Fact]
    public void TwoPhase_PhaseFilter_Phase1Includes1b()
    {
        var cfg = new CollectionPlan.LaunchManifest
        {
            Target = "T",
            CollectionMode = IrCollectionMode.TwoPhase,
            Phase1Module = PackageDefinition.DefaultPhase1Module
        };
        var only1 = CollectionPlan.BuildPhases(cfg, new CollectionPlan.RuntimeOptions("C:", PhaseFilter: 1));
        Assert.Equal(3, only1.Count);
        Assert.All(only1, p => Assert.True(CollectionPlan.MatchesPhaseFilter(p.Name, 1)));

        var only2 = CollectionPlan.BuildPhases(cfg, new CollectionPlan.RuntimeOptions("C:", PhaseFilter: 2));
        Assert.Single(only2);
        Assert.Equal("2", only2[0].Name);
    }

    [Fact]
    public void TwoPhase_ResultsRoot_UsesAbsoluteDestPaths()
    {
        var cfg = new CollectionPlan.LaunchManifest
        {
            Target = "T",
            CollectionMode = IrCollectionMode.TwoPhase,
            Phase1Module = PackageDefinition.DefaultPhase1Module
        };
        var root = @"E:\Evidence\Case1";
        var phases = CollectionPlan.BuildPhases(cfg,
            new CollectionPlan.RuntimeOptions("C:", ResultsRoot: root));
        var p1 = KapeCliArgs.Build(phases[0].Options);
        var p2 = KapeCliArgs.Build(phases[^1].Options);
        Assert.Contains(p1, a => a.Contains(@"E:\Evidence\Case1\RESULTS\%m\Phase1_Volatile", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(p2, a => a.Contains(@"E:\Evidence\Case1\RESULTS\%m\Phase2_Disk", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ResolveHostResultsDir_UsesResultsRoot()
    {
        var dir = CollectionPlan.ResolveHostResultsDir(@"C:\pack", @"D:\out");
        Assert.Equal(Path.Combine(@"D:\out", "RESULTS", Environment.MachineName), dir);
    }

    [Fact]
    public void Single_UnchangedShape()
    {
        var cfg = new CollectionPlan.LaunchManifest
        {
            Target = "T1",
            Module = "M1",
            CollectionMode = IrCollectionMode.Single,
            ZipOutput = true
        };
        var phases = CollectionPlan.BuildPhases(cfg, new CollectionPlan.RuntimeOptions("D:"));
        Assert.Single(phases);
        var args = KapeCliArgs.Build(phases[0].Options);
        Assert.Contains("--tsource", args);
        Assert.Contains("D:", args);
        Assert.Contains("--target", args);
        Assert.Contains("T1", args);
        Assert.Contains("--module", args);
        Assert.Contains("M1", args);
    }

    [Fact]
    public void EvidenceWrapUp_WritesManifestAndCoC()
    {
        var dir = Path.Combine(Path.GetTempPath(), "kape_ev_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "sample.txt"), "hello");
            EvidenceWrapUp.WriteAll(new EvidenceWrapUp.Context(
                dir,
                "IR-TEST",
                "Pkg",
                "two_phase",
                Path.Combine(dir, "missing.exe"),
                new[] { "1: exit=0" },
                DateTimeOffset.UtcNow.AddMinutes(-1),
                DateTimeOffset.UtcNow));

            Assert.True(File.Exists(Path.Combine(dir, "collection_log.txt")));
            Assert.True(File.Exists(Path.Combine(dir, "evidence_manifest.sha256")));
            Assert.True(File.Exists(Path.Combine(dir, "chain_of_custody.txt")));
            Assert.True(File.Exists(Path.Combine(dir, EvidenceWrapUp.FindingsTemplateFileName)));
            var manifest = File.ReadAllText(Path.Combine(dir, "evidence_manifest.sha256"));
            Assert.Contains("sample.txt", manifest);
            Assert.Contains("collection_log.txt", manifest);
            Assert.Contains(EvidenceWrapUp.FindingsTemplateFileName, manifest);
            var coc = File.ReadAllText(Path.Combine(dir, "chain_of_custody.txt"));
            Assert.Contains("IR-TEST", coc);
            Assert.Contains("TimeZone Id:", coc);
            var log = File.ReadAllText(Path.Combine(dir, "collection_log.txt"));
            Assert.Contains("TimeZone Display:", log);
            Assert.Contains(EvidenceWrapUp.FindingsTemplateFileName, log);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void PackageDefinition_TwoPhase_ModuleName_IsPhase1VolatileFirst()
    {
        var pkg = new PackageDefinition
        {
            Name = "IR_Phase1",
            CollectionMode = IrCollectionMode.TwoPhase,
            Phase1ModuleName = PackageDefinition.DefaultPhase1Module,
            Modules =
            {
                new SelectionEntry
                {
                    Name = PackageDefinition.DefaultPhase1Module,
                    Path = PackageDefinition.DefaultPhase1Module + ".mkape"
                }
            }
        };
        Assert.Equal(PackageDefinition.DefaultPhase1Module, pkg.ModuleCompoundName);
        Assert.Equal("IR_Phase1", pkg.TargetCompoundName);
    }

    [Fact]
    public void PackageDefinition_TwoPhase_ExtraModules_ArePhase2()
    {
        var pkg = new PackageDefinition
        {
            Name = "MyIR",
            CollectionMode = IrCollectionMode.TwoPhase,
            Phase1ModuleName = PackageDefinition.DefaultPhase1Module,
            Modules =
            {
                new SelectionEntry
                {
                    Name = PackageDefinition.DefaultPhase1Module,
                    Path = PackageDefinition.DefaultPhase1Module + ".mkape"
                },
                new SelectionEntry
                {
                    Name = PackageDefinition.DefaultPhase1ModuleNoMemory,
                    Path = PackageDefinition.DefaultPhase1ModuleNoMemory + ".mkape"
                },
                new SelectionEntry
                {
                    Name = PackageDefinition.DefaultPhase1MemoryTriage,
                    Path = PackageDefinition.DefaultPhase1MemoryTriage + ".mkape"
                },
                new SelectionEntry { Name = "Hayabusa", Path = "Hayabusa.mkape" },
                new SelectionEntry { Name = "PECmd", Path = "PECmd.mkape" }
            }
        };
        Assert.Equal(PackageDefinition.DefaultPhase1Module, pkg.ModuleCompoundName);
        var p2 = pkg.GetPhase2ModuleEntries();
        Assert.Equal(2, p2.Count);
        Assert.Equal("MyIR_Modules", pkg.ResolvePhase2ModuleName());
    }

    [Fact]
    public void PackageDefinition_TwoPhase_OnlyPhase1_NoPhase2Module()
    {
        var pkg = new PackageDefinition
        {
            Name = "OnlyVol",
            CollectionMode = IrCollectionMode.TwoPhase,
            Modules =
            {
                new SelectionEntry
                {
                    Name = PackageDefinition.DefaultPhase1Module,
                    Path = PackageDefinition.DefaultPhase1Module + ".mkape"
                }
            }
        };
        Assert.Null(pkg.ResolvePhase2ModuleName());
        Assert.Empty(pkg.GetPhase2ModuleEntries());
    }

    [Fact]
    public void KapeCliArgs_ModuleOnly_UsesMsourceOverride()
    {
        var args = KapeCliArgs.Build(new KapeCliArgs.Options(
            "C:",
            Target: null,
            Module: PackageDefinition.DefaultPhase1MemoryTriage,
            ModuleOnly: true,
            Mdest: @"RESULTS\%m\Phase1_Volatile\MemoryAnalysis",
            Msource: @"RESULTS\%m\Phase1_Volatile"));
        Assert.Contains("--msource", args);
        Assert.Contains(@"RESULTS\%m\Phase1_Volatile", args);
        Assert.DoesNotContain("--tsource", args);
        Assert.DoesNotContain("--target", args);
    }
}
