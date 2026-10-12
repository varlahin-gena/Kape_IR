namespace KapeIR.Core.Models;

/// <summary>
/// Provenance of a Targets/Modules catalog file relative to EricZimmerman/KapeFiles sync.
/// </summary>
public enum CatalogOrigin
{
    /// <summary>No upstream path inventory yet (sync never wrote last_kapefiles_paths.txt).</summary>
    Unknown = 0,
    /// <summary>Relative path was present in the last GitHub KapeFiles zip.</summary>
    GitHub = 1,
    /// <summary>File exists only locally and is not first-party KapeIR / Pack Builder content.</summary>
    Local = 2,
    /// <summary>First-party KapeIR (Phase1, Hayabusa v41, Pack Builder–authored assemblies).</summary>
    KapeIR = 3
}

public static class CatalogOriginLabels
{
    public const string PackBuilderAuthorPrefix = "KapeIR";
    public const string LegacyPackBuilderAuthorPrefix = "KAPE Pack Builder";

    public static string Display(CatalogOrigin origin) => origin switch
    {
        CatalogOrigin.GitHub => "GitHub",
        CatalogOrigin.KapeIR => "KapeIR",
        CatalogOrigin.Local => "локальный",
        _ => "?"
    };

    public static string Short(CatalogOrigin origin) => origin switch
    {
        CatalogOrigin.GitHub => "[GH]",
        CatalogOrigin.KapeIR => "[KapeIR]",
        CatalogOrigin.Local => "[лок.]",
        _ => "[?]"
    };

    public static string SearchToken(CatalogOrigin origin) => origin switch
    {
        CatalogOrigin.GitHub => "github gh upstream",
        CatalogOrigin.KapeIR => "kapeir phase1 hayabusa pack builder",
        CatalogOrigin.Local => "локальный local custom",
        _ => "unknown неизвестно"
    };

    public static bool LooksLikePackBuilderAuthor(string? author)
        => !string.IsNullOrWhiteSpace(author) &&
           (author.StartsWith(PackBuilderAuthorPrefix, StringComparison.OrdinalIgnoreCase)
            || author.StartsWith(LegacyPackBuilderAuthorPrefix, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// First-party paths seeded/maintained by KapeIR (Phase1 + Hayabusa v41),
    /// even if Author has not been rewritten yet.
    /// </summary>
    public static bool LooksLikeKapeIrFirstPartyPath(string? relativePath)
    {
        var norm = NormalizeRelativePath(relativePath ?? "");
        if (norm.Length == 0)
            return false;

        if (norm.StartsWith("Modules/Apps/Local/Phase1/", StringComparison.OrdinalIgnoreCase)
            || norm.StartsWith("Modules/Apps/Local/Hayabusa/", StringComparison.OrdinalIgnoreCase))
            return true;

        if (norm.StartsWith("Modules/Compound/Phase1_", StringComparison.OrdinalIgnoreCase))
            return true;

        // Hayabusa 4.1+ offline compound (not stock Compound/Hayabusa.mkape)
        if (norm.Equals("Modules/Compound/Hayabusa_Offline_v41.mkape", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    public static bool IsKapeIrOrigin(string relativePath, string? author)
        => LooksLikePackBuilderAuthor(author) || LooksLikeKapeIrFirstPartyPath(relativePath);

    public static CatalogOrigin Resolve(string relativePath, string? author, IReadOnlySet<string>? upstreamPaths)
    {
        var norm = NormalizeRelativePath(relativePath);
        if (upstreamPaths is not null)
        {
            if (upstreamPaths.Contains(norm))
                return CatalogOrigin.GitHub;
            return IsKapeIrOrigin(norm, author) ? CatalogOrigin.KapeIR : CatalogOrigin.Local;
        }

        if (IsKapeIrOrigin(norm, author))
            return CatalogOrigin.KapeIR;

        return CatalogOrigin.Unknown;
    }

    public static string NormalizeRelativePath(string relativePath)
        => (relativePath ?? "").Replace('\\', '/').TrimStart('/');
}
