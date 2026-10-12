using System.Net.Http;
using System.Text;

namespace KapeIR.Core.Services;

/// <summary>Phase 1 definitions seed + WinPmem/Vol3/live tools for two_phase packs.</summary>
public static class Phase1Toolkit
{
    public sealed class BinStatus
    {
        public WinPmemStatus WinPmem { get; init; } = new();
        public Volatility3Status Volatility3 { get; init; } = new();
        public Phase1LiveToolsStatus LiveTools { get; init; } = new();
        public bool Ok => WinPmem.Ok && Volatility3.Ok && LiveTools.Ok;
        public bool NeedsInstall =>
            WinPmem.NeedsInstall || Volatility3.NeedsInstall || LiveTools.NeedsInstall;
        public string Message { get; init; } = "";
    }

    public sealed class EnsureBinsResult
    {
        public bool Ok { get; init; }
        public string Message { get; init; } = "";
        public WinPmemInstallResult? WinPmem { get; init; }
        public Volatility3InstallResult? Volatility3 { get; init; }
        public Phase1LiveToolsInstallResult? LiveTools { get; init; }
    }

    public static BinStatus CheckBins(string kapeRoot)
    {
        var wp = WinPmemInstaller.Check(kapeRoot);
        var vol = Volatility3Installer.Check(kapeRoot);
        var live = Phase1LiveToolsInstaller.Check(kapeRoot);
        var sb = new StringBuilder();
        sb.AppendLine(wp.Message);
        sb.AppendLine(vol.Message);
        sb.Append(live.Message);
        return new BinStatus
        {
            WinPmem = wp,
            Volatility3 = vol,
            LiveTools = live,
            Message = sb.ToString().TrimEnd()
        };
    }

    /// <summary>Seed Phase1 mkape/scripts into the KAPE root (idempotent).</summary>
    public static Phase1CatalogSeeder.Result SeedDefinitions(string kapeRoot, bool force = false)
        => Phase1CatalogSeeder.Ensure(kapeRoot, force);

    /// <summary>Download/install missing Phase1 binaries (WinPmem, Vol3, Sysinternals, scripts).</summary>
    public static async Task<EnsureBinsResult> EnsureBinsAsync(
        string kapeRoot,
        bool installWinPmem = true,
        bool installVolatility3 = true,
        bool installLiveTools = true,
        IProgress<string>? progress = null,
        CancellationToken ct = default,
        HttpMessageHandler? httpHandler = null)
    {
        var messages = new List<string>();
        WinPmemInstallResult? wp = null;
        Volatility3InstallResult? vol = null;
        Phase1LiveToolsInstallResult? live = null;
        var ok = true;

        if (installLiveTools && Phase1LiveToolsInstaller.Check(kapeRoot).NeedsInstall)
        {
            live = await Phase1LiveToolsInstaller.InstallAsync(kapeRoot, progress, ct, httpHandler)
                .ConfigureAwait(false);
            messages.Add(live.Message);
            ok &= live.Ok;
        }

        if (installWinPmem && WinPmemInstaller.Check(kapeRoot).NeedsInstall)
        {
            wp = await WinPmemInstaller.InstallAsync(kapeRoot, progress, ct, httpHandler)
                .ConfigureAwait(false);
            messages.Add(wp.Message);
            ok &= wp.Ok;
        }

        if (installVolatility3 && Volatility3Installer.Check(kapeRoot).NeedsInstall)
        {
            vol = await Volatility3Installer.InstallAsync(kapeRoot, progress, ct, httpHandler)
                .ConfigureAwait(false);
            messages.Add(vol.Message);
            ok &= vol.Ok;
        }

        if (messages.Count == 0)
        {
            return new EnsureBinsResult
            {
                Ok = true,
                Message = "Phase1 binaries уже на месте."
            };
        }

        return new EnsureBinsResult
        {
            Ok = ok,
            Message = string.Join("\n", messages),
            WinPmem = wp,
            Volatility3 = vol,
            LiveTools = live
        };
    }
}
