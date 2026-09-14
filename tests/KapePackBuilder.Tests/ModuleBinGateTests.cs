using KapePack.Core.Models;
using KapePack.Core.Services;

namespace KapePackBuilder.Tests;

public class ModuleBinGateTests
{
    [Theory]
    [InlineData("!!ToolSync", "!!ToolSync.mkape", "Sync", true)]
    [InlineData("ToolSync", "ToolSync.mkape", "Maintenance", true)]
    [InlineData("Sync_KAPE", "Sync_KAPE.mkape", "General", true)]
    [InlineData("Maps_Sync", "Maps_Sync.mkape", "KAPESync", true)]
    [InlineData("AmcacheParser", "AmcacheParser.mkape", "EZTools", false)]
    [InlineData("VolatileFirst", "VolatileFirst.mkape", "Compound", false)]
    public void IsSyncOrMaintenanceModule_ByNamePathCategory(
        string name, string path, string category, bool expected)
    {
        var entry = new SelectionEntry { Name = name, Path = path, Category = category };
        Assert.Equal(expected, ModuleBinGate.IsSyncOrMaintenanceModule(entry));
    }

    [Fact]
    public void IsSyncOrMaintenanceModule_CatalogItem_UsesFileName()
    {
        var item = new CatalogItem
        {
            Name = "!!ToolSync",
            RelativePath = @"Modules\Compound\!!ToolSync.mkape",
            AbsolutePath = @"C:\fake\Modules\Compound\!!ToolSync.mkape",
            Category = "Sync",
            Kind = ItemKind.Module
        };
        Assert.True(ModuleBinGate.IsSyncOrMaintenanceModule(item));
    }

    [Theory]
    [InlineData("powershell.exe", true)]
    [InlineData("cmd.exe", true)]
    [InlineData(@"%SystemRoot%\System32\net.exe", true)]
    [InlineData(@"C:\Windows\System32\ipconfig.exe", true)]
    [InlineData("hayabusa.exe", false)]
    [InlineData("winpmem.exe", false)]
    public void IsHostBuiltin_RecognizesOsTools(string exe, bool expected)
        => Assert.Equal(expected, ModuleBinGate.IsHostBuiltin(exe));

    [Fact]
    public void BinaryExists_FindsFileUnderModulesBin()
    {
        var root = Path.Combine(Path.GetTempPath(), "kape_bin_" + Guid.NewGuid().ToString("N"));
        var bin = Path.Combine(root, "Modules", "bin");
        Directory.CreateDirectory(bin);
        var tool = Path.Combine(bin, "fakeparser.exe");
        File.WriteAllText(tool, "x");

        try
        {
            Assert.True(ModuleBinGate.BinaryExists(bin, "fakeparser.exe"));
            Assert.False(ModuleBinGate.BinaryExists(bin, "missing.exe"));
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { /* ignore */ }
        }
    }

    [Theory]
    [InlineData("!!ToolSync.mkape", "'!!ToolSync.mkape'")]
    [InlineData("!EZParser.mkape", "'!EZParser.mkape'")]
    [InlineData("path:with:colons", "'path:with:colons'")]
    [InlineData("  padded  ", "'  padded  '")]
    [InlineData("normal.mkape", "normal.mkape")]
    [InlineData("yes", "'yes'")]
    [InlineData("", "\"\"")]
    public void FormatYamlScalar_RegressionMatrix(string input, string expected)
        => Assert.Equal(expected, KapeFileIo.FormatYamlScalar(input));
}
