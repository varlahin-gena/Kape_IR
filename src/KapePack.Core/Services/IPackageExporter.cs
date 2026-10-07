using KapeIR.Core.Models;

namespace KapeIR.Core.Services;

/// <summary>Builds a CollectPack folder / standalone EXE from a package definition.</summary>
public interface IPackageExporter
{
    ExportResult Export(
        PackageDefinition pkg,
        string outputDir,
        ExportOptions? options = null,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Creates an exporter bound to a catalog snapshot.
/// Catalog is per-root and may be rebound; do not cache a single exporter for the app lifetime.
/// </summary>
public interface IPackageExporterFactory
{
    IPackageExporter ForCatalog(KapeCatalog catalog);
}

public sealed class PackageExporterFactory : IPackageExporterFactory
{
    public IPackageExporter ForCatalog(KapeCatalog catalog) => new PackageExporter(catalog);
}
