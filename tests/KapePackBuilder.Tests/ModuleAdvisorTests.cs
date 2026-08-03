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
    public void ExtractDocumentationLinks_ReadsPrefetchComments()
    {
        var path = Path.Combine(KapeRoot, "Targets", "Windows", "Prefetch.tkape");
        if (!File.Exists(path)) return;
        var urls = KapeFileIo.ExtractDocumentationLinks(path);
        Assert.NotEmpty(urls);
        Assert.Contains(urls, u => u.Contains("forensicswiki", StringComparison.OrdinalIgnoreCase) ||
                                   u.StartsWith("https://", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ExtractDocumentationLinksFromText_IgnoresNonCommentUrls()
    {
        var text = """
Description: test
Path: C:\Windows\
# Documentation
# https://example.com/docs
# not a url
BinaryUrl: https://download.example.com/tool.zip
""";
        var urls = KapeFileIo.ExtractDocumentationLinksFromText(text);
        Assert.Single(urls);
        Assert.Equal("https://example.com/docs", urls[0]);
    }
}
