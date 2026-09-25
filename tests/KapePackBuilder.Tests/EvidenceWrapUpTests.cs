using System.Text;
using KapePack.Core.Services;

namespace KapePackBuilder.Tests;

public class EvidenceWrapUpTests
{
    [Fact]
    public void WriteManifest_SkipsMemoryDumpExtensions()
    {
        var root = Path.Combine(Path.GetTempPath(), "kape_evid_" + Guid.NewGuid().ToString("N"));
        var phase1 = Path.Combine(root, "Phase1_Volatile");
        Directory.CreateDirectory(phase1);
        try
        {
            File.WriteAllText(Path.Combine(root, "note.txt"), "small");
            File.WriteAllBytes(Path.Combine(phase1, "memory.raw"), new byte[1024]);
            File.WriteAllText(Path.Combine(phase1, "memory_hash.sha256"), "abc  memory.raw\n");

            var path = EvidenceWrapUp.WriteManifest(root);
            var text = File.ReadAllText(path);
            Assert.Contains("note.txt", text);
            Assert.Contains("memory_hash.sha256", text);
            Assert.Contains("# skipped-dump", text);
            Assert.Contains("memory.raw", text);
            Assert.False(System.Text.RegularExpressions.Regex.IsMatch(
                text,
                @"^[a-f0-9]{64}\s+.*memory\.raw",
                System.Text.RegularExpressions.RegexOptions.Multiline));
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void WriteAll_IncludesHostTimeZoneInLogAndCoC()
    {
        var dir = Path.Combine(Path.GetTempPath(), "kape_tz_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            EvidenceWrapUp.WriteAll(new EvidenceWrapUp.Context(
                dir,
                "IR-TZ",
                "PkgTz",
                "two_phase",
                Path.Combine(dir, "missing.exe"),
                new[] { "1: exit=0" },
                DateTimeOffset.UtcNow.AddMinutes(-1),
                DateTimeOffset.UtcNow));

            var log = File.ReadAllText(Path.Combine(dir, "collection_log.txt"));
            var coc = File.ReadAllText(Path.Combine(dir, "chain_of_custody.txt"));
            var tz = TimeZoneInfo.Local;

            foreach (var text in new[] { log, coc })
            {
                Assert.Contains($"TimeZone Id: {tz.Id}", text);
                Assert.Contains("TimeZone Display:", text);
                Assert.Contains(tz.DisplayName, text);
                Assert.Contains("UTC offset at collection:", text);
                Assert.Contains("Daylight saving:", text);
            }

            Assert.True(File.Exists(Path.Combine(dir, EvidenceWrapUp.FindingsTemplateFileName)));
            Assert.Contains(EvidenceWrapUp.FindingsTemplateFileName, log);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void PostCollectionChecklistRu_HasCoreItems()
    {
        var text = string.Join('\n', EvidenceWrapUp.PostCollectionChecklistRu);
        Assert.Contains("collection_log.txt", text);
        Assert.Contains("TimeZone", text);
        Assert.Contains("evidence_manifest.sha256", text);
        Assert.Contains("findings_template.csv", text);
        Assert.True(EvidenceWrapUp.PostCollectionChecklistRu.Count >= 6);
    }

    [Fact]
    public void WriteFindingsTemplate_HasHeaderAndExampleRows()
    {
        var dir = Path.Combine(Path.GetTempPath(), "kape_find_" + Guid.NewGuid().ToString("N"));
        try
        {
            var path = EvidenceWrapUp.WriteFindingsTemplate(dir);
            var text = File.ReadAllText(path);
            Assert.StartsWith("Time,Host,Artifact,Finding,Confidence,Evidence path", text);
            Assert.Contains("EXAMPLE-HOST", text);
            Assert.Contains("unverified", text);
            Assert.Contains("Prefetch", text);
            Assert.Equal(4, text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void AppendHostTimeZone_FormatsOffsetWithSign()
    {
        var sb = new StringBuilder();
        EvidenceWrapUp.AppendHostTimeZone(sb, DateTimeOffset.UtcNow);
        var text = sb.ToString();
        Assert.Matches(@"UTC offset at collection: [+-]\d{2}:\d{2}", text);
        Assert.Contains($"TimeZone Id: {TimeZoneInfo.Local.Id}", text);
    }
}
