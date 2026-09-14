using KapePack.Core.Models;
using KapePack.Core.Services;

namespace KapePackBuilder.Tests;

public class SelectiveModulesBinCopierTests
{
    [Theory]
    [InlineData("PECmd.exe", "PECmd", true)]
    [InlineData("PECmd.dll", "PECmd", true)]
    [InlineData("PECmd.runtimeconfig.json", "PECmd", true)]
    [InlineData("PECmd.deps.json", "PECmd", true)]
    [InlineData("MFTECmd.exe", "PECmd", false)]
    [InlineData("System.Runtime.dll", "PECmd", false)]
    public void BelongsToStem_MatchesToolCompanions(string fileName, string stem, bool expected)
        => Assert.Equal(expected, SelectiveModulesBinCopier.BelongsToStem(fileName, stem));

    [Fact]
    public void IsExclusiveToForeignStem_KeepsSharedAndRequired_DropsOtherTools()
    {
        var required = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "PECmd" };
        var all = new[] { "PECmd", "MFTECmd", "AmcacheParser" };

        Assert.False(SelectiveModulesBinCopier.IsExclusiveToForeignStem("PECmd.dll", required, all));
        Assert.False(SelectiveModulesBinCopier.IsExclusiveToForeignStem("System.Runtime.dll", required, all));
        Assert.False(SelectiveModulesBinCopier.IsExclusiveToForeignStem("Microsoft.Win32.Registry.dll", required, all));
        Assert.True(SelectiveModulesBinCopier.IsExclusiveToForeignStem("MFTECmd.exe", required, all));
        Assert.True(SelectiveModulesBinCopier.IsExclusiveToForeignStem("AmcacheParser.dll", required, all));
    }

    [Fact]
    public void Copy_SelectsRootToolSharedDlls_NestedFolder_AndSkipsForeignTools()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "kape_selbin_" + Guid.NewGuid().ToString("N"));
        var root = Path.Combine(tmp, "kape");
        var bin = Path.Combine(root, "Modules", "bin");
        var ez = Path.Combine(root, "Modules", "EZTools");
        var apps = Path.Combine(root, "Modules", "Apps");
        var targets = Path.Combine(root, "Targets", "Apps");
        Directory.CreateDirectory(bin);
        Directory.CreateDirectory(Path.Combine(bin, "chainsaw", "rules"));
        Directory.CreateDirectory(ez);
        Directory.CreateDirectory(apps);
        Directory.CreateDirectory(targets);

        File.WriteAllText(Path.Combine(bin, "PECmd.exe"), "pecmd");
        File.WriteAllText(Path.Combine(bin, "PECmd.dll"), "pecmd-dll");
        File.WriteAllText(Path.Combine(bin, "System.Runtime.dll"), "shared");
        File.WriteAllText(Path.Combine(bin, "MFTECmd.exe"), "mft");
        File.WriteAllText(Path.Combine(bin, "MFTECmd.dll"), "mft-dll");
        File.WriteAllText(Path.Combine(bin, "chainsaw", "Chainsaw.exe"), "cs");
        File.WriteAllText(Path.Combine(bin, "chainsaw", "rules", "a.yml"), "rule");
        File.WriteAllText(Path.Combine(bin, "winpmem.exe"), "mem");

        File.WriteAllText(Path.Combine(ez, "PECmd.mkape"), """
Description: demo
Author: test
Version: 1.0
Id: 11111111-1111-1111-1111-111111111111
ExportFormat: csv
Processors:
    -
        Executable: PECmd.exe
        CommandLine: -f %sourceFile%
        ExportFormat: csv
""");
        File.WriteAllText(Path.Combine(apps, "Chainsaw_Demo.mkape"), """
Description: demo
Author: test
Version: 1.0
Id: 22222222-2222-2222-2222-222222222222
ExportFormat: json
Processors:
    -
        Executable: chainsaw\Chainsaw.exe
        CommandLine: hunt
        ExportFormat: json
""");
        File.WriteAllText(Path.Combine(targets, "Leaf.tkape"), """
Description: demo
Author: test
Version: 1.0
Id: 33333333-3333-3333-3333-333333333333
RecreateDirectories: true
Targets:
    -
        Name: Demo
        Category: Apps
        Path: C:\Windows\
        FileMask: '*.log'
""");

        var packageDir = Path.Combine(tmp, "pack");
        Directory.CreateDirectory(Path.Combine(packageDir, "Modules"));

        try
        {
            var cat = new KapeCatalog(root);
            cat.Refresh();
            var pkg = new PackageDefinition
            {
                Name = "SelBin",
                Modules =
                {
                    new SelectionEntry { Name = "PECmd", Path = "PECmd.mkape", Category = "EZTools" },
                    new SelectionEntry { Name = "Chainsaw_Demo", Path = "Chainsaw_Demo.mkape", Category = "Apps" }
                }
            };

            var result = SelectiveModulesBinCopier.Copy(cat, pkg, packageDir);
            Assert.True(result.FilesCopied >= 4, $"copied={result.FilesCopied}");
            Assert.Contains("PECmd", result.RootStems);
            Assert.Contains("chainsaw", result.NestedFolders, StringComparer.OrdinalIgnoreCase);

            var outBin = Path.Combine(packageDir, "Modules", "bin");
            Assert.True(File.Exists(Path.Combine(outBin, "PECmd.exe")));
            Assert.True(File.Exists(Path.Combine(outBin, "PECmd.dll")));
            Assert.True(File.Exists(Path.Combine(outBin, "System.Runtime.dll")));
            Assert.True(File.Exists(Path.Combine(outBin, "chainsaw", "Chainsaw.exe")));
            Assert.True(File.Exists(Path.Combine(outBin, "chainsaw", "rules", "a.yml")));
            Assert.False(File.Exists(Path.Combine(outBin, "MFTECmd.exe")));
            Assert.False(File.Exists(Path.Combine(outBin, "MFTECmd.dll")));
            Assert.False(File.Exists(Path.Combine(outBin, "winpmem.exe")));
        }
        finally
        {
            try { Directory.Delete(tmp, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void Copy_PromotesFromNetFolder_AndCopiesMapsForRECmd()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "kape_selbin_net_" + Guid.NewGuid().ToString("N"));
        var root = Path.Combine(tmp, "kape");
        var bin = Path.Combine(root, "Modules", "bin");
        var net9 = Path.Combine(bin, "net9");
        var maps = Path.Combine(bin, "Maps");
        var ez = Path.Combine(root, "Modules", "EZTools");
        Directory.CreateDirectory(net9);
        Directory.CreateDirectory(maps);
        Directory.CreateDirectory(ez);
        Directory.CreateDirectory(Path.Combine(root, "Targets"));

        File.WriteAllText(Path.Combine(net9, "RECmd.exe"), "recmd");
        File.WriteAllText(Path.Combine(net9, "RECmd.dll"), "recmd-dll");
        File.WriteAllText(Path.Combine(net9, "System.Collections.dll"), "shared");
        File.WriteAllText(Path.Combine(net9, "WxTCmd.exe"), "wxt");
        File.WriteAllText(Path.Combine(maps, "UserActivity.reb"), "map");

        File.WriteAllText(Path.Combine(ez, "RECmd_Demo.mkape"), """
Description: demo
Author: test
Version: 1.0
Id: 44444444-4444-4444-4444-444444444444
ExportFormat: csv
Processors:
    -
        Executable: RECmd.exe
        CommandLine: -d %sourceDirectory% --bn .\Maps\UserActivity.reb
        ExportFormat: csv
""");

        var packageDir = Path.Combine(tmp, "pack");
        Directory.CreateDirectory(Path.Combine(packageDir, "Modules"));

        try
        {
            var cat = new KapeCatalog(root);
            cat.Refresh();
            var pkg = new PackageDefinition
            {
                Name = "RECmdPack",
                Modules =
                {
                    new SelectionEntry { Name = "RECmd_Demo", Path = "RECmd_Demo.mkape", Category = "EZTools" }
                }
            };

            var result = SelectiveModulesBinCopier.Copy(cat, pkg, packageDir);
            Assert.Contains("RECmd", result.RootStems);
            Assert.Contains("Maps", result.NestedFolders);

            var outBin = Path.Combine(packageDir, "Modules", "bin");
            Assert.True(File.Exists(Path.Combine(outBin, "RECmd.exe")));
            Assert.True(File.Exists(Path.Combine(outBin, "RECmd.dll")));
            Assert.True(File.Exists(Path.Combine(outBin, "System.Collections.dll")));
            Assert.True(File.Exists(Path.Combine(outBin, "Maps", "UserActivity.reb")));
            Assert.False(File.Exists(Path.Combine(outBin, "WxTCmd.exe")));
            Assert.False(Directory.Exists(Path.Combine(outBin, "net9")));
        }
        finally
        {
            try { Directory.Delete(tmp, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void CollectRequiredExecutables_FlattensCompound_SkipsBuiltinsAndSync()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "kape_selbin_collect_" + Guid.NewGuid().ToString("N"));
        var root = Path.Combine(tmp, "kape");
        var compound = Path.Combine(root, "Modules", "Compound");
        var live = Path.Combine(root, "Modules", "LiveResponse");
        Directory.CreateDirectory(compound);
        Directory.CreateDirectory(live);
        Directory.CreateDirectory(Path.Combine(root, "Targets"));
        Directory.CreateDirectory(Path.Combine(root, "Modules", "bin"));

        File.WriteAllText(Path.Combine(compound, "Phase1.mkape"), """
Description: c
Author: t
Version: 1.0
Id: 55555555-5555-5555-5555-555555555555
ExportFormat: txt
Processors:
    -
        Executable: LeafMem.mkape
    -
        Executable: '!!ToolSync.mkape'
""");
        File.WriteAllText(Path.Combine(live, "LeafMem.mkape"), """
Description: l
Author: t
Version: 1.0
Id: 66666666-6666-6666-6666-666666666666
ExportFormat: txt
Processors:
    -
        Executable: winpmem.exe
    -
        Executable: powershell.exe
""");
        File.WriteAllText(Path.Combine(compound, "!!ToolSync.mkape"), """
Description: sync
Author: t
Category: Sync
Version: 1.0
Id: 77777777-7777-7777-7777-777777777777
ExportFormat: txt
Processors:
    -
        Executable: sync.exe
""");

        try
        {
            var cat = new KapeCatalog(root);
            cat.Refresh();
            var pkg = new PackageDefinition
            {
                Modules =
                {
                    new SelectionEntry { Name = "Phase1", Path = "Phase1.mkape", Category = "Compound" }
                }
            };

            var exes = SelectiveModulesBinCopier.CollectRequiredExecutables(cat, pkg);
            Assert.Contains("winpmem.exe", exes, StringComparer.OrdinalIgnoreCase);
            Assert.DoesNotContain(exes, e => e.Contains("powershell", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(exes, e => e.Contains("sync", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            try { Directory.Delete(tmp, true); } catch { /* ignore */ }
        }
    }
}
