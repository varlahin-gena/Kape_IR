using System.Text;
using KapeIR.Core.Models;

namespace KapeIR.Core.Services;

/// <summary>
/// Maps .mkape modules to Modules\bin readiness (Executable + CommandLine refs).
/// </summary>
public enum ModuleBinStatus
{
    /// <summary>All required Modules\bin payloads present (or builtin-only).</summary>
    Ok = 0,
    /// <summary>Some required payloads present, some missing (typical for compounds).</summary>
    Partial = 1,
    /// <summary>Required Modules\bin payloads missing; module cannot run as packaged.</summary>
    Missing = 2,
    /// <summary>Sync / ToolSync — not triage CollectPack material.</summary>
    SyncSkipped = 3
}

public sealed record ModuleBinAuditEntry(
    string Name,
    string? Path,
    ModuleBinStatus Status,
    IReadOnlyList<string> Required,
    IReadOnlyList<string> Missing)
{
    public bool HasIssues => Status is ModuleBinStatus.Missing or ModuleBinStatus.Partial;

    /// <summary>Short column glyph: ✓ / ~ / ✗ / —</summary>
    public string DisplayGlyph => Status switch
    {
        ModuleBinStatus.Ok => "✓",
        ModuleBinStatus.Partial => "~",
        ModuleBinStatus.Missing => "✗",
        ModuleBinStatus.SyncSkipped => "—",
        _ => "—"
    };

    public string DisplayLabel => Status switch
    {
        ModuleBinStatus.Ok => "есть",
        ModuleBinStatus.Partial => "частично",
        ModuleBinStatus.Missing => "нет",
        ModuleBinStatus.SyncSkipped => "sync",
        _ => "—"
    };

    public string Tooltip
    {
        get
        {
            if (Status == ModuleBinStatus.SyncSkipped)
                return "Модуль sync/maintenance — не для CollectPack";
            if (Required.Count == 0)
                return "Нет зависимостей Modules\\bin (builtin / пустой)";
            if (Missing.Count == 0)
                return "Бинарники на месте: " + string.Join(", ", Required.Take(8));
            return "Нет в Modules\\bin: " + string.Join(", ", Missing.Take(12)) +
                   (Missing.Count > 12 ? $" (+{Missing.Count - 12})" : "");
        }
    }
}

public sealed record ModuleBinAuditReport(
    string ModulesBinPath,
    IReadOnlyList<ModuleBinAuditEntry> Entries)
{
    public IReadOnlyList<ModuleBinAuditEntry> Issues =>
        Entries.Where(e => e.HasIssues).ToList();

    public bool HasIssues => Issues.Count > 0;

    public int OkCount => Entries.Count(e => e.Status == ModuleBinStatus.Ok);
    public int MissingCount => Entries.Count(e => e.Status == ModuleBinStatus.Missing);
    public int PartialCount => Entries.Count(e => e.Status == ModuleBinStatus.Partial);
    public int SyncSkippedCount => Entries.Count(e => e.Status == ModuleBinStatus.SyncSkipped);
}

/// <summary>
/// Built-in check: which .mkape modules can see their binaries under Modules\bin.
/// </summary>
public static class ModuleBinAudit
{
    /// <summary>
    /// Index every catalog module (leaf + compound) keyed by AbsolutePath (fallback: Name).
    /// </summary>
    public static IReadOnlyDictionary<string, ModuleBinAuditEntry> IndexCatalog(
        KapeCatalog catalog,
        string? modulesBinOverride = null)
    {
        var bin = modulesBinOverride ?? Path.Combine(catalog.KapeRoot, "Modules", "bin");
        var map = new Dictionary<string, ModuleBinAuditEntry>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in catalog.Modules)
        {
            if (ModuleBinGate.IsSyncOrMaintenanceModule(item))
            {
                var skipped = new ModuleBinAuditEntry(
                    item.Name,
                    Path.GetFileName(item.RelativePath),
                    ModuleBinStatus.SyncSkipped,
                    Array.Empty<string>(),
                    Array.Empty<string>());
                AddKeys(map, item, skipped);
                continue;
            }

            var entry = AuditCatalogItem(catalog, item, bin);
            AddKeys(map, item, entry);
        }

        return map;
    }

    public static ModuleBinAuditReport AuditPackage(
        KapeCatalog catalog,
        PackageDefinition pkg,
        string? modulesBinOverride = null)
    {
        var bin = modulesBinOverride ?? Path.Combine(catalog.KapeRoot, "Modules", "bin");
        var list = new List<ModuleBinAuditEntry>();
        foreach (var sel in pkg.Modules)
        {
            if (ModuleBinGate.IsSyncOrMaintenanceModule(sel))
            {
                list.Add(new ModuleBinAuditEntry(
                    sel.Name,
                    sel.Path,
                    ModuleBinStatus.SyncSkipped,
                    Array.Empty<string>(),
                    Array.Empty<string>()));
                continue;
            }

            list.Add(AuditSelection(catalog, sel, bin));
        }

        return new ModuleBinAuditReport(bin, list);
    }

    public static ModuleBinAuditReport AuditCatalog(
        KapeCatalog catalog,
        string? modulesBinOverride = null)
    {
        var bin = modulesBinOverride ?? Path.Combine(catalog.KapeRoot, "Modules", "bin");
        var index = IndexCatalog(catalog, bin);
        // Prefer AbsolutePath keys only once per module.
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var list = new List<ModuleBinAuditEntry>();
        foreach (var item in catalog.Modules)
        {
            var key = string.IsNullOrEmpty(item.AbsolutePath) ? item.Name : item.AbsolutePath;
            if (!seen.Add(key)) continue;
            if (index.TryGetValue(key, out var e) || index.TryGetValue(item.Name, out e))
                list.Add(e);
        }

        return new ModuleBinAuditReport(bin, list);
    }

    public static ModuleBinAuditEntry AuditSelection(
        KapeCatalog catalog,
        SelectionEntry sel,
        string modulesBin)
    {
        var leaves = catalog.FlattenToLeaves(
            new[] { string.IsNullOrWhiteSpace(sel.Path) ? sel.Name : sel.Path },
            ItemKind.Module);

        if (leaves.Count == 0)
        {
            // Unknown / empty — treat as Ok (same as FilterByAvailableBinaries).
            return new ModuleBinAuditEntry(
                sel.Name,
                sel.Path,
                ModuleBinStatus.Ok,
                Array.Empty<string>(),
                Array.Empty<string>());
        }

        return AggregateLeaves(sel.Name, sel.Path, leaves, modulesBin);
    }

    public static ModuleBinAuditEntry AuditCatalogItem(
        KapeCatalog catalog,
        CatalogItem item,
        string modulesBin)
    {
        if (!item.IsCompound)
            return AuditLeafFile(item.Name, Path.GetFileName(item.RelativePath), item.AbsolutePath, modulesBin);

        var leaves = catalog.FlattenToLeaves(
            new[] { Path.GetFileName(item.RelativePath) },
            ItemKind.Module);
        if (leaves.Count == 0)
            return new ModuleBinAuditEntry(
                item.Name,
                Path.GetFileName(item.RelativePath),
                ModuleBinStatus.Ok,
                Array.Empty<string>(),
                Array.Empty<string>());

        return AggregateLeaves(item.Name, Path.GetFileName(item.RelativePath), leaves, modulesBin);
    }

    public static string FormatReport(ModuleBinAuditReport report, int maxLines = 24, string? title = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine(title ?? "Проверка бинарников Modules\\bin для .mkape");
        sb.AppendLine($"Папка: {report.ModulesBinPath}");
        sb.AppendLine(
            $"Итого: ✓ {report.OkCount}, ~ {report.PartialCount}, ✗ {report.MissingCount}" +
            (report.SyncSkippedCount > 0 ? $", — sync {report.SyncSkippedCount}" : ""));
        sb.AppendLine();

        var issues = report.Issues;
        if (issues.Count == 0)
        {
            sb.Append("Проблем нет: выбранные модули видят нужные файлы в Modules\\bin.");
            return sb.ToString();
        }

        sb.AppendLine($"Модули с пробелами ({issues.Count}):");
        foreach (var e in issues.Take(maxLines))
        {
            var miss = e.Missing.Count == 0
                ? ""
                : " — нет " + string.Join(", ", e.Missing.Take(4)) +
                  (e.Missing.Count > 4 ? $" (+{e.Missing.Count - 4})" : "");
            sb.AppendLine($"  {e.DisplayGlyph} {e.Name}{miss}");
        }

        if (issues.Count > maxLines)
            sb.AppendLine($"  … и ещё {issues.Count - maxLines}");

        sb.AppendLine();
        sb.Append("Положите недостающее в Modules\\bin (или «Обновить с GitHub…») и повторите проверку.");
        return sb.ToString();
    }

    public static string FormatSyncSummary(ModuleBinAuditReport catalogReport, ModuleBinAuditReport? packageReport)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Каталог Modules после sync:");
        sb.AppendLine(
            $"  ✓ {catalogReport.OkCount}  ~ {catalogReport.PartialCount}  ✗ {catalogReport.MissingCount}" +
            (catalogReport.SyncSkippedCount > 0 ? $"  — sync {catalogReport.SyncSkippedCount}" : ""));

        if (catalogReport.HasIssues)
        {
            sb.AppendLine();
            sb.AppendLine("Примеры без бинарника:");
            foreach (var e in catalogReport.Issues.Take(12))
            {
                var hint = e.Missing.Count > 0 ? e.Missing[0] : "?";
                sb.AppendLine($"  ✗ {e.Name} ({hint})");
            }

            if (catalogReport.Issues.Count > 12)
                sb.AppendLine($"  … и ещё {catalogReport.Issues.Count - 12}");
        }

        if (packageReport is { Entries.Count: > 0 })
        {
            sb.AppendLine();
            if (packageReport.HasIssues)
            {
                sb.AppendLine("Текущая сборка — не хватает bin:");
                foreach (var e in packageReport.Issues.Take(10))
                {
                    var hint = e.Missing.Count > 0 ? string.Join(", ", e.Missing.Take(3)) : "?";
                    sb.AppendLine($"  {e.DisplayGlyph} {e.Name} ({hint})");
                }

                if (packageReport.Issues.Count > 10)
                    sb.AppendLine($"  … и ещё {packageReport.Issues.Count - 10}");
            }
            else
            {
                sb.AppendLine("Текущая сборка: все выбранные модули видят Modules\\bin.");
            }
        }

        sb.AppendLine();
        sb.Append("Фильтр «Без бинарника» на вкладке Модули или кнопка «Проверить bin».");
        return sb.ToString();
    }

    private static ModuleBinAuditEntry AggregateLeaves(
        string name,
        string? path,
        IReadOnlyList<CatalogItem> leaves,
        string modulesBin)
    {
        var required = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var missing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var anyOkLeaf = false;
        var anyMissingLeaf = false;
        var anyPartialLeaf = false;
        var onlySync = true;

        foreach (var leaf in leaves)
        {
            if (ModuleBinGate.IsSyncOrMaintenanceModule(leaf))
                continue;

            onlySync = false;
            var leafEntry = AuditLeafFile(
                leaf.Name,
                Path.GetFileName(leaf.RelativePath),
                leaf.AbsolutePath,
                modulesBin);

            foreach (var r in leafEntry.Required)
                required.Add(r);
            foreach (var m in leafEntry.Missing)
                missing.Add(m);

            switch (leafEntry.Status)
            {
                case ModuleBinStatus.Ok:
                    anyOkLeaf = true;
                    break;
                case ModuleBinStatus.Missing:
                    anyMissingLeaf = true;
                    break;
                case ModuleBinStatus.Partial:
                    anyPartialLeaf = true;
                    anyOkLeaf = true;
                    anyMissingLeaf = true;
                    break;
            }
        }

        if (onlySync)
        {
            return new ModuleBinAuditEntry(
                name, path, ModuleBinStatus.SyncSkipped,
                Array.Empty<string>(), Array.Empty<string>());
        }

        ModuleBinStatus status;
        if (anyPartialLeaf || (anyOkLeaf && anyMissingLeaf))
            status = ModuleBinStatus.Partial;
        else if (anyMissingLeaf)
            status = ModuleBinStatus.Missing;
        else
            status = ModuleBinStatus.Ok;

        return new ModuleBinAuditEntry(
            name,
            path,
            status,
            required.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList(),
            missing.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList());
    }

    private static ModuleBinAuditEntry AuditLeafFile(
        string name,
        string? path,
        string absolutePath,
        string modulesBin)
    {
        if (string.IsNullOrWhiteSpace(absolutePath) || !File.Exists(absolutePath))
        {
            return new ModuleBinAuditEntry(
                name, path, ModuleBinStatus.Ok,
                Array.Empty<string>(), Array.Empty<string>());
        }

        var payloads = ModuleBinGate.ExtractLeafBinPayloads(absolutePath);
        if (payloads.Count == 0)
        {
            return new ModuleBinAuditEntry(
                name, path, ModuleBinStatus.Ok,
                Array.Empty<string>(), Array.Empty<string>());
        }

        var missing = payloads
            .Where(p => !ModuleBinGate.BinaryExists(modulesBin, p))
            .ToList();

        var status = missing.Count == 0
            ? ModuleBinStatus.Ok
            : missing.Count == payloads.Count
                ? ModuleBinStatus.Missing
                : ModuleBinStatus.Partial;

        return new ModuleBinAuditEntry(name, path, status, payloads, missing);
    }

    private static void AddKeys(
        Dictionary<string, ModuleBinAuditEntry> map,
        CatalogItem item,
        ModuleBinAuditEntry entry)
    {
        if (!string.IsNullOrEmpty(item.AbsolutePath))
            map[item.AbsolutePath] = entry;
        map[item.Name] = entry;
        var file = Path.GetFileName(item.RelativePath);
        if (!string.IsNullOrEmpty(file))
            map[file] = entry;
    }
}
