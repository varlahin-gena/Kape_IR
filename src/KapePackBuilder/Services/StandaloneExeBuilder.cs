using System.Reflection;
using System.Text;

namespace KapePackBuilder.Services;

/// <summary>
/// Builds a single self-extracting EXE: [KapePackRunner stub][zip payload][footer].
/// Footer: Int64 zipStart, Int64 zipLen, ASCII "KAPEPACK".
/// </summary>
public static class StandaloneExeBuilder
{
    public const string Magic = "KAPEPACK";
    private const int FooterSize = 8 + 8 + 8;

    public static string Build(string stubExePath, string zipPath, string outputExePath)
    {
        if (!File.Exists(stubExePath))
            throw new FileNotFoundException("Не найден stub KapePackRunner.exe", stubExePath);
        if (!File.Exists(zipPath))
            throw new FileNotFoundException("Не найден ZIP пакета", zipPath);

        Directory.CreateDirectory(Path.GetDirectoryName(outputExePath)!);
        if (File.Exists(outputExePath))
            File.Delete(outputExePath);

        using (var outFs = File.Create(outputExePath))
        {
            long zipStart;
            using (var stub = File.OpenRead(stubExePath))
            {
                stub.CopyTo(outFs);
                zipStart = outFs.Position;
            }

            long zipLen;
            using (var zip = File.OpenRead(zipPath))
            {
                zip.CopyTo(outFs);
                zipLen = zip.Length;
            }

            using var bw = new BinaryWriter(outFs, Encoding.ASCII, leaveOpen: true);
            bw.Write(zipStart);
            bw.Write(zipLen);
            bw.Write(Encoding.ASCII.GetBytes(Magic));
            if (Magic.Length != 8)
                throw new InvalidOperationException("Magic must be 8 bytes");
        }

        return outputExePath;
    }

    /// <summary>
    /// Resolve stub: embedded resource, then tools\ next to app, then artifacts\runner.
    /// </summary>
    public static string ResolveStubPath()
    {
        var baseDir = AppContext.BaseDirectory;
        var candidates = new[]
        {
            Path.Combine(baseDir, "KapePackRunner.exe"),
            Path.Combine(baseDir, "tools", "KapePackRunner.exe"),
            Path.Combine(baseDir, "..", "tools", "KapePackRunner.exe"),
            Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "tools", "KapePackRunner.exe")),
            Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "artifacts", "runner", "KapePackRunner.exe")),
        };

        foreach (var c in candidates)
        {
            if (File.Exists(c))
                return c;
        }

        // Extract embedded resource once
        var embedded = ExtractEmbeddedStub(Path.Combine(baseDir, "tools"));
        if (embedded is not null)
            return embedded;

        throw new FileNotFoundException(
            "KapePackRunner.exe не найден. Пересоберите Pack Builder через publish.ps1 " +
            "(он публикует stub в tools\\).");
    }

    private static string? ExtractEmbeddedStub(string toolsDir)
    {
        var asm = Assembly.GetExecutingAssembly();
        var name = asm.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("KapePackRunner.exe", StringComparison.OrdinalIgnoreCase));
        if (name is null) return null;

        Directory.CreateDirectory(toolsDir);
        var dest = Path.Combine(toolsDir, "KapePackRunner.exe");
        using var stream = asm.GetManifestResourceStream(name);
        if (stream is null) return null;
        using var fs = File.Create(dest);
        stream.CopyTo(fs);
        return dest;
    }
}
