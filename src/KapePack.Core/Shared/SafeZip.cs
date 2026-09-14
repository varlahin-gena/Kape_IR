using System.IO.Compression;

namespace KapePack.Core.Shared;

/// <summary>
/// Zip extract that rejects entries whose resolved path escapes the destination (zip-slip).
/// </summary>
public static class SafeZip
{
    public static void ExtractToDirectory(string zipPath, string destinationDirectory, bool overwriteFiles = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(zipPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationDirectory);
        if (!File.Exists(zipPath))
            throw new FileNotFoundException("ZIP not found", zipPath);

        var destRoot = Path.GetFullPath(destinationDirectory);
        if (!destRoot.EndsWith(Path.DirectorySeparatorChar) && !destRoot.EndsWith(Path.AltDirectorySeparatorChar))
            destRoot += Path.DirectorySeparatorChar;

        Directory.CreateDirectory(destRoot);

        using var archive = ZipFile.OpenRead(zipPath);
        foreach (var entry in archive.Entries)
        {
            var name = entry.FullName.Replace('\\', '/');
            if (string.IsNullOrWhiteSpace(name))
                continue;

            // Directory markers
            if (name.EndsWith('/'))
            {
                var dirPath = ResolveUnderRoot(destRoot, name.TrimEnd('/'));
                Directory.CreateDirectory(dirPath);
                continue;
            }

            var destPath = ResolveUnderRoot(destRoot, name);
            var parent = Path.GetDirectoryName(destPath);
            if (!string.IsNullOrEmpty(parent))
                Directory.CreateDirectory(parent);

            entry.ExtractToFile(destPath, overwriteFiles);
        }
    }

    public static string ResolveUnderRoot(string destRootWithSep, string relativeEntry)
    {
        // Normalize and block absolute / rooted paths inside the zip.
        var combined = Path.GetFullPath(Path.Combine(destRootWithSep, relativeEntry));
        if (!combined.StartsWith(destRootWithSep, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Zip-slip: entry escapes destination ({relativeEntry})");
        return combined;
    }
}
