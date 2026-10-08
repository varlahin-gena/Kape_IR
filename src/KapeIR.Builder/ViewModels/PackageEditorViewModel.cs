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
        _shell.StatusText = "Новый пустой пакет";
    }

    [RelayCommand]
    private async Task SaveSessionAsync()
    {
        PullFormToPackage();
        if (string.IsNullOrWhiteSpace(_shell.Package.Name))
        {
            _shell.Dialogs.ShowMessage("Укажите имя пакета — оно станет именем сессии.", "Сессия");
            return;
        }

        var root = await _shell.EnsureCatalogBoundToUiRootAsync();
        if (root is null) return;

        var path = PackageSessionStore.SessionPath(root, _shell.Package.Name);
        if (File.Exists(path) &&
            !_shell.Dialogs.Confirm($"Перезаписать сессию «{PackageSessionStore.SafeSessionFileName(_shell.Package.Name)}»?", "Сессия"))
            return;

        try
        {
            PackageSessionStore.Save(root, _shell.Package.Name, _shell.Package);
            _shell.StatusText = $"Сессия сохранена (локальная): {PackageSessionStore.SafeSessionFileName(_shell.Package.Name)}";
            _shell.Dialogs.ShowMessage(
                $"Локальная сессия Pack Builder:\n{path}\n\nТаргетов: {_shell.Package.Targets.Count}, модулей: {_shell.Package.Modules.Count}",
                "Сессия");
        }
        catch (Exception ex)
        {
            _shell.Dialogs.ShowMessage(ex.Message, "Сессия", DialogIcon.Error);
        }
    }

    [RelayCommand]
    private async Task LoadSessionAsync()
    {
        var root = await _shell.EnsureCatalogBoundToUiRootAsync();
        if (root is null) return;

        var dir = PackageSessionStore.SessionsDir(root);
        Directory.CreateDirectory(dir);
        var names = PackageSessionStore.ListSessionNames(root);
        if (names.Count == 0)
        {
            _shell.Dialogs.ShowMessage(
                $"Нет сохранённых сессий в:\n{dir}\n\nСначала «Сохранить сессию».",
                "Сессия");
            return;
        }

        var picked = _shell.Dialogs.PickOpenFile(
            "Открыть сессию Pack Builder",
            "Сессии (*.json)|*.json|Все файлы (*.*)|*.*",
            dir);
        if (picked is null) return;

        try
        {
            // Prefer store load by name when file is under sessions dir; else package.json shape.
            _shell.Package = _shell.CatalogOps.LoadPackageJson(picked);
            PushPackageToForm();
            _shell.Catalog.TargetFilter = _shell.Package.Targets.Count > 0 ? "Только выбранные" : "Все";
            _shell.Catalog.ModuleFilter = _shell.Package.Modules.Count > 0 ? "Только выбранные" : "Все";
            _shell.Catalog.SyncAllViews();
            _shell.StatusText = $"Загружена локальная сессия: {_shell.Package.Name} ({_shell.Package.Targets.Count}t / {_shell.Package.Modules.Count}m)";
        }
        catch (Exception ex)
        {
            _shell.Dialogs.ShowMessage(ex.Message, "Сессия", DialogIcon.Error);
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
