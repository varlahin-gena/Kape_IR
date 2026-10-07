using KapePack.Core.Models;
using KapePack.Core.Services;

namespace KapePackBuilder.Tests;

public class TwoPhaseWinpmemPackTests
{
    [Fact]
    public void CollectRequired_TwoPhase_IncludesWinpmemFromVolatileFirst()
    {
        var root = @"D:\Distr\HACK\Kape";
        if (!Directory.Exists(Path.Combine(root, "Modules", "Compound")))
            return; // skip if catalog not present in CI

        KapeCatalog.ClearFileCache();
        var cat = new KapeCatalog(root);
        cat.Refresh();

        var pkg = new PackageDefinition
        {
            Name = "TestTriage",
            CollectionMode = IrCollectionMode.TwoPhase,
            Targets =
            {
                new SelectionEntry { Name = "KapeTriage", Path = "KapeTriage.tkape" }
            }
        };

        var vf = cat.FindModule("VolatileFirst");
        Assert.NotNull(vf);
        Assert.True(vf!.IsCompound, "VolatileFirst must be compound");
        Assert.True(
            vf.Children.Any(c => c.Contains("WinPmem", StringComparison.OrdinalIgnoreCase)),
            "VolatileFirst children=[" + string.Join(", ", vf.Children) + "]");

        var leaves = cat.FlattenToLeaves(new[] { "VolatileFirst.mkape" }, ItemKind.Module);
        Assert.Contains(leaves, l => l.Name.Contains("WinPmem", StringComparison.OrdinalIgnoreCase));

        foreach (var name in new[] { "VolatileFirst", "VolatileFirst_NoMemory" })
        {
            var hit = cat.Modules.FirstOrDefault(m =>
                m.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            Assert.NotNull(hit);
            pkg.Modules.Add(new SelectionEntry
            {
                Name = hit!.Name,
                Path = Path.GetFileName(hit.AbsolutePath),
                Category = hit.Category
            });
        }

        var req = SelectiveModulesBinCopier.CollectRequiredExecutables(cat, pkg);
        Assert.True(
            req.Any(x => x.Equals("winpmem.exe", StringComparison.OrdinalIgnoreCase)),
            "required=[" + string.Join(", ", req.Take(40)) + "] leaves=" +
            string.Join(", ", leaves.Select(l => l.Name).Take(30)));

        Assert.True(File.Exists(Path.Combine(root, "Modules", "bin", "winpmem.exe")));

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
