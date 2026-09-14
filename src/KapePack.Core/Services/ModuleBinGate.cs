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

    public sealed class FilterResult
    {
        public List<SelectionEntry> Kept { get; init; } = new();
        public List<string> Skipped { get; init; } = new();
    }

    /// <summary>
    /// Keep modules that either use built-in OS tools (powershell/cmd/…) or have
    /// their primary Executable present under Modules\bin (KAPE-visible layout).
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
                var exes = ExtractLeafExecutables(leaf.AbsolutePath);
                if (exes.Count == 0)
                {
                    anyRunnable = true;
                    break;
                }

                foreach (var exe in exes)
                {
                    if (IsHostBuiltin(exe) || BinaryExists(bin, exe))
                    {
                        anyRunnable = true;
                        break;
                    }

                    missingHint ??= exe;
                }

                if (anyRunnable)
                    break;
            }

            if (anyRunnable)
                kept.Add(entry);
            else
                skipped.Add($"{entry.Name} (нет {missingHint ?? "?"})");
        }

        return new FilterResult { Kept = kept, Skipped = skipped };
    }

    public static List<string> ExtractLeafExecutables(string mkapePath)
    {
        if (!KapeFileIo.TryLoadKapeFile(mkapePath, out var data))
            return new List<string>();
        if (KapeFileIo.IsCompoundModule(data))
            return new List<string>();
        return KapeFileIo.ExtractModuleExecutables(data);
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
