using KapeIR.Core.Services;

namespace KapeIR.Builder.Tests;

public sealed class WinPmemMkapePatchTests
{
    [Fact]
    public void TryEnsureAcquireCommandLine_RewritesLegacyMiniCli()
    {
        var dir = Path.Combine(Path.GetTempPath(), "kapeir-winpmem-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, WinPmemMkapePatch.ModuleFileName);
            File.WriteAllText(path, """
Description: WinPmem Memory Dump
Processors:
    -
        Executable: winpmem.exe
        CommandLine: "%destinationDirectory%\\memory.raw"
        ExportFormat: raw
""");

            Assert.True(WinPmemMkapePatch.TryEnsureAcquireCommandLine(path));
            var text = File.ReadAllText(path);
            Assert.Contains("acquire --progress", text, StringComparison.OrdinalIgnoreCase);
            Assert.False(WinPmemMkapePatch.TryEnsureAcquireCommandLine(path));
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void TryEnsureAcquireCommandLine_IgnoresOtherModules()
    {
        var dir = Path.Combine(Path.GetTempPath(), "kapeir-winpmem-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "Other.mkape");
            File.WriteAllText(path, "CommandLine: \"%destinationDirectory%\\memory.raw\"\n");
            Assert.False(WinPmemMkapePatch.TryEnsureAcquireCommandLine(path));
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* ignore */ }
        }
    }
}
