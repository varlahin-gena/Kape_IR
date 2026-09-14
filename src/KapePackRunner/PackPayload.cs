using KapePack.Core.Services;

namespace KapePackRunner;

internal static class PackPayload
{
    public static bool TryReadPayload(string exePath, out long zipStart, out long zipLen)
        => KapepackPayload.TryRead(exePath, out zipStart, out zipLen);

    public static void Extract(string exePath, long zipStart, long zipLen, string outDir, Action<string>? log = null)
        => KapepackPayload.Extract(exePath, zipStart, zipLen, outDir, log);

    public static CollectionPlan.LaunchManifest? ReadLaunchConfig(string packageDir)
        => LaunchManifestIo.TryReadFromPackageDir(packageDir);

    public static List<string> BuildKapeArgs(CollectionPlan.LaunchManifest cfg, bool simulate = false)
        => KapeCliArgs.Build(new KapeCliArgs.Options(
            cfg.Tsource,
            cfg.Target,
            string.IsNullOrWhiteSpace(cfg.Module) ? null : cfg.Module,
            cfg.ZipOutput,
            cfg.Flush,
            cfg.Vss,
            Simulate: simulate));
}
