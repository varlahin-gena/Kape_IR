using System.IO.Compression;
using System.Text;
using KapePack.Core.Models;
using KapePack.Core.Services;

namespace KapePackBuilder.Tests;

public class KapeCliAndToolkitTests
{
    [Fact]
    public void KapeCliArgs_FleetVars_UsesDoublePercent()
    {
        var line = KapeCliArgs.RenderCliLine(new KapeCliArgs.Options(
            "C:", "PSBCollection", "Mods", ZipOutput: true, FleetCliVars: true));
        Assert.Contains(@"%%d\RESULTS\%%m", line);
        Assert.Contains("--zip %%m", line);
        Assert.Contains("--target PSBCollection", line);
        Assert.Contains("--module Mods", line);
    }

    [Fact]
    public void KapeCliArgs_PackageVars_UsesSinglePercent()
    {
        var args = KapeCliArgs.Build(new KapeCliArgs.Options("D:", "T1", null, ZipOutput: true));
        Assert.Contains(@"RESULTS\%m", args);
        Assert.Contains("%m", args);
        Assert.DoesNotContain(args, a => a.Contains("%%"));
    }

    [Fact]
    public async Task ChainsawInstaller_NestsExeRulesSigmaMappings()
    {
        var root = Path.Combine(Path.GetTempPath(), "kape_chainsaw_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        await using var zipMs = new MemoryStream();
        using (var zip = new ZipArchive(zipMs, ZipArchiveMode.Create, leaveOpen: true))
        {
            void Add(string name, string content)
            {
                var e = zip.CreateEntry(name);
                using var w = new StreamWriter(e.Open(), Encoding.UTF8);
                w.Write(content);
            }

            Add("chainsaw_x86_64-pc-windows-msvc.exe", "MZ-fake");
            Add("chainsaw_x86_64-unknown-linux-gnu", "elf");
            Add("rules/example.yml", "rule: 1");
            Add("sigma/windows/proc.yml", "sigma: 1");
            Add("mappings/sigma-event-logs-all.yml", "map: 1");
        }

        zipMs.Position = 0;
        var result = await ChainsawInstaller.InstallAsync(root, zipStreamOverride: zipMs);
        Assert.True(result.Ok, result.Message);

        var status = ChainsawInstaller.Check(root);
        Assert.True(status.Ok, status.Message);
        Assert.True(File.Exists(ChainsawInstaller.GetExePath(root)));
        Assert.Equal("MZ-fake", File.ReadAllText(ChainsawInstaller.GetExePath(root)));
        Assert.True(File.Exists(Path.Combine(root, "Modules", "bin", "chainsaw", "rules", "example.yml")));
        Assert.True(File.Exists(Path.Combine(
            root, "Modules", "bin", "chainsaw", "mappings", "sigma-event-logs-all.yml")));
    }

    [Fact]
    public void EzToolsUpdater_Check_ReportsMissing()
    {
        var root = Path.Combine(Path.GetTempPath(), "kape_ez_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "Modules", "bin"));
        var status = EzToolsUpdater.Check(root);
        Assert.False(status.Ok);
        Assert.True(status.NeedsUpdate);
        Assert.True(status.Missing.Count >= 5);
    }

    [Fact]
    public void EzToolsUpdater_Check_DoesNotCountNetOnlyAsPresent()
    {
        var root = Path.Combine(Path.GetTempPath(), "kape_ez2_" + Guid.NewGuid().ToString("N"));
        var dir = Path.Combine(root, "Modules", "bin", "net9");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "PECmd.exe"), "x");
        var status = EzToolsUpdater.Check(root);
        Assert.DoesNotContain("PECmd.exe", status.Present);
        Assert.Contains("netN", status.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EzToolsLayout_Promote_CopiesNet9ToBinRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "kape_ez_promote_" + Guid.NewGuid().ToString("N"));
        var bin = Path.Combine(root, "Modules", "bin");
        var net9 = Path.Combine(bin, "net9");
        Directory.CreateDirectory(Path.Combine(net9, "EvtxECmd"));
        File.WriteAllText(Path.Combine(net9, "PECmd.exe"), "pe");
        File.WriteAllText(Path.Combine(net9, "EvtxECmd", "Maps.txt"), "m");
        File.WriteAllText(Path.Combine(bin, "winpmem.exe"), "keep");

        var n = EzToolsLayout.PromoteNetFolderToBinRoot(bin);
        Assert.True(n >= 2);
        Assert.True(File.Exists(Path.Combine(bin, "PECmd.exe")));
        Assert.True(File.Exists(Path.Combine(bin, "EvtxECmd", "Maps.txt")));
        Assert.Equal("keep", File.ReadAllText(Path.Combine(bin, "winpmem.exe")));
        Assert.Contains("PECmd.exe", EzToolsUpdater.Check(root).Present);

        Assert.Equal(1, EzToolsLayout.RemoveNetRuntimeFolders(bin));
        Assert.False(Directory.Exists(net9));
        Assert.True(File.Exists(Path.Combine(bin, "PECmd.exe")));
    }

    [Fact]
    public void ModuleBinGate_SkipsMissingThirdParty()
    {
        KapeCatalog.ClearFileCache();
        var root = Path.Combine(Path.GetTempPath(), "kape_bingate_" + Guid.NewGuid().ToString("N"));
        var bin = Path.Combine(root, "Modules", "bin");
        var modDir = Path.Combine(root, "Modules", "EZTools");
        Directory.CreateDirectory(bin);
        Directory.CreateDirectory(modDir);
        File.WriteAllText(Path.Combine(bin, "PECmd.exe"), "x");
        File.WriteAllText(Path.Combine(modDir, "PECmd.mkape"), """
Description: test
Category: Modules
Author: t
Version: 1.0
Id: 11111111-1111-1111-1111-111111111111
Processors:
    -
        Executable: PECmd.exe
        CommandLine: ""
        ExportFormat: csv
""");
        // Unique missing name — LogParser.exe may exist under a real KAPE Modules\bin on the host.
        File.WriteAllText(Path.Combine(modDir, "LogParser_X.mkape"), """
Description: test
Category: Modules
Author: t
Version: 1.0
Id: 22222222-2222-2222-2222-222222222222
Processors:
    -
        Executable: ZzMissingParser_GateTest.exe
        CommandLine: ""
        ExportFormat: csv
""");

        try
        {
            var catalog = new KapeCatalog(root);
            catalog.Refresh();
            var entries = new[]
            {
                new SelectionEntry { Name = "PECmd", Path = "Modules\\EZTools\\PECmd.mkape", Category = "Modules" },
                new SelectionEntry { Name = "LogParser_X", Path = "Modules\\EZTools\\LogParser_X.mkape", Category = "Modules" },
            };
            var r = ModuleBinGate.FilterByAvailableBinaries(catalog, entries, modulesBinOverride: bin);
            Assert.Single(r.Kept);
            Assert.Equal("PECmd", r.Kept[0].Name);
            Assert.Single(r.Skipped);
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void EzToolsUpdater_SanitizeToolOutput_FixesNbspMojibake()
    {
        Assert.Equal(
            "Downloaded SBECmd.zip (Size: 3 177 325) (net 9)",
            EzToolsUpdater.SanitizeToolOutput("Downloaded SBECmd.zip (Size: 3я177я325) (net 9)"));
        Assert.Equal(
            "Size: 3 177 325",
            EzToolsUpdater.SanitizeToolOutput("Size: 3\u00A0177\u00A0325"));
        Assert.Equal("обычный текст", EzToolsUpdater.SanitizeToolOutput("обычный текст"));
    }

    [Fact]
    public void ModulesBinInventory_Scan_FindsExeAndVersions()
    {
        var root = Path.Combine(Path.GetTempPath(), "kape_bininv_" + Guid.NewGuid().ToString("N"));
        var net9 = Path.Combine(root, "Modules", "bin", "net9");
        Directory.CreateDirectory(net9);
        var peCmd = Path.Combine(net9, "PECmd.exe");
        File.WriteAllBytes(peCmd, "MZ"u8.ToArray());
        File.WriteAllText(Path.Combine(root, "Modules", "bin", "Get-ZimmermanTools.ps1"), "# test");

        var report = ModulesBinInventory.Scan(root);
        Assert.True(report.Exists);
        Assert.Contains(report.Items, i => i.Name.Equals("PECmd.exe", StringComparison.OrdinalIgnoreCase) && i.IsKeyTool);
        Assert.Contains(report.Items, i => i.Name.Equals("Get-ZimmermanTools.ps1", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("EZ Tools", report.Items.First(i => i.Name == "PECmd.exe").Category);
        Assert.Contains("Modules\\bin", report.Message);
    }

    [Fact]
    public void KapeCliArgs_Simulate_AddsSimSwitch()
    {
        var args = KapeCliArgs.Build(new KapeCliArgs.Options("C:", "T1", Simulate: true));
        Assert.Contains("--sim", args);
        var noSim = KapeCliArgs.Build(new KapeCliArgs.Options("C:", "T1", Simulate: false));
        Assert.DoesNotContain("--sim", noSim);
    }

    [Fact]
    public void KapeCliArgs_Simulate_OmitsModulesAndZip()
    {
        var sim = KapeCliArgs.Build(new KapeCliArgs.Options(
            "C:", "PSBCollection", Module: "PSBCollection_Modules", ZipOutput: true, Simulate: true));
        Assert.Contains("--sim", sim);
        Assert.Contains("--target", sim);
        Assert.DoesNotContain("--module", sim);
        Assert.DoesNotContain("--zip", sim);
        Assert.DoesNotContain("--zm", sim);
        Assert.DoesNotContain("--mdest", sim);

        var full = KapeCliArgs.Build(new KapeCliArgs.Options(
            "C:", "PSBCollection", Module: "PSBCollection_Modules", ZipOutput: true, Simulate: false));
        Assert.Contains("--module", full);
        Assert.Contains("PSBCollection_Modules", full);
        Assert.Contains("--zip", full);
    }

    [Fact]
    public void KapeBatchCliGuard_HoldAside_RestoresOnDispose()
    {
        var dir = Path.Combine(Path.GetTempPath(), "kape_cli_hold_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var cli = Path.Combine(dir, "_kape.cli");
            File.WriteAllText(cli, "--tsource C: --target T\r\n");
            var logs = new List<string>();
            using (KapeBatchCliGuard.HoldAside(dir, logs.Add))
            {
                Assert.False(File.Exists(cli));
                Assert.True(File.Exists(cli + ".packhold"));
                Assert.Contains(logs, l => l.Contains("_kape.cli", StringComparison.OrdinalIgnoreCase));
            }

            Assert.True(File.Exists(cli));
            Assert.False(File.Exists(cli + ".packhold"));
            Assert.Contains("--tsource", File.ReadAllText(cli));
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* ignore */ }
        }
    }
}