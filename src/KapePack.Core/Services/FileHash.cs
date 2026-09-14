using System.Security.Cryptography;
using System.Text;

namespace KapePack.Core.Services;

public static class FileHash
{
    public static string Sha256Hex(string path)
    {
        using var fs = File.OpenRead(path);
        var hash = SHA256.HashData(fs);
        return Convert.ToHexString(hash).ToLowerInvariant();
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
    public static bool TryVerifySidecar(string filePath, out string message, bool requireSidecar = false)
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
            var actual = Sha256Hex(filePath);
            if (!string.Equals(expected, actual, StringComparison.Ordinal))
            {
                message = $"SHA256 не совпадает.\nОжидалось: {expected}\nПолучено: {actual}";
                return false;
            }

            message = "SHA256 OK (" + actual[..Math.Min(16, actual.Length)] + "…)";
            return true;
        }
        catch (Exception ex)
        {
            message = "Ошибка проверки SHA256: " + ex.Message;
            return false;
        }
    }
}
