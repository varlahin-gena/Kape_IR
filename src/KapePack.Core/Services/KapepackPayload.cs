using System.Text;
using KapePack.Core.Shared;

namespace KapePack.Core.Services;

/// <summary>
/// KAPEPACK payload appended after a stub EXE: [stub][zip][Int64 start][Int64 len][ASCII KAPEPACK].
/// Shared by StandaloneExeBuilder (write) and PackRunner (read/extract).
/// </summary>
public static class KapepackPayload
{
    public const string Magic = "KAPEPACK";
    public const int FooterSize = 8 + 8 + 8;

    public static bool TryRead(string exePath, out long zipStart, out long zipLen)
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

    public static void Extract(
        string exePath,
        long zipStart,
        long zipLen,
        string outDir,
        Action<string>? log = null,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        // Stage on the same volume as outDir (USB / share), not %TEMP% on C:.
        var tmpZip = CollectPackPaths.CreateSiblingTempFile(outDir, ".kapepack_extract_", ".zip");
        try
        {
            log?.Invoke($"Извлечение архива ({zipLen:N0} байт)…");
            using (var fs = File.OpenRead(exePath))
            using (var outFs = File.Create(tmpZip))
            {
                fs.Seek(zipStart, SeekOrigin.Begin);
                CopyExactly(fs, outFs, zipLen, ct);
            }

            ct.ThrowIfCancellationRequested();
            if (Directory.Exists(outDir))
            {
                log?.Invoke("Очистка предыдущей распаковки…");
                Directory.Delete(outDir, true);
            }

            Directory.CreateDirectory(outDir);
            SafeZip.ExtractToDirectory(tmpZip, outDir, overwriteFiles: true);
            log?.Invoke($"Распаковано в: {outDir}");
        }
        finally
        {
            try { File.Delete(tmpZip); } catch { /* ignore */ }
        }
    }

    private static void CopyExactly(Stream input, Stream output, long count, CancellationToken ct)
    {
        var buffer = new byte[1024 * 256];
        long left = count;
        while (left > 0)
        {
            ct.ThrowIfCancellationRequested();
            var n = input.Read(buffer, 0, (int)Math.Min(buffer.Length, left));
            if (n <= 0) throw new EndOfStreamException("Неожиданный конец файла при чтении payload.");
            output.Write(buffer, 0, n);
            left -= n;
        }
    }
}
