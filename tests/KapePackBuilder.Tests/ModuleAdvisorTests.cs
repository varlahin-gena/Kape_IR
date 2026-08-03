using KapePackBuilder.Models;
using KapePackBuilder.Services;

namespace KapePackBuilder.Tests;

public class ModuleAdvisorTests
{
    private static string KapeRoot => @"D:\Distr\HACK\Kape";

    [Fact]
    public void MaskOverlap_MatchesAmcache()
    {
        var hit = ModuleAdvisor.MaskOverlap(
            new[] { "Amcache.hve", "Amcache.hve.LOG*" },
            new[] { "Amcache.hve" });
        Assert.Equal("Amcache.hve", hit);
    }

    [Fact]
    public void MaskOverlap_MatchesPrefetchExtension()
    {
        var hit = ModuleAdvisor.MaskOverlap(
            new[] { "*.pf" },
            new[] { "*.pf" });
        Assert.NotNull(hit);
    }

    [Fact]
    public void Suggest_Prefetch_IncludesPECmd()
    {
        if (!Directory.Exists(Path.Combine(KapeRoot, "Targets")))
            return;

        var cat = new KapeCatalog(KapeRoot);
        cat.Refresh();
        var advisor = new ModuleAdvisor(cat);
        var suggestions = advisor.Suggest(new[]
        {
            new SelectionEntry { Name = "Prefetch", Path = "Prefetch.tkape", Category = "Prefetch" }
        });

        Assert.Contains(suggestions, s => s.Module.Name.Equals("PECmd", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Suggest_Amcache_IncludesAmcacheParser_ViaFileMask()
    {
        if (!Directory.Exists(Path.Combine(KapeRoot, "Targets")))
            return;

        var cat = new KapeCatalog(KapeRoot);
        cat.Refresh();
        var amcache = cat.FindTarget("Amcache");
        Assert.NotNull(amcache);
        Assert.Contains(amcache!.FileMasks, m => m.Contains("Amcache", StringComparison.OrdinalIgnoreCase));

        var advisor = new ModuleAdvisor(cat);
        var suggestions = advisor.Suggest(new[]
        {
            new SelectionEntry { Name = "Amcache", Path = "Amcache.tkape" }
        });

        Assert.Contains(suggestions, s => s.Module.Name.Equals("AmcacheParser", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ExtractTargetFileMasks_ReadsPrefetch()
    {
        var path = Path.Combine(KapeRoot, "Targets", "Windows", "Prefetch.tkape");
        if (!File.Exists(path)) return;
        var data = KapeFileIo.LoadKapeFile(path);
        var masks = KapeFileIo.ExtractTargetFileMasks(data);
        Assert.Contains(masks, m => m.Contains(".pf", StringComparison.OrdinalIgnoreCase));
    }
}
