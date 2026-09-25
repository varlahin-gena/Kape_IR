using KapePack.Core.Models;
using KapePack.Core.Services;

namespace KapePackBuilder.Tests;

public class CollectionPlanTests
{
    [Fact]
    public void TwoPhase_BuildsModuleOnlyThenDisk()
    {
        var cfg = new CollectionPlan.LaunchManifest
        {
            Name = "IR",
            Tsource = "C:",
            Target = "PSBCollection",
            CollectionMode = IrCollectionMode.TwoPhase,
            Phase1Module = "VolatileFirst",
            ZipOutput = true
        };

        var phases = CollectionPlan.BuildPhases(cfg, new CollectionPlan.RuntimeOptions("C:"));
        Assert.Equal(2, phases.Count);
        Assert.Equal("1", phases[0].Name);
        Assert.Equal("2", phases[1].Name);

        var p1 = KapeCliArgs.Build(phases[0].Options);
        Assert.Contains("--msource", p1);
        Assert.Contains("--module", p1);
        Assert.Contains("VolatileFirst", p1);
        Assert.Contains(@"RESULTS\%m\Phase1_Volatile", p1);
        Assert.DoesNotContain("--target", p1);

        var p2 = KapeCliArgs.Build(phases[1].Options);
        Assert.Contains("--tsource", p2);
        Assert.Contains("--target", p2);
        Assert.Contains("PSBCollection", p2);
        Assert.Contains(@"RESULTS\%m\Phase2_Disk", p2);
        Assert.Contains("--zip", p2);
    }

    [Fact]
    public void TwoPhase_SkipMemory_UsesNoMemoryCompound()
    {
        var cfg = new CollectionPlan.LaunchManifest
        {
            Target = "T",
            CollectionMode = IrCollectionMode.TwoPhase,
            Phase1Module = "VolatileFirst"
        };
        var phases = CollectionPlan.BuildPhases(cfg,
            new CollectionPlan.RuntimeOptions("C:", SkipMemory: true));
        Assert.Contains("VolatileFirst_NoMemory", KapeCliArgs.Build(phases[0].Options));
    }

    [Fact]
    public void TwoPhase_PhaseFilter_ReturnsOnePhase()
    {
        var cfg = new CollectionPlan.LaunchManifest
        {
            Target = "T",
            CollectionMode = IrCollectionMode.TwoPhase,
            Phase1Module = "VolatileFirst"
        };
        var only1 = CollectionPlan.BuildPhases(cfg, new CollectionPlan.RuntimeOptions("C:", PhaseFilter: 1));
        Assert.Single(only1);
        Assert.Equal("1", only1[0].Name);

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
            Phase1Module = "VolatileFirst"
        };
        var root = @"E:\Evidence\Case1";
        var phases = CollectionPlan.BuildPhases(cfg,
            new CollectionPlan.RuntimeOptions("C:", ResultsRoot: root));
        var p1 = KapeCliArgs.Build(phases[0].Options);
        var p2 = KapeCliArgs.Build(phases[1].Options);
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
    public void PackageDefinition_TwoPhase_ModuleName_IsVolatileFirst()
    {
        var pkg = new PackageDefinition
        {
            Name = "IR_VolatileFirst",
            CollectionMode = IrCollectionMode.TwoPhase,
            Phase1ModuleName = "VolatileFirst",
            Modules =
            {
                new SelectionEntry { Name = "VolatileFirst", Path = "VolatileFirst.mkape" }
            }
        };
        Assert.Equal("VolatileFirst", pkg.ModuleCompoundName);
        Assert.Equal("IR_VolatileFirst", pkg.TargetCompoundName);
    }

    [Fact]
    public void PackageDefinition_TwoPhase_ExtraModules_ArePhase2()
    {
        var pkg = new PackageDefinition
        {
            Name = "MyIR",
            CollectionMode = IrCollectionMode.TwoPhase,
            Phase1ModuleName = "VolatileFirst",
            Modules =
            {
                new SelectionEntry { Name = "VolatileFirst", Path = "VolatileFirst.mkape" },
                new SelectionEntry { Name = "VolatileFirst_NoMemory", Path = "VolatileFirst_NoMemory.mkape" },
                new SelectionEntry { Name = "Hayabusa", Path = "Hayabusa.mkape" },
                new SelectionEntry { Name = "PECmd", Path = "PECmd.mkape" }
            }
        };
        Assert.Equal("VolatileFirst", pkg.ModuleCompoundName);
        var p2 = pkg.GetPhase2ModuleEntries();
        Assert.Equal(2, p2.Count);
        Assert.Equal("MyIR_Modules", pkg.ResolvePhase2ModuleName());
    }

    [Fact]
    public void PackageDefinition_TwoPhase_OnlyVolatile_NoPhase2Module()
    {
        var pkg = new PackageDefinition
        {
            Name = "OnlyVol",
            CollectionMode = IrCollectionMode.TwoPhase,
            Modules =
            {
                new SelectionEntry { Name = "VolatileFirst", Path = "VolatileFirst.mkape" }
            }
        };
        Assert.Null(pkg.ResolvePhase2ModuleName());
        Assert.Empty(pkg.GetPhase2ModuleEntries());
    }

    [Fact]
    public void KapeCliArgs_ModuleOnly_UsesMsource()
    {
        var args = KapeCliArgs.Build(new KapeCliArgs.Options(
            "C:",
            Target: null,
            Module: "VolatileFirst",
            ModuleOnly: true,
            Mdest: @"RESULTS\%m\Phase1_Volatile"));
        Assert.Contains("--msource", args);
        Assert.Contains("--mdest", args);
        Assert.DoesNotContain("--tsource", args);
        Assert.DoesNotContain("--target", args);
    }
}
