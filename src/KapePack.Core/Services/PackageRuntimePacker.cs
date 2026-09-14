using KapePack.Core.Models;

namespace KapePack.Core.Services;

/// <summary>Copies kape.exe runtime + Modules\bin into a CollectPack folder.</summary>
public sealed class PackageRuntimePacker
{
    private readonly KapeCatalog _catalog;

    public PackageRuntimePacker(KapeCatalog catalog) => _catalog = catalog;

    public List<string> CopyRuntime(
        string packageDir,
        bool includeModuleBin,
        PackageDefinition? pkg = null,
        bool fullModuleBin = false,
        CancellationToken ct = default,
        IProgress<string>? progress = null)
    {
        ct.ThrowIfCancellationRequested();
        var warnings = new List<string>();
        var kape = KapeRootPaths.FindKapeExeInRoot(_catalog.KapeRoot);
        if (kape is null)
        {
            warnings.Add(
                "kape.exe не найден в корне выбранного KAPE — автономный EXE не сможет запустить сбор без него. " +
                "Положите kape.exe в корень, указанный вверху окна Builder, и пересоберите пакет.");
        }
        else
        {
            File.Copy(kape, Path.Combine(packageDir, "kape.exe"), true);
            foreach (var name in new[] { "kape.db", "KAPE.db", "gkape.exe", "Get-KAPEUpdate.ps1", "CHANGELOG.txt" })
            {
                ct.ThrowIfCancellationRequested();
                var src = Path.Combine(Path.GetDirectoryName(kape)!, name);
                if (File.Exists(src))
                    File.Copy(src, Path.Combine(packageDir, name), true);
            }
        }

        if (includeModuleBin)
        {
            if (fullModuleBin || pkg is null)
                warnings.AddRange(CopyModulesBinFull(packageDir, ct, progress));
            else
                warnings.AddRange(SelectiveModulesBinCopier.Copy(_catalog, pkg, packageDir, ct, progress).Warnings);
        }

        return warnings;
    }

    /// <summary>Legacy: ship entire Modules\bin (minus netN duplicate after promote).</summary>
    private List<string> CopyModulesBinFull(
        string packageDir,
        CancellationToken ct,
        IProgress<string>? progress)
    {
        var warnings = new List<string>();
        var binSrc = Path.Combine(_catalog.KapeRoot, "Modules", "bin");
        if (Directory.Exists(binSrc))
        {
            var binDst = Path.Combine(packageDir, "Modules", "bin");
            progress?.Report("Копирование Modules\\bin целиком (без дубля netN)…");
            CopyDirectory(binSrc, binDst, ct, progress, skipRelPath: EzToolsLayout.IsUnderNetRuntimeFolder);
            var netSrc = EzToolsLayout.FindPreferredNetDir(binSrc);
            var promoted = netSrc is not null
                ? EzToolsLayout.CopyNetRuntimeIntoBinRoot(netSrc, binDst, progress)
                : EzToolsLayout.PromoteNetFolderToBinRoot(binDst, progress);
            var removed = EzToolsLayout.RemoveNetRuntimeFolders(binDst, progress);
            if (promoted > 0)
                progress?.Report($"Пакет: раскладка net→bin ({promoted:N0} файлов) для KAPE");
            if (removed > 0)
                progress?.Report($"Пакет: удалено {removed} папок netN (дубликат)");

            var ez = EzToolsUpdater.Check(packageDir);
            if (!ez.Ok && ez.Message.Contains("netN", StringComparison.OrdinalIgnoreCase))
            {
                warnings.Add(
                    "EZ Tools остались только в Modules\\bin\\netN — KAPE их не увидит. " +
                    "Обновите EZ Tools в Builder или скопируйте netN в корень bin.");
            }
        }
        else if (Directory.Exists(Path.Combine(packageDir, "Modules")))
        {
            warnings.Add("Modules\\bin отсутствует — модули-парсеры могут не запуститься на целевой машине.");
        }

        return warnings;
    }

    public static void CopyDirectory(
        string src,
        string dst,
        CancellationToken ct = default,
        IProgress<string>? progress = null,
        Func<string, bool>? skipRelPath = null)
    {
        Directory.CreateDirectory(dst);
        foreach (var dir in Directory.EnumerateDirectories(src, "*", SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();
            var rel = Path.GetRelativePath(src, dir);
            if (skipRelPath?.Invoke(rel) == true)
                continue;
            Directory.CreateDirectory(Path.Combine(dst, rel));
        }

        var files = Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories)
            .Where(f => skipRelPath?.Invoke(Path.GetRelativePath(src, f)) != true)
            .ToList();
        var total = files.Count;
        var n = 0;
        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();
            n++;
            if (n == 1 || n == total || n % 50 == 0)
                progress?.Report($"Modules\\bin: {n:N0}/{total:N0} файлов…");

            var rel = Path.GetRelativePath(src, file);
            var dest = Path.Combine(dst, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(file, dest, true);
        }

        if (total > 0)
            progress?.Report($"Modules\\bin: скопировано {total:N0} файлов");
    }
}
