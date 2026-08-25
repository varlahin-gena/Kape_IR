using System.IO;
using System.Text.Json;

namespace KapePackRunner;

internal static class PackPayload
{
    public static bool TryReadPayload(string exePath, out long zipStart, out long zipLen)
        => KapePackBuilder.Services.KapepackPayload.TryRead(exePath, out zipStart, out zipLen);

    public static void Extract(string exePath, long zipStart, long zipLen, string outDir, Action<string>? log = null)
        => KapePackBuilder.Services.KapepackPayload.Extract(exePath, zipStart, zipLen, outDir, log);

    public static LaunchConfig? ReadLaunchConfig(string packageDir)
    {
        var path = Path.Combine(packageDir, "package.json");
        if (!File.Exists(path)) return null;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var root = doc.RootElement;
            return new LaunchConfig
            {
                Name = GetStr(root, "name") ?? "Package",
                Tsource = GetStr(root, "tsource") ?? "C:",
                Target = GetStr(root, "target_compound") ?? "",
                Module = GetStr(root, "module_compound"),
                ZipOutput = root.TryGetProperty("zip_output", out var z) && z.ValueKind == JsonValueKind.True,
                Flush = root.TryGetProperty("flush", out var f) && f.ValueKind == JsonValueKind.True,
                Vss = root.TryGetProperty("vss", out var v) && v.ValueKind == JsonValueKind.True
            };
        }
        catch
        {
            return null;
        }
    }

    public static List<string> BuildKapeArgs(LaunchConfig cfg)
    {
        var args = new List<string>
        {
            "--tsource", cfg.Tsource,
            "--tdest", @"RESULTS\%m",
            "--target", cfg.Target
        };
        if (cfg.ZipOutput)
            args.AddRange(new[] { "--zip", "%m" });
        if (!string.IsNullOrWhiteSpace(cfg.Module))
        {
            args.AddRange(new[]
            {
                "--mdest", @"RESULTS\%m\ModuleOutput",
                "--zm", "true",
                "--module", cfg.Module!
            });
        }
        if (cfg.Flush) args.Add("--flush");
        if (cfg.Vss) args.Add("--vss");
        return args;
    }

    private static string? GetStr(JsonElement root, string name)
        => root.TryGetProperty(name, out var p) ? p.GetString() : null;
}

internal sealed class LaunchConfig
{
    public string Name { get; init; } = "";
    public string Tsource { get; set; } = "C:";
    public string Target { get; init; } = "";
    public string? Module { get; init; }
    public bool ZipOutput { get; init; }
    public bool Flush { get; init; }
    public bool Vss { get; init; }
}
