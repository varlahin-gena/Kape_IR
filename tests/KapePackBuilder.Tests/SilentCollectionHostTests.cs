using KapePackRunner;

namespace KapePackBuilder.Tests;

public class SilentCollectionHostTests
{
    [Fact]
    public async Task RunAsync_WithParseErrors_Returns2()
    {
        var opt = RunnerCliOptions.Parse(new[] { "--silent", "--phase", "9" });
        Assert.NotEmpty(opt.Errors);

        var code = await SilentCollectionHost.RunAsync(opt);
        Assert.Equal(2, code);
    }

    [Fact]
    public async Task RunAsync_UnknownSwitch_Returns2()
    {
        var opt = RunnerCliOptions.Parse(new[] { "--silent", "--nope" });
        Assert.NotEmpty(opt.Errors);

        var code = await SilentCollectionHost.RunAsync(opt);
        Assert.Equal(2, code);
    }

    [Fact]
    public void RunnerCliOptions_Parse_MapsCoreFlags()
    {
        var o = RunnerCliOptions.Parse(new[]
        {
            "--silent", "--sim", "--tsource", "D:", "--case-id", "CASE-1",
            "--phase", "1", "--skip-memory", "--verify", "--log", "x.log"
        });
        Assert.True(o.Silent);
        Assert.True(o.SimOnly);
        Assert.Equal("D:", o.Tsource);
        Assert.Equal("CASE-1", o.CaseId);
        Assert.Equal(1, o.Phase);
        Assert.True(o.SkipMemory);
        Assert.True(o.VerifySha256);
        Assert.Equal("x.log", o.LogPath);
        Assert.Empty(o.Errors);
    }
}
