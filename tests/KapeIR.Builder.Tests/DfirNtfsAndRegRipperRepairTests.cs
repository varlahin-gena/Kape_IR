using System.IO.Compression;
using System.Text;
using KapeIR.Core.Services;

namespace KapeIR.Builder.Tests;

public sealed class DfirNtfsAndRegRipperRepairTests
{
    [Fact]
    public void ExtractBinRefs_AcceptsEscapedDoubleBackslashes()
    {
        var cmd =
            @"python.exe %kapeDirectory%\\Modules\\bin\\dfir_ntfs\\ntfs_parser --log x";
        var refs = KapeCompoundIo.ExtractBinRefsFromCommandLine(cmd).ToList();
        Assert.Contains(@"dfir_ntfs\ntfs_parser", refs, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DfirNtfsInstaller_InstallsFromZipStream()
    {
        var root = Path.Combine(Path.GetTempPath(), "kapeir-dfir-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await using var zip = BuildMinimalDfirZip();
            var result = await DfirNtfsInstaller.InstallAsync(
                root, zipStreamOverride: zip);
            Assert.True(result.Ok, result.Message);
            Assert.True(File.Exists(DfirNtfsInstaller.GetParserPath(root)));
            Assert.True(DfirNtfsInstaller.Check(root).Ok);
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void RegRipperSanitizeProfiles_RemovesMissingPluginLines()
    {
        var dir = Path.Combine(Path.GetTempPath(), "kapeir-rr-" + Guid.NewGuid().ToString("N"));
        var plugins = Path.Combine(dir, "plugins");
        Directory.CreateDirectory(plugins);
        try
        {
            File.WriteAllText(Path.Combine(plugins, "system"), "winver\nprinter_settings\ncrashcontrol\n");
            File.WriteAllText(Path.Combine(plugins, "winver.pl"), "# stub\n");
            var pruned = RegRipperPluginRepair.SanitizeProfilesInPlace(plugins);
            Assert.Equal(2, pruned);
            var lines = File.ReadAllLines(Path.Combine(plugins, "system"))
                .Select(l => l.Trim())
                .Where(l => l.Length > 0)
                .ToList();
            Assert.Equal(new[] { "winver" }, lines);
            Assert.Empty(RegRipperPluginRepair.CollectMissingPluginNames(plugins));
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* ignore */ }
        }
    }

    private static MemoryStream BuildMinimalDfirZip()
    {
        var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            var root = DfirNtfsInstaller.TagFolderName + "/";
            void Add(string name, string content)
            {
                var e = zip.CreateEntry(root + name);
                using var w = new StreamWriter(e.Open(), Encoding.UTF8);
                w.Write(content);
            }

            Add("ntfs_parser", "#!/usr/bin/env python3\nprint('ok')\n");
            Add("dfir_ntfs/__init__.py", "# pkg\n");
            Add("setup.py", "from setuptools import setup\n");
        }

        ms.Position = 0;
        return ms;
    }
}
