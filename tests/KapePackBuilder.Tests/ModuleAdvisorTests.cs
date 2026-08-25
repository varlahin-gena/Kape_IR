using KapePackBuilder.Models;
using KapePackBuilder.Services;

namespace KapePackBuilder.Tests;

public class ModuleAdvisorTests
{
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

    [SkippableFact]
    public void Suggest_Prefetch_IncludesPECmd()
    {
        var root = TestKapeRoot.TryGet();
        Skip.If(root is null, "Set KAPE_ROOT to a KAPE install with Targets/.");

        var cat = new KapeCatalog(root!);
        cat.Refresh();
        var advisor = new ModuleAdvisor(cat);
        var suggestions = advisor.Suggest(new[]
        {
            new SelectionEntry { Name = "Prefetch", Path = "Prefetch.tkape", Category = "Prefetch" }
        });
        Assert.Contains(suggestions, s => s.Module.Name.Contains("PECmd", StringComparison.OrdinalIgnoreCase)
                                          || s.Reason.Contains("prefetch", StringComparison.OrdinalIgnoreCase)
                                          || s.Module.Name.Contains("Prefetch", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ExtractDocumentationLinks_ParsesUrls()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "kape_doc_" + Guid.NewGuid().ToString("N") + ".tkape");
        File.WriteAllText(tmp, """
# Documentation
# https://example.com/docs/one
# http://example.com/two
Description: x
Author: a
Version: 1.0
Id: 22222222-2222-2222-2222-222222222222
RecreateDirectories: true
Targets:
    -
        Name: x
        Category: x
        Path: C:\
""");
        try
        {
            var links = KapeFileIo.ExtractDocumentationLinks(tmp);
            Assert.Contains(links, u => u.Contains("example.com/docs/one"));
            Assert.Contains(links, u => u.Contains("example.com/two"));
        }
        finally
        {
            try { File.Delete(tmp); } catch { /* ignore */ }
        }
    }
}
