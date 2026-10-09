using KapeIR.Core.Models;
using KapeIR.Core.Services;

namespace KapeIR.Builder.Services;

/// <summary>Use-case API for catalog maintenance, assembly load, and sync metadata.</summary>
public interface ICatalogOpsFacade
{
    IReadOnlyDictionary<string, string>? ReadLastSync(string kapeRoot);
    string? ReadLastZipSha256(string kapeRoot);

    IReadOnlyList<NameCollisionFixer.CollisionGroup> FindCollisions(IEnumerable<CatalogItem> items);

    Task<(NameCollisionFixer.FixResult Targets, NameCollisionFixer.FixResult Modules)> FixCollisionsAsync(
        string kapeRoot,
        IReadOnlyList<CatalogItem> targets,
        IReadOnlyList<CatalogItem> modules,
        CancellationToken cancellationToken = default);

    PackageDefinition LoadPackageJson(string path);

    /// <summary>
    /// Load a compound target as a package and optionally seed modules from a matching
    /// <c>{name}_Modules</c> compound module when present in the catalog.
    /// Merges PackBuilder/assemblies sidecar IR fields when present.
    /// </summary>
    PackageDefinition LoadPackageFromCompound(string compoundAbsolutePath, KapeCatalog catalog);

    /// <summary>Save editor package as local compounds + sidecar (forks GitHub names).</summary>
    SaveAssemblyResult SaveLocalAssembly(string kapeRoot, PackageDefinition pkg, bool forceLocalCopy = false);

    /// <summary>Delete a local assembly (refuses GitHub upstream paths).</summary>
    bool DeleteLocalAssembly(string kapeRoot, string name);
}
