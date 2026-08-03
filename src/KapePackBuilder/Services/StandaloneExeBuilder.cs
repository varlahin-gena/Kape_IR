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
    private const ushort ImageSubsystemWindowsGui = 2;
    private const ushort ImageSubsystemWindowsCui = 3;

    public static string Build(string stubExePath, string zipPath, string outputExePath)
    {
        if (!File.Exists(stubExePath))
            throw new FileNotFoundException("Не найден stub KapePackRunner.exe", stubExePath);
        if (!File.Exists(zipPath))
            throw new FileNotFoundException("Не найден ZIP пакета", zipPath);
        if (!IsGuiStub(stubExePath))
            throw new InvalidOperationException(
                "Найден устаревший console-stub KapePackRunner.exe (две консоли).\n" +
                $"Файл: {stubExePath}\n" +
                "Пересоберите через publish.ps1 или положите рядом GUI-stub (~70+ МБ, WinExe).");

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
    /// Resolve GUI stub: skip old console stubs next to the app.
    /// Preference: tools\ / artifacts\, then beside app, then embedded resource.
    /// </summary>
    public static string ResolveStubPath()
    {
        var baseDir = AppContext.BaseDirectory;
        var candidates = new[]
        {
            // Prefer published GUI stub from repo tools/ (dev) and dist tools/
            Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "tools", "KapePackRunner.exe")),
            Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "artifacts", "runner", "KapePackRunner.exe")),
            Path.Combine(baseDir, "tools", "KapePackRunner.exe"),
            Path.Combine(baseDir, "..", "tools", "KapePackRunner.exe"),
            Path.Combine(baseDir, "KapePackRunner.exe"),
        };

        string? consoleFallback = null;
        foreach (var c in candidates)
        {
            if (!File.Exists(c)) continue;
            if (IsGuiStub(c))
                return Path.GetFullPath(c);
            consoleFallback ??= Path.GetFullPath(c);
        }

        var embedded = ExtractEmbeddedStub(Path.Combine(baseDir, "tools"));
        if (embedded is not null && IsGuiStub(embedded))
            return embedded;

        if (consoleFallback is not null)
            throw new InvalidOperationException(
                "Рядом лежит только старый console KapePackRunner.exe — из‑за него открываются две консоли.\n" +
                $"Удалите или замените: {consoleFallback}\n" +
                "Скопируйте GUI-stub из PackBuilder.Net\\dist\\KapePackRunner.exe (или запустите publish.ps1).");

        throw new FileNotFoundException(
            "KapePackRunner.exe (GUI) не найден. Пересоберите через publish.ps1.");
    }

    /// <summary>True if PE subsystem is WINDOWS GUI (not console).</summary>
    public static bool IsGuiStub(string exePath)
    {
        try
        {
            using var fs = File.OpenRead(exePath);
            if (fs.Length < 0x40) return false;
            using var br = new BinaryReader(fs);
            if (br.ReadUInt16() != 0x5A4D) return false; // MZ
            fs.Seek(0x3C, SeekOrigin.Begin);
            var peOffset = br.ReadInt32();
            if (peOffset <= 0 || peOffset + 24 + 70 >= fs.Length) return false;
            fs.Seek(peOffset, SeekOrigin.Begin);
            if (br.ReadUInt32() != 0x00004550) return false; // PE\0\0
            fs.Seek(peOffset + 24 + 68, SeekOrigin.Begin); // OptionalHeader.Subsystem
            var subsystem = br.ReadUInt16();
            return subsystem == ImageSubsystemWindowsGui;
        }
        catch
        {
            return false;
        }
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
