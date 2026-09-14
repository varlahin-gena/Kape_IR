using KapePack.Core.Models;
using KapePack.Core.Services;

namespace KapePackBuilder.Tests;

public class CatalogOriginTests
{
    [Fact]
    public void Resolve_WithInventory_MarksLocalVsGitHub()
    {
        var upstream = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Targets/Windows/Amcache.tkape",
            "Modules/Compound/!EZParser.mkape"
        };

        Assert.Equal(CatalogOrigin.GitHub,
            CatalogOriginLabels.Resolve("Targets/Windows/Amcache.tkape", "Eric Zimmerman", upstream));
        Assert.Equal(CatalogOrigin.Local,
            CatalogOriginLabels.Resolve("Targets/Compound/!LocalOnly.tkape", "test", upstream));
        Assert.Equal(CatalogOrigin.Local,
            CatalogOriginLabels.Resolve("Modules/Compound/VolatileFirst.mkape",
                "KAPE Pack Builder / IR VolatileFirst", upstream));
    }

    [Fact]
    public void Resolve_WithoutInventory_UsesPackBuilderAuthorHeuristic()
    {
        Assert.Equal(CatalogOrigin.Local,
            CatalogOriginLabels.Resolve("Modules/Compound/Hayabusa_Offline.mkape",
                "KAPE Pack Builder (local)", upstreamPaths: null));
        Assert.Equal(CatalogOrigin.Unknown,
            CatalogOriginLabels.Resolve("Targets/Windows/Amcache.tkape", "Eric Zimmerman", null));
    }

    [Fact]
    public void Labels_DisplayAndShort()
    {
        Assert.Equal("GitHub", CatalogOriginLabels.Display(CatalogOrigin.GitHub));
        Assert.Equal("локальный", CatalogOriginLabels.Display(CatalogOrigin.Local));
        Assert.Equal("?", CatalogOriginLabels.Display(CatalogOrigin.Unknown));
        Assert.Equal("[GH]", CatalogOriginLabels.Short(CatalogOrigin.GitHub));
        Assert.Equal("[лок.]", CatalogOriginLabels.Short(CatalogOrigin.Local));
    }

    [Fact]
    public void Refresh_WithoutInventory_MarksPackBuilderAuthorAsLocal()
    {
        var root = Path.Combine(Path.GetTempPath(), "kape_origin_auth_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "Targets", "Apps"));
        Directory.CreateDirectory(Path.Combine(root, "Modules", "Compound"));

        File.WriteAllText(Path.Combine(root, "Modules", "Compound", "Mine.mkape"), """
Description: local module
Author: KAPE Pack Builder (local)
Version: 1.0
Id: 44444444-4444-4444-4444-444444444444
Category: Compound
ExportFormat: csv
Processors:
    -
        Executable: cmd.exe
        CommandLine: echo
        ExportFormat: csv
""");
        File.WriteAllText(Path.Combine(root, "Targets", "Apps", "Stockish.tkape"), """
Description: unknown until sync
Author: Someone Else
Version: 1.0
Id: 55555555-5555-5555-5555-555555555555
RecreateDirectories: true
Targets:
    -
        Name: x
        Category: x
        Path: C:\Windows\x
        FileMask: '*'
""");

        try
        {
            var cat = new KapeCatalog(root);
            cat.Refresh();
            Assert.False(cat.HasUpstreamInventory);
            Assert.Equal(CatalogOrigin.Local, cat.FindModule("Mine")!.Origin);
            Assert.Equal(CatalogOrigin.Unknown, cat.FindTarget("Stockish")!.Origin);
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { /* ignore */ }
        }
    }
}
