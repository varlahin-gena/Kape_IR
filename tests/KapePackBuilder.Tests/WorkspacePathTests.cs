using KapePack.Core.Services;

namespace KapePackBuilder.Tests;

public class KapeRootPathsTests
{
    [Fact]
    public void FindKapeExeInRoot_IgnoresNestedExportsCopy()
    {
        var root = Path.Combine(Path.GetTempPath(), "kape_root_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(Path.Combine(root, "Targets"));
        var nested = Path.Combine(root, "PackBuilder", "exports", "SomePack");
        Directory.CreateDirectory(nested);
        File.WriteAllBytes(Path.Combine(nested, "kape.exe"), new byte[] { 1, 2, 3 });
        try
        {
            Assert.Null(KapeRootPaths.FindKapeExeInRoot(root));

            var real = Path.Combine(root, "kape.exe");
            File.WriteAllBytes(real, new byte[] { 9 });
            Assert.Equal(Path.GetFullPath(real), Path.GetFullPath(KapeRootPaths.FindKapeExeInRoot(root)!));
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void KapeFilesZipCachePath_IsUnderSelectedRoot()
    {
        var root = @"D:\KAPE_Install";
        var cache = KapeRootPaths.KapeFilesZipCachePath(root);
        Assert.StartsWith(Path.GetFullPath(root), Path.GetFullPath(cache), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("PackBuilder", cache, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SameRoot_NormalizesSeparators()
    {
        Assert.True(KapeRootPaths.SameRoot(@"C:\Kape", @"C:\Kape\"));
        Assert.False(KapeRootPaths.SameRoot(@"C:\KapeA", @"C:\KapeB"));
    }
}

public class CollectPackPathsTests
{
    [Fact]
    public void ResolvePackageDirectory_UsesLaunchDirAndExeName()
    {
        var launch = Path.Combine(Path.GetTempPath(), "usb_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(launch);
        try
        {
            var exe = Path.Combine(launch, "MyCollect.exe");
            var pkg = CollectPackPaths.ResolvePackageDirectory(launch, exe);
            Assert.Equal(Path.Combine(Path.GetFullPath(launch), "MyCollect"), Path.GetFullPath(pkg));
        }
        finally
        {
            try { Directory.Delete(launch, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void CreateSiblingTempFile_StaysBesideAnchor()
    {
        var launch = Path.Combine(Path.GetTempPath(), "usb2_" + Guid.NewGuid().ToString("N"));
        var outDir = Path.Combine(launch, "Pack");
        Directory.CreateDirectory(outDir);
        try
        {
            var tmp = CollectPackPaths.CreateSiblingTempFile(outDir, ".kapepack_extract_", ".zip");
            Assert.Equal(Path.GetFullPath(launch), Path.GetFullPath(Path.GetDirectoryName(tmp)!));
            Assert.EndsWith(".zip", tmp, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { Directory.Delete(launch, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void ResolveLaunchDirectory_FromProcessPath()
    {
        var launch = Path.Combine(Path.GetTempPath(), "usb3_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(launch);
        var exe = Path.Combine(launch, "CollectPack.exe");
        File.WriteAllText(exe, "x");
        try
        {
            Assert.Equal(Path.GetFullPath(launch), CollectPackPaths.ResolveLaunchDirectory(exe));
        }
        finally
        {
            try { Directory.Delete(launch, true); } catch { /* ignore */ }
        }
    }
}
