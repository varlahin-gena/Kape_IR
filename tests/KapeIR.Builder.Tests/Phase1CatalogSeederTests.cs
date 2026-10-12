using System.IO.Compression;
using System.Text;
using KapeIR.Core.Models;
using KapeIR.Core.Services;
using KapeIR.Builder.Tests.Fixtures;

namespace KapeIR.Builder.Tests;

public class Phase1CatalogSeederTests
{
    [Fact]
    public void Ensure_WritesPhase1CompoundsAndMarker()
    {
        using var fx = FakeKapeRoot.Create(FakeKapeProfile.Minimal);
        var result = Phase1CatalogSeeder.Ensure(fx.Root);
        Assert.True(result.Ok, result.Message);
        Assert.False(result.SkippedUpToDate);
        Assert.True(result.FilesWritten > 0, result.Message);

        Assert.True(File.Exists(Path.Combine(fx.Root, "Modules", "Compound",
            PackageDefinition.DefaultPhase1Module + ".mkape")));
        Assert.True(File.Exists(Path.Combine(fx.Root, "Modules", "Compound",
            PackageDefinition.DefaultPhase1MemoryTriage + ".mkape")));
        Assert.True(File.Exists(Path.Combine(fx.Root, "Modules", "Compound",
            "Hayabusa_Offline_v41.mkape")));
        Assert.True(File.Exists(Path.Combine(fx.Root, "Modules", "Apps", "Local", "Hayabusa",
            "hayabusa_OfflineEventLogs_v41.mkape")));
        Assert.True(File.Exists(Path.Combine(fx.Root, "Modules", "bin",
            "Invoke-Volatility3Plugin.ps1")));
        Assert.True(File.Exists(Path.Combine(fx.Root, Phase1CatalogSeeder.VersionMarkerRelativePath)));
        Assert.Equal(Phase1CatalogSeeder.ReadEmbeddedVersion(),
            Phase1CatalogSeeder.ReadInstalledVersion(fx.Root));
        Assert.Equal("1.2.0", Phase1CatalogSeeder.ReadEmbeddedVersion());
    }

    [Fact]
    public void Ensure_SecondCall_IsIdempotent()
    {
        using var fx = FakeKapeRoot.Create(FakeKapeProfile.Minimal);
        var first = Phase1CatalogSeeder.Ensure(fx.Root);
        Assert.True(first.Ok);
        var second = Phase1CatalogSeeder.Ensure(fx.Root);
        Assert.True(second.Ok, second.Message);
        Assert.True(second.SkippedUpToDate, second.Message);
        Assert.Equal(0, second.FilesWritten);
    }

    [Fact]
    public async Task WinPmemInstaller_InstallsFromStreamOverride()
    {
        using var fx = FakeKapeRoot.Create(FakeKapeProfile.Minimal);
        var payload = Encoding.ASCII.GetBytes("MZ" + new string('X', 2048));
        await using var stream = new MemoryStream(payload);
        var result = await WinPmemInstaller.InstallAsync(fx.Root, exeStreamOverride: stream);
        Assert.True(result.Ok, result.Message);
        Assert.True(File.Exists(WinPmemInstaller.GetExePath(fx.Root)));
        Assert.True(WinPmemInstaller.Check(fx.Root).Ok);
    }

    [Fact]
    public async Task Volatility3Installer_InstallsFromZipOverride()
    {
        using var fx = FakeKapeRoot.Create(FakeKapeProfile.Minimal);
        await using var zipStream = new MemoryStream();
        using (var zip = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = zip.CreateEntry("volatility3/vol.exe");
            await using var es = entry.Open();
            var bytes = Encoding.ASCII.GetBytes("MZ" + new string('V', 2048));
            await es.WriteAsync(bytes);
        }

        zipStream.Position = 0;
        var result = await Volatility3Installer.InstallAsync(fx.Root, zipStreamOverride: zipStream);
        Assert.True(result.Ok, result.Message);
        Assert.True(File.Exists(Volatility3Installer.GetExePath(fx.Root)));
        Assert.True(Volatility3Installer.Check(fx.Root).Ok);
    }

    [Fact]
    public void Phase1Toolkit_CheckBins_ReportsMissing()
    {
        using var fx = FakeKapeRoot.Create(FakeKapeProfile.Minimal);
        var status = Phase1Toolkit.CheckBins(fx.Root);
        Assert.True(status.NeedsInstall);
        Assert.True(status.WinPmem.NeedsInstall);
        Assert.True(status.Volatility3.NeedsInstall);
        Assert.True(status.LiveTools.NeedsInstall);
    }

    [Fact]
    public void Ensure_SeedsLiveScriptsIntoModulesBin()
    {
        using var fx = FakeKapeRoot.Create(FakeKapeProfile.Minimal);
        var result = Phase1CatalogSeeder.Ensure(fx.Root, force: true);
        Assert.True(result.Ok, result.Message);
        Assert.True(File.Exists(Path.Combine(fx.Root, "Modules", "bin", "Get-LoggedOnUsers.ps1")));
        Assert.True(File.Exists(Path.Combine(fx.Root, "Modules", "bin", "Get-Sessions.ps1")));
        Assert.True(File.Exists(Path.Combine(fx.Root, "Modules", "bin", "Get-InjectedThread.ps1")));
    }

    [Fact]
    public void Phase1LiveTools_Check_ListsSysinternalsGaps()
    {
        using var fx = FakeKapeRoot.Create(FakeKapeProfile.Minimal);
        Phase1CatalogSeeder.Ensure(fx.Root, force: true);
        var status = Phase1LiveToolsInstaller.Check(fx.Root);
        Assert.True(status.NeedsInstall);
        Assert.Contains(status.Missing, m => m.Equals("handle.exe", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(status.Missing, m => m.Equals("pslist.exe", StringComparison.OrdinalIgnoreCase));
    }
}
