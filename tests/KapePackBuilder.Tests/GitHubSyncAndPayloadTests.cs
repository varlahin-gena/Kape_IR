using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using KapePackBuilder.Models;
using KapePackBuilder.Services;
using KapePackShared;

namespace KapePackBuilder.Tests;

public class GitHubSyncMockTests
{
    [Fact]
    public async Task SyncAsync_AddsAndSkipsEqual_WithMockedZip()
    {
        var root = Path.Combine(Path.GetTempPath(), "kape_sync_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "Targets", "Apps"));
        Directory.CreateDirectory(Path.Combine(root, "Modules", "Compound"));
        File.WriteAllText(Path.Combine(root, "Targets", "Apps", "LocalOnly.tkape"), "local-only");
        File.WriteAllText(Path.Combine(root, "Targets", "Apps", "Same.tkape"), "same-bytes");
        File.WriteAllText(Path.Combine(root, "Targets", "Apps", "Old.tkape"), "old");

        var zipBytes = BuildKapeFilesZip(entries: new Dictionary<string, string>
        {
            ["Targets/Apps/Same.tkape"] = "same-bytes",
            ["Targets/Apps/Old.tkape"] = "new",
            ["Targets/Apps/NewOne.tkape"] = "brand-new",
            ["Modules/Compound/M1.mkape"] = "mod"
        });

        using var handler = new FixedBytesHandler(zipBytes);
        var result = await GitHubKapeFilesSync.SyncAsync(root, httpHandler: handler);

        Assert.True(result.Ok, result.Message);
        Assert.True(File.Exists(Path.Combine(root, "Targets", "Apps", "LocalOnly.tkape")));
        Assert.Equal("local-only", File.ReadAllText(Path.Combine(root, "Targets", "Apps", "LocalOnly.tkape")));
        Assert.Equal("new", File.ReadAllText(Path.Combine(root, "Targets", "Apps", "Old.tkape")));
        Assert.Equal("brand-new", File.ReadAllText(Path.Combine(root, "Targets", "Apps", "NewOne.tkape")));
        Assert.Equal("same-bytes", File.ReadAllText(Path.Combine(root, "Targets", "Apps", "Same.tkape")));
        Assert.Equal("mod", File.ReadAllText(Path.Combine(root, "Modules", "Compound", "M1.mkape")));
        Assert.True(result.TargetsAdded >= 1, result.Message);
        Assert.True(result.TargetsUpdated >= 1, result.Message);
        Assert.True(result.ModulesAdded >= 1, result.Message);
        Assert.False(string.IsNullOrEmpty(result.ZipSha256));
        Assert.True(File.Exists(Path.Combine(root, "PackBuilder", GitHubKapeFilesSync.LastZipSha256FileName)));
        Assert.Equal(result.ZipSha256, GitHubKapeFilesSync.ReadLastZipSha256(root));
    }

    [Fact]
    public async Task SyncAsync_DryRun_DoesNotModify_ButReportsWouldChange()
    {
        var root = Path.Combine(Path.GetTempPath(), "kape_sync_dry_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "Targets", "Apps"));
        Directory.CreateDirectory(Path.Combine(root, "Modules", "Compound"));
        File.WriteAllText(Path.Combine(root, "Targets", "Apps", "Old.tkape"), "old");
        File.WriteAllText(Path.Combine(root, "Targets", "Apps", "Same.tkape"), "same-bytes");

        var zipBytes = BuildKapeFilesZip(new Dictionary<string, string>
        {
            ["Targets/Apps/Same.tkape"] = "same-bytes",
            ["Targets/Apps/Old.tkape"] = "new",
            ["Targets/Apps/NewOne.tkape"] = "brand-new",
            ["Modules/Compound/M1.mkape"] = "mod"
        });

        using var handler = new FixedBytesHandler(zipBytes);
        var result = await GitHubKapeFilesSync.SyncAsync(
            root,
            httpHandler: handler,
            options: new SyncOptions { DryRun = true });

        Assert.True(result.Ok, result.Message);
        Assert.True(result.IsDryRun);
        Assert.Equal("old", File.ReadAllText(Path.Combine(root, "Targets", "Apps", "Old.tkape")));
        Assert.False(File.Exists(Path.Combine(root, "Targets", "Apps", "NewOne.tkape")));
        Assert.False(File.Exists(Path.Combine(root, "Modules", "Compound", "M1.mkape")));
        Assert.False(Directory.Exists(Path.Combine(root, "PackBuilder", "sync_backup")));
        Assert.False(File.Exists(Path.Combine(root, "PackBuilder", GitHubKapeFilesSync.LastZipSha256FileName)));
        Assert.True(result.TargetsAdded >= 1);
        Assert.True(result.TargetsUpdated >= 1);
        Assert.True(result.ModulesAdded >= 1);
        Assert.Contains(result.AddedSamples, s => s.Contains("NewOne", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.UpdatedSamples, s => s.Contains("Old", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task SyncAsync_Backup_CreatesSyncBackupAndUpdatesFile()
    {
        var root = Path.Combine(Path.GetTempPath(), "kape_sync_bak_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "Targets", "Apps"));
        Directory.CreateDirectory(Path.Combine(root, "Modules", "Compound"));
        File.WriteAllText(Path.Combine(root, "Targets", "Apps", "Old.tkape"), "old-content");

        var zipBytes = BuildKapeFilesZip(new Dictionary<string, string>
        {
            ["Targets/Apps/Old.tkape"] = "new-content",
            ["Modules/Compound/M1.mkape"] = "mod"
        });

        using var handler = new FixedBytesHandler(zipBytes);
        var result = await GitHubKapeFilesSync.SyncAsync(
            root,
            httpHandler: handler,
            options: new SyncOptions { BackupBeforeOverwrite = true });

        Assert.True(result.Ok, result.Message);
        Assert.Equal("new-content", File.ReadAllText(Path.Combine(root, "Targets", "Apps", "Old.tkape")));
        Assert.False(string.IsNullOrEmpty(result.BackupDir));
        Assert.True(Directory.Exists(result.BackupDir));
        var backed = Directory.EnumerateFiles(result.BackupDir!, "Old.tkape", SearchOption.AllDirectories).FirstOrDefault();
        Assert.NotNull(backed);
        Assert.Equal("old-content", File.ReadAllText(backed!));
    }

    [Fact]
    public async Task SyncAsync_ExpectedZipSha256Mismatch_FailsBeforeExtract()
    {
        var root = Path.Combine(Path.GetTempPath(), "kape_sync_sha_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "Targets", "Apps"));
        Directory.CreateDirectory(Path.Combine(root, "Modules"));
        File.WriteAllText(Path.Combine(root, "Targets", "Apps", "Keep.tkape"), "keep-me");

        var zipBytes = BuildKapeFilesZip(new Dictionary<string, string>
        {
            ["Targets/Apps/Keep.tkape"] = "overwritten",
            ["Modules/b.mkape"] = "y"
        });

        using var handler = new FixedBytesHandler(zipBytes);
        var result = await GitHubKapeFilesSync.SyncAsync(
            root,
            httpHandler: handler,
            options: new SyncOptions { ExpectedZipSha256 = "deadbeef" + new string('0', 56) });

        Assert.False(result.Ok);
        Assert.Contains("не совпадает", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(string.IsNullOrEmpty(result.ZipSha256));
        Assert.Equal("keep-me", File.ReadAllText(Path.Combine(root, "Targets", "Apps", "Keep.tkape")));
        Assert.False(File.Exists(Path.Combine(root, "PackBuilder", GitHubKapeFilesSync.LastZipSha256FileName)));
    }

    [Fact]
    public async Task SyncAsync_CancelDuringDownload_Throws()
    {
        var root = Path.Combine(Path.GetTempPath(), "kape_sync_c_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        using var handler = new FixedBytesHandler(BuildKapeFilesZip(new Dictionary<string, string>
        {
            ["Targets/a.tkape"] = "x",
            ["Modules/b.mkape"] = "y"
        }));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await GitHubKapeFilesSync.SyncAsync(root, ct: cts.Token, httpHandler: handler));
    }

    [Fact]
    public async Task SyncAsync_HttpError_ReturnsNotOk()
    {
        var root = Path.Combine(Path.GetTempPath(), "kape_sync_e_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        using var handler = new StatusHandler(HttpStatusCode.NotFound);

        var result = await GitHubKapeFilesSync.SyncAsync(root, httpHandler: handler);
        Assert.False(result.Ok);
        Assert.Contains("Ошибка загрузки", result.Message);
    }

    private static byte[] BuildKapeFilesZip(Dictionary<string, string> entries)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (rel, content) in entries)
            {
                var e = zip.CreateEntry("KapeFiles-master/" + rel.Replace('\\', '/'));
                using var w = new StreamWriter(e.Open(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                w.Write(content);
            }
        }
        return ms.ToArray();
    }

    private sealed class FixedBytesHandler : HttpMessageHandler
    {
        private readonly byte[] _bytes;
        public FixedBytesHandler(byte[] bytes) => _bytes = bytes;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var resp = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(_bytes)
            };
            resp.Content.Headers.ContentLength = _bytes.Length;
            return Task.FromResult(resp);
        }
    }

    private sealed class StatusHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _code;
        public StatusHandler(HttpStatusCode code) => _code = code;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(_code));
    }
}

public class PackageSessionStoreTests
{
    [Fact]
    public void SaveLoad_RoundTripsPackageDefinition()
    {
        var root = Path.Combine(Path.GetTempPath(), "kape_sess_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var pkg = new PackageDefinition
            {
                Name = "SessionPack",
                Description = "desc",
                Author = "author",
                Version = "2.1",
                Tsource = "D:",
                ZipOutput = false,
                Flush = true,
                Vss = true,
                Notes = "note",
                Targets =
                {
                    new SelectionEntry { Name = "T1", Category = "Apps", Path = "T1.tkape", Comments = "c" }
                },
                Modules =
                {
                    new SelectionEntry { Name = "M1", Category = "Compound", Path = "M1.mkape" }
                }
            };

            PackageSessionStore.Save(root, "My Session!", pkg);
            Assert.Contains("My_Session", PackageSessionStore.ListSessionNames(root));

            var loaded = PackageSessionStore.Load(root, "My Session!");
            Assert.Equal("SessionPack", loaded.Name);
            Assert.Equal("desc", loaded.Description);
            Assert.Equal("author", loaded.Author);
            Assert.Equal("2.1", loaded.Version);
            Assert.Equal("D:", loaded.Tsource);
            Assert.False(loaded.ZipOutput);
            Assert.True(loaded.Flush);
            Assert.True(loaded.Vss);
            Assert.Equal("note", loaded.Notes);
            Assert.Single(loaded.Targets);
            Assert.Equal("T1", loaded.Targets[0].Name);
            Assert.Equal("Apps", loaded.Targets[0].Category);
            Assert.Single(loaded.Modules);

            Assert.True(PackageSessionStore.Delete(root, "My Session!"));
            Assert.Empty(PackageSessionStore.ListSessionNames(root));
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { /* ignore */ }
        }
    }
}

public class FileHashTests
{
    [Fact]
    public void WriteSha256Sidecar_MatchesFileContent()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "kape_hash_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var file = Path.Combine(tmp, "pack.exe");
            File.WriteAllBytes(file, Encoding.UTF8.GetBytes("hello-hash"));
            var sidecar = FileHash.WriteSha256Sidecar(file);
            Assert.True(File.Exists(sidecar));
            Assert.EndsWith(".sha256", sidecar);
            var text = File.ReadAllText(sidecar);
            var expected = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("hello-hash"))).ToLowerInvariant();
            Assert.StartsWith(expected + "  pack.exe", text);
            Assert.Equal(expected, FileHash.Sha256Hex(file));
        }
        finally
        {
            try { Directory.Delete(tmp, true); } catch { /* ignore */ }
        }
    }
}

public class KapepackPayloadTests
{
    [Fact]
    public void Extract_RoundTripsPayload_AndBlocksZipSlip()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "kapepack_ext_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var stub = Path.Combine(tmp, "stub.exe");
            WriteMinimalGuiPe(stub);

            var goodZip = Path.Combine(tmp, "good.zip");
            using (var zs = File.Create(goodZip))
            using (var archive = new ZipArchive(zs, ZipArchiveMode.Create))
            {
                var e = archive.CreateEntry("hello.txt");
                using var w = new StreamWriter(e.Open());
                w.Write("payload-ok");
            }

            var outExe = Path.Combine(tmp, "pack.exe");
            StandaloneExeBuilder.Build(stub, goodZip, outExe);
            Assert.True(KapepackPayload.TryRead(outExe, out var start, out var len));

            var extractDir = Path.Combine(tmp, "out");
            KapepackPayload.Extract(outExe, start, len, extractDir);
            Assert.Equal("payload-ok", File.ReadAllText(Path.Combine(extractDir, "hello.txt")));

            // Zip-slip via SafeZip directly (same path Extract uses)
            var evilZip = Path.Combine(tmp, "evil.zip");
            using (var zs = File.Create(evilZip))
            using (var archive = new ZipArchive(zs, ZipArchiveMode.Create))
            {
                archive.CreateEntry("../evil.txt");
            }

            var evilOut = Path.Combine(tmp, "evil_out");
            Directory.CreateDirectory(evilOut);
            Assert.Throws<InvalidDataException>(() =>
                SafeZip.ExtractToDirectory(evilZip, evilOut));
        }
        finally
        {
            try { Directory.Delete(tmp, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void Extract_RespectsCancellation()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "kapepack_cancel_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var stub = Path.Combine(tmp, "stub.exe");
            WriteMinimalGuiPe(stub);
            var zip = Path.Combine(tmp, "p.zip");
            using (var zs = File.Create(zip))
            using (var archive = new ZipArchive(zs, ZipArchiveMode.Create))
            {
                var e = archive.CreateEntry("a.txt");
                using var w = new StreamWriter(e.Open());
                w.Write(new string('x', 1024));
            }
            var outExe = Path.Combine(tmp, "pack.exe");
            StandaloneExeBuilder.Build(stub, zip, outExe);
            Assert.True(KapepackPayload.TryRead(outExe, out var start, out var len));

            using var cts = new CancellationTokenSource();
            cts.Cancel();
            Assert.ThrowsAny<OperationCanceledException>(() =>
                KapepackPayload.Extract(outExe, start, len, Path.Combine(tmp, "out"), ct: cts.Token));
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
        while (fs.Position < peOffset)
            bw.Write((byte)0);
        bw.Write(0x00004550);
        bw.Write(new byte[20]);
        bw.Write((ushort)0x20B);
        bw.Write(new byte[66]);
        bw.Write((ushort)2);
        bw.Write(new byte[256]);
    }
}
