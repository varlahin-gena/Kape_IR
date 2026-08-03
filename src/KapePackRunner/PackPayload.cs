using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace KapePackRunner;

internal static class PackPayload
{
    public const string Magic = "KAPEPACK";
    private const int FooterSize = 8 + 8 + 8;

    public static bool TryReadPayload(string exePath, out long zipStart, out long zipLen)
    {
        zipStart = 0;
        zipLen = 0;
        var fi = new FileInfo(exePath);
        if (fi.Length < FooterSize + 64) return false;

        using var fs = File.OpenRead(exePath);
        fs.Seek(-FooterSize, SeekOrigin.End);
        using var br = new BinaryReader(fs, Encoding.ASCII, leaveOpen: true);
        zipStart = br.ReadInt64();
        zipLen = br.ReadInt64();
        var magic = Encoding.ASCII.GetString(br.ReadBytes(8));
        if (!string.Equals(magic, Magic, StringComparison.Ordinal)) return false;
        if (zipStart < 0 || zipLen <= 0) return false;
        if (zipStart + zipLen + FooterSize > fi.Length) return false;
        return true;
    }

    public static void Extract(string exePath, long zipStart, long zipLen, string outDir, Action<string>? log = null)
    {
        var tmpZip = Path.Combine(Path.GetTempPath(), "kapepack_" + Guid.NewGuid().ToString("N") + ".zip");
        try
        {
            log?.Invoke($"Извлечение архива ({zipLen:N0} байт)…");
            using (var fs = File.OpenRead(exePath))
            using (var outFs = File.Create(tmpZip))
            {
                fs.Seek(zipStart, SeekOrigin.Begin);
                CopyExactly(fs, outFs, zipLen);
            }

            if (Directory.Exists(outDir))
            {
                log?.Invoke("Очистка предыдущей распаковки…");
                Directory.Delete(outDir, true);
            }

            Directory.CreateDirectory(outDir);
            ZipFile.ExtractToDirectory(tmpZip, outDir, overwriteFiles: true);
            log?.Invoke($"Распаковано в: {outDir}");
        }
        finally
        {
            try { File.Delete(tmpZip); } catch { /* ignore */ }
        }
    }

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

    private static void CopyExactly(Stream input, Stream output, long count)
    {
        var buffer = new byte[1024 * 256];
        long left = count;
        while (left > 0)
        {
            var n = input.Read(buffer, 0, (int)Math.Min(buffer.Length, left));
            if (n <= 0) throw new EndOfStreamException("Неожиданный конец файла при чтении payload.");
            output.Write(buffer, 0, n);
            left -= n;
        }
    }
}

internal sealed class LaunchConfig
{
    public string Name { get; init; } = "";
    public string Tsource { get; init; } = "C:";
    public string Target { get; init; } = "";
    public string? Module { get; init; }
    public bool ZipOutput { get; init; }
    public bool Flush { get; init; }
    public bool Vss { get; init; }
}
