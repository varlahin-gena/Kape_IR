using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace KapePack.Core.Services;

/// <summary>Scans KAPE Modules\bin for installed tool binaries (exe + root scripts).</summary>
public static class ModulesBinInventory
{
    private static readonly HashSet<string> KeyNames =
        new(EzToolsUpdater.KeyBinaries, StringComparer.OrdinalIgnoreCase);

    public static ModulesBinReport Scan(string kapeRoot)
    {
        var bin = Path.Combine(kapeRoot, "Modules", "bin");
        var items = new List<ModulesBinItem>();
        if (!Directory.Exists(bin))
        {
            return new ModulesBinReport
            {
                ModulesBinPath = bin,
                Exists = false,
                Message = "Папка Modules\\bin отсутствует.",
                Chainsaw = ChainsawInstaller.Check(kapeRoot),
                EzTools = EzToolsUpdater.Check(kapeRoot)
            };
        }

        foreach (var exe in Directory.EnumerateFiles(bin, "*.exe", SearchOption.AllDirectories))
        {
            try { items.Add(BuildItem(bin, exe)); }
            catch { /* skip locked/unreadable */ }
        }

        foreach (var ps1 in Directory.EnumerateFiles(bin, "*.ps1", SearchOption.TopDirectoryOnly))
        {
            try { items.Add(BuildItem(bin, ps1)); }
            catch { /* skip */ }
        }

        items = items
            .OrderBy(i => i.Group, StringComparer.OrdinalIgnoreCase)
            .ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        long totalBytes = items.Sum(i => i.SizeBytes);
        var chainsaw = ChainsawInstaller.Check(kapeRoot);
        var ez = EzToolsUpdater.Check(kapeRoot);

        var presentKeys = items
            .Where(i => KeyNames.Contains(i.Name))
            .Select(i => i.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new ModulesBinReport
        {
            ModulesBinPath = bin,
            Exists = true,
            Items = items,
            TotalCount = items.Count,
            TotalBytes = totalBytes,
            ExeCount = items.Count(i => i.Kind == ModulesBinKind.Executable),
            Chainsaw = chainsaw,
            EzTools = ez,
            PresentKeyTools = presentKeys,
            Message = BuildSummary(bin, items.Count, totalBytes, chainsaw, ez)
        };
    }

    private static ModulesBinItem BuildItem(string binRoot, string absolutePath)
    {
        var fi = new FileInfo(absolutePath);
        var rel = Path.GetRelativePath(binRoot, absolutePath);
        var dir = Path.GetDirectoryName(rel) ?? "";
        if (string.IsNullOrEmpty(dir) || dir == ".")
            dir = "(корень)";

        string? product = null, fileVer = null, productVer = null, description = null, company = null;
        if (fi.Extension.Equals(".exe", StringComparison.OrdinalIgnoreCase) ||
            fi.Extension.Equals(".dll", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var vi = FileVersionInfo.GetVersionInfo(absolutePath);
                product = NullIfEmpty(vi.ProductName);
                fileVer = NullIfEmpty(vi.FileVersion);
                productVer = NullIfEmpty(vi.ProductVersion);
                description = NullIfEmpty(vi.FileDescription);
                company = NullIfEmpty(vi.CompanyName);
            }
            catch { /* non-PE or locked */ }
        }

        var name = fi.Name;
        var category = Classify(rel, name);
        var kind = fi.Extension.Equals(".ps1", StringComparison.OrdinalIgnoreCase)
            ? ModulesBinKind.Script
            : ModulesBinKind.Executable;

        return new ModulesBinItem
        {
            Name = name,
            RelativePath = rel.Replace('/', '\\'),
            Group = dir.Replace('/', '\\'),
            AbsolutePath = absolutePath,
            SizeBytes = fi.Length,
            ModifiedUtc = fi.LastWriteTimeUtc,
            ProductName = product,
            FileVersion = fileVer,
            ProductVersion = productVer,
            Description = description,
            Company = company,
            Category = category,
            Kind = kind,
            IsKeyTool = KeyNames.Contains(name)
        };
    }

    private static string Classify(string relativePath, string fileName)
    {
        var rel = relativePath.Replace('/', '\\');
        if (rel.StartsWith("chainsaw\\", StringComparison.OrdinalIgnoreCase) ||
            rel.Equals("chainsaw", StringComparison.OrdinalIgnoreCase))
            return "Chainsaw";
        if (rel.StartsWith("net9\\", StringComparison.OrdinalIgnoreCase) ||
            rel.StartsWith("net6\\", StringComparison.OrdinalIgnoreCase) ||
            rel.StartsWith("ZimmermanTools\\", StringComparison.OrdinalIgnoreCase))
            return "EZ Tools";
        if (fileName.Equals("autorunsc.exe", StringComparison.OrdinalIgnoreCase) ||
            fileName.Equals("pslist.exe", StringComparison.OrdinalIgnoreCase) ||
            fileName.StartsWith("Ps", StringComparison.OrdinalIgnoreCase))
            return "Sysinternals";
        if (fileName.Equals("7z.exe", StringComparison.OrdinalIgnoreCase))
            return "Утилита";
        if (fileName.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase))
            return "Скрипт";
        if (KeyNames.Contains(fileName))
            return "EZ Tools";
        return "Прочее";
    }

    private static string BuildSummary(
        string bin, int count, long bytes, ChainsawStatus chainsaw, EzToolsStatus ez)
    {
        var sb = new StringBuilder();
        sb.Append($"{bin} — {count} файл(ов), {FormatSize(bytes)}");
        sb.Append(chainsaw.Ok ? " · Chainsaw OK" : " · Chainsaw: " + (chainsaw.NeedsInstall ? "нет/неполный" : "проблема"));
        if (ez.NeedsUpdate)
            sb.Append($" · нет ключевых EZ: {ez.Missing.Count}");
        else
            sb.Append(" · ключевые EZ OK");
        return sb.ToString();
    }

    public static string FormatSize(long bytes)
    {
        double b = bytes;
        string[] units = ["Б", "КБ", "МБ", "ГБ"];
        var u = 0;
        while (b >= 1024 && u < units.Length - 1)
        {
            b /= 1024;
            u++;
        }
        return u == 0
            ? $"{bytes} {units[u]}"
            : string.Format(CultureInfo.InvariantCulture, "{0:0.##} {1}", b, units[u]);
    }

    private static string? NullIfEmpty(string? s)
        => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}

public enum ModulesBinKind
{
    Executable,
    Script
}

public sealed class ModulesBinItem
{
    public string Name { get; init; } = "";
    public string RelativePath { get; init; } = "";
    public string Group { get; init; } = "";
    public string AbsolutePath { get; init; } = "";
    public long SizeBytes { get; init; }
    public DateTime ModifiedUtc { get; init; }
    public string? ProductName { get; init; }
    public string? FileVersion { get; init; }
    public string? ProductVersion { get; init; }
    public string? Description { get; init; }
    public string? Company { get; init; }
    public string Category { get; init; } = "";
    public ModulesBinKind Kind { get; init; }
    public bool IsKeyTool { get; init; }

    public string SizeDisplay => ModulesBinInventory.FormatSize(SizeBytes);
    public string ModifiedLocalDisplay => ModifiedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
    public string VersionDisplay => FileVersion ?? ProductVersion ?? "—";
}

public sealed class ModulesBinReport
{
    public string ModulesBinPath { get; init; } = "";
    public bool Exists { get; init; }
    public List<ModulesBinItem> Items { get; init; } = new();
    public int TotalCount { get; init; }
    public int ExeCount { get; init; }
    public long TotalBytes { get; init; }
    public ChainsawStatus Chainsaw { get; init; } = new();
    public EzToolsStatus EzTools { get; init; } = new();
    public List<string> PresentKeyTools { get; init; } = new();
    public string Message { get; init; } = "";
}
