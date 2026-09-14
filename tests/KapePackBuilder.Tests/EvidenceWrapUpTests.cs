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
}
