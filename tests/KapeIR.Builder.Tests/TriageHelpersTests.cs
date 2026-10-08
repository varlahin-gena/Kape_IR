using KapeIR.Triage.ViewModels;

namespace KapeIR.Builder.Tests;

public sealed class TriageHelpersTests
{
    [Fact]
    public void LogJournal_Append_StampsAndBuffers()
    {
        var j = new TriageLogJournal();
        var frag = j.Append("hello");
        Assert.NotNull(frag);
        Assert.Contains("hello", frag);
        Assert.Contains("hello", j.FullText);
        Assert.Null(j.Append(""));
    }

    [Theory]
    [InlineData("one line", "one line")]
    [InlineData("first\nsecond", "first…")]
    public void LogJournal_TrimBanner(string input, string expected)
        => Assert.Equal(expected, TriageLogJournal.TrimBanner(input));

    [Fact]
    public void PrepareLog_ParsesPercentMilestones()
    {
        var effect = TriageProgressBinder.InterpretPrepareLog("Проверка SHA256… 40%");
        Assert.Equal("Проверка SHA256…", effect.StatusText);
        Assert.False(effect.Indeterminate);
        Assert.Equal(40, effect.Percent);
        Assert.True(effect.AppendToJournal); // 40 % 20 == 0

        var mid = TriageProgressBinder.InterpretPrepareLog("Проверка SHA256… 42%");
        Assert.False(mid.AppendToJournal);
        Assert.Equal(42, mid.Percent);
    }

    [Fact]
    public void PathGuards_RequiresDiskAndResults()
    {
        var missingDisk = TriagePathGuards.TryResolve("", null, @"C:\out");
        Assert.False(missingDisk.Ok);

        var dir = Path.Combine(Path.GetTempPath(), "kapeir-triage-path-" + Guid.NewGuid().ToString("N"));
        try
        {
            var ok = TriagePathGuards.TryResolve("C:", null, dir);
            Assert.True(ok.Ok, ok.Error);
            Assert.True(Directory.Exists(dir));
            Assert.Equal("C:", ok.Tsource);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* ignore */ }
        }
    }
}
