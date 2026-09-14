namespace KapePack.Core.Services;

/// <summary>
/// CollectPack is portable: work next to the launched EXE (USB, share, local disk).
/// Never prefer %TEMP% / fixed C: paths for package contents or RESULTS.
/// </summary>
public static class CollectPackPaths
{
    /// <summary>Directory that contains the CollectPack EXE (launch / USB folder).</summary>
    public static string ResolveLaunchDirectory(string? processPath = null)
    {
        var self = processPath ?? Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(self))
        {
            try
            {
                var full = Path.GetFullPath(self);
                var dir = Path.GetDirectoryName(full);
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                    return dir;
            }
            catch
            {
                /* fall through */
            }
        }

        return Path.GetFullPath(Environment.CurrentDirectory);
    }

    /// <summary>
    /// Unpacked payload + kape.exe + RESULTS live in &lt;launchDir&gt;\&lt;exeNameWithoutExt&gt;\.
    /// </summary>
    public static string ResolvePackageDirectory(string launchDir, string exePath)
    {
        var name = Path.GetFileNameWithoutExtension(exePath);
        if (string.IsNullOrWhiteSpace(name))
            name = "CollectPack";
        return Path.Combine(Path.GetFullPath(launchDir), name);
    }

    public static void UseAsWorkingDirectory(string launchDir)
    {
        try
        {
            var full = Path.GetFullPath(launchDir);
            if (Directory.Exists(full))
                Directory.SetCurrentDirectory(full);
        }
        catch
        {
            /* non-fatal — relative kape paths still use explicit WorkingDirectory */
        }
    }

    /// <summary>Staging file on the same volume as <paramref name="anchorPath"/> (USB-safe).</summary>
    public static string CreateSiblingTempFile(string anchorPath, string prefix, string extension)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(anchorPath));
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
            dir = Path.GetTempPath();
        return Path.Combine(dir, prefix + Guid.NewGuid().ToString("N") + extension);
    }
}
