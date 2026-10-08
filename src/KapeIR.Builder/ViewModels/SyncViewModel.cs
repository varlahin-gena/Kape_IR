using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KapeIR.Core.Services;
using KapeIR.Builder.Services;
using KapeIR.Builder.Workspaces;
using KapeIR.Ui.Dialogs;

namespace KapeIR.Builder.ViewModels;

public sealed partial class SyncViewModel : ObservableObject, IDisposable
{
    private readonly IBuilderShell _shell;
    private readonly ToolkitUpdateWorkspace _toolkitWs;
    private CancellationTokenSource? _syncCts;
    private bool _disposed;

    internal SyncViewModel(IBuilderShell shell, ToolkitUpdateWorkspace toolkitWorkspace)
    {
        _shell = shell ?? throw new ArgumentNullException(nameof(shell));
        _toolkitWs = toolkitWorkspace ?? throw new ArgumentNullException(nameof(toolkitWorkspace));
    }

    private bool CanUpdateFromGitHub() => !_shell.IsSyncing && !_shell.IsBuilding;

    [RelayCommand(CanExecute = nameof(CanUpdateFromGitHub))]
    private async Task UpdateFromGitHubAsync()
    {
        var root = await _shell.EnsureCatalogBoundToUiRootAsync();
        if (root is null) return;

        var last = _toolkitWs.ReadLastSync(root);
        var lastHash = _toolkitWs.ReadLastZipSha256(root);
        var lastLine = last is not null && last.TryGetValue("synced_at", out var at)
            ? $"\n\nПоследняя синхронизация KapeFiles: {at}"
            : "";
        if (!string.IsNullOrEmpty(lastHash))
            lastLine += $"\nSHA256 прошлого ZIP: {lastHash[..Math.Min(16, lastHash.Length)]}…";

        if (!_shell.Dialogs.Confirm(
                "Проверить возможность обновления:\n" +
                "• Targets/Modules (EricZimmerman/KapeFiles)\n" +
                "• EZ Tools (Get-ZimmermanTools → Modules\\bin)\n" +
                "• Chainsaw (вложенность Modules\\bin\\chainsaw\\ для Chainsaw.mkape)\n" +
                "• Hayabusa + rules (Modules\\bin\\hayabusa\\ — exe, config, ~4500 rules)\n" +
                "• dfir_ntfs (Modules\\bin\\dfir_ntfs — нужен python.exe на цели)\n" +
                "• RegRipper plugins (докачка недостающих .pl / правка профилей)\n" +
                "Локальные custom-файлы и уже лежащие бинарники не удаляются без замены.\n" +
                "После проверки будет обновлён список путей для меток «GitHub» / «локальный» в каталоге." +
                lastLine + "\n\nЗапустить проверку?",
                "Обновление KAPE"))
            return;

        _syncCts?.Cancel();
        _syncCts?.Dispose();
        _syncCts = new CancellationTokenSource();
        var ct = _syncCts.Token;

        // Freeze catalog for the duration of sync (reload blocked while IsSyncing).
        _shell.Catalog.CancelPendingReload();

        _shell.IsSyncing = true;
        _shell.StatusText = "Проверка обновлений…";
        var progress = new Progress<string>(m => _shell.StatusText = m);
        try
        {
            var report = await _toolkitWs.CheckAsync(root, progress, ct);

            if (!report.KapeFiles.Ok && report.EzTools.Present.Count == 0 && !report.Chainsaw.Ok
                && !report.Hayabusa.Ok && !report.DfirNtfs.Ok && !report.RegRipper.Ok)
            {
                _shell.Dialogs.ShowMessage(
                    report.Summary + "\n\nПроверка KapeFiles не удалась — сеть или корень KAPE.",
                    "Обновление KAPE", DialogIcon.Error);
                _shell.StatusText = "Ошибка проверки обновлений";
                return;
            }

            if (!report.AnythingToUpdate)
            {
                _shell.Dialogs.ShowMessage(report.Summary + "\n\nОбновление не требуется.", "Обновление KAPE");
                _shell.StatusText = "Всё актуально";
                return;
            }

            var applyKape = report.KapeFiles.Ok &&
                            (report.KapeFiles.TargetsAdded + report.KapeFiles.TargetsUpdated +
                             report.KapeFiles.ModulesAdded + report.KapeFiles.ModulesUpdated) > 0;
            var applyEz = report.EzTools.NeedsUpdate;
            var applyChainsaw = report.Chainsaw.NeedsInstall;
            var applyHayabusa = report.Hayabusa.NeedsUpdate;
            var applyDfir = report.DfirNtfs.NeedsInstall;
            var applyRr = report.RegRipper.NeedsRepair;

            var confirm =
                report.Summary +
                "\n\nПрименить доступные обновления?\n" +
                $"  KapeFiles: {(applyKape ? "да" : "нет")}\n" +
                $"  EZ Tools: {(applyEz ? "да" : "нет")}\n" +
                $"  Chainsaw: {(applyChainsaw ? "да" : "нет")}\n" +
                $"  Hayabusa + rules: {(applyHayabusa ? "да" : "нет")}\n" +
                $"  dfir_ntfs: {(applyDfir ? "да" : "нет")}\n" +
                $"  RegRipper plugins: {(applyRr ? "да" : "нет")}\n" +
                "Перед перезаписью .tkape/.mkape будет backup в PackBuilder\\sync_backup\\.";

            if (!_shell.Dialogs.Confirm(confirm, "Применить обновления"))
            {
                _shell.StatusText = "Обновление не применено";
                return;
            }

            _shell.StatusText = "Применение обновлений…";
            var result = await _toolkitWs.ApplyAsync(
                root,
                new ToolkitApplyOptions
                {
                    UpdateKapeFiles = applyKape,
                    UpdateEzTools = applyEz,
                    UpdateChainsaw = applyChainsaw,
                    UpdateHayabusa = applyHayabusa,
                    UpdateDfirNtfs = applyDfir,
                    RepairRegRipperPlugins = applyRr,
                    CachedKapeFilesZipPath = report.CachedKapeFilesZipPath,
                    ExpectedZipSha256 = applyKape ? report.KapeFiles.ZipSha256 : null,
                    HayabusaDownloadUrl = applyHayabusa ? report.Hayabusa.DownloadUrl : null,
                    HayabusaReleaseTag = applyHayabusa ? report.Hayabusa.LatestTag : null
                },
                progress,
                ct);

            if (result.Ok)
            {
                _shell.Dialogs.ShowMessage(result.Message, "Обновление KAPE");
                await _shell.Catalog.ReloadCatalogAsync(promptIfMissing: true);
                await _shell.Binaries.LoadBinariesAsync(updateStatus: true);
                _shell.StatusText = "Обновление завершено";
            }
            else
            {
                _shell.Dialogs.ShowMessage(result.Message, "Обновление KAPE", DialogIcon.Error);
                _shell.StatusText = "Обновление завершено с ошибками";
                if (applyKape && result.KapeFiles is { Ok: true })
                    await _shell.Catalog.ReloadCatalogAsync(promptIfMissing: true);
                if (applyEz || applyChainsaw || applyHayabusa || applyDfir || applyRr)
                    await _shell.Binaries.LoadBinariesAsync(updateStatus: true);
            }
        }
        catch (OperationCanceledException)
        {
            _shell.StatusText = "Обновление отменено";
            _shell.Dialogs.ShowMessage("Обновление отменено.", "Обновление KAPE", DialogIcon.Warning);
        }
        catch (Exception ex)
        {
            _shell.Dialogs.ShowMessage(ex.Message, "Обновление KAPE", DialogIcon.Error);
            _shell.StatusText = "Ошибка обновления";
        }
        finally
        {
            _shell.IsSyncing = false;
            _syncCts?.Dispose();
            _syncCts = null;
        }
    }

    [RelayCommand]
    private void CancelSync()
    {
        _syncCts?.Cancel();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _syncCts?.Cancel();
        _syncCts?.Dispose();
        _syncCts = null;
        GC.SuppressFinalize(this);
    }
}
