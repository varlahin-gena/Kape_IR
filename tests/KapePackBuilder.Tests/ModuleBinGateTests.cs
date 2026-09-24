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

    [Fact]
    public void FilterByAvailableBinaries_RequiresCommandLineBinScripts()
    {
        KapeCatalog.ClearFileCache();
        var root = Path.Combine(Path.GetTempPath(), "kape_gate_cmd_" + Guid.NewGuid().ToString("N"));
        var bin = Path.Combine(root, "Modules", "bin");
        var modDir = Path.Combine(root, "Modules", "Windows");
        Directory.CreateDirectory(bin);
        Directory.CreateDirectory(modDir);
        Directory.CreateDirectory(Path.Combine(root, "Targets"));

        File.WriteAllText(Path.Combine(bin, "Invoke-Utf8Capture.ps1"), "ok");
        File.WriteAllText(Path.Combine(modDir, "Windows_IPConfig.mkape"), """
Description: test
Category: Windows
Author: t
Version: 1.0
Id: aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa
Processors:
    -
        Executable: C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe
        CommandLine: -File "%kapeDirectory%\Modules\bin\Invoke-Utf8Capture.ps1" -Exe ipconfig
        ExportFormat: txt
""");
        File.WriteAllText(Path.Combine(modDir, "Hindsight_X.mkape"), """
Description: test
Category: Windows
Author: t
Version: 1.0
Id: bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb
Processors:
    -
        Executable: powershell.exe
        CommandLine: -File "%kapeDirectory%\Modules\bin\Run-Hindsight.ps1" -InputPath x
        ExportFormat: xlsx
""");

        try
        {
            var catalog = new KapeCatalog(root);
            catalog.Refresh();
            var entries = new[]
            {
                new SelectionEntry { Name = "Windows_IPConfig", Path = "Windows_IPConfig.mkape", Category = "Windows" },
                new SelectionEntry { Name = "Hindsight_X", Path = "Hindsight_X.mkape", Category = "Windows" },
            };
            var r = ModuleBinGate.FilterByAvailableBinaries(catalog, entries, modulesBinOverride: bin);
            Assert.Single(r.Kept);
            Assert.Equal("Windows_IPConfig", r.Kept[0].Name);
            Assert.Single(r.Skipped);
            Assert.Contains("Hindsight_X", r.Skipped[0], StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Run-Hindsight.ps1", r.Skipped[0], StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void ExtractLeafBinPayloads_MergesExecutableAndCommandLine()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "kape_payload_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        var path = Path.Combine(tmp, "demo.mkape");
        File.WriteAllText(path, """
Description: t
Author: t
Version: 1.0
Id: cccccccc-cccc-cccc-cccc-cccccccccccc
Processors:
    -
        Executable: powershell.exe
        CommandLine: -File "%kapeDirectory%\Modules\bin\Get-Sessions.ps1"
    -
        Executable: listdlls.exe
        CommandLine: -accepteula
""");
        try
        {
            var payloads = ModuleBinGate.ExtractLeafBinPayloads(path);
            Assert.Contains("Get-Sessions.ps1", payloads, StringComparer.OrdinalIgnoreCase);
            Assert.Contains("listdlls.exe", payloads, StringComparer.OrdinalIgnoreCase);
            Assert.DoesNotContain(payloads, p => p.Contains("powershell", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            try { Directory.Delete(tmp, true); } catch { /* ignore */ }
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
