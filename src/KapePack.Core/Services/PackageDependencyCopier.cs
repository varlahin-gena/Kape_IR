using KapePack.Core.Models;

namespace KapePack.Core.Services;

/// <summary>Copies target/module dependency closure into a package folder.</summary>
public sealed class PackageDependencyCopier
{
    private readonly KapeCatalog _catalog;

    public PackageDependencyCopier(KapeCatalog catalog) => _catalog = catalog;

    public List<string> CopyTargets(PackageDefinition pkg, string packageDir, CancellationToken ct = default)
    {
        var warnings = new List<string>();
        var refs = pkg.Targets.Select(t => t.Path).ToList();
        var items = _catalog.ResolveClosure(refs, ItemKind.Target);
        var known = items.Select(i => Path.GetFileName(i.AbsolutePath).ToLowerInvariant()).ToHashSet();
        foreach (var r in refs)
        {
            if (!known.Contains(Path.GetFileName(r).ToLowerInvariant()) &&
                items.All(i => !i.Name.Equals(r, StringComparison.OrdinalIgnoreCase)))
                warnings.Add($"Таргет не найден в каталоге: {r}");
        }

        // KAPE: one basename under Targets\ — never copy Apps\X and Compound\X together.
        var unique = DeduplicateByBasename(items, warnings);

        var generated = (pkg.TargetCompoundName + ".tkape").ToLowerInvariant();
        var writtenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in unique)
        {
            ct.ThrowIfCancellationRequested();
            var dest = ResolveDest(packageDir, item.AbsolutePath, "Targets");
            var destName = Path.GetFileName(dest);
            if (destName.Equals(generated, StringComparison.OrdinalIgnoreCase) && File.Exists(dest))
                continue;
            if (!writtenNames.Add(destName))
            {
                warnings.Add($"Пропуск повторного копирования {destName} ({item.RelativePath})");
                continue;
            }

            var targetsRoot = Path.Combine(packageDir, "Targets");
            if (!NameCollisionFixer.ShouldWriteUniqueBasename(targetsRoot, dest, item.AbsolutePath, out var skip) &&
                skip is not null)
            {
                warnings.Add(skip);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(item.AbsolutePath, dest, true);
        }

        return warnings;
    }

    public List<string> CopyModules(
        PackageDefinition pkg,
        string packageDir,
        bool includeBin,
        CancellationToken ct = default)
    {
        var warnings = new List<string>();
        var refs = pkg.Modules.Select(m => m.Path).ToList();
        var items = _catalog.ResolveClosure(refs, ItemKind.Module);
        var known = items.Select(i => Path.GetFileName(i.AbsolutePath).ToLowerInvariant()).ToHashSet();
        foreach (var r in refs)
        {
            if (!known.Contains(Path.GetFileName(r).ToLowerInvariant()) &&
                items.All(i => !i.Name.Equals(r, StringComparison.OrdinalIgnoreCase)))
                warnings.Add($"Модуль не найден в каталоге: {r}");
        }

        var unique = DeduplicateByBasename(items, warnings)
            .Where(i =>
            {
                if (!ModuleBinGate.IsSyncOrMaintenanceModule(i))
                    return true;
                warnings.Add($"Пропуск Sync/ToolSync модуля (не для triage): {i.RelativePath}");
                return false;
            })
            .ToList();

        var generated = (pkg.ModuleCompoundName + ".mkape")?.ToLowerInvariant();
        var writtenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in unique)
        {
            ct.ThrowIfCancellationRequested();
            var dest = ResolveDest(packageDir, item.AbsolutePath, "Modules");
            var destName = Path.GetFileName(dest);
            if (generated is not null &&
                destName.Equals(generated, StringComparison.OrdinalIgnoreCase) &&
                File.Exists(dest))
                continue;
            if (!writtenNames.Add(destName))
            {
                warnings.Add($"Пропуск повторного копирования {destName} ({item.RelativePath})");
                continue;
            }

            var modulesRoot = Path.Combine(packageDir, "Modules");
            if (!NameCollisionFixer.ShouldWriteUniqueBasename(modulesRoot, dest, item.AbsolutePath, out var skip) &&
                skip is not null)
            {
                warnings.Add(skip);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(item.AbsolutePath, dest, true);
        }

        if (!includeBin && Directory.Exists(Path.Combine(_catalog.KapeRoot, "Modules", "bin")))
        {
            warnings.Add(
                "Modules\\bin не включён в пакет. Для автономных модулей включите «Включить Modules\\bin».");
        }

        if (includeBin && pkg.IsTwoPhase)
            warnings.AddRange(CheckTwoPhaseBinaries());

        return warnings;
    }

    private List<string> CheckTwoPhaseBinaries()
    {
        var warnings = new List<string>();
        var winpmem = Path.Combine(_catalog.KapeRoot, "Modules", "bin", "winpmem.exe");
        if (!File.Exists(winpmem))
        {
            warnings.Add(
                "ОШИБКА: winpmem.exe не найден в Modules\\bin. CollectPack без --skip-memory " +
                "остановит Phase 1. Нужен go-winpmem_amd64_1.0-rc2_signed.exe → winpmem.exe " +
                "(см. Velocidex_WinPmem.mkape BinaryUrl), не unsigned mini.");
        }
        else
        {
            try
            {
                var len = new FileInfo(winpmem).Length;
                if (len > 0 && len < 1_500_000)
                {
                    warnings.Add(
                        $"ВНИМАНИЕ: winpmem.exe ≈ {len:N0} байт (похоже на unsigned mini). " +
                        "На Secure Boot RAM-дамп обычно пустой. Замените на " +
                        "go-winpmem_amd64_1.0-rc2_signed.exe → Modules\\bin\\winpmem.exe.");
                }
            }
            catch { /* ignore */ }
        }

        var listdlls = Path.Combine(_catalog.KapeRoot, "Modules", "bin", "listdlls.exe");
        if (!File.Exists(listdlls))
        {
            warnings.Add(
                "listdlls.exe не найден в Modules\\bin — SysInternals_ListDlls в Phase 1 не отработает. " +
                "Скачайте ListDlls.zip (см. SysInternals_ListDlls.mkape BinaryUrl).");
        }

        return warnings;
    }

    private static List<CatalogItem> DeduplicateByBasename(List<CatalogItem> items, List<string> warnings)
        => items
            .GroupBy(i => Path.GetFileName(i.AbsolutePath), StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                if (g.Count() > 1)
                {
                    var keep = NameCollisionFixer.Prefer(g);
                    warnings.Add(
                        $"Дубликат имени {g.Key}: в пакет взят {keep.RelativePath}, " +
                        $"пропущено: {string.Join(", ", g.Where(x => x != keep).Select(x => x.RelativePath))}");
                    return keep;
                }

                return g.First();
            })
            .ToList();

    private string ResolveDest(string packageDir, string absolutePath, string fallbackRoot)
    {
        try
        {
            var rel = Path.GetRelativePath(_catalog.KapeRoot, absolutePath);
            if (!rel.StartsWith(".."))
                return Path.Combine(packageDir, rel);
        }
        catch { /* ignore */ }

        return Path.Combine(packageDir, fallbackRoot, Path.GetFileName(absolutePath));
    }
}
