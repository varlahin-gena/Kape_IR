using System.IO.Compression;
using System.Text;
using System.Text.Json;
using KapeIR.Core.Services;

namespace KapeIR.Builder.Tests;

public sealed class HayabusaInstallerTests
{
    [Fact]
    public void Check_ReportsMissingWhenAbsent()
    {
        var root = Path.Combine(Path.GetTempPath(), "kape_haya_miss_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var status = HayabusaInstaller.Check(root);
            Assert.False(status.Ok);
            Assert.True(status.NeedsUpdate);
            Assert.Contains("hayabusa.exe", status.MissingParts);
            Assert.Contains("rules\\", status.MissingParts);
            Assert.Contains("config\\", status.MissingParts);
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public async Task InstallAsync_NestsExeConfigRules_AndWritesTag()
    {
        var root = Path.Combine(Path.GetTempPath(), "kape_haya_inst_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await using var zipMs = BuildMinimalHayabusaZip();
            var result = await HayabusaInstaller.InstallAsync(
                root,
                zipStreamOverride: zipMs,
                releaseTagOverride: "v4.1.0");
            Assert.True(result.Ok, result.Message);
            Assert.Equal(2, result.RuleFileCount);

            var status = HayabusaInstaller.Check(root);
            Assert.True(status.Ok, status.Message);
            Assert.False(status.NeedsUpdate);
            Assert.True(File.Exists(HayabusaInstaller.GetExePath(root)));
            Assert.Equal("MZ-fake", File.ReadAllText(HayabusaInstaller.GetExePath(root)));
            Assert.True(File.Exists(Path.Combine(root, "Modules", "bin", "hayabusa", "rules", "sigma", "demo.yml")));
            Assert.True(File.Exists(Path.Combine(root, "Modules", "bin", "hayabusa", "config", "config.yaml")));
            Assert.Equal("v4.1.0", HayabusaInstaller.ReadInstalledTag(root));
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void PickWindowsAsset_PrefersFullWinX64_NotLiveResponse()
    {
        const string json = """
            {
              "assets": [
                { "name": "hayabusa-4.1.0-win-x64-live-response.zip", "browser_download_url": "https://example/live.zip" },
                { "name": "hayabusa-4.1.0-win-x64.zip", "browser_download_url": "https://example/full.zip" },
                { "name": "hayabusa-4.1.0-win-aarch64.zip", "browser_download_url": "https://example/arm.zip" }
              ]
            }
            """;
        using var doc = JsonDocument.Parse(json);
        var assets = doc.RootElement.GetProperty("assets");
        var picked = HayabusaInstaller.PickWindowsAsset(assets, preferArm: false);
        Assert.NotNull(picked);
        Assert.Equal("hayabusa-4.1.0-win-x64.zip", picked.Value.Name);
        Assert.Equal("https://example/full.zip", picked.Value.Url);
    }

    [Fact]
    public void FindWindowsExecutable_PicksWinX64()
    {
        var dir = Path.Combine(Path.GetTempPath(), "kape_haya_find_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "hayabusa-4.1.0-lin-x64-gnu"), "elf");
            File.WriteAllText(Path.Combine(dir, "hayabusa-4.1.0-win-x64.exe"), "MZ");
            var found = HayabusaInstaller.FindWindowsExecutable(dir);
            Assert.NotNull(found);
            Assert.EndsWith("hayabusa-4.1.0-win-x64.exe", found, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void TagsEqual_NormalizesVPrefix()
    {
        Assert.True(HayabusaInstaller.TagsEqual("4.1.0", "v4.1.0"));
        Assert.True(HayabusaInstaller.TagsEqual("v4.1.0", "V4.1.0"));
        Assert.False(HayabusaInstaller.TagsEqual("v4.0.0", "v4.1.0"));
    }

    private static MemoryStream BuildMinimalHayabusaZip()
    {
        var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            void Add(string name, string content)
            {
                var e = zip.CreateEntry(name);
                using var w = new StreamWriter(e.Open(), Encoding.UTF8);
                w.Write(content);
            }

            Add("hayabusa-4.1.0-win-x64/hayabusa-4.1.0-win-x64.exe", "MZ-fake");
            Add("hayabusa-4.1.0-win-x64/rules/sigma/demo.yml", "title: demo\n");
            Add("hayabusa-4.1.0-win-x64/rules/hayabusa/builtin.yml", "title: builtin\n");
            Add("hayabusa-4.1.0-win-x64/config/config.yaml", "key: value\n");
        }

        ms.Position = 0;
        return ms;
    }
}
