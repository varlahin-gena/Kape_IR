using KapePack.Core.Models;

namespace KapePack.Core.Services;

/// <summary>
/// Copies only Modules\bin payloads required by selected modules (plus .NET shared DLLs),
/// instead of shipping the entire EZ Tools / Chainsaw tree.
/// </summary>
public static class SelectiveModulesBinCopier
{
    public sealed record CopyResult
    {
        public int FilesCopied { get; init; }
        public IReadOnlyList<string> RequiredExecutables { get; init; } = Array.Empty<string>();
        public IReadOnlyList<string> Missing { get; init; } = Array.Empty<string>();
        public IReadOnlyList<string> NestedFolders { get; init; } = Array.Empty<string>();
        public IReadOnlyList<string> RootStems { get; init; } = Array.Empty<string>();
        public IReadOnlyList<string> ExplicitRootFiles { get; init; } = Array.Empty<string>();
        public List<string> Warnings { get; init; } = new();
    }

    /// <summary>
    /// Resolve leaf-module Executable values + CommandLine Modules\bin refs and copy matching
    /// files into package Modules\bin.
    /// Nested tools (chainsaw\, hayabusa\) copy the whole top-level folder; root EZ tools copy
    /// stem companions + shared runtime DLLs; scripts/helpers from CommandLine copy explicitly.
    /// Sources under net{N}\ are promoted to bin root (KAPE-visible layout).
    /// </summary>
    public static CopyResult Copy(
        KapeCatalog catalog,
        PackageDefinition pkg,
        string packageDir,
        CancellationToken ct = default,
        IProgress<string>? progress = null)
    {
        var warnings = new List<string>();
        var binSrc = Path.Combine(catalog.KapeRoot, "Modules", "bin");
        if (!Directory.Exists(binSrc))
        {
            if (Directory.Exists(Path.Combine(packageDir, "Modules")))
            {
                warnings.Add(
                    "Modules\\bin отсутствует — модули-парсеры могут не запуститься на целевой машине.");
            }

            return new CopyResult { Warnings = warnings };
        }

        var required = CollectRequiredExecutables(catalog, pkg);
        if (required.Count == 0)
        {
            progress?.Report("Modules\\bin: выбранные модули не требуют локальных бинарников");
            return new CopyResult
            {
                RequiredExecutables = required,
                Warnings = warnings
            };
        }

        progress?.Report($"Modules\\bin (selective): разбор {required.Count} зависимостей…");

        var nestedFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var rootStems = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var explicitRootFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var missing = new List<string>();

        foreach (var exe in required)
        {
            ct.ThrowIfCancellationRequested();
            if (ModuleBinGate.IsHostBuiltin(exe))
                continue;

            var rel = NormalizeBinRelative(exe);
            if (string.IsNullOrEmpty(rel))
                continue;

            var parts = SplitRel(rel);
            if (parts.Length >= 2)
            {
                nestedFolders.Add(parts[0]);
                continue;
            }

            var fileName = parts[0];
            if (string.IsNullOrEmpty(fileName))
                continue;

            if (LocateFile(binSrc, rel) is null &&
                LocateFile(binSrc, fileName) is null)
            {
                missing.Add(exe);
                continue;
            }

            explicitRootFiles.Add(fileName);
            var stem = Path.GetFileNameWithoutExtension(fileName);
            var ext = Path.GetExtension(fileName);
            // Tool companions (PECmd.*) only for native/PE payloads — scripts stay explicit-only.
            if (!string.IsNullOrEmpty(stem) &&
                (ext.Equals(".exe", StringComparison.OrdinalIgnoreCase) ||
                 ext.Equals(".dll", StringComparison.OrdinalIgnoreCase) ||
                 ext.Equals(".cmd", StringComparison.OrdinalIgnoreCase) ||
                 ext.Equals(".bat", StringComparison.OrdinalIgnoreCase)))
            {
                rootStems.Add(stem);
            }
        }

        // RECmd / related batch modules expect Maps\ next to the EXE.
        if (rootStems.Any(s => s.StartsWith("RECmd", StringComparison.OrdinalIgnoreCase)) &&
            Directory.Exists(Path.Combine(binSrc, "Maps")))
        {
            nestedFolders.Add("Maps");
        }

        var binDst = Path.Combine(packageDir, "Modules", "bin");
        Directory.CreateDirectory(binDst);

        var copied = 0;
        var seenDest = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var folder in nestedFolders.OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            ct.ThrowIfCancellationRequested();
            var srcDir = Path.Combine(binSrc, folder);
            if (!Directory.Exists(srcDir))
            {
                missing.Add(folder + "\\");
                warnings.Add($"Нет папки Modules\\bin\\{folder} — вложенный инструмент не скопирован.");
                continue;
            }

            progress?.Report($"Modules\\bin: папка {folder}\\…");
            copied += CopyDirectoryFlat(srcDir, Path.Combine(binDst, folder), ct, seenDest);
        }

        if (rootStems.Count > 0 || explicitRootFiles.Count > 0)
        {
            progress?.Report(
                $"Modules\\bin: инструменты {string.Join(", ", rootStems.OrderBy(s => s, StringComparer.OrdinalIgnoreCase).Take(8))}" +
                (rootStems.Count > 8 ? $" +{rootStems.Count - 8}" : "") +
                (explicitRootFiles.Count > 0
                    ? $"; +{explicitRootFiles.Count} явных файлов"
                    : "") + "…");
            copied += CopyRootSelective(
                binSrc, binDst, rootStems, explicitRootFiles, ct, seenDest);
        }

        foreach (var m in missing.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x))
            warnings.Add($"Не найден бинарник для пакета: {m}");

        progress?.Report(
            $"Modules\\bin (selective): скопировано {copied:N0} файлов " +
            $"({rootStems.Count} root, {explicitRootFiles.Count} явных, {nestedFolders.Count} папок)");

        if (copied == 0 && required.Count > 0)
        {
            warnings.Add(
                "Selective Modules\\bin: ни одного файла не скопировано — проверьте, что парсеры лежат в Modules\\bin.");
        }

        return new CopyResult
        {
            FilesCopied = copied,
            RequiredExecutables = required,
            Missing = missing.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList(),
            NestedFolders = nestedFolders.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList(),
            RootStems = rootStems.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList(),
            ExplicitRootFiles = explicitRootFiles.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList(),
            Warnings = warnings
        };
    }

    /// <summary>
    /// Leaf Executable values (non-builtin) plus Modules\bin paths from CommandLine
    /// for the package module closure.
    /// </summary>
    public static List<string> CollectRequiredExecutables(KapeCatalog catalog, PackageDefinition pkg)
    {
        var refs = pkg.Modules
            .Where(m => !ModuleBinGate.IsSyncOrMaintenanceModule(m))
            .Select(m => string.IsNullOrWhiteSpace(m.Path) ? m.Name : m.Path)
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .ToList();

        if (refs.Count == 0)
            return new List<string>();

        var leaves = catalog.FlattenToLeaves(refs, ItemKind.Module);
        var exes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var leaf in leaves)
        {
            if (ModuleBinGate.IsSyncOrMaintenanceModule(leaf))
                continue;
            foreach (var payload in ModuleBinGate.ExtractLeafBinPayloads(leaf.AbsolutePath))
            {
                if (string.IsNullOrWhiteSpace(payload) || ModuleBinGate.IsHostBuiltin(payload))
                    continue;
                exes.Add(payload.Trim().Trim('"', '\''));
            }
        }

        return exes.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static bool BelongsToStem(string fileName, string stem)
    {
        if (string.IsNullOrEmpty(fileName) || string.IsNullOrEmpty(stem))
            return false;
        if (fileName.Equals(stem, StringComparison.OrdinalIgnoreCase))
            return true;
        return fileName.StartsWith(stem + ".", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>True when the file is exclusive to a tool that is not in the required set.</summary>
    public static bool IsExclusiveToForeignStem(
        string fileName,
        IReadOnlySet<string> requiredStems,
        IReadOnlyCollection<string> allToolStems)
    {
        foreach (var stem in allToolStems)
        {
            if (!BelongsToStem(fileName, stem))
                continue;
            if (requiredStems.Contains(stem))
                return false;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Shared .NET / framework libs that EZ Tools need beside tool-specific stem.* files.
    /// Orphan scripts and unrelated helpers are not included — those must be explicit.
    /// </summary>
    public static bool IsSharedDotNetRuntime(string fileName)
    {
        if (string.IsNullOrEmpty(fileName))
            return false;

        if (fileName.StartsWith("System.", StringComparison.OrdinalIgnoreCase) ||
            fileName.StartsWith("Microsoft.", StringComparison.OrdinalIgnoreCase) ||
            fileName.StartsWith("netstandard", StringComparison.OrdinalIgnoreCase) ||
            fileName.StartsWith("WindowsBase", StringComparison.OrdinalIgnoreCase) ||
            fileName.StartsWith("Presentation", StringComparison.OrdinalIgnoreCase) ||
            fileName.StartsWith("WindowsForms", StringComparison.OrdinalIgnoreCase) ||
            fileName.StartsWith("DirectWriteForwarder", StringComparison.OrdinalIgnoreCase))
            return true;

        return fileName.Equals("hostfxr.dll", StringComparison.OrdinalIgnoreCase) ||
               fileName.Equals("hostpolicy.dll", StringComparison.OrdinalIgnoreCase) ||
               fileName.Equals("coreclr.dll", StringComparison.OrdinalIgnoreCase) ||
               fileName.Equals("clrjit.dll", StringComparison.OrdinalIgnoreCase) ||
               fileName.Equals("clretwrc.dll", StringComparison.OrdinalIgnoreCase) ||
               fileName.Equals("clrcompression.dll", StringComparison.OrdinalIgnoreCase) ||
               fileName.Equals("mscordaccore.dll", StringComparison.OrdinalIgnoreCase) ||
               fileName.Equals("mscordbi.dll", StringComparison.OrdinalIgnoreCase) ||
               fileName.Equals("msquic.dll", StringComparison.OrdinalIgnoreCase) ||
               fileName.Equals("createdump.exe", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Allowlist: required stem companions, explicit CommandLine files, shared runtime.</summary>
    public static bool ShouldCopyRootFile(
        string fileName,
        IReadOnlySet<string> requiredStems,
        IReadOnlySet<string> explicitRootFiles)
    {
        if (explicitRootFiles.Contains(fileName))
            return true;

        foreach (var stem in requiredStems)
        {
            if (BelongsToStem(fileName, stem))
                return true;
        }

        return IsSharedDotNetRuntime(fileName);
    }

    private static int CopyRootSelective(
        string binSrc,
        string binDst,
        HashSet<string> requiredStems,
        HashSet<string> explicitRootFiles,
        CancellationToken ct,
        HashSet<string> seenDest)
    {
        var searchRoots = new List<string> { binSrc };
        var net = EzToolsLayout.FindPreferredNetDir(binSrc);
        if (net is not null)
            searchRoots.Add(net);

        var copied = 0;
        foreach (var root in searchRoots)
        {
            if (!Directory.Exists(root)) continue;
            foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.TopDirectoryOnly))
            {
                ct.ThrowIfCancellationRequested();
                var name = Path.GetFileName(file);
                if (!ShouldCopyRootFile(name, requiredStems, explicitRootFiles))
                    continue;

                var dest = Path.Combine(binDst, name);
                if (!seenDest.Add(dest))
                    continue;
                Directory.CreateDirectory(binDst);
                File.Copy(file, dest, overwrite: true);
                copied++;
            }

            // Same-named folders next to EZ tools (rare, but cheap).
            foreach (var stem in requiredStems)
            {
                var dir = Path.Combine(root, stem);
                if (!Directory.Exists(dir)) continue;
                if (EzToolsLayout.IsNetRuntimeFolder(stem)) continue;
                copied += CopyDirectoryFlat(dir, Path.Combine(binDst, stem), ct, seenDest);
            }
        }

        return copied;
    }

    private static int CopyDirectoryFlat(
        string srcDir,
        string dstDir,
        CancellationToken ct,
        HashSet<string> seenDest)
    {
        var copied = 0;
        Directory.CreateDirectory(dstDir);
        foreach (var dir in Directory.EnumerateDirectories(srcDir, "*", SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();
            var rel = Path.GetRelativePath(srcDir, dir);
            Directory.CreateDirectory(Path.Combine(dstDir, rel));
        }

        foreach (var file in Directory.EnumerateFiles(srcDir, "*", SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();
            var rel = Path.GetRelativePath(srcDir, file);
            var dest = Path.Combine(dstDir, rel);
            if (!seenDest.Add(dest))
                continue;
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(file, dest, overwrite: true);
            copied++;
        }

        return copied;
    }

    private static string? LocateFile(string modulesBin, string relativeOrName)
    {
        var normalized = relativeOrName.Replace('/', '\\').TrimStart('\\');
        var direct = Path.Combine(modulesBin, normalized);
        if (File.Exists(direct))
            return direct;

        var fileName = Path.GetFileName(normalized);
        var visible = EzToolsLayout.FindKapeVisibleBinary(modulesBin, fileName);
        if (visible is not null)
            return visible;

        var net = EzToolsLayout.FindPreferredNetDir(modulesBin);
        if (net is not null)
        {
            var underNet = Path.Combine(net, fileName);
            if (File.Exists(underNet))
                return underNet;
            var underNetRel = Path.Combine(net, normalized);
            if (File.Exists(underNetRel))
                return underNetRel;
        }

        return null;
    }

    private static string NormalizeBinRelative(string executable)
    {
        var e = executable.Trim().Trim('"', '\'');
        if (string.IsNullOrEmpty(e))
            return "";
        if (Path.IsPathRooted(e))
            return Path.GetFileName(e);

        // Strip leading Modules\bin\ if authors wrote a long relative path.
        var norm = e.Replace('/', '\\');
        const string prefix = @"Modules\bin\";
        if (norm.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            norm = norm[prefix.Length..];
        if (norm.StartsWith(@"bin\", StringComparison.OrdinalIgnoreCase))
            norm = norm[4..];
        return norm.TrimStart('\\');
    }

    private static string[] SplitRel(string rel)
        => rel.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);
}
