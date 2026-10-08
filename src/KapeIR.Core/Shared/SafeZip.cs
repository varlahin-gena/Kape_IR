using System.IO.Compression;

namespace KapeIR.Core.Shared;

/// <summary>
/// Zip extract that rejects entries whose resolved path escapes the destination (zip-slip).
/// </summary>
public static class SafeZip
{
    public static void ExtractToDirectory(
        string zipPath,
        string destinationDirectory,
        bool overwriteFiles = true,
        Action<double>? progress = null)
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
        var total = Math.Max(1, archive.Entries.Count);
        var done = 0;
        var lastPct = -1;
        foreach (var entry in archive.Entries)
        {
            var name = entry.FullName.Replace('\\', '/');
            if (string.IsNullOrWhiteSpace(name))
            {
                done++;
                continue;
            }

            // Directory markers
            if (name.EndsWith('/'))
            {
                var dirPath = ResolveUnderRoot(destRoot, name.TrimEnd('/'));
                Directory.CreateDirectory(dirPath);
            }
            else
            {
                var destPath = ResolveUnderRoot(destRoot, name);
                var parent = Path.GetDirectoryName(destPath);
                if (!string.IsNullOrEmpty(parent))
                    Directory.CreateDirectory(parent);

                entry.ExtractToFile(destPath, overwriteFiles);
            }

            done++;
            if (progress is null) continue;
            var pct = (int)Math.Min(100, done * 100.0 / total);
            if (pct != lastPct && (pct == 100 || pct - lastPct >= 5))
            {
                lastPct = pct;
                progress(pct);
            }
        }

        progress?.Invoke(100);
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
