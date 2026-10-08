using System.Security.Cryptography;
using System.Text;

namespace KapeIR.Core.Services;

public static class FileHash
{
    public static string Sha256Hex(string path)
        => Sha256Hex(path, progress: null);

    /// <param name="progress">Optional 0–100 percent while hashing large files.</param>
    public static string Sha256Hex(
        string path,
        IProgress<double>? progress,
        CancellationToken cancellationToken = default)
    {
        var fi = new FileInfo(path);
        var total = Math.Max(1L, fi.Length);
        using var fs = File.OpenRead(path);
        using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[1024 * 1024];
        long read = 0;
        int lastPct = -1;
        int n;
        while ((n = fs.Read(buffer, 0, buffer.Length)) > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            hasher.AppendData(buffer.AsSpan(0, n));
            read += n;
            var pct = (int)Math.Min(100, read * 100.0 / total);
            if (pct != lastPct && (pct == 100 || pct - lastPct >= 2))
            {
                lastPct = pct;
                progress?.Report(pct);
            }
        }

        progress?.Report(100);
        return Convert.ToHexString(hasher.GetHashAndReset()).ToLowerInvariant();
    }

    /// <summary>
    /// Writes <paramref name="filePath"/>.sha256 with GNU coreutils style:
    /// "{hash}  {filename}\n"
    /// </summary>
    public static string WriteSha256Sidecar(string filePath)
    {
        var hash = Sha256Hex(filePath);
        var sidecar = filePath + ".sha256";
        var line = $"{hash}  {Path.GetFileName(filePath)}\n";
        File.WriteAllText(sidecar, line, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return sidecar;
    }

    /// <summary>
    /// Verifies <paramref name="filePath"/> against sibling <c>.sha256</c> (GNU style).
    /// </summary>
    public static bool TryVerifySidecar(
        string filePath,
        out string message,
        bool requireSidecar = false,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var sidecar = filePath + ".sha256";
        if (!File.Exists(sidecar))
        {
            if (requireSidecar)
            {
                message = "Нет файла " + Path.GetFileName(sidecar);
                return false;
            }

            message = "Sidecar SHA256 отсутствует — проверка пропущена";
            return true;
        }

        if (!File.Exists(filePath))
        {
            message = "Файл для проверки не найден: " + filePath;
            return false;
        }

        try
        {
            var text = File.ReadAllText(sidecar).Trim();
            if (string.IsNullOrEmpty(text))
            {
                message = "Пустой sidecar SHA256";
                return false;
            }

            var expected = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)[0]
                .Trim()
                .ToLowerInvariant();
            var actual = Sha256Hex(filePath, progress, cancellationToken);
            if (!string.Equals(expected, actual, StringComparison.Ordinal))
            {
                message = $"SHA256 не совпадает.\nОжидалось: {expected}\nПолучено: {actual}";
                return false;
            }

            message = "SHA256 OK (" + actual[..Math.Min(16, actual.Length)] + "…)";
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            message = "Ошибка проверки SHA256: " + ex.Message;
            return false;
        }
    }
}
