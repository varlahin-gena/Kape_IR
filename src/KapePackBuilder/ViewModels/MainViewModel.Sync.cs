using CommunityToolkit.Mvvm.Input;
using KapePack.Core.Services;
using KapePackBuilder.Services;

namespace KapePackBuilder.ViewModels;

public partial class MainViewModel
{
    [RelayCommand(CanExecute = nameof(CanUpdateFromGitHub))]
    private async Task UpdateFromGitHubAsync()
    {
        var root = await EnsureCatalogBoundToUiRootAsync();
        if (root is null) return;

        var last = GitHubKapeFilesSync.ReadLastSync(root);
        var lastHash = GitHubKapeFilesSync.ReadLastZipSha256(root);
        var lastLine = last is not null && last.TryGetValue("synced_at", out var at)
            ? $"\n\nПоследняя синхронизация KapeFiles: {at}"
            : "";
        if (!string.IsNullOrEmpty(lastHash))
            lastLine += $"\nSHA256 прошлого ZIP: {lastHash[..Math.Min(16, lastHash.Length)]}…";

        if (!_dialogs.Confirm(
                "Проверить возможность обновления:\n" +
                "• Targets/Modules (EricZimmerman/KapeFiles)\n" +
                "• EZ Tools (Get-ZimmermanTools → Modules\\bin)\n" +
                "• Chainsaw (вложенность Modules\\bin\\chainsaw\\ для Chainsaw.mkape)\n" +
                "Локальные custom-файлы и уже лежащие бинарники не удаляются без замены.\n" +
                "После проверки будет обновлён список путей для меток «GitHub» / «локальный» в каталоге." +
                lastLine + "\n\nЗапустить проверку?",
                "Обновление KAPE"))
            return;

        _syncCts?.Cancel();
        _syncCts?.Dispose();
        _syncCts = new CancellationTokenSource();
        var ct = _syncCts.Token;

        IsSyncing = true;
        StatusText = "Проверка обновлений…";
        var progress = new Progress<string>(m => StatusText = m);
        try
        {
            var report = await _toolkitWs.CheckAsync(root, progress, ct);

            if (!report.KapeFiles.Ok && report.EzTools.Present.Count == 0 && !report.Chainsaw.Ok)
            {
                _dialogs.ShowMessage(
                    report.Summary + "\n\nПроверка KapeFiles не удалась — сеть или корень KAPE.",
                    "Обновление KAPE", DialogIcon.Error);
                StatusText = "Ошибка проверки обновлений";
                return;
            }

            if (!report.AnythingToUpdate)
            {
                _dialogs.ShowMessage(report.Summary + "\n\nОбновление не требуется.", "Обновление KAPE");
                StatusText = "Всё актуально";
                return;
            }

            var applyKape = report.KapeFiles.Ok &&
                            (report.KapeFiles.TargetsAdded + report.KapeFiles.TargetsUpdated +
                             report.KapeFiles.ModulesAdded + report.KapeFiles.ModulesUpdated) > 0;
            var applyEz = report.EzTools.NeedsUpdate;
            var applyChainsaw = report.Chainsaw.NeedsInstall;

            var confirm =
                report.Summary +
                "\n\nПрименить доступные обновления?\n" +
                $"  KapeFiles: {(applyKape ? "да" : "нет")}\n" +
                $"  EZ Tools: {(applyEz ? "да" : "нет")}\n" +
                $"  Chainsaw: {(applyChainsaw ? "да" : "нет")}\n" +
                "Перед перезаписью .tkape/.mkape будет backup в PackBuilder\\sync_backup\\.";

            if (!_dialogs.Confirm(confirm, "Применить обновления"))
            {
                StatusText = "Обновление не применено";
                return;
            }

            StatusText = "Применение обновлений…";
            var result = await _toolkitWs.ApplyAsync(
                root,
                new ToolkitApplyOptions
                {
                    UpdateKapeFiles = applyKape,
                    UpdateEzTools = applyEz,
                    UpdateChainsaw = applyChainsaw,
                    CachedKapeFilesZipPath = report.CachedKapeFilesZipPath,
                    ExpectedZipSha256 = applyKape ? report.KapeFiles.ZipSha256 : null
                },
                progress,
                ct);

            if (result.Ok)
            {
                _dialogs.ShowMessage(result.Message, "Обновление KAPE");
                await ReloadCatalogAsync();
                await LoadBinariesAsync(updateStatus: true);
                StatusText = "Обновление завершено";
            }
            else
            {
                _dialogs.ShowMessage(result.Message, "Обновление KAPE", DialogIcon.Error);
                StatusText = "Обновление завершено с ошибками";
                if (applyKape && result.KapeFiles is { Ok: true })
                    await ReloadCatalogAsync();
                if (applyEz || applyChainsaw)
                    await LoadBinariesAsync(updateStatus: true);
            }
        }
        catch (OperationCanceledException)
        {
            StatusText = "Обновление отменено";
            _dialogs.ShowMessage("Обновление отменено.", "Обновление KAPE", DialogIcon.Warning);
        }
        catch (Exception ex)
        {
            _dialogs.ShowMessage(ex.Message, "Обновление KAPE", DialogIcon.Error);
            StatusText = "Ошибка обновления";
        }
        finally
        {
            IsSyncing = false;
        }
    }

    [RelayCommand]
    private void CancelSync()
    {
        _syncCts?.Cancel();
    }
}
