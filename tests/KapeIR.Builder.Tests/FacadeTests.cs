using KapeIR.Builder.Services;
using KapeIR.Core.Models;
using KapeIR.Core.Services;
using KapeIR.Builder.Tests.Fixtures;

namespace KapeIR.Builder.Tests;

public sealed class FacadeTests
{
    [Fact]
    public void CatalogOps_FindCollisions_DelegatesToCore()
    {
        var ops = new CatalogOpsFacade();
        var items = new[]
        {
            new CatalogItem
            {
                Name = "A",
                Kind = ItemKind.Target,
                AbsolutePath = @"C:\kape\Targets\Apps\A.tkape",
                RelativePath = "Targets/Apps/A.tkape"
            },
            new CatalogItem
            {
                Name = "A",
                Kind = ItemKind.Target,
                AbsolutePath = @"C:\kape\Targets\Other\A.tkape",
                RelativePath = "Targets/Other/A.tkape"
            }
        };

        var groups = ops.FindCollisions(items);
        Assert.Single(groups);
        Assert.Equal("A.tkape", groups[0].FileName);
        Assert.Equal(2, groups[0].Items.Count);
    }

    [Fact]
    public void PackageBuild_FormatModulesBinConfirm_MentionsBin()
    {
        var build = new PackageBuildFacade(new PackageExporterFactory());
        var result = new ModulesBinPreflight.Result(
            MissingPayloads: new[] { "winpmem.exe" },
            SkippedModules: Array.Empty<string>(),
            WinpmemMissing: true,
            WinpmemSuspectMini: false,
            ModulesBinPath: @"C:\kape\Modules\bin");

        var msg = build.FormatModulesBinConfirm(result);
        Assert.Contains("Modules\\bin", msg, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CatalogOps_LoadPackageFromCompound_SeedsModulesWhenPresent()
    {
        using var fake = FakeKapeRoot.Create(FakeKapeProfile.WithCompounds);
        var ops = new CatalogOpsFacade();
        var compound = fake.Catalog.Targets.First(t => t.IsCompound);
        var pkg = ops.LoadPackageFromCompound(compound.AbsolutePath, fake.Catalog);
        Assert.False(string.IsNullOrWhiteSpace(pkg.Name));
        Assert.True(pkg.Targets.Count > 0 || !string.IsNullOrWhiteSpace(pkg.TargetCompoundName));
    }
}
