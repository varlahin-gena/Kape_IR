using System.IO.Compression;
using System.Text;
using KapeIR.Core.Models;
using KapeIR.Core.Services;
using KapeIR.Builder.Tests.Fixtures;

namespace KapeIR.Builder.Tests;

public class StandaloneExeBuilderTests
{
    [Fact]
    public void Build_WritesKapepackFooter_ReadableByMagic()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "kapepack_footer_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var stub = Path.Combine(tmp, "stub.exe");
            // Minimal PE-like file with WINDOWS GUI subsystem so IsGuiStub passes.
            WriteMinimalGuiPe(stub);
            var zip = Path.Combine(tmp, "payload.zip");
            using (var zs = File.Create(zip))
            using (var archive = new ZipArchive(zs, ZipArchiveMode.Create))
            {
                var e = archive.CreateEntry("hello.txt");
                using var w = new StreamWriter(e.Open());
                w.Write("hi");
            }

            var outExe = Path.Combine(tmp, "pack.exe");
            StandaloneExeBuilder.Build(stub, zip, outExe);
            Assert.True(File.Exists(outExe));

            var fi = new FileInfo(outExe);
            using var fs = File.OpenRead(outExe);
            fs.Seek(-24, SeekOrigin.End);
            using var br = new BinaryReader(fs, Encoding.ASCII, leaveOpen: true);
            var zipStart = br.ReadInt64();
            var zipLen = br.ReadInt64();
            var magic = Encoding.ASCII.GetString(br.ReadBytes(8));
            Assert.Equal(StandaloneExeBuilder.Magic, magic);
            Assert.Equal(new FileInfo(stub).Length, zipStart);
            Assert.Equal(new FileInfo(zip).Length, zipLen);
            Assert.Equal(zipStart + zipLen + 24, fi.Length);
        }
        finally
        {
            try { Directory.Delete(tmp, true); } catch { /* ignore */ }
        }
    }

    private static void WriteMinimalGuiPe(string path)
    {
        // Tiny fake PE: MZ + e_lfanew + PE + OptionalHeader.Subsystem=2 (GUI)
        using var fs = File.Create(path);
        using var bw = new BinaryWriter(fs);
        var peOffset = 0x80;
        bw.Write((ushort)0x5A4D); // MZ
        bw.Write(new byte[0x3A]);
        bw.Write(peOffset);
        while (fs.Position < peOffset)
            bw.Write((byte)0);
        bw.Write(0x00004550); // PE\0\0
        bw.Write(new byte[20]); // COFF
        // Optional header start
        bw.Write((ushort)0x20B); // PE32+ magic
        bw.Write(new byte[66]); // pad to subsystem at +68 from optional start
        // We wrote 2 + 66 = 68 bytes into optional header → subsystem here
        bw.Write((ushort)2); // IMAGE_SUBSYSTEM_WINDOWS_GUI
        bw.Write(new byte[256]); // padding so file isn't tiny/odd
    }
}

public class PackageExporterTests
{
    [Fact]
    public void Export_CreatesCompoundAndPackageJson_WithoutKapeTree()
    {
        using var fx = FakeKapeRoot.Create(FakeKapeProfile.Minimal);
        var outDir = Path.Combine(Path.GetTempPath(), "kape_export_out_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outDir);
        try
        {
            Assert.Contains(fx.Catalog.Targets, t => t.Name == FakeKapeRoot.DemoLeafName);

            var pkg = new PackageDefinition
            {
                Name = "UnitPack",
                Targets =
                {
                    new SelectionEntry
                    {
                        Name = FakeKapeRoot.DemoLeafName,
                        Category = "Apps",
                        Path = FakeKapeRoot.DemoLeafName + ".tkape"
                    }
                }
            };
            var exporter = new PackageExporter(fx.Catalog);
            var result = exporter.Export(pkg, outDir, ExportOptions.FolderOnly);

            Assert.True(Directory.Exists(result.PackageDir));
            Assert.True(File.Exists(result.TargetFile));
            Assert.EndsWith("UnitPack.tkape", result.TargetFile, StringComparison.OrdinalIgnoreCase);
            Assert.True(File.Exists(result.ManifestFile));
            Assert.True(File.Exists(Path.Combine(result.PackageDir, "Targets", "Apps", FakeKapeRoot.DemoLeafName + ".tkape")));
            Assert.Contains(FakeKapeRoot.DemoLeafName, File.ReadAllText(result.TargetFile));
            Assert.Equal("UnitPack", pkg.TargetCompoundName);
        }
        finally
        {
            try { Directory.Delete(outDir, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void Export_RefusesOverwrite_WithoutFlag()
    {
        using var fx = FakeKapeRoot.Create(FakeKapeProfile.Minimal);
        var outDir = Path.Combine(Path.GetTempPath(), "kape_export_ow_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outDir);
        try
        {
            var pkg = new PackageDefinition
            {
                Name = "OwPack",
                Targets =
                {
                    new SelectionEntry
                    {
                        Name = FakeKapeRoot.DemoLeafName,
                        Category = "Apps",
                        Path = FakeKapeRoot.DemoLeafName + ".tkape"
                    }
                }
            };
            var exporter = new PackageExporter(fx.Catalog);
            var first = exporter.Export(pkg, outDir, ExportOptions.FolderOnly);
            Assert.True(Directory.Exists(first.PackageDir));
            File.WriteAllText(Path.Combine(first.PackageDir, "marker.txt"), "keep");

            var ex = Assert.Throws<IOException>(() =>
                exporter.Export(pkg, outDir, ExportOptions.FolderOnly with { OverwriteExisting = false }));
            Assert.Contains("уже существует", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.True(File.Exists(Path.Combine(first.PackageDir, "marker.txt")));

            var second = exporter.Export(pkg, outDir, ExportOptions.FolderOnly with { OverwriteExisting = true });
            Assert.False(File.Exists(Path.Combine(second.PackageDir, "marker.txt")));
        }
        finally
        {
            try { Directory.Delete(outDir, true); } catch { /* ignore */ }
        }
    }
}
