using KapeIR.Core.Models;
using KapeIR.Core.Services;
using KapeIR.Builder.Tests.Fixtures;

namespace KapeIR.Builder.Tests;

public class Phase1Vol3PackageExportTests
{
    [Fact]
    public void Export_TwoPhase_FolderOnly_IncludesVolatility3VolExe_WithoutFalseWarning()
    {
        using var fx = FakeKapeRoot.Create(FakeKapeProfile.TwoPhaseIr);
        var pkg = new PackageDefinition
        {
            Name = "Vol3Pack",
            CollectionMode = IrCollectionMode.TwoPhase,
            Targets =
            {
                new SelectionEntry
                {
                    Name = FakeKapeRoot.DemoLeafName,
                    Path = FakeKapeRoot.DemoLeafName + ".tkape"
                }
            }
        };

        var outDir = Path.Combine(Path.GetTempPath(), "kape_vol3exp_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outDir);
        try
        {
            var exporter = new PackageExporter(fx.Catalog);
            var result = exporter.Export(
                pkg,
                outDir,
                new ExportOptions
                {
                    BuildStandaloneExe = false,
                    IncludeModuleBin = true,
                    CopyDependencies = true,
                    OverwriteExisting = true
                });

            var vol = Path.Combine(outDir, "Vol3Pack", "Modules", "bin", "volatility3", "vol.exe");
            Assert.True(File.Exists(vol), "vol.exe must be in package after selective bin copy");
            Assert.DoesNotContain(
                result.Warnings,
                w => w.Contains("нет Modules\\bin\\volatility3\\vol.exe", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(
                result.Warnings,
                w => w.Contains("не скопирован в пакет", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            try { Directory.Delete(outDir, true); } catch { /* ignore */ }
        }
    }
}
