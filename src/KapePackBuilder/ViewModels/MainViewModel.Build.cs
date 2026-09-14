using CommunityToolkit.Mvvm.Input;
using KapePack.Core.Models;
using KapePack.Core.Services;
using KapePackBuilder.Services;

namespace KapePackBuilder.ViewModels;

public partial class MainViewModel
{
    [RelayCommand(CanExecute = nameof(CanBuildPackage))]
    private async Task BuildPackageAsync()
    {
        PullFormToPackage();
        // Two-phase packs may ship Phase1-only (empty disk targets) for --phase 1.
        if (Package.Targets.Count == 0 && !TwoPhaseCollection)
        {
            _dialogs.ShowMessage("Добавьте хотя бы один таргет (или включите двухфазный IR для только-volatile).", "Сборка", DialogIcon.Error);
            return;
        }

        var root = await EnsureCatalogBoundToUiRootAsync();
        if (root is null) return;

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

        var catalog = _catalog;
        if (!_catalogWs.IsBoundTo(root))
        {
            _dialogs.ShowMessage(
                "Каталог не совпадает с корнем KAPE вверху окна. Обновите каталог и повторите сборку.",
                "Сборка",
                DialogIcon.Error);
            return;
        }

        var makeZip = MakeZip;
        // Modules\bin: auto for two_phase (Phase1 needs winpmem/live tools) or any selected modules (parsers).
        var includeModuleBin = TwoPhaseCollection || pkg.Modules.Count > 0;

        _buildCts?.Cancel();
        _buildCts?.Dispose();
        _buildCts = new CancellationTokenSource();
        var ct = _buildCts.Token;

        IsBuilding = true;
        StatusText = "Сборка автономного EXE…";
        var progress = new Progress<string>(m => StatusText = m);
        try
        {
            var result = await Task.Run(() =>
            {
                var exporter = new PackageExporter(catalog);
                return exporter.Export(
                    pkg,
                    outputDir,
                    installIntoKape: false,
                    makeZip: makeZip,
                    copyDependencies: true,
                    includeModuleBin: includeModuleBin,
                    buildStandaloneExe: true,
                    overwriteExisting: overwriteExisting,
                    progress: progress,
                    cancellationToken: ct);
            }, ct);

            var stubInfo = "";
            try
            {
                if (StandaloneExeBuilder.TryGetEmbeddedStubInfo(out var embSize, out _))
                    stubInfo = $"\nStub: встроен в Pack Builder ({embSize / (1024 * 1024)} МБ, GUI)";
                else
                {
                    var stub = StandaloneExeBuilder.ResolveStubPath(root);
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
            _dialogs.ShowMessage(msg.Trim(), "Сборка завершена");
            StatusText = result.StandaloneExe is not null
                ? $"Собран EXE: {Path.GetFileName(result.StandaloneExe)}"
                : $"Собран пакет: {Package.Name}";
            await ReloadCatalogAsync();
        }
        catch (OperationCanceledException)
        {
            StatusText = "Сборка отменена";
            TryCleanupPartialExport(packageDirPreview);
            _dialogs.ShowMessage(
                "Сборка отменена. Неполная папка/EXE удалена (если не была занята).",
                "Сборка",
                DialogIcon.Warning);
        }
        catch (Exception ex)
        {
            AppLog.Error("Build failed", ex);
            _dialogs.ShowMessage(ex.Message, "Ошибка сборки", DialogIcon.Error);
            StatusText = "Ошибка сборки";
        }
        finally
        {
            IsBuilding = false;
        }
    }

    private static void TryCleanupPartialExport(string packageDir)
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

    [RelayCommand(CanExecute = nameof(CanCancelBuild))]
    private void CancelBuild() => _buildCts?.Cancel();
}
