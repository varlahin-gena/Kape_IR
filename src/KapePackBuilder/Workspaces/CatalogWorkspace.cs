using KapePack.Core.Services;

namespace KapePackBuilder.Workspaces;

/// <summary>Owns the active <see cref="KapeCatalog"/> bound to the UI KAPE root.</summary>
public sealed class CatalogWorkspace
{
    public KapeCatalog Catalog { get; private set; }

    public CatalogWorkspace(string? initialRoot = null)
    {
        Catalog = new KapeCatalog(string.IsNullOrWhiteSpace(initialRoot) ? "" : initialRoot);
    }

    public bool IsBoundTo(string root) => KapeRootPaths.SameRoot(Catalog.KapeRoot, root);

    /// <summary>
    /// Reload catalog for <paramref name="root"/>. Reuses the same instance when possible
    /// so the process-wide file parse cache stays warm.
    /// </summary>
    public CatalogRefreshStats Reload(string root)
    {
        root = KapeRootPaths.Normalize(root);
        if (!KapeRootPaths.SameRoot(Catalog.KapeRoot, root))
            Catalog.Rebind(root);
        Catalog.Refresh();
        return Catalog.LastRefreshStats;
    }
}
