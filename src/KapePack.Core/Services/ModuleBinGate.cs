using KapePack.Core.Models;

namespace KapePack.Core.Services;

/// <summary>
/// Drops leaf modules whose required executables are missing from Modules\bin
/// (stops KAPE spam / wasted time on LogParser, RegRipper, hayabusa, … when not shipped).
/// </summary>
public static class ModuleBinGate
{
    /// <summary>
    /// Sync / ToolSync modules update Maps and KapeFiles — not for triage CollectPack.
    /// Including <c>!!ToolSync.mkape</c> also breaks YAML unless quoted (<c>!!</c> = tag).
    /// </summary>
    public static bool IsSyncOrMaintenanceModule(SelectionEntry entry)
    {
        var name = entry.Name ?? "";
        var path = Path.GetFileName(entry.Path ?? "");
        var cat = entry.Category ?? "";

        if (cat.Contains("Sync", StringComparison.OrdinalIgnoreCase))
            return true;
        if (name.Contains("ToolSync", StringComparison.OrdinalIgnoreCase) ||
            path.Contains("ToolSync", StringComparison.OrdinalIgnoreCase))
            return true;
        if (name.StartsWith("Sync_", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("Sync_", StringComparison.OrdinalIgnoreCase))
            return true;
        return false;
    }

    public static bool IsSyncOrMaintenanceModule(CatalogItem item)
        => IsSyncOrMaintenanceModule(new SelectionEntry
        {
            Name = item.Name,
            Path = Path.GetFileName(item.RelativePath),
            Category = item.Category
        });

    public sealed record FilterResult(List<SelectionEntry> Kept, List<string> Skipped);

    /// <summary>
    /// Keep modules that have at least one runnable leaf: OS builtins with no bin deps,
    /// or any required Modules\bin payload (Executable and/or CommandLine refs) present.
    /// </summary>
    public static FilterResult FilterByAvailableBinaries(
        KapeCatalog catalog,
        IEnumerable<SelectionEntry> entries,
        string? modulesBinOverride = null)
    {
        var bin = modulesBinOverride ?? Path.Combine(catalog.KapeRoot, "Modules", "bin");
        var kept = new List<SelectionEntry>();
        var skipped = new List<string>();

        foreach (var entry in entries)
        {
            var leaves = catalog.FlattenToLeaves(
                new[] { string.IsNullOrWhiteSpace(entry.Path) ? entry.Name : entry.Path },
                ItemKind.Module);

            if (leaves.Count == 0)
            {
                kept.Add(entry);
                continue;
            }

            var anyRunnable = false;
            string? missingHint = null;
            foreach (var leaf in leaves)
            {
                var payloads = ExtractLeafBinPayloads(leaf.AbsolutePath);
                if (payloads.Count == 0)
                {
                    // No Modules\bin deps (builtins-only or empty processors).
                    anyRunnable = true;
                    break;
                }

                foreach (var payload in payloads)
                {
                    if (BinaryExists(bin, payload))
                    {
                        anyRunnable = true;
                        break;
                    }

                    missingHint ??= payload;
                }

                if (anyRunnable)
                    break;
            }

            if (anyRunnable)
                kept.Add(entry);
            else
                skipped.Add($"{entry.Name} (нет {missingHint ?? "?"})");
        }

        return new FilterResult(kept, skipped);
    }

    public static List<string> ExtractLeafExecutables(string mkapePath)
    {
        if (!KapeFileIo.TryLoadKapeFile(mkapePath, out var data))
            return new List<string>();
        if (KapeFileIo.IsCompoundModule(data))
            return new List<string>();
        return KapeFileIo.ExtractModuleExecutables(data);
    }

    /// <summary>
    /// Non-builtin Executable values plus Modules\bin paths from CommandLine.
    /// Compounds return empty (children are resolved via FlattenToLeaves).
    /// </summary>
    public static List<string> ExtractLeafBinPayloads(string mkapePath)
    {
        if (!KapeFileIo.TryLoadKapeFile(mkapePath, out var data))
            return new List<string>();
        if (KapeFileIo.IsCompoundModule(data))
            return new List<string>();

        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var exe in KapeFileIo.ExtractModuleExecutables(data))
        {
            if (string.IsNullOrWhiteSpace(exe) || IsHostBuiltin(exe))
                continue;
            set.Add(exe.Trim().Trim('"', '\''));
        }

        foreach (var rel in KapeFileIo.ExtractModuleBinCommandLineRefs(data))
        {
            if (string.IsNullOrWhiteSpace(rel) || IsHostBuiltin(rel))
                continue;
            set.Add(rel.Trim().Trim('"', '\''));
        }

        return set.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static bool IsHostBuiltin(string executable)
    {
        var e = executable.Trim().Trim('"', '\'');
        if (e.Contains("%systemroot%", StringComparison.OrdinalIgnoreCase) ||
            e.Contains("%windir%", StringComparison.OrdinalIgnoreCase) ||
            e.Contains(@"\Windows\System32\", StringComparison.OrdinalIgnoreCase) ||
            e.Contains(@"\SysWOW64\", StringComparison.OrdinalIgnoreCase))
            return true;

        var name = Path.GetFileName(e);
        return name.Equals("powershell.exe", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("pwsh.exe", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("cmd.exe", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("python.exe", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("python3.exe", StringComparison.OrdinalIgnoreCase);
    }

    public static bool BinaryExists(string modulesBin, string executable)
    {
        var e = executable.Trim().Trim('"', '\'');
        if (string.IsNullOrEmpty(e))
            return false;

        if (Path.IsPathRooted(e) && File.Exists(e))
            return true;

        var underBin = Path.Combine(modulesBin, e.Replace('/', '\\'));
        if (File.Exists(underBin))
            return true;

        var fileName = Path.GetFileName(e);
        if (string.IsNullOrEmpty(fileName))
            return false;

        return EzToolsLayout.FindKapeVisibleBinary(modulesBin, fileName) is not null ||
               File.Exists(Path.Combine(modulesBin, fileName));
    }
}
