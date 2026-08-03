using KapePackBuilder.Services;

namespace KapePackBuilder.Tests;

public class CatalogTests
{
    private static string KapeRoot => @"D:\Distr\HACK\Kape";

    [Fact]
    public void Refresh_LoadsTargetsAndModules()
    {
        if (!Directory.Exists(Path.Combine(KapeRoot, "Targets")))
            return; // skip if lab path missing

        var cat = new KapeCatalog(KapeRoot);
        cat.Refresh();
        Assert.True(cat.Targets.Count > 50);
        Assert.True(cat.Modules.Count > 10);
        Assert.Contains(cat.Targets, t => t.IsCompound);
    }

    [Fact]
    public void Flatten_MergesOverlappingCompounds_WithoutDuplicates()
    {
        if (!Directory.Exists(Path.Combine(KapeRoot, "Targets")))
            return;

        var cat = new KapeCatalog(KapeRoot);
        cat.Refresh();
        var refs = new List<string>();
        foreach (var name in new[] { "!SANS_Triage", "!BasicCollection", "KapeTriage", "!PSBCollection" })
        {
            if (cat.FindTarget(name) is not null)
                refs.Add(name);
        }
        Assert.NotEmpty(refs);

        var leaves = cat.FlattenToLeaves(refs, Models.ItemKind.Target);
        var paths = leaves.Select(l => Path.GetFileName(l.RelativePath).ToLowerInvariant()).ToList();
        Assert.Equal(paths.Count, paths.Distinct().Count());
        Assert.True(leaves.Count > 10);
    }

    [Fact]
    public void Prefetch_HasMultipleIncludingCompounds()
    {
        if (!Directory.Exists(Path.Combine(KapeRoot, "Targets")))
            return;

        var cat = new KapeCatalog(KapeRoot);
        cat.Refresh();
        var prefetch = cat.FindTarget("Prefetch");
        Assert.NotNull(prefetch);
        var packs = cat.IncludingCompounds("Prefetch", Models.ItemKind.Target);
        Assert.True(packs.Count >= 2);
    }

    [Fact]
    public void RenderCompoundTarget_ContainsRequiredFields()
    {
        var pkg = new Models.PackageDefinition
        {
            Name = "TestPack",
            Description = "desc",
            Author = "author",
            Targets =
            {
                new Models.SelectionEntry { Name = "Prefetch", Category = "Prefetch", Path = "Prefetch.tkape" }
            }
        };
        var text = KapeFileIo.RenderCompoundTarget(pkg);
        Assert.Contains("Description:", text);
        Assert.Contains("Prefetch.tkape", text);
        Assert.Equal("TestPack", pkg.TargetCompoundName);
    }

    [Fact]
    public void SanitizeYaml_ConvertsTabs()
    {
        var cleaned = KapeFileIo.SanitizeYamlText("a:\n\t- b");
        Assert.DoesNotContain("\t", cleaned);
        Assert.Contains("    - b", cleaned);
    }

    [Fact]
    public void GitHubSync_KeepsLocalOnlyFile()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "kape_sync_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(tmp, "Targets", "Compound"));
        Directory.CreateDirectory(Path.Combine(tmp, "Modules"));
        var custom = Path.Combine(tmp, "Targets", "Compound", "!LocalOnlyTest.tkape");
        File.WriteAllText(custom, """
Description: local
Author: test
Version: 1.0
Id: 00000000-0000-0000-0000-000000000001
RecreateDirectories: true
Targets:
    -
        Name: x
        Category: x
        Path: Prefetch.tkape
""");
        try
        {
            var result = GitHubKapeFilesSync.SyncAsync(tmp).GetAwaiter().GetResult();
            Assert.True(result.Ok);
            // Fresh tree: almost everything is added (not "copied over identical files").
            Assert.True(result.TargetsAdded + result.TargetsUpdated > 100);
            Assert.True(File.Exists(custom));
            Assert.True(Directory.EnumerateFiles(Path.Combine(tmp, "Targets"), "Prefetch.tkape", SearchOption.AllDirectories).Any());

            var again = GitHubKapeFilesSync.SyncAsync(tmp).GetAwaiter().GetResult();
            Assert.True(again.Ok);
            Assert.Equal(0, again.TargetsAdded);
            Assert.Equal(0, again.TargetsUpdated);
            Assert.True(again.TargetsUnchanged > 100);
            Assert.Contains("изменений нет", again.Message);
        }
        finally
        {
            try { Directory.Delete(tmp, true); } catch { /* ignore */ }
        }
    }
}
