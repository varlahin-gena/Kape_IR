using KapeIR.Core.Models;
using KapeIR.Core.Services;

namespace KapeIR.Builder.Services;

public sealed class CatalogOpsFacade : ICatalogOpsFacade
{
    public IReadOnlyDictionary<string, string>? ReadLastSync(string kapeRoot)
        => GitHubKapeFilesSync.ReadLastSync(kapeRoot);

    public string? ReadLastZipSha256(string kapeRoot)
        => GitHubKapeFilesSync.ReadLastZipSha256(kapeRoot);

    public IReadOnlyList<NameCollisionFixer.CollisionGroup> FindCollisions(IEnumerable<CatalogItem> items)
        => NameCollisionFixer.FindCollisions(items);

    public Task<(NameCollisionFixer.FixResult Targets, NameCollisionFixer.FixResult Modules)> FixCollisionsAsync(
        string kapeRoot,
        IReadOnlyList<CatalogItem> targets,
        IReadOnlyList<CatalogItem> modules,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            var tf = NameCollisionFixer.FixCollisions(kapeRoot, targets);
            var mf = NameCollisionFixer.FixCollisions(kapeRoot, modules);
            return (tf, mf);
        }, cancellationToken);
    }

    public PackageDefinition LoadPackageJson(string path)
        => PackageExporter.LoadPackageJson(path);

    public PackageDefinition LoadPackageFromCompound(string compoundAbsolutePath, KapeCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        var loaded = KapeCompoundIo.PackageFromCompoundTarget(compoundAbsolutePath);
        loaded.Modules = new List<SelectionEntry>();
        var guess = loaded.TargetCompoundName + "_Modules";
        var mod = catalog.FindModule(guess) ?? catalog.FindModule(guess.TrimStart('!'));
        if (mod?.IsCompound == true)
        {
            foreach (var child in mod.Children)
            {
                var childItem = catalog.FindModule(child);
                loaded.Modules.Add(childItem is null
                    ? new SelectionEntry
                    {
                        Name = Path.GetFileNameWithoutExtension(child),
                        Path = child.EndsWith(".mkape", StringComparison.OrdinalIgnoreCase) ? child : child + ".mkape",
                        Category = "General"
                    }
                    : new SelectionEntry
                    {
                        Name = childItem.Name,
                        Category = childItem.Category,
                        Path = Path.GetFileName(childItem.RelativePath)
                    });
            }
        }

        var root = catalog.KapeRoot;
        if (!string.IsNullOrWhiteSpace(root))
        {
            PackageAssemblyStore.MigrateSessionsIfNeeded(root);
            PackageAssemblyStore.MergeSidecarIntoPackage(loaded, root);
        }

        return loaded;
    }

    public SaveAssemblyResult SaveLocalAssembly(string kapeRoot, PackageDefinition pkg, bool forceLocalCopy = false)
    {
        PackageAssemblyStore.MigrateSessionsIfNeeded(kapeRoot);
        return PackageAssemblyStore.SaveLocal(kapeRoot, pkg, forceLocalCopy: forceLocalCopy);
    }

    public bool DeleteLocalAssembly(string kapeRoot, string name)
    {
        PackageAssemblyStore.MigrateSessionsIfNeeded(kapeRoot);
        return PackageAssemblyStore.DeleteLocal(kapeRoot, name);
    }
}
