using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KapeIR.Core.Models;
using KapeIR.Core.Services;
using KapeIR.Builder.Services;
using KapeIR.Ui.Dialogs;

namespace KapeIR.Builder.ViewModels;

public sealed partial class PackageEditorViewModel : ObservableObject
{
    private readonly IBuilderShell _shell;

    [ObservableProperty] private string _packageName = "";
    [ObservableProperty] private string _packageDescription = "";
    [ObservableProperty] private string _packageAuthor = "";
    [ObservableProperty] private string _packageVersion = "";
    [ObservableProperty] private string _tsource = "C:";
    [ObservableProperty] private string _notes = "";
    [ObservableProperty] private bool _zipOutput = true;
    [ObservableProperty] private bool _vss;
    [ObservableProperty] private bool _twoPhaseCollection;
    [ObservableProperty] private string _caseId = "";
    [ObservableProperty] private bool _copyDeps = true;

    [ObservableProperty] private string _selectedTargetsText = "";
    [ObservableProperty] private string _selectedModulesText = "";
    [ObservableProperty] private string _packageCompositionSummary = "0 таргетов · 0 модулей";
    [ObservableProperty] private CatalogRowVm? _selectedExisting;

    internal PackageEditorViewModel(IBuilderShell shell)
    {
        _shell = shell ?? throw new ArgumentNullException(nameof(shell));
    }

    partial void OnTwoPhaseCollectionChanged(bool value)
    {
        if (value && string.IsNullOrWhiteSpace(_shell.Package.Phase1ModuleName))
            _shell.Package.Phase1ModuleName = PackageDefinition.DefaultPhase1Module;
    }

    [RelayCommand]
    private void NewPack()
    {
        _shell.Package = new PackageDefinition();
        PushPackageToForm();
        _shell.Catalog.SyncAllViews();
        _shell.StatusText = "Новая пустая сборка";
    }

    [RelayCommand]
    private async Task SaveAssemblyAsync()
    {
        PullFormToPackage();
        if (string.IsNullOrWhiteSpace(_shell.Package.Name))
        {
            _shell.Dialogs.ShowMessage("Укажите имя сборки.", "Сборка");
            return;
        }

        var root = await _shell.EnsureCatalogBoundToUiRootAsync();
        if (root is null) return;

        var forceCopy = SelectedExisting?.Item.Origin == CatalogOrigin.GitHub
                        && string.Equals(
                            SelectedExisting.Item.Name,
                            _shell.Package.TargetCompoundName,
                            StringComparison.OrdinalIgnoreCase);

        var wouldFork = forceCopy
                        || PackageAssemblyStore.WouldOverwriteUpstream(root, _shell.Package);
        var targetPath = PackageAssemblyStore.TargetCompoundPath(root, _shell.Package.TargetCompoundName);
        if (!wouldFork && File.Exists(targetPath) &&
            !_shell.Dialogs.Confirm(
                $"Перезаписать локальную сборку «{_shell.Package.TargetCompoundName}»?",
                "Сборка"))
            return;

        try
        {
            var result = _shell.CatalogOps.SaveLocalAssembly(root, _shell.Package, forceLocalCopy: forceCopy);
            _shell.Package = result.Package;
            PushPackageToForm();
            await _shell.Catalog.ReloadCatalogAsync(promptIfMissing: false);

            if (result.CreatedLocalCopy)
            {
                _shell.StatusText =
                    $"Локальная копия: {result.PreviousName} → {result.Package.TargetCompoundName}";
                _shell.Dialogs.ShowMessage(
                    $"Сборка с GitHub не перезаписывается.\n" +
                    $"Создана локальная копия «{result.Package.TargetCompoundName}».\n\n" +
                    $"{result.TargetFile}",
                    "Сборка");
            }
            else
            {
                _shell.StatusText =
                    $"Сборка сохранена: {result.Package.TargetCompoundName} " +
                    $"({result.Package.Targets.Count}t / {result.Package.Modules.Count}m)";
            }
        }
        catch (Exception ex)
        {
            _shell.Dialogs.ShowMessage(ex.Message, "Сборка", DialogIcon.Error);
        }
    }

    [RelayCommand]
    private async Task DeleteAssemblyAsync()
    {
        if (SelectedExisting is null)
        {
            _shell.Dialogs.ShowMessage("Сначала выберите сборку в списке.", "Удаление");
            return;
        }

        if (SelectedExisting.Item.Origin == CatalogOrigin.GitHub)
        {
            _shell.Dialogs.ShowMessage(
                "Сборки с GitHub удалять нельзя. Сохраните локальную копию и удалите её.",
                "Удаление");
            return;
        }

        if (SelectedExisting.Item.Origin != CatalogOrigin.Local)
        {
            _shell.Dialogs.ShowMessage(
                "Удаление доступно только для локальных сборок (колонка «Источник»).\n" +
                "Сначала «Обновить с GitHub…», чтобы метки стали точными, либо сохраните копию.",
                "Удаление");
            return;
        }

        var name = SelectedExisting.Item.Name;
        if (!_shell.Dialogs.Confirm(
                $"Удалить локальную сборку «{name}»?\n\nБудут удалены .tkape / companion _Modules.mkape и sidecar.",
                "Удаление",
                DialogIcon.Warning))
            return;

        var root = await _shell.EnsureCatalogBoundToUiRootAsync();
        if (root is null) return;

        try
        {
            var deleted = _shell.CatalogOps.DeleteLocalAssembly(root, name);
            SelectedExisting = null;
            await _shell.Catalog.ReloadCatalogAsync(promptIfMissing: false);
            _shell.StatusText = deleted
                ? $"Удалена локальная сборка: {name}"
                : $"Файлы сборки «{name}» не найдены";
        }
        catch (Exception ex)
        {
            _shell.Dialogs.ShowMessage(ex.Message, "Удаление", DialogIcon.Error);
        }
    }

    internal void SyncSelectionTexts()
    {
        SelectedTargetsText = _shell.Selection.FormatSelectionText(_shell.Package.Targets);
        SelectedModulesText = _shell.Selection.FormatSelectionText(_shell.Package.Modules);
        PackageCompositionSummary =
            $"{_shell.Package.Targets.Count} таргетов · {_shell.Package.Modules.Count} модулей";
    }

    internal void PushPackageToForm()
    {
        var s = PackageFormMapper.FromPackage(_shell.Package);
        PackageName = s.Name;
        PackageDescription = s.Description;
        PackageAuthor = s.Author;
        PackageVersion = s.Version;
        Tsource = s.Tsource;
        ZipOutput = s.ZipOutput;
        Vss = s.Vss;
        Notes = s.Notes;
        TwoPhaseCollection = s.TwoPhase;
        CaseId = s.CaseId;
    }

    internal void PullFormToPackage()
    {
        PackageFormMapper.ApplyToPackage(_shell.Package, new PackageFormMapper.FormSnapshot(
            PackageName,
            PackageDescription,
            PackageAuthor,
            PackageVersion,
            Tsource,
            ZipOutput,
            Vss,
            Notes,
            TwoPhaseCollection,
            CaseId ?? ""));
    }
}
