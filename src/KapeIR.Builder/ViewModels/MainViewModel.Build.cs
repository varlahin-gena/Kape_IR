using CommunityToolkit.Mvvm.Input;
using KapeIR.Core.Models;
using KapeIR.Core.Services;
using KapeIR.Builder.Services;
using KapeIR.Ui.Dialogs;
using Microsoft.Extensions.Logging;

namespace KapeIR.Builder.ViewModels;

public partial class MainViewModel
{
    [RelayCommand(CanExecute = nameof(CanBuildPackage))]
    private async Task BuildPackageAsync()
    {
        PackageEditor.PullFormToPackage();
        // Two-phase packs may ship Phase1-only (empty disk targets) for --phase 1.
        if (Package.Targets.Count == 0 && !PackageEditor.TwoPhaseCollection)
        {
            _dialogs.ShowMessage("Добавьте хотя бы один таргет (или включите двухфазный IR для только-volatile).", "Сборка", DialogIcon.Error);
            return;
        }

        var root = await EnsureCatalogBoundToUiRootAsync();
        if (root is null) return;

        if (!_catalogWs.IsBoundTo(root))
        {
            _dialogs.ShowMessage(
                "Каталог не совпадает с корнем KAPE вверху окна. Обновите каталог и повторите сборку.",
                "Сборка",
                DialogIcon.Error);
            return;
        }

        // Preflight: missing Modules\bin before picking output folder (cancel / continue).
        var includeModuleBinPreview = PackageEditor.TwoPhaseCollection || Package.Modules.Count > 0;
        if (includeModuleBinPreview)
        {
            StatusText = "Проверка Modules\\bin…";
            var preflightPkg = Package.Clone();
            var catalogForCheck = _catalogWs.Catalog;
            var preflight = await _build.CheckModulesBinAsync(catalogForCheck, preflightPkg);
            if (preflight.HasIssues)
            {
                StatusText = "Недостающие бинарники — подтвердите сборку";
                var ok = _dialogs.Confirm(
                    _build.FormatModulesBinConfirm(preflight),
                    "Недостающие бинарники",
                    DialogIcon.Warning);
                if (!ok)
                {
                    StatusText = "Сборка отменена (бинарники)";
                    return;
                }
            }
        }

        var initial = KapeRootPaths.ExportsDir(root);
        Directory.CreateDirectory(initial);
        var outputDir = _dialogs.PickFolder("Выберите папку для сохранения автономного EXE", initial);
        if (outputDir is null) return;

        var pkg = Package;
        var packageDirPreview = Path.Combine(outputDir, PackageDefinition.SafeDir(pkg.Name));
        var overwriteExisting = false;
        if (Directory.Exists(packageDirPreview))
        {
            if (!_dialogs.Confirm(
                    $"Папка пакета уже существует и будет полностью удалена:\n{packageDirPreview}\n\nПродолжить?",
                    "Перезапись пакета",
                    DialogIcon.Warning))
                return;
            overwriteExisting = true;
        }

        // Cancel any in-flight catalog reload so Export reads a stable catalog instance.
        Catalog.CancelPendingReload();

        // Modules\bin: auto for two_phase (Phase1 needs winpmem/live tools) or any selected modules (parsers).
        var includeModuleBin = PackageEditor.TwoPhaseCollection || pkg.Modules.Count > 0;

        // Snapshot: Export must not see mid-reload mutations (reload blocked while IsBuilding).
        var catalogSnapshot = _catalogWs.Catalog;
        var pkgSnapshot = pkg.Clone();

        _buildCts?.Cancel();
        _buildCts?.Dispose();
        _buildCts = new CancellationTokenSource();
        var ct = _buildCts.Token;

        IsBuilding = true;
        StatusText = "Сборка автономного EXE…";
        var progress = new Progress<string>(m => StatusText = m);
        try
        {
            var exportOptions = new ExportOptions
            {
                CopyDependencies = true,
                IncludeModuleBin = includeModuleBin,
                BuildStandaloneExe = true,
                OverwriteExisting = overwriteExisting
            };
            var result = await _build.ExportAsync(
                catalogSnapshot,
                pkgSnapshot,
                outputDir,
                exportOptions,
                progress,
                ct);

            _dialogs.ShowMessage(
                _build.FormatExportSuccessMessage(result, root),
                "Сборка завершена");
            StatusText = result.StandaloneExe is not null
                ? $"Собран EXE: {Path.GetFileName(result.StandaloneExe)}"
                : $"Собран пакет: {Package.Name}";
            await Catalog.ReloadCatalogAsync(promptIfMissing: true);
        }
        catch (OperationCanceledException)
        {
            StatusText = "Сборка отменена";
            _build.CleanupPartialExport(packageDirPreview);
            _dialogs.ShowMessage(
                "Сборка отменена. Неполная папка/EXE удалена (если не была занята).",
                "Сборка",
                DialogIcon.Warning);
        }
        catch (Exception ex)
        {
            LogBuildFailed(ex, pkgSnapshot.Name);
            AppLog.Error(ex, "Build failed for package {PackageName}", pkgSnapshot.Name);
            _dialogs.ShowMessage(ex.Message, "Ошибка сборки", DialogIcon.Error);
            StatusText = "Ошибка сборки";
        }
        finally
        {
            IsBuilding = false;
            _buildCts?.Dispose();
            _buildCts = null;
        }
    }

    [LoggerMessage(EventId = 1002, Level = LogLevel.Error, Message = "Build failed for package {PackageName}")]
    private partial void LogBuildFailed(Exception ex, string packageName);

    [RelayCommand(CanExecute = nameof(CanCancelBuild))]
    private void CancelBuild() => _buildCts?.Cancel();
}
