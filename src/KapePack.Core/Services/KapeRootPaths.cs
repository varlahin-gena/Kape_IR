namespace KapePack.Core.Services;

/// <summary>
/// All Builder state for a given KAPE tree lives under the UI-selected root
/// (…/PackBuilder/…), never under a guessed BaseDirectory or another install.
/// </summary>
public static class KapeRootPaths
{
    public static string Normalize(string? kapeRoot)
    {
        if (string.IsNullOrWhiteSpace(kapeRoot))
            throw new ArgumentException("Корень KAPE не задан.", nameof(kapeRoot));
        return Path.GetFullPath(kapeRoot.Trim());
    }

    public static bool LooksLikeKapeRoot(string? kapeRoot)
    {
        if (string.IsNullOrWhiteSpace(kapeRoot)) return false;
        try
        {
            var root = Path.GetFullPath(kapeRoot.Trim());
            return Directory.Exists(Path.Combine(root, "Targets"));
        }
        catch
        {
            return false;
        }
    }

    public static string PackBuilderDir(string kapeRoot)
        => Path.Combine(Normalize(kapeRoot), "PackBuilder");

    public static string ExportsDir(string kapeRoot)
        => Path.Combine(PackBuilderDir(kapeRoot), "exports");

    public static string StubCacheDir(string kapeRoot)
        => Path.Combine(PackBuilderDir(kapeRoot), "stub");

    public static string KapeFilesZipCachePath(string kapeRoot)
        => Path.Combine(PackBuilderDir(kapeRoot), "cache", "kapefiles_last_check.zip");

    public static bool SameRoot(string? a, string? b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return false;
        try
        {
            return string.Equals(
                Path.GetFullPath(a.Trim()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                Path.GetFullPath(b.Trim()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// kape.exe must sit in the selected root — never recurse into PackBuilder/exports
    /// (previous CollectPack trees contain their own kape.exe and cause collisions).
    /// </summary>
    public static string? FindKapeExeInRoot(string kapeRoot)
    {
        var root = Normalize(kapeRoot);
        foreach (var name in new[] { "kape.exe", "KAPE.exe", "Kape.exe" })
        {
            var p = Path.Combine(root, name);
            if (File.Exists(p)) return p;
        }

        return null;
    }
}
