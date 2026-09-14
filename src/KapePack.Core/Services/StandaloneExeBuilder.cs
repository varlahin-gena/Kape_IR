using System.Reflection;
using System.Text;

namespace KapePack.Core.Services;

/// <summary>
/// Builds a single self-extracting EXE: [KapePackRunner stub][zip payload][footer].
/// Footer: Int64 zipStart, Int64 zipLen, ASCII "KAPEPACK".
/// </summary>
public static class StandaloneExeBuilder
{
    public const string Magic = KapepackPayload.Magic;
    private const int FooterSize = KapepackPayload.FooterSize;
    private const ushort ImageSubsystemWindowsGui = 2;
    private const ushort ImageSubsystemWindowsCui = 3;

    public static string Build(string stubExePath, string zipPath, string outputExePath)
    {
        if (!File.Exists(stubExePath))
            throw new FileNotFoundException("Не найден stub KapePackRunner (GUI)", stubExePath);
        if (!File.Exists(zipPath))
            throw new FileNotFoundException("Не найден ZIP пакета", zipPath);
        if (!IsGuiStub(stubExePath))
            throw new InvalidOperationException(
                "Найден устаревший console-stub (две консоли).\n" +
                $"Файл: {stubExePath}\n" +
                "Пересоберите Pack Builder через publish.ps1 (stub встраивается в один EXE).");

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
    /// Resolve GUI stub. Preference: under selected KapeRoot\PackBuilder\stub → LocalAppData → tools/.
    /// </summary>
    public static string ResolveStubPath(string? kapeRoot = null)
    {
        if (!string.IsNullOrWhiteSpace(kapeRoot) && KapeRootPaths.LooksLikeKapeRoot(kapeRoot))
        {
            var underRoot = ExtractEmbeddedStub(KapeRootPaths.StubCacheDir(kapeRoot));
            if (underRoot is not null && IsGuiStub(underRoot))
                return underRoot;
        }

        var baseDir = AppContext.BaseDirectory;

        // Embedded resource inside single-file Builder
        var embedded = ExtractEmbeddedStub(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "KapePackBuilder",
            "stub"));
        if (embedded is not null && IsGuiStub(embedded))
            return embedded;

        var candidates = new[]
        {
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

        if (consoleFallback is not null)
            throw new InvalidOperationException(
                "Рядом лежит только старый console KapePackRunner.exe — из‑за него открываются две консоли.\n" +
                $"Удалите: {consoleFallback}\n" +
                "Нужен актуальный KapePackBuilder.exe из publish (stub встроен внутрь).");

        throw new FileNotFoundException(
            "GUI-stub для автономного пакета не найден.\n" +
            "Пересоберите KapePackBuilder через publish.ps1 — stub встраивается в один EXE.");
    }

    public static bool TryGetEmbeddedStubInfo(out long sizeBytes, out string? resourceName)
    {
        sizeBytes = 0;
        resourceName = null;
        var asm = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
        resourceName = asm.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("KapePackRunner.exe", StringComparison.OrdinalIgnoreCase));
        if (resourceName is null) return false;
        using var stream = asm.GetManifestResourceStream(resourceName);
        if (stream is null) return false;
        sizeBytes = stream.Length;
        return sizeBytes > 0;
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
        var asm = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
        var name = asm.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("KapePackRunner.exe", StringComparison.OrdinalIgnoreCase));
        if (name is null) return null;

        Directory.CreateDirectory(toolsDir);
        var dest = Path.Combine(toolsDir, "KapePackRunner.exe");
        var marker = dest + ".embedsha256";

        using var stream = asm.GetManifestResourceStream(name);
        if (stream is null) return null;

        // Materialize once: embedded streams are often non-seekable, and size-only
        // cache previously reused same-length but outdated stubs after UI fixes.
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        var payload = ms.ToArray();
        var embedHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(payload))
            .ToLowerInvariant();

        if (File.Exists(dest) &&
            File.Exists(marker) &&
            string.Equals(File.ReadAllText(marker).Trim(), embedHash, StringComparison.OrdinalIgnoreCase) &&
            IsGuiStub(dest))
            return dest;

        File.WriteAllBytes(dest, payload);
        File.WriteAllText(marker, embedHash + Environment.NewLine);
        return dest;
    }
}
