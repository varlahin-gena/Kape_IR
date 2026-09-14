using System.Text.Json;
using KapePack.Core.Models;
using KapePack.Core.Services;

namespace KapePackBuilder.Tests;

public class FileHashVerifyTests
{
    [Fact]
    public void TryVerifySidecar_Ok_AndMismatch()
    {
        var dir = Path.Combine(Path.GetTempPath(), "kape_hash_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, "CollectPack.exe");
        File.WriteAllText(file, "payload-bytes");
        try
        {
            FileHash.WriteSha256Sidecar(file);
            Assert.True(FileHash.TryVerifySidecar(file, out var okMsg, requireSidecar: true));
            Assert.Contains("OK", okMsg, StringComparison.OrdinalIgnoreCase);

            File.WriteAllText(file + ".sha256", "deadbeef" + new string('0', 56) + "  CollectPack.exe\n");
            Assert.False(FileHash.TryVerifySidecar(file, out var bad, requireSidecar: true));
            Assert.Contains("не совпадает", bad, StringComparison.OrdinalIgnoreCase);

            File.Delete(file + ".sha256");
            Assert.False(FileHash.TryVerifySidecar(file, out _, requireSidecar: true));
            Assert.True(FileHash.TryVerifySidecar(file, out var skip, requireSidecar: false));
            Assert.Contains("пропущена", skip, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* ignore */ }
        }
    }
}

public class PackageJsonRoundTripTests
{
    [Fact]
    public void ExportManifest_ToPhases_ToCliArgs_TwoPhase()
    {
        var pkg = new PackageDefinition
        {
            Name = "IrRoundTrip",
            Description = "rt",
            Author = "test",
            Version = "1.0",
            Tsource = "C:",
            ZipOutput = true,
            Vss = true,
            CollectionMode = IrCollectionMode.TwoPhase,
            CaseId = "CASE-RT",
            Phase1ModuleName = PackageDefinition.DefaultPhase1Module,
            Phase2ModuleName = "IrRoundTrip_Phase2",
            Targets =
            {
                new SelectionEntry { Name = "Prefetch", Category = "Windows", Path = "Prefetch.tkape" }
            },
            Modules =
            {
                new SelectionEntry { Name = "VolatileFirst", Category = "LiveResponse", Path = "VolatileFirst.mkape" },
                new SelectionEntry { Name = "AmcacheParser", Category = "EZTools", Path = "AmcacheParser.mkape" }
            }
        };

        // Simulate package.json shape written by exporter.
        var json = JsonSerializer.Serialize(new
        {
            name = pkg.Name,
            tsource = pkg.Tsource,
            target_compound = pkg.TargetCompoundName,
            module_compound = pkg.ModuleCompoundName,
            zip_output = pkg.ZipOutput,
            flush = pkg.Flush,
            vss = pkg.Vss,
            collection_mode = "two_phase",
            case_id = pkg.CaseId,
            phase1_module = pkg.Phase1ModuleName,
            phase2_module = pkg.Phase2ModuleName
        });

        using var doc = JsonDocument.Parse(json);
        var manifest = LaunchManifestIo.Parse(doc.RootElement);
        Assert.Equal(IrCollectionMode.TwoPhase, manifest.CollectionMode);
        Assert.Equal("IrRoundTrip", manifest.Target);
        Assert.Equal("CASE-RT", manifest.CaseId);
        Assert.Equal(PackageDefinition.DefaultPhase1Module, manifest.Phase1Module);
        Assert.Equal("IrRoundTrip_Phase2", manifest.Phase2Module);

        var phases = CollectionPlan.BuildPhases(
            manifest,
            new CollectionPlan.RuntimeOptions("E:", Simulate: false));
        Assert.Equal(2, phases.Count);
        Assert.Equal("1", phases[0].Name);
        Assert.Equal("2", phases[1].Name);

        var p1 = KapeCliArgs.Build(phases[0].Options);
        Assert.Contains("--module", p1);
        Assert.Contains(PackageDefinition.DefaultPhase1Module, p1);
        Assert.DoesNotContain("--target", p1);

        var p2 = KapeCliArgs.Build(phases[1].Options);
        Assert.Contains("--target", p2);
        Assert.Contains("IrRoundTrip", p2);
        Assert.Contains("--tsource", p2);
        Assert.Contains("E:", p2);

        var only1 = CollectionPlan.BuildPhases(
            manifest,
            new CollectionPlan.RuntimeOptions("E:", PhaseFilter: 1, SkipMemory: true));
        Assert.Single(only1);
        Assert.Equal("1", only1[0].Name);
        var argsNoMem = KapeCliArgs.Build(only1[0].Options);
        Assert.Contains(PackageDefinition.DefaultPhase1ModuleNoMemory, argsNoMem);
    }

    [Fact]
    public void CollectPackCli_Parse_VerifyFlag()
    {
        var o = CollectPackCliOptions.Parse(new[] { "--silent", "--tsource", "C:", "--verify" });
        Assert.True(o.VerifySha256);
        Assert.True(o.Silent);
    }
}
