using System.Diagnostics;
using CommunityToolkit.Mvvm.Input;
using KapePackBuilder.Models;
using KapePackBuilder.Services;

namespace KapePackBuilder.ViewModels;

public partial class MainViewModel
{
    [RelayCommand]
    private void NewPack()
    {
        Package = new PackageDefinition
        {
            Name = "WindowsTriage",
            Description = "Пакет Windows triage",
            Author = PackageAuthor
        };
        PushPackageToForm();
        SyncAllViews();
        StatusText = "Новый пустой пакет";
    }

    [RelayCommand]
    private void SaveSession()
    {
        PullFormToPackage();
        if (string.IsNullOrWhiteSpace(Package.Name))
        {
            _dialogs.ShowMessage("Укажите имя пакета — оно станет именем сессии.", "Сессия");
            return;
        }

        var path = PackageSessionStore.SessionPath(KapeRoot, Package.Name);
        if (File.Exists(path) &&
            !_dialogs.Confirm($"Перезаписать сессию «{PackageSessionStore.SafeSessionFileName(Package.Name)}»?", "Сессия"))
            return;

        try
        {
            PackageSessionStore.Save(KapeRoot, Package.Name, Package);
            StatusText = $"Сессия сохранена: {PackageSessionStore.SafeSessionFileName(Package.Name)}";
            _dialogs.ShowMessage(
                $"Сохранено:\n{path}\n\nТаргетов: {Package.Targets.Count}, модулей: {Package.Modules.Count}",
                "Сессия");
        }
        catch (Exception ex)
        {
            _dialogs.ShowMessage(ex.Message, "Сессия", DialogIcon.Error);
        }
    }

    [RelayCommand]
    private void LoadSession()
    {
        var dir = PackageSessionStore.SessionsDir(KapeRoot);
        Directory.CreateDirectory(dir);
        var names = PackageSessionStore.ListSessionNames(KapeRoot);
        if (names.Count == 0)
        {
            _dialogs.ShowMessage(
                $"Нет сохранённых сессий в:\n{dir}\n\nСначала «Сохранить сессию».",
                "Сессия");
            return;
        }

        var picked = _dialogs.PickOpenFile(
            "Открыть сессию Pack Builder",
            "Сессии (*.json)|*.json|Все файлы (*.*)|*.*",
            dir);
        if (picked is null) return;

        try
        {
            // Prefer store load by name when file is under sessions dir; else package.json shape.
            Package = PackageExporter.LoadPackageJson(picked);
            PushPackageToForm();
            TargetFilter = Package.Targets.Count > 0 ? "Только выбранные" : "Все";
            ModuleFilter = Package.Modules.Count > 0 ? "Только выбранные" : "Все";
            SyncAllViews();
            StatusText = $"Загружена сессия: {Package.Name} ({Package.Targets.Count}t / {Package.Modules.Count}m)";
        }
        catch (Exception ex)
        {
            _dialogs.ShowMessage(ex.Message, "Сессия", DialogIcon.Error);
        }
    }

    [RelayCommand]
    private void LoadSelectedCompound()
    {
        if (SelectedExisting is null)
        {
            _dialogs.ShowMessage("Сначала выберите compound-таргет.", "Загрузка");
            return;
        }
        var loaded = KapeFileIo.PackageFromCompoundTarget(SelectedExisting.Item.AbsolutePath);
        loaded.Modules = new List<SelectionEntry>();
        var guess = loaded.TargetCompoundName + "_Modules";
        var mod = _catalog.FindModule(guess) ?? _catalog.FindModule(guess.TrimStart('!'));
        if (mod?.IsCompound == true)
        {
            foreach (var child in mod.Children)
            {
                var childItem = _catalog.FindModule(child);
                loaded.Modules.Add(childItem is null
                    ? new SelectionEntry { Name = Path.GetFileNameWithoutExtension(child), Path = child.EndsWith(".mkape") ? child : child + ".mkape", Category = "General" }
                    : new SelectionEntry { Name = childItem.Name, Category = childItem.Category, Path = Path.GetFileName(childItem.RelativePath) });
            }
        }
        Package = loaded;
        PushPackageToForm();
        TargetFilter = "Только выбранные";
        if (Package.Modules.Count > 0) ModuleFilter = "Только выбранные";
        SyncAllViews();
        StatusText = $"Загружен {SelectedExisting.Item.Name}: {Package.Targets.Count} таргетов, {Package.Modules.Count} модулей";
    }

    [RelayCommand]
    private void OpenPackageJson()
    {
        var path = _dialogs.PickOpenFile("Открыть package.json", "JSON|*.json|Все|*.*");
        if (path is null) return;
        Package = PackageExporter.LoadPackageJson(path);
        PushPackageToForm();
        TargetFilter = "Только выбранные";
        if (Package.Modules.Count > 0) ModuleFilter = "Только выбранные";
        SyncAllViews();
        StatusText = $"Загружен package.json: {Package.Targets.Count} таргетов, {Package.Modules.Count} модулей";
    }

    [RelayCommand]
    private void MergeTreeIntoPackage()
    {
        ApplyCheckedTree(replace: false);
    }

    [RelayCommand]
    private void ReplacePackageFromTree()
    {
        ApplyCheckedTree(replace: true);
    }

    private void ApplyCheckedTree(bool replace)
    {
        var kind = TreeIsTargets ? ItemKind.Target : ItemKind.Module;
        var refs = CollectCheckedRefs(TreeRoots).ToList();
        if (refs.Count == 0)
        {
            _dialogs.ShowMessage("В дереве ничего не отмечено.", "Дерево");
            return;
        }
        var incoming = _catalog.SelectionFromRefs(refs, kind, flatten: true);
        if (kind == ItemKind.Target)
            Package.Targets = replace ? incoming : KapeCatalog.MergeEntries(Package.Targets, incoming);
        else
            Package.Modules = replace ? incoming : KapeCatalog.MergeEntries(Package.Modules, incoming);
        var stats = _catalog.OverlapStats(refs, kind);
        StatusText = $"{(replace ? "Заменено" : "Добавлено")}: {refs.Count} ссылок → {stats.UniqueLeaves} уникальных leaf";
        SyncAllViews();
    }

    private static IEnumerable<string> CollectCheckedRefs(IEnumerable<TreeNodeVm> nodes)
    {
        foreach (var n in nodes)
        {
            if (n.IsSelected)
                yield return Path.GetFileName(n.Item.RelativePath);
            foreach (var c in CollectCheckedRefs(n.Children))
                yield return c;
        }
    }

    [RelayCommand]
    private void PreviewCommand()
    {
        PullFormToPackage();
        var text = KapeFileIo.RenderRunBat(Package);
        _dialogs.ShowMessage(text, "Превью скрипта запуска");
    }

    public void ShowItemInfo(CatalogItem item)
    {
        var text =
            $"{item.Name}\nПуть: {item.RelativePath}\nКатегория: {item.Category}\n" +
            $"Автор: {item.Author} | Версия: {item.Version}\nCompound: {item.IsCompound}\n" +
            $"Описание: {item.Description}\n";
        if (item.FileMasks.Count > 0)
            text += "FileMask: " + string.Join(", ", item.FileMasks.Take(12)) + "\n";
        if (item.Children.Count > 0)
            text += "Дочерние: " + string.Join(", ", item.Children.Take(30)) + "\n";
        if (item.DocumentationUrls.Count > 0)
            text += $"Документация ({item.DocumentationUrls.Count}): см. ссылки ниже\n";
        else
            text += "Документация: нет ссылок в файле\n";
        DetailText = text;

        DocumentationLinks.Clear();
        foreach (var url in item.DocumentationUrls)
            DocumentationLinks.Add(url);
        HasDocumentationLinks = DocumentationLinks.Count > 0;
    }

    [RelayCommand]
    private void OpenDocumentationLink(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _dialogs.ShowMessage(ex.Message, "Не удалось открыть ссылку", DialogIcon.Warning);
        }
    }

    private void PushPackageToForm()
    {
        PackageName = Package.Name;
        PackageDescription = Package.Description;
        PackageAuthor = Package.Author;
        PackageVersion = Package.Version;
        Tsource = Package.Tsource;
        ZipOutput = Package.ZipOutput;
        Flush = Package.Flush;
        Vss = Package.Vss;
        Notes = Package.Notes;
    }

    private void PullFormToPackage()
    {
        Package.Name = string.IsNullOrWhiteSpace(PackageName) ? "WindowsTriage" : PackageName.Trim();
        Package.Description = PackageDescription.Trim();
        Package.Author = PackageAuthor.Trim();
        Package.Version = string.IsNullOrWhiteSpace(PackageVersion) ? "1.0" : PackageVersion.Trim();
        Package.Tsource = string.IsNullOrWhiteSpace(Tsource) ? "C:" : Tsource.Trim();
        Package.ZipOutput = ZipOutput;
        Package.Flush = Flush;
        Package.Vss = Vss;
        Package.Notes = Notes.Trim();
        if (string.IsNullOrWhiteSpace(Package.PackageId))
            Package.PackageId = Guid.NewGuid().ToString();
    }
}
