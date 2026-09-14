using System.Text.Json;
using KapePack.Core.Models;
using KapePack.Core.Services;

namespace KapePackBuilder.Tests;

public class LaunchManifestIoTests
{
    [Theory]
    [InlineData("two_phase", IrCollectionMode.TwoPhase)]
    [InlineData("TwoPhase", IrCollectionMode.TwoPhase)]
    [InlineData("2", IrCollectionMode.TwoPhase)]
    [InlineData("single", IrCollectionMode.Single)]
    public void ParseCollectionMode_Strings(string mode, IrCollectionMode expected)
    {
        using var doc = JsonDocument.Parse($$"""{"collection_mode":"{{mode}}"}""");
        Assert.Equal(expected, LaunchManifestIo.ParseCollectionMode(doc.RootElement));
    }

    [Fact]
    public void ParseCollectionMode_NumericOne_IsTwoPhase()
    {
        using var doc = JsonDocument.Parse("""{"collection_mode":1}""");
        Assert.Equal(IrCollectionMode.TwoPhase, LaunchManifestIo.ParseCollectionMode(doc.RootElement));
    }

    [Fact]
    public void Parse_ReadsTwoPhaseFields()
    {
        using var doc = JsonDocument.Parse("""
        {
          "name": "IR",
          "tsource": "D:",
          "target_compound": "IR",
          "module_compound": "VolatileFirst",
          "collection_mode": "two_phase",
          "case_id": "CASE-1",
          "phase1_module": "VolatileFirst",
          "phase2_module": "IR_Phase2",
          "zip_output": false,
          "flush": true,
          "vss": true
        }
        """);
        var m = LaunchManifestIo.Parse(doc.RootElement);
        Assert.Equal("IR", m.Name);
        Assert.Equal("D:", m.Tsource);
        Assert.Equal(IrCollectionMode.TwoPhase, m.CollectionMode);
        Assert.Equal("CASE-1", m.CaseId);
        Assert.Equal("VolatileFirst", m.Phase1Module);
        Assert.Equal("IR_Phase2", m.Phase2Module);
        Assert.False(m.ZipOutput);
        Assert.True(m.Flush);
        Assert.True(m.Vss);
    }

    [Fact]
    public void TryReadFromPackageDir_ReturnsNull_WhenMissing()
    {
        var dir = Path.Combine(Path.GetTempPath(), "kape_lm_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            Assert.Null(LaunchManifestIo.TryReadFromPackageDir(dir));
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* ignore */ }
        }
    }
}

public class CollectPackCliOptionsTests
{
    [Fact]
    public void Parse_SilentTsourceCasePhase()
    {
        var o = CollectPackCliOptions.Parse(new[]
        {
            "--silent", "--tsource", "C:", "--case-id", "IR-1", "--phase", "1", "--skip-memory"
        });
        Assert.True(o.Silent);
        Assert.Equal("C:", o.Tsource);
        Assert.Equal("IR-1", o.CaseId);
        Assert.Equal(1, o.Phase);
        Assert.True(o.SkipMemory);
        Assert.Empty(o.Errors);
    }

    [Fact]
    public void Parse_SimOnlyImpliesSilent()
    {
        var o = CollectPackCliOptions.Parse(new[] { "--sim-only", "--tsource=E:" });
        Assert.True(o.SimOnly);
        Assert.True(o.Silent);
        Assert.Equal("E:", o.Tsource);
    }

    [Fact]
    public void Parse_UnknownArg_IsError()
    {
        var o = CollectPackCliOptions.Parse(new[] { "--nope" });
        Assert.Contains(o.Errors, e => e.Contains("--nope", StringComparison.Ordinal));
    }

    [Fact]
    public void Parse_BadPhase_IsError()
    {
        var o = CollectPackCliOptions.Parse(new[] { "--phase", "9" });
        Assert.NotEmpty(o.Errors);
    }
}
