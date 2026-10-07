using KapeIR.Core.Models;
using KapeIR.Core.Services;
using KapeIR.Builder.Tests.Fixtures;

namespace KapeIR.Builder.Tests;

public class ModulesBinPreflightTests
{
    [Fact]
    public void Analyze_ReportsMissingRootExeAndNestedFolder()
    {
        using var fx = FakeKapeRoot.Create(FakeKapeProfile.Minimal);
        var modDir = Path.Combine(fx.Root, "Modules", "Windows");
        Directory.CreateDirectory(modDir);

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

        KapeCatalog.ClearFileCache();
        fx.Catalog.Refresh();

        var pkg = new PackageDefinition
        {
            Name = "PreflightTest",
            Modules =
            {
                new SelectionEntry { Name = "NeedParser", Path = "NeedParser.mkape", Category = "Windows" }
            }
        };

        var analysis = SelectiveModulesBinCopier.Analyze(fx.Catalog, pkg);
        Assert.Contains(analysis.Missing, m => m.Contains("MissingParserX", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(analysis.Missing, m => m.Equals("hayabusa\\", StringComparison.OrdinalIgnoreCase));

        var pre = ModulesBinPreflight.Check(fx.Catalog, pkg);
        Assert.True(pre.HasIssues);
        Assert.NotEmpty(pre.SkippedModules);
        var msg = ModulesBinPreflight.FormatConfirmMessage(pre);
        Assert.Contains("Modules\\bin", msg);
        Assert.Contains("Да = продолжить", msg);
    }

    [Fact]
    public void Check_TwoPhase_FlagsMissingWinpmem()
    {
        using var fx = FakeKapeRoot.Create(
            FakeKapeProfile.TwoPhaseIr,
            new FakeKapeOptions { IncludeWinpmemBin = false });

        var pkg = new PackageDefinition
        {
            Name = "TwoPhasePre",
            CollectionMode = IrCollectionMode.TwoPhase,
            Targets =
            {
                new SelectionEntry
                {
                    Name = FakeKapeRoot.DemoLeafName,
                    Path = FakeKapeRoot.DemoLeafName + ".tkape"
                }
            }
        };

        var pre = ModulesBinPreflight.Check(fx.Catalog, pkg);
        Assert.True(pre.WinpmemMissing);
        Assert.True(pre.HasIssues);
    }
}
