using KapePack.Core.Models;

namespace KapePack.Core.Services;

/// <summary>Builds a CollectPack folder / standalone EXE from a package definition.</summary>
public interface IPackageExporter
{
    ExportResult Export(
        PackageDefinition pkg,
        string outputDir,
        bool installIntoKape = false,
        bool makeZip = false,
        bool copyDependencies = true,
        bool includeModuleBin = true,
        bool buildStandaloneExe = true,
        bool overwriteExisting = false,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default);
}
