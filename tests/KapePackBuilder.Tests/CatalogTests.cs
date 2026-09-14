using KapePack.Core.Services;

namespace KapePackBuilder.Tests;

public class CatalogTests
{
    private static string? KapeRoot => TestKapeRoot.TryGet();

    private static string RequireRoot()
    {
        var root = KapeRoot;
        Skip.If(root is null, "Set KAPE_ROOT to a KAPE install with Targets/, or place one discoverable by AppSettings.CandidateRoots.");
        return root!;
    }

    [SkippableFact]
    public void Refresh_SkipsDisabledFolders()
    {
        var workspace = TestKapeRoot.TryGet();
        Skip.If(workspace is null, "KAPE root with Targets/ not found.");
        var sampleTarget = Directory.EnumerateFiles(Path.Combine(workspace!, "Targets"), "*.tkape", SearchOption.AllDirectories)
            .First(p => !NameCollisionFixer.IsUnderDisabledFolder(p));
        var sampleModule = Directory.EnumerateFiles(Path.Combine(workspace!, "Modules"), "*.mkape", SearchOption.AllDirectories)
            .First();

        var root = Path.Combine(Path.GetTempPath(), "kape_cat_" + Guid.NewGuid().ToString("N"));
        var apps = Path.Combine(root, "Targets", "Apps");
        var disabled = Path.Combine(root, "Targets", "!Disabled");
        var modActive = Path.Combine(root, "Modules", "EZTools");
        var modDisabled = Path.Combine(root, "Modules", "!Disabled");
        Directory.CreateDirectory(apps);
        Directory.CreateDirectory(disabled);
        Directory.CreateDirectory(modActive);
        Directory.CreateDirectory(modDisabled);

        File.Copy(sampleTarget, Path.Combine(apps, "Active.tkape"));
        File.Copy(sampleTarget, Path.Combine(disabled, "Hidden.tkape"));
        File.Copy(sampleModule, Path.Combine(modActive, "Active.mkape"));
        File.Copy(sampleModule, Path.Combine(modDisabled, "Hidden.mkape"));

        try
        {
            var cat = new KapeCatalog(root);
            cat.Refresh();
            Assert.Contains(cat.Targets, t => t.Name == "Active");
            Assert.DoesNotContain(cat.Targets, t => t.Name == "Hidden");
            Assert.Contains(cat.Modules, m => m.Name == "Active");
            Assert.DoesNotContain(cat.Modules, m => m.Name == "Hidden");
            Assert.DoesNotContain(cat.Targets, t => t.RelativePath.Contains("!Disabled", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(cat.Modules, m => m.RelativePath.Contains("!Disabled", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { /* ignore */ }
        }
    }

    [SkippableFact]
    public void Refresh_LoadsTargetsAndModules()
    {
        var cat = new KapeCatalog(RequireRoot());
        cat.Refresh();
        Assert.True(cat.Targets.Count > 50);
        Assert.True(cat.Modules.Count > 10);
        Assert.Contains(cat.Targets, t => t.IsCompound);
    }

    [SkippableFact]
    public void Flatten_MergesOverlappingCompounds_WithoutDuplicates()
    {
        var cat = new KapeCatalog(RequireRoot());
        cat.Refresh();
        var refs = new List<string>();
        foreach (var name in new[] { "!SANS_Triage", "!BasicCollection", "KapeTriage", "!PSBCollection" })
        {
            if (cat.FindTarget(name) is not null)
                refs.Add(name);
        }
        Assert.NotEmpty(refs);

        var leaves = cat.FlattenToLeaves(refs, KapePack.Core.Models.ItemKind.Target);
        var paths = leaves.Select(l => Path.GetFileName(l.RelativePath).ToLowerInvariant()).ToList();
        Assert.Equal(paths.Count, paths.Distinct().Count());
        Assert.True(leaves.Count > 10);
    }

    [SkippableFact]
    public void Prefetch_HasMultipleIncludingCompounds()
    {
        var cat = new KapeCatalog(RequireRoot());
        cat.Refresh();
        Assert.NotNull(cat.FindTarget("Prefetch"));
        var packs = cat.IncludingCompounds("Prefetch", KapePack.Core.Models.ItemKind.Target);
        Assert.True(packs.Count >= 1);
    }

    [Fact]
    public void RenderCompoundTarget_ContainsRequiredFields()
    {
        var pkg = new KapePack.Core.Models.PackageDefinition
        {
            Name = "TestPack",
            Description = "desc",
            Author = "author",
            Targets =
            {
                new KapePack.Core.Models.SelectionEntry { Name = "Prefetch", Category = "Prefetch", Path = "Prefetch.tkape" }
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
    public void RenderCompoundModule_SetsExportFormatCsv()
    {
        var pkg = new KapePack.Core.Models.PackageDefinition
        {
            Name = "T",
            Modules =
            {
                new KapePack.Core.Models.SelectionEntry { Name = "AmcacheParser", Category = "EZTools", Path = "AmcacheParser.mkape" }
            }
        };
        var yaml = KapeFileIo.RenderCompoundModule(pkg);
        Assert.Contains("ExportFormat: csv", yaml);
    }

    [Theory]
    [InlineData("!!ToolSync.mkape", "'!!ToolSync.mkape'")]
    [InlineData("!EZParser.mkape", "'!EZParser.mkape'")]
    [InlineData("AmcacheParser.mkape", "AmcacheParser.mkape")]
    [InlineData("true", "'true'")]
    public void FormatYamlScalar_QuotesYamlTagLikeValues(string input, string expected)
        => Assert.Equal(expected, KapeFileIo.FormatYamlScalar(input));

    [Fact]
    public void RenderCompoundModule_QuotesBangBangToolSyncPath()
    {
        var pkg = new KapePack.Core.Models.PackageDefinition
        {
            Name = "T",
            Modules =
            {
                new KapePack.Core.Models.SelectionEntry
                {
                    Name = "!!ToolSync",
                    Category = "Sync",
                    Path = "!!ToolSync.mkape"
                }
            }
        };
        var yaml = KapeFileIo.RenderCompoundModule(pkg);
        Assert.Contains("Executable: '!!ToolSync.mkape'", yaml);
        Assert.DoesNotContain("Executable: !!ToolSync.mkape\n", yaml.Replace("\r\n", "\n"));
    }

    [Fact]
    public void IsSyncOrMaintenanceModule_DetectsToolSync()
    {
        Assert.True(ModuleBinGate.IsSyncOrMaintenanceModule(new KapePack.Core.Models.SelectionEntry
        {
            Name = "!!ToolSync",
            Path = "!!ToolSync.mkape",
            Category = "Sync"
        }));
        Assert.True(ModuleBinGate.IsSyncOrMaintenanceModule(new KapePack.Core.Models.SelectionEntry
        {
            Name = "Sync_KAPE",
            Path = "Sync_KAPE.mkape",
            Category = "KAPESync"
        }));
        Assert.False(ModuleBinGate.IsSyncOrMaintenanceModule(new KapePack.Core.Models.SelectionEntry
        {
            Name = "AmcacheParser",
            Path = "AmcacheParser.mkape",
            Category = "EZTools"
        }));
    }
}
