using System.Security.Cryptography;
using System.Text;

namespace KapePackBuilder.Services;

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
}
