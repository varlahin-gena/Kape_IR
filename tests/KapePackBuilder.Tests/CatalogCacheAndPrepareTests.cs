using System.IO.Compression;
using System.Text;
using KapePack.Core.Services;

namespace KapePackBuilder.Tests;

public class CatalogCacheTests
{
    [Fact]
    public void Refresh_SecondPass_UsesFileCache()
    {
        KapeCatalog.ClearFileCache();
        var root = Path.Combine(Path.GetTempPath(), "kape_cache_" + Guid.NewGuid().ToString("N"));
        var apps = Path.Combine(root, "Targets", "Apps");
        Directory.CreateDirectory(apps);
        Directory.CreateDirectory(Path.Combine(root, "Modules"));
        File.WriteAllText(Path.Combine(apps, "Leaf.tkape"), """
Description: demo
Author: test
Version: 1.0
Id: 11111111-1111-1111-1111-111111111111
RecreateDirectories: true
Targets:
    -
        Name: Demo
        Category: Apps
        Path: C:\Windows\
        FileMask: '*.log'
""");
        try
        {
            var cat = new KapeCatalog(root);
            cat.Refresh();
            Assert.True(cat.LastRefreshStats.CacheMisses >= 1);
            Assert.Equal(0, cat.LastRefreshStats.CacheHits);

            cat.Refresh();
            Assert.True(cat.LastRefreshStats.CacheHits >= 1);
            Assert.Equal(0, cat.LastRefreshStats.CacheMisses);
            Assert.Contains(cat.Targets, t => t.Name == "Leaf");
        }
        finally
        {
            KapeCatalog.ClearFileCache();
            try { Directory.Delete(root, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void Refresh_AfterFileChange_IsCacheMiss()
    {
        KapeCatalog.ClearFileCache();
        var root = Path.Combine(Path.GetTempPath(), "kape_cache2_" + Guid.NewGuid().ToString("N"));
        var apps = Path.Combine(root, "Targets", "Apps");
        Directory.CreateDirectory(apps);
        Directory.CreateDirectory(Path.Combine(root, "Modules"));
        var leaf = Path.Combine(apps, "Leaf.tkape");
        File.WriteAllText(leaf, "Description: v1\nAuthor: a\nVersion: 1\nId: 11111111-1111-1111-1111-111111111111\nTargets: []\n");
        try
        {
            var cat = new KapeCatalog(root);
            cat.Refresh();
            File.WriteAllText(leaf, "Description: v2-changed\nAuthor: a\nVersion: 2\nId: 11111111-1111-1111-1111-111111111111\nTargets: []\n");
            // Ensure mtime/size differs on coarse FS.
            File.SetLastWriteTimeUtc(leaf, DateTime.UtcNow.AddSeconds(2));
            cat.Refresh();
            Assert.True(cat.LastRefreshStats.CacheMisses >= 1);
            Assert.Equal("v2-changed", cat.FindTarget("Leaf")!.Description);
        }
        finally
        {
            KapeCatalog.ClearFileCache();
            try { Directory.Delete(root, true); } catch { /* ignore */ }
        }
    }
}

public class CollectPackPrepareTests
{
    [Fact]
    public void Prepare_PlainExe_ReturnsExit2()
    {
        var dir = Path.Combine(Path.GetTempPath(), "kapeprep_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var exe = Path.Combine(dir, "plain.exe");
        File.WriteAllBytes(exe, new byte[] { 0x4D, 0x5A, 0, 0, 0, 0, 0, 0 });
        try
        {
            var r = CollectPackPrepare.Prepare(exe, tsourceOverride: "C:", requireTsource: true);
            Assert.Equal(2, r.ExitCode);
            Assert.Contains("KAPEPACK", r.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void Prepare_PackWithoutKapeExe_ReturnsExit3()
    {
        var dir = Path.Combine(Path.GetTempPath(), "kapeprep2_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var exe = Path.Combine(dir, "CollectPack.exe");
        BuildKapepack(exe, packageJson: """{"name":"P","target_compound":"P","tsource":"C:"}""", includeKape: false);
        try
        {
            var r = CollectPackPrepare.Prepare(exe, tsourceOverride: "C:", requireTsource: true);
            Assert.Equal(3, r.ExitCode);
            Assert.Contains("kape.exe", r.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void Prepare_PackWithoutTarget_ReturnsExit2()
    {
        var dir = Path.Combine(Path.GetTempPath(), "kapeprep3_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var exe = Path.Combine(dir, "CollectPack.exe");
        BuildKapepack(exe, packageJson: """{"name":"P","tsource":"C:"}""", includeKape: true);
        try
        {
            var r = CollectPackPrepare.Prepare(exe, tsourceOverride: "C:", requireTsource: true);
            Assert.Equal(2, r.ExitCode);
            Assert.Contains("target_compound", r.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void Prepare_MissingTsource_ReturnsExit2()
    {
        var dir = Path.Combine(Path.GetTempPath(), "kapeprep4_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var exe = Path.Combine(dir, "CollectPack.exe");
        BuildKapepack(exe, packageJson: """{"name":"P","target_compound":"P","tsource":""}""", includeKape: true);
        try
        {
            var r = CollectPackPrepare.Prepare(exe, tsourceOverride: null, requireTsource: true);
            Assert.Equal(2, r.ExitCode);
            Assert.Contains("--tsource", r.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void Prepare_ValidPack_ReturnsExit0()
    {
        var dir = Path.Combine(Path.GetTempPath(), "kapeprep5_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var exe = Path.Combine(dir, "CollectPack.exe");
        BuildKapepack(exe, packageJson: """{"name":"P","target_compound":"P","tsource":"D:"}""", includeKape: true);
        try
        {
            var r = CollectPackPrepare.Prepare(exe, tsourceOverride: "C:", requireTsource: true);
            Assert.Equal(0, r.ExitCode);
            Assert.NotNull(r.PackageDir);
            Assert.NotNull(r.KapeExe);
            Assert.NotNull(r.Manifest);
            Assert.Equal("C:", r.Manifest!.Tsource);
            Assert.Equal("P", r.Manifest.Target);
            Assert.True(Directory.Exists(r.PackageDir));
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* ignore */ }
        }
    }

    private static void BuildKapepack(string outExe, string packageJson, bool includeKape)
    {
        var tmp = Path.Combine(Path.GetTempPath(), "kapebuild_" + Guid.NewGuid().ToString("N"));
        var content = Path.Combine(tmp, "content");
        Directory.CreateDirectory(content);
        try
        {
            File.WriteAllText(Path.Combine(content, "package.json"), packageJson);
            if (includeKape)
                File.WriteAllBytes(Path.Combine(content, "kape.exe"), Encoding.ASCII.GetBytes("fake-kape"));

            var zipPath = Path.Combine(tmp, "payload.zip");
            ZipFile.CreateFromDirectory(content, zipPath, CompressionLevel.Fastest, includeBaseDirectory: false);

            var stub = Path.Combine(tmp, "stub.exe");
            WriteMinimalGuiPe(stub);
            StandaloneExeBuilder.Build(stub, zipPath, outExe);
        }
        finally
        {
            try { Directory.Delete(tmp, true); } catch { /* ignore */ }
        }
    }

    private static void WriteMinimalGuiPe(string path)
    {
        using var fs = File.Create(path);
        using var bw = new BinaryWriter(fs);
        var peOffset = 0x80;
        bw.Write((ushort)0x5A4D);
        bw.Write(new byte[0x3A]);
        bw.Write(peOffset);
        while (fs.Position < peOffset) bw.Write((byte)0);
        bw.Write(0x00004550);
        bw.Write(new byte[20]);
        bw.Write((ushort)0x20B);
        bw.Write(new byte[66]);
        bw.Write((ushort)2);
        bw.Write(new byte[256]);
    }
}

public class PackageFormMapperTests
{
    [Fact]
    public void RoundTrip_PreservesTwoPhase()
    {
        var pkg = new KapePack.Core.Models.PackageDefinition
        {
            Name = "IR",
            CollectionMode = KapePack.Core.Models.IrCollectionMode.TwoPhase,
            CaseId = "C-1",
            Phase1ModuleName = "VolatileFirst"
        };
        var form = PackageFormMapper.FromPackage(pkg);
        Assert.True(form.TwoPhase);
        var again = new KapePack.Core.Models.PackageDefinition();
        PackageFormMapper.ApplyToPackage(again, form with { Name = "IR2", TwoPhase = true, CaseId = "C-2" });
        Assert.Equal("IR2", again.Name);
        Assert.True(again.IsTwoPhase);
        Assert.Equal("C-2", again.CaseId);
        Assert.Equal(KapePack.Core.Models.PackageDefinition.DefaultPhase1Module, again.Phase1ModuleName);
    }
}
