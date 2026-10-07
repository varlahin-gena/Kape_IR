using KapeIR.Core.Models;
using KapeIR.Core.Services;
using KapeIR.Builder.Tests.Fixtures;
using KapeIR.Builder.Workspaces;

namespace KapeIR.Builder.Tests;

public class CatalogSelectionCoordinatorTests
{
    [Fact]
    public void SetItemSelected_AddsAndRemovesLeaf()
    {
        using var fx = FakeKapeRoot.Create(FakeKapeProfile.Minimal);
        var leaf = fx.RequireTarget(FakeKapeRoot.DemoLeafName);
        var pkg = new PackageDefinition { Name = "P" };
        var sel = new CatalogSelectionCoordinator();

        var status = sel.SetItemSelected(fx.Catalog, pkg, leaf, ItemKind.Target, selected: true);
        Assert.Single(pkg.Targets);
        Assert.Equal(leaf.Name, pkg.Targets[0].Name);
        Assert.Contains("Добавлено", status);

        status = sel.SetItemSelected(fx.Catalog, pkg, leaf, ItemKind.Target, selected: false);
        Assert.Empty(pkg.Targets);
        Assert.Contains("Убрано", status);
    }

    [Fact]
    public void SetItemSelected_Compound_AddsAllLeaves()
    {
        using var fx = FakeKapeRoot.Create(FakeKapeProfile.WithCompounds);
        var compound = fx.RequireTarget(FakeKapeRoot.BundleName);
        var pkg = new PackageDefinition { Name = "P" };
        var sel = new CatalogSelectionCoordinator();
        sel.SetItemSelected(fx.Catalog, pkg, compound, ItemKind.Target, selected: true);

        Assert.Equal(2, pkg.Targets.Count);
        Assert.Contains(pkg.Targets, t => t.Name == FakeKapeRoot.LeafAName);
        Assert.Contains(pkg.Targets, t => t.Name == FakeKapeRoot.LeafBName);

        var keys = KapeCatalog.BuildSelectionKeys(pkg.Targets);
        Assert.True(sel.AllLeavesSelected(fx.Catalog, compound, ItemKind.Target, keys));
    }

    [Fact]
    public void SuppressEvents_NestsAndReleases()
    {
        var sel = new CatalogSelectionCoordinator();
        Assert.False(sel.IsSuppressed);
        using (sel.SuppressEvents())
        {
            Assert.True(sel.IsSuppressed);
            using (sel.SuppressEvents())
                Assert.True(sel.IsSuppressed);
            Assert.True(sel.IsSuppressed);
        }
        Assert.False(sel.IsSuppressed);
    }

    [Fact]
    public void FormatSelectionText_JoinsLines()
    {
        var sel = new CatalogSelectionCoordinator();
        var text = sel.FormatSelectionText(new[]
        {
            new SelectionEntry { Path = "A.tkape", Name = "A", Category = "Apps" },
            new SelectionEntry { Path = "B.tkape", Name = "B", Category = "Logs" }
        });
        Assert.Contains("A.tkape  |  A  |  Apps", text);
        Assert.Contains("B.tkape  |  B  |  Logs", text);
    }

    [Fact]
    public void MergeAndClear_Modules()
    {
        var pkg = new PackageDefinition
        {
            Modules = { new SelectionEntry { Name = "Old", Path = "Old.mkape", Category = "X" } }
        };
        var sel = new CatalogSelectionCoordinator();
        sel.MergeEntries(pkg, ItemKind.Module, new[]
        {
            new SelectionEntry { Name = "New", Path = "New.mkape", Category = "Y" }
        });
        Assert.Equal(2, pkg.Modules.Count);
        sel.Clear(pkg, ItemKind.Module);
        Assert.Empty(pkg.Modules);
    }
}
