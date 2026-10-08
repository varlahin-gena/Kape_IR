using KapeIR.Core.Services;

namespace KapeIR.Builder.Tests;

public sealed class EstimateSummaryParserTests
{
    [Fact]
    public void Observe_ParsesTypicalSimLines_AndFormatsBlock()
    {
        var p = new EstimateSummaryParser();
        p.Observe("  ** Simulate copy is True. No files will actually be copied to D:\\test\\RESULTS\\V_HONOR **");
        p.Observe("Found 982 files in 1.428 seconds. Beginning copy...");
        p.Observe("Deferred file count: 196. Copying locked files...");
        p.Observe(
            "Copied 737 (Deduplicated: 245) out of 982 files in 12.8937 seconds. See CopyLog.csv for copy details");

        Assert.True(p.SawSimulateBanner);
        Assert.Equal(982, p.FilesFound);
        Assert.Equal(196, p.DeferredCount);
        Assert.Equal(737, p.CopiedCount);
        Assert.Equal(245, p.Deduplicated);
        Assert.Equal(982, p.OutOf);

        var block = p.FormatLogBlock();
        Assert.Contains("=== Оценка объёма ===", block);
        Assert.Contains("Файлов найдено: 982", block);
        Assert.Contains("К копированию (sim): 737 (дедуп 245, из 982)", block);
        Assert.Contains("Отложено (locked): 196", block);
        Assert.Contains("--sim", block);
    }

    [Fact]
    public void FormatLogBlock_EmptyWhenNoData()
    {
        var p = new EstimateSummaryParser();
        p.Observe("KAPE version 1.3.0.2");
        Assert.False(p.HasUsefulData);
        Assert.Equal("", p.FormatLogBlock());
    }
}
