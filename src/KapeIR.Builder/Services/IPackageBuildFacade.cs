using KapeIR.Core.Models;
using KapeIR.Core.Services;

namespace KapeIR.Builder.Services;

/// <summary>Use-case API for package preflight + export (keeps Build VM off Core statics).</summary>
public interface IPackageBuildFacade
{
    Task<ModulesBinPreflight.Result> CheckModulesBinAsync(
        KapeCatalog catalog,
        PackageDefinition package,
        CancellationToken cancellationToken = default);

    string FormatModulesBinConfirm(ModulesBinPreflight.Result result);

    Task<ExportResult> ExportAsync(
        KapeCatalog catalog,
        PackageDefinition package,
        string outputDir,
        ExportOptions options,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default);

    string FormatExportSuccessMessage(ExportResult result, string kapeRoot);

    void CleanupPartialExport(string packageDir);
}
