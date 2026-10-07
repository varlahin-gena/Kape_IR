using KapeIR.Core.Models;
using KapeIR.Core.Services;
using KapeIR.Builder.Tests.Fixtures;

namespace KapeIR.Builder.Tests;

public class TwoPhaseWinpmemPackTests
{
    [Fact]
    public void CollectRequired_TwoPhase_IncludesWinpmemFromVolatileFirst()
    {
        using var fx = FakeKapeRoot.Create(FakeKapeProfile.TwoPhaseIr);
        var cat = fx.Catalog;

        var vf = fx.RequireModule(FakeKapeRoot.VolatileFirstName);
        Assert.True(vf.IsCompound, "VolatileFirst must be compound");
        Assert.True(
            vf.Children.Any(c => c.Contains("WinPmem", StringComparison.OrdinalIgnoreCase)),
            "VolatileFirst children=[" + string.Join(", ", vf.Children) + "]");

        var leaves = cat.FlattenToLeaves(new[] { FakeKapeRoot.VolatileFirstName + ".mkape" }, ItemKind.Module);
        Assert.Contains(leaves, l => l.Name.Contains("WinPmem", StringComparison.OrdinalIgnoreCase));

        var pkg = new PackageDefinition
        {
            Name = "TestTriage",
            CollectionMode = IrCollectionMode.TwoPhase,
            Targets =
            {
                new SelectionEntry { Name = FakeKapeRoot.DemoLeafName, Path = FakeKapeRoot.DemoLeafName + ".tkape" }
            }
        };

        foreach (var name in new[] { FakeKapeRoot.VolatileFirstName, FakeKapeRoot.VolatileFirstNoMemoryName })
        {
            var hit = fx.RequireModule(name);
            pkg.Modules.Add(new SelectionEntry
            {
                Name = hit.Name,
                Path = Path.GetFileName(hit.AbsolutePath),
                Category = hit.Category
            });
        }

        var req = SelectiveModulesBinCopier.CollectRequiredExecutables(cat, pkg);
        Assert.True(
            req.Any(x => x.Equals("winpmem.exe", StringComparison.OrdinalIgnoreCase)),
            "required=[" + string.Join(", ", req.Take(40)) + "] leaves=" +
            string.Join(", ", leaves.Select(l => l.Name).Take(30)));

        var tmp = Path.Combine(Path.GetTempPath(), "kape_wp_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(tmp);
            var copy = SelectiveModulesBinCopier.Copy(cat, pkg, tmp);
            var dest = Path.Combine(tmp, "Modules", "bin", "winpmem.exe");
            Assert.True(File.Exists(dest),
                "winpmem.exe must be selectively copied for two_phase. Missing=" +
                string.Join(", ", copy.Missing) + " Warnings=" + string.Join("; ", copy.Warnings));
        }
        finally
        {
            try { Directory.Delete(tmp, true); } catch { /* ignore */ }
        }
    }
}
