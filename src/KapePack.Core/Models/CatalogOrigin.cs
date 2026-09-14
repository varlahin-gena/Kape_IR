namespace KapePack.Core.Models;

/// <summary>
/// Provenance of a Targets/Modules catalog file relative to EricZimmerman/KapeFiles sync.
/// </summary>
public enum CatalogOrigin
{
    /// <summary>No upstream path inventory yet (sync never wrote last_kapefiles_paths.txt).</summary>
    Unknown = 0,
    /// <summary>Relative path was present in the last GitHub KapeFiles zip.</summary>
    GitHub = 1,
    /// <summary>File exists only locally (or Pack Builder–authored when inventory missing).</summary>
    Local = 2
}

public static class CatalogOriginLabels
{
    public const string PackBuilderAuthorPrefix = "KAPE Pack Builder";

    public static string Display(CatalogOrigin origin) => origin switch
    {
        CatalogOrigin.GitHub => "GitHub",
        CatalogOrigin.Local => "локальный",
        _ => "?"
    };

    public static string Short(CatalogOrigin origin) => origin switch
    {
        CatalogOrigin.GitHub => "[GH]",
        CatalogOrigin.Local => "[лок.]",
        _ => "[?]"
    };

    public static string SearchToken(CatalogOrigin origin) => origin switch
    {
        CatalogOrigin.GitHub => "github gh upstream",
        CatalogOrigin.Local => "локальный local custom",
        _ => "unknown неизвестно"
    };

    public static bool LooksLikePackBuilderAuthor(string? author)
        => !string.IsNullOrWhiteSpace(author) &&
           author.StartsWith(PackBuilderAuthorPrefix, StringComparison.OrdinalIgnoreCase);

    public static CatalogOrigin Resolve(string relativePath, string? author, IReadOnlySet<string>? upstreamPaths)
    {
        var norm = NormalizeRelativePath(relativePath);
        if (upstreamPaths is not null)
        {
            if (upstreamPaths.Contains(norm))
                return CatalogOrigin.GitHub;
            return CatalogOrigin.Local;
        }

        if (LooksLikePackBuilderAuthor(author))
            return CatalogOrigin.Local;

        return CatalogOrigin.Unknown;
    }

    public static string NormalizeRelativePath(string relativePath)
        => (relativePath ?? "").Replace('\\', '/').TrimStart('/');
}
