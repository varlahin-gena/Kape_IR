using CommunityToolkit.Mvvm.Input;
using KapePackBuilder.Services;

namespace KapePackBuilder.ViewModels;

public partial class MainViewModel
{
    [RelayCommand(CanExecute = nameof(CanUpdateFromGitHub))]
    private async Task UpdateFromGitHubAsync()
    {
        var last = GitHubKapeFilesSync.ReadLastSync(KapeRoot);
        var lastHash = GitHubKapeFilesSync.ReadLastZipSha256(KapeRoot);
        var lastLine = last is not null && last.TryGetValue("synced_at", out var at)
            ? $"\n\nПоследняя синхронизация: {at}"
            : "";
        if (!string.IsNullOrEmpty(lastHash))
            lastLine += $"\nSHA256 прошлого ZIP: {lastHash[..Math.Min(16, lastHash.Length)]}…";

        if (!_dialogs.Confirm(
                "Скачать KapeFiles с GitHub и показать, что изменится (без записи).\n" +
                $"https://github.com/{GitHubKapeFilesSync.Repo}\n" +
                "Локальные custom-файлы и Modules\\bin сохранятся." +
                lastLine + "\n\nПродолжить проверку?",
                "Обновление с GitHub"))
            return;

        _syncCts?.Cancel();
        _syncCts?.Dispose();
        _syncCts = new CancellationTokenSource();
        var ct = _syncCts.Token;

        IsSyncing = true;
        StatusText = "Проверка изменений с GitHub…";
        var progress = new Progress<string>(m => StatusText = m);
        var root = KapeRoot;
        try
        {
            var preview = await GitHubKapeFilesSync.SyncAsync(
                root, progress, ct, options: new SyncOptions { DryRun = true });

            if (!preview.Ok)
            {
                _dialogs.ShowMessage(preview.Message, "Синхронизация GitHub", DialogIcon.Error);
                StatusText = "Ошибка проверки GitHub";
                return;
            }

            var changed = preview.TargetsAdded + preview.TargetsUpdated +
                          preview.ModulesAdded + preview.ModulesUpdated;
            var hashNote = "";
            if (!string.IsNullOrEmpty(preview.ZipSha256))
            {
                if (!string.IsNullOrEmpty(lastHash) &&
                    !string.Equals(lastHash, preview.ZipSha256, StringComparison.OrdinalIgnoreCase))
                    hashNote = "\n\n⚠ SHA256 ZIP изменился с прошлой синхронизации.";
                else if (string.IsNullOrEmpty(lastHash))
                    hashNote = $"\n\nSHA256 ZIP: {preview.ZipSha256[..Math.Min(16, preview.ZipSha256.Length)]}…";
            }

            var samples = "";
            if (preview.AddedSamples.Count > 0)
                samples += "\n+\n" + string.Join("\n", preview.AddedSamples.Take(8).Select(s => "  + " + s));
            if (preview.UpdatedSamples.Count > 0)
                samples += "\n~\n" + string.Join("\n", preview.UpdatedSamples.Take(8).Select(s => "  ~ " + s));
            if (preview.AddedSamples.Count + preview.UpdatedSamples.Count > 16)
                samples += "\n  …";

            if (changed == 0)
            {
                _dialogs.ShowMessage(
                    preview.Message + hashNote + "\n\nЗапись не требуется.",
                    "Синхронизация GitHub");
                StatusText = "Изменений с GitHub нет";
                return;
            }

            if (!_dialogs.Confirm(
                    preview.Message + hashNote + samples +
                    "\n\nПрименить? Перед перезаписью будет backup в PackBuilder\\sync_backup\\.",
                    "Применить синхронизацию"))
            {
                StatusText = "Синхронизация не применена";
                return;
            }

            StatusText = "Применение синхронизации…";
            var result = await GitHubKapeFilesSync.SyncAsync(
                root, progress, ct,
                options: new SyncOptions
                {
                    DryRun = false,
                    BackupBeforeOverwrite = true,
                    RememberZipSha256 = true
                });

            if (result.Ok)
            {
                var msg = result.Message;
                if (!string.IsNullOrEmpty(result.BackupDir))
                    msg += $"\n\nBackup: {result.BackupDir}";
                if (!string.IsNullOrEmpty(result.ZipSha256))
                    msg += $"\nSHA256: {result.ZipSha256}";
                _dialogs.ShowMessage(msg, "Синхронизация GitHub");
                await ReloadCatalogAsync();
            }
            else
            {
                _dialogs.ShowMessage(result.Message, "Синхронизация GitHub", DialogIcon.Error);
                StatusText = "Ошибка синхронизации GitHub";
            }
        }
        catch (OperationCanceledException)
        {
            StatusText = "Синхронизация отменена";
            _dialogs.ShowMessage("Синхронизация отменена.", "Синхронизация GitHub", DialogIcon.Warning);
        }
        catch (Exception ex)
        {
            _dialogs.ShowMessage(ex.Message, "Синхронизация GitHub", DialogIcon.Error);
            StatusText = "Ошибка синхронизации GitHub";
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
