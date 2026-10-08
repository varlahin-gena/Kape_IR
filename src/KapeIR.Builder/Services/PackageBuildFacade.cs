using KapeIR.Core.Models;
using KapeIR.Core.Services;

namespace KapeIR.Builder.Services;

public sealed class PackageBuildFacade : IPackageBuildFacade
{
    private readonly IPackageExporterFactory _exporters;

    public PackageBuildFacade(IPackageExporterFactory exporters)
    {
        _exporters = exporters ?? throw new ArgumentNullException(nameof(exporters));
    }

    public Task<ModulesBinPreflight.Result> CheckModulesBinAsync(
        KapeCatalog catalog,
        PackageDefinition package,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(package);
        return Task.Run(() => ModulesBinPreflight.Check(catalog, package), cancellationToken);
    }

    public string FormatModulesBinConfirm(ModulesBinPreflight.Result result)
        => ModulesBinPreflight.FormatConfirmMessage(result);

    public Task<ExportResult> ExportAsync(
        KapeCatalog catalog,
        PackageDefinition package,
        string outputDir,
        ExportOptions options,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(package);
        return Task.Run(() =>
        {
            var exporter = _exporters.ForCatalog(catalog);
            return exporter.Export(package, outputDir, options, progress, cancellationToken);
        }, cancellationToken);
    }

    public string FormatExportSuccessMessage(ExportResult result, string kapeRoot)
    {
        ArgumentNullException.ThrowIfNull(result);
        var stubInfo = "";
        try
        {
            if (StandaloneExeBuilder.TryGetEmbeddedStubInfo(out var embSize, out _))
                stubInfo = $"\nStub: встроен в KapeIR ({embSize / (1024 * 1024)} МБ, GUI)";
            else
            {
                var stub = StandaloneExeBuilder.ResolveStubPath(kapeRoot);
                stubInfo = $"\nStub: {stub} ({new FileInfo(stub).Length / (1024 * 1024)} МБ, GUI)";
            }
        }
        catch { /* ignore */ }

        var msg = result.StandaloneExe is not null
            ? $"Автономный EXE:\n{result.StandaloneExe}{stubInfo}\n"
            : "";
        if (result.StandaloneExe is not null && File.Exists(result.StandaloneExe + ".sha256"))
            msg += $"\nSHA256: {result.StandaloneExe}.sha256\n";
        if (!string.IsNullOrEmpty(result.PackageDir))
            msg += $"\nПапка пакета:\n{result.PackageDir}";
        if (result.ZipFile is not null) msg += $"\nZIP: {result.ZipFile}";
        if (result.Warnings.Count > 0)
            msg += "\n\nПредупреждения:\n - " + string.Join("\n - ", result.Warnings.Take(12));
        return msg.Trim();
    }

    public void CleanupPartialExport(string packageDir)
    {
        try
        {
            if (Directory.Exists(packageDir))
                Directory.Delete(packageDir, true);
        }
        catch (Exception ex)
        {
            AppLog.Warn("Partial export cleanup failed: " + ex.Message);
        }

        try
        {
            var exe = packageDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + ".exe";
            if (File.Exists(exe))
                File.Delete(exe);
            if (File.Exists(exe + ".sha256"))
                File.Delete(exe + ".sha256");
        }
        catch (Exception ex)
        {
            AppLog.Warn("Partial EXE cleanup failed: " + ex.Message);
        }
    }
}
