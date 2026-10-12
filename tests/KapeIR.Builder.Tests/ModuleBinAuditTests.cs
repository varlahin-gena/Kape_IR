using KapeIR.Core.Models;
using KapeIR.Core.Services;

namespace KapeIR.Builder.Tests;

public class ModuleBinAuditTests
{
    [Fact]
    public void AuditLeaf_ReportsMissingAndOk()
    {
        KapeCatalog.ClearFileCache();
        var root = Path.Combine(Path.GetTempPath(), "kape_audit_" + Guid.NewGuid().ToString("N"));
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
            var index = ModuleBinAudit.IndexCatalog(catalog);

            Assert.True(index.TryGetValue("Windows_IPConfig", out var ok));
            Assert.Equal(ModuleBinStatus.Ok, ok.Status);
            Assert.Equal("✓", ok.DisplayGlyph);

            Assert.True(index.TryGetValue("Hindsight_X", out var miss));
            Assert.Equal(ModuleBinStatus.Missing, miss.Status);
            Assert.Contains("Run-Hindsight.ps1", miss.Missing, StringComparer.OrdinalIgnoreCase);
            Assert.Equal("✗", miss.DisplayGlyph);

            var pkg = new PackageDefinition
            {
                Modules =
                {
                    new SelectionEntry { Name = "Windows_IPConfig", Path = "Windows_IPConfig.mkape" },
                    new SelectionEntry { Name = "Hindsight_X", Path = "Hindsight_X.mkape" }
                }
            };
            var report = ModuleBinAudit.AuditPackage(catalog, pkg);
            Assert.True(report.HasIssues);
            Assert.Equal(1, report.MissingCount);
            Assert.Equal(1, report.OkCount);
            Assert.Contains("Hindsight_X", ModuleBinAudit.FormatReport(report), StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void Audit_SyncModule_IsSkipped()
    {
        KapeCatalog.ClearFileCache();
        var root = Path.Combine(Path.GetTempPath(), "kape_audit_sync_" + Guid.NewGuid().ToString("N"));
        var modDir = Path.Combine(root, "Modules", "Sync");
        Directory.CreateDirectory(modDir);
        Directory.CreateDirectory(Path.Combine(root, "Modules", "bin"));
        Directory.CreateDirectory(Path.Combine(root, "Targets"));

        File.WriteAllText(Path.Combine(modDir, "!!ToolSync.mkape"), """
Description: sync
Category: Sync
Author: t
Version: 1.0
Id: cccccccc-cccc-cccc-cccc-cccccccccccc
Processors:
    -
        Executable: fake.exe
        CommandLine: x
""");

        try
        {
            var catalog = new KapeCatalog(root);
            catalog.Refresh();
            var index = ModuleBinAudit.IndexCatalog(catalog);
            Assert.True(index.TryGetValue("!!ToolSync", out var e));
            Assert.Equal(ModuleBinStatus.SyncSkipped, e.Status);
            Assert.Equal("—", e.DisplayGlyph);
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void FormatSyncSummary_IncludesPackageSection()
    {
        var catalogReport = new ModuleBinAuditReport(
            @"C:\KAPE\Modules\bin",
            new[]
            {
                new ModuleBinAuditEntry("A", "A.mkape", ModuleBinStatus.Ok, Array.Empty<string>(), Array.Empty<string>()),
                new ModuleBinAuditEntry("B", "B.mkape", ModuleBinStatus.Missing, new[] { "x.exe" }, new[] { "x.exe" })
            });
        var packageReport = new ModuleBinAuditReport(
            @"C:\KAPE\Modules\bin",
            new[]
            {
                new ModuleBinAuditEntry("B", "B.mkape", ModuleBinStatus.Missing, new[] { "x.exe" }, new[] { "x.exe" })
            });

        var text = ModuleBinAudit.FormatSyncSummary(catalogReport, packageReport);
        Assert.Contains("✗ 1", text);
        Assert.Contains("Текущая сборка", text);
        Assert.Contains("B", text);
    }
}
