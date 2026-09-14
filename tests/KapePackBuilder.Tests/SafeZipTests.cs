using System.IO.Compression;
using KapePack.Core.Shared;

namespace KapePackBuilder.Tests;

public class SafeZipTests
{
    [Fact]
    public void ResolveUnderRoot_BlocksZipSlip()
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "safezip_root")) + Path.DirectorySeparatorChar;
        Assert.Throws<InvalidDataException>(() =>
            SafeZip.ResolveUnderRoot(root, @"..\..\Windows\System32\evil.txt"));
        Assert.Throws<InvalidDataException>(() =>
            SafeZip.ResolveUnderRoot(root, @"foo/../../../evil.txt"));
    }

    [Fact]
    public void ResolveUnderRoot_AllowsNormalEntry()
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "safezip_root")) + Path.DirectorySeparatorChar;
        var path = SafeZip.ResolveUnderRoot(root, "Targets/Apps/Foo.tkape");
        Assert.StartsWith(root, path, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith("Foo.tkape", path, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ExtractToDirectory_RejectsEscapingEntry()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "safezip_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        var zipPath = Path.Combine(tmp, "evil.zip");
        var dest = Path.Combine(tmp, "out");
        Directory.CreateDirectory(dest);

        try
        {
            using (var zs = File.Create(zipPath))
            using (var archive = new ZipArchive(zs, ZipArchiveMode.Create))
            {
                var entry = archive.CreateEntry("../escaped.txt");
                using var w = new StreamWriter(entry.Open());
                w.Write("pwn");
            }

            Assert.Throws<InvalidDataException>(() =>
                SafeZip.ExtractToDirectory(zipPath, dest));
            Assert.False(File.Exists(Path.Combine(tmp, "escaped.txt")));
        }
        finally
        {
            try { Directory.Delete(tmp, true); } catch { /* ignore */ }
        }
    }
}
