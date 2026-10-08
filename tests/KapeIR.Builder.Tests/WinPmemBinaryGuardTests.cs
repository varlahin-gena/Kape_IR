using KapeIR.Core.Services;

namespace KapeIR.Builder.Tests;

public sealed class WinPmemBinaryGuardTests
{
    [Fact]
    public void Inspect_Missing_WhenFileAbsent()
    {
        var path = Path.Combine(Path.GetTempPath(), "kapeir-no-winpmem-" + Guid.NewGuid().ToString("N"), "winpmem.exe");
        var check = WinPmemBinaryGuard.Inspect(path);
        Assert.Equal(WinPmemBinaryStatus.Missing, check.Status);
        Assert.Null(check.LengthBytes);
        Assert.NotNull(WinPmemBinaryGuard.FormatBuilderWarning(check));
        Assert.Equal(3, WinPmemBinaryGuard.FormatRunnerLogs(check).Count);
        Assert.Equal(2, WinPmemBinaryGuard.FormatPreflightNotes(check).Count);
    }

    [Fact]
    public void Inspect_SuspectMini_WhenUnderThreshold()
    {
        var dir = Path.Combine(Path.GetTempPath(), "kapeir-mini-winpmem-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, WinPmemBinaryGuard.FileName);
        try
        {
            File.WriteAllBytes(path, new byte[1024]);
            var check = WinPmemBinaryGuard.Inspect(path);
            Assert.Equal(WinPmemBinaryStatus.SuspectMini, check.Status);
            Assert.Equal(1024, check.LengthBytes);
            Assert.Contains("mini", WinPmemBinaryGuard.FormatBuilderWarning(check)!, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(
                WinPmemBinaryGuard.SignedArtifactHint,
                WinPmemBinaryGuard.FormatRunnerLogs(check)[2],
                StringComparison.Ordinal);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void Inspect_Ok_WhenAtOrAboveThreshold()
    {
        var dir = Path.Combine(Path.GetTempPath(), "kapeir-ok-winpmem-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, WinPmemBinaryGuard.FileName);
        try
        {
            // Exactly at threshold is OK (only strictly smaller is SuspectMini).
            using (var fs = File.Create(path))
                fs.SetLength(WinPmemBinaryGuard.SuspectMiniMaxBytes);

            var check = WinPmemBinaryGuard.Inspect(path);
            Assert.Equal(WinPmemBinaryStatus.Ok, check.Status);
            Assert.Null(WinPmemBinaryGuard.FormatBuilderWarning(check));
            Assert.Empty(WinPmemBinaryGuard.FormatRunnerLogs(check));
            Assert.Empty(WinPmemBinaryGuard.FormatPreflightNotes(check));
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void ResolveUnderRoot_JoinsModulesBin()
    {
        var root = Path.Combine(Path.GetTempPath(), "kape-root-" + Guid.NewGuid().ToString("N"));
        var expected = Path.Combine(root, "Modules", "bin", WinPmemBinaryGuard.FileName);
        Assert.Equal(expected, WinPmemBinaryGuard.ResolveUnderRoot(root));
    }
}
