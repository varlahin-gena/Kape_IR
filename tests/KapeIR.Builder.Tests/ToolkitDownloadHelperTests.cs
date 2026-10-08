using System.IO.Compression;
using System.Text;
using KapeIR.Core.Services;

namespace KapeIR.Builder.Tests;

public sealed class ToolkitDownloadHelperTests
{
    [Fact]
    public void CreateTempWorkspace_CreatesAndCleansOnDispose()
    {
        string path;
        using (var ws = ToolkitDownloadHelper.CreateTempWorkspace("kapeir_tdh_"))
        {
            path = ws.Path;
            Assert.True(Directory.Exists(path));
            File.WriteAllText(Path.Combine(path, "marker.txt"), "x");
        }

        Assert.False(Directory.Exists(path));
    }

    [Fact]
    public void CopyDirectory_RecursesWithNestedFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), "kapeir-copy-" + Guid.NewGuid().ToString("N"));
        var src = Path.Combine(root, "src");
        var dst = Path.Combine(root, "dst");
        try
        {
            Directory.CreateDirectory(Path.Combine(src, "a", "b"));
            File.WriteAllText(Path.Combine(src, "root.txt"), "r");
            File.WriteAllText(Path.Combine(src, "a", "b", "nested.txt"), "n");

            ToolkitDownloadHelper.CopyDirectory(src, dst);

            Assert.Equal("r", File.ReadAllText(Path.Combine(dst, "root.txt")));
            Assert.Equal("n", File.ReadAllText(Path.Combine(dst, "a", "b", "nested.txt")));
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public async Task MaterializeZip_FromStream_ThenExtract()
    {
        var root = Path.Combine(Path.GetTempPath(), "kapeir-zip-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await using var zipMs = new MemoryStream();
            using (var zip = new ZipArchive(zipMs, ZipArchiveMode.Create, leaveOpen: true))
            {
                var e = zip.CreateEntry("hello.txt");
                await using var w = new StreamWriter(e.Open(), Encoding.UTF8);
                await w.WriteAsync("hi");
            }

            zipMs.Position = 0;
            var zipPath = Path.Combine(root, "t.zip");
            await ToolkitDownloadHelper.MaterializeZipAsync(
                zipPath,
                url: "https://example.invalid/unused.zip",
                zipStreamOverride: zipMs,
                progress: null,
                downloadMessage: "dl",
                overrideMessage: "ov");

            Assert.True(File.Exists(zipPath));

            var extract = Path.Combine(root, "out");
            ToolkitDownloadHelper.ExtractZip(zipPath, extract);
            Assert.Equal("hi", File.ReadAllText(Path.Combine(extract, "hello.txt")));
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { /* ignore */ }
        }
    }
}
