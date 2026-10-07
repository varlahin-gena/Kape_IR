using KapePack.Core.Models;
using KapePack.Core.Services;

namespace KapePackBuilder.Tests;

public class ModulesBinPreflightTests
{
    [Fact]
    public void Analyze_ReportsMissingRootExeAndNestedFolder()
    {
        KapeCatalog.ClearFileCache();
        var root = Path.Combine(Path.GetTempPath(), "kape_preflight_" + Guid.NewGuid().ToString("N"));
        var bin = Path.Combine(root, "Modules", "bin");
        var modDir = Path.Combine(root, "Modules", "Windows");
        Directory.CreateDirectory(bin);
        Directory.CreateDirectory(modDir);
        Directory.CreateDirectory(Path.Combine(root, "Targets"));

        File.WriteAllText(Path.Combine(modDir, "NeedParser.mkape"), """
Description: test
Category: Windows
Author: test
Version: 1.0
Id: aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeee01
Processors:
    -
        Executable: MissingParserX.exe
        CommandLine: ""
        ExportFormat: txt
    -
        Executable: C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe
        CommandLine: -File "%kapeDirectory%\Modules\bin\hayabusa\hayabusa.exe"
        ExportFormat: txt
""");

        try
        {
            var catalog = new KapeCatalog(root);
            catalog.Refresh();
            var pkg = new PackageDefinition
            {
                Name = "PreflightTest",
                Modules =
                {
                    new SelectionEntry { Name = "NeedParser", Path = "NeedParser.mkape", Category = "Windows" }
                }
            };

            var analysis = SelectiveModulesBinCopier.Analyze(catalog, pkg);
            Assert.Contains(analysis.Missing, m => m.Contains("MissingParserX", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(analysis.Missing, m => m.Equals("hayabusa\\", StringComparison.OrdinalIgnoreCase));

            var pre = ModulesBinPreflight.Check(catalog, pkg);
            Assert.True(pre.HasIssues);
            Assert.NotEmpty(pre.SkippedModules);
            var msg = ModulesBinPreflight.FormatConfirmMessage(pre);
            Assert.Contains("Modules\\bin", msg);
            Assert.Contains("Да = продолжить", msg);
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void Check_TwoPhase_FlagsMissingWinpmem()
    {
        KapeCatalog.ClearFileCache();
        var root = Path.Combine(Path.GetTempPath(), "kape_preflight_wp_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "Modules", "bin"));
        Directory.CreateDirectory(Path.Combine(root, "Modules", "Compound"));
        Directory.CreateDirectory(Path.Combine(root, "Targets"));

        // Minimal VolatileFirst compound so EnsureTwoPhase can resolve it.
        File.WriteAllText(Path.Combine(root, "Modules", "Compound", "VolatileFirst.mkape"), """
Description: vf
Category: LiveResponse
Author: test
Version: 1.0
Id: aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeee02
Processors:
    -
        Executable: Velocidex_WinPmem.mkape
        CommandLine: ""
        ExportFormat: ""
""");
        File.WriteAllText(Path.Combine(root, "Modules", "Compound", "VolatileFirst_NoMemory.mkape"), """
Description: vf-nm
Category: LiveResponse
Author: test
Version: 1.0
Id: aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeee03
Processors:
    -
        Executable: Windows_IPConfig.mkape
        CommandLine: ""
        ExportFormat: ""
""");
        Directory.CreateDirectory(Path.Combine(root, "Modules", "Apps", "GitHub"));
        File.WriteAllText(Path.Combine(root, "Modules", "Apps", "GitHub", "Velocidex_WinPmem.mkape"), """
Description: mem
Category: Memory
Author: test
Version: 1.0
Id: aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeee04
Processors:
    -
        Executable: winpmem.exe
        CommandLine: acquire memory.raw
        ExportFormat: raw
""");
        Directory.CreateDirectory(Path.Combine(root, "Modules", "Windows"));
        File.WriteAllText(Path.Combine(root, "Modules", "Windows", "Windows_IPConfig.mkape"), """
Description: ip
Category: LiveResponse
Author: test
Version: 1.0
Id: aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeee05
Processors:
    -
        Executable: C:\Windows\System32\ipconfig.exe
        CommandLine: /all
        ExportFormat: txt
""");

        try
        {
            var catalog = new KapeCatalog(root);
            catalog.Refresh();
            var pkg = new PackageDefinition
            {
                Name = "TwoPhasePre",
                CollectionMode = IrCollectionMode.TwoPhase,
                Targets =
                {
                    new SelectionEntry { Name = "Dummy", Path = "Dummy.tkape" }
                }
            };

            var pre = ModulesBinPreflight.Check(catalog, pkg);
            Assert.True(pre.WinpmemMissing);
            Assert.True(pre.HasIssues);
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { /* ignore */ }
        }
    }
}
