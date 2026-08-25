using CommunityToolkit.Mvvm.Input;
using KapePackBuilder.Services;

namespace KapePackBuilder.ViewModels;

public partial class MainViewModel
{
    [RelayCommand(CanExecute = nameof(CanBuildPackage))]
    private async Task BuildPackageAsync()
    {
        PullFormToPackage();
        if (Package.Targets.Count == 0)
        {
            _dialogs.ShowMessage("Добавьте хотя бы один таргет.", "Сборка", DialogIcon.Error);
            return;
        }

        var initial = Path.Combine(KapeRoot, "PackBuilder", "exports");
        Directory.CreateDirectory(initial);
        var outputDir = _dialogs.PickFolder("Выберите папку для сохранения автономного EXE", initial);
        if (outputDir is null) return;

        var pkg = Package;
        var catalog = _catalog;
        var installIntoKape = InstallIntoKape;
        var makeZip = MakeZip;
        var includeModuleBin = IncludeModuleBin;

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
                    installIntoKape: installIntoKape,
                    makeZip: makeZip,
                    copyDependencies: true,
                    includeModuleBin: includeModuleBin,
                    buildStandaloneExe: true,
                    progress: progress,
                    cancellationToken: ct);
            }, ct);

            var stubInfo = "";
            try
            {
                var stub = StandaloneExeBuilder.ResolveStubPath();
                stubInfo = $"\nStub: {stub} ({new FileInfo(stub).Length / (1024 * 1024)} МБ, GUI)";
            }
            catch { /* ignore */ }

            var msg = result.StandaloneExe is not null
                ? $"Автономный EXE:\n{result.StandaloneExe}{stubInfo}\n\n"
                : "";
            if (result.StandaloneExe is not null && File.Exists(result.StandaloneExe + ".sha256"))
                msg += $"SHA256: {result.StandaloneExe}.sha256\n\n";
            msg += $"Папка пакета:\n{result.PackageDir}";
            if (result.ZipFile is not null) msg += $"\nZIP: {result.ZipFile}";
            if (result.InstalledTarget is not null) msg += $"\nТакже установлено в KAPE: {result.InstalledTarget}";
            if (result.Warnings.Count > 0)
                msg += "\n\nПредупреждения:\n - " + string.Join("\n - ", result.Warnings.Take(12));
            _dialogs.ShowMessage(msg, "Сборка завершена");
            StatusText = result.StandaloneExe is not null
                ? $"Собран EXE: {Path.GetFileName(result.StandaloneExe)}"
                : $"Собран пакет: {Package.Name}";
            await ReloadCatalogAsync();
        }
        catch (OperationCanceledException)
        {
            StatusText = "Сборка отменена";
            _dialogs.ShowMessage("Сборка отменена. Неполная папка/EXE в выбранном каталоге может остаться — удалите вручную при необходимости.", "Сборка", DialogIcon.Warning);
        }
        catch (Exception ex)
        {
            _dialogs.ShowMessage(ex.Message, "Ошибка сборки", DialogIcon.Error);
            StatusText = "Ошибка сборки";
        }
        finally
        {
            IsBuilding = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanCancelBuild))]
    private void CancelBuild() => _buildCts?.Cancel();
}
