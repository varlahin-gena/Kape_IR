using KapeIR.Core.Models;
using KapeIR.Core.Services;

namespace KapeIR.Builder.Tests.Fixtures;

public class FakeKapeRootTests
{
    [Fact]
    public void Minimal_HasDemoLeafAndLooksLikeRoot()
    {
        using var fx = FakeKapeRoot.Create(FakeKapeProfile.Minimal);
        Assert.True(KapeRootPaths.LooksLikeKapeRoot(fx.Root));
        Assert.NotNull(fx.Catalog.FindTarget(FakeKapeRoot.DemoLeafName));
        Assert.True(File.Exists(Path.Combine(fx.Root, "kape.exe")));
    }

    [Fact]
    public void WithCompounds_BundleFlattensToTwoLeaves()
    {
        using var fx = FakeKapeRoot.Create(FakeKapeProfile.WithCompounds);
        var bundle = fx.RequireTarget(FakeKapeRoot.BundleName);
        Assert.True(bundle.IsCompound);
        var leaves = fx.Catalog.FlattenToLeaves(new[] { FakeKapeRoot.BundleName + ".tkape" }, ItemKind.Target);
        Assert.Equal(2, leaves.Count);
        Assert.Contains(leaves, l => l.Name == FakeKapeRoot.LeafAName);
        Assert.Contains(leaves, l => l.Name == FakeKapeRoot.LeafBName);
    }

    [Fact]
    public void TwoPhaseIr_HasWinpmemInBinAndVolatileFirstChildren()
    {
        using var fx = FakeKapeRoot.Create(FakeKapeProfile.TwoPhaseIr);
        var vf = fx.RequireModule(FakeKapeRoot.VolatileFirstName);
        Assert.True(vf.IsCompound);
        Assert.Contains(vf.Children, c => c.Contains("WinPmem", StringComparison.OrdinalIgnoreCase));
        Assert.True(File.Exists(Path.Combine(fx.Root, "Modules", "bin", "winpmem.exe")));
        Assert.NotNull(fx.Catalog.FindModule(FakeKapeRoot.VolatileFirstNoMemoryName));
    }

    [Fact]
    public void TwoPhaseIr_CanOmitWinpmemBin()
    {
        using var fx = FakeKapeRoot.Create(
            FakeKapeProfile.TwoPhaseIr,
            new FakeKapeOptions { IncludeWinpmemBin = false });
        Assert.False(File.Exists(Path.Combine(fx.Root, "Modules", "bin", "winpmem.exe")));
        Assert.NotNull(fx.Catalog.FindModule(FakeKapeRoot.WinPmemModuleName));
    }

    [Fact]
    public void WithDisabled_SkipsHiddenUnderBangDisabled()
    {
        using var fx = FakeKapeRoot.Create(FakeKapeProfile.WithDisabled);
        Assert.Contains(fx.Catalog.Targets, t => t.Name == "Active");
        Assert.DoesNotContain(fx.Catalog.Targets, t => t.Name == "Hidden");
        Assert.Contains(fx.Catalog.Modules, m => m.Name == "Active");
        Assert.DoesNotContain(fx.Catalog.Modules, m => m.Name == "Hidden");
    }
}
