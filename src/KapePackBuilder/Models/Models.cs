namespace KapePackBuilder.Models;

public enum ItemKind
{
    Target,
    Module
}

public sealed class CatalogItem
{
    public ItemKind Kind { get; init; }
    public string Name { get; init; } = "";
    public string RelativePath { get; init; } = "";
    public string Category { get; init; } = "";
    public string Description { get; init; } = "";
    public string Author { get; init; } = "";
    public string Version { get; init; } = "";
    public string ItemId { get; init; } = "";
    public bool IsCompound { get; init; }
    public List<string> Children { get; init; } = new();
    public List<string> FileMasks { get; init; } = new();
    public List<string> DocumentationUrls { get; init; } = new();
    public string AbsolutePath { get; init; } = "";

    public string DisplayName => IsCompound ? $"[C] {Name}" : Name;

    public string SearchBlob =>
        string.Join(' ', new[] { Name, Category, Description, Author, RelativePath }
                .Concat(Children)
                .Concat(FileMasks)
                .Concat(DocumentationUrls))
            .ToLowerInvariant();
}

public sealed class ModuleSuggestion
{
    public CatalogItem Module { get; init; } = null!;
    public int Score { get; init; }
    public string Reason { get; init; } = "";
    public List<string> MatchedTargets { get; init; } = new();
    public bool AlreadySelected { get; init; }
}

public sealed class SelectionEntry
{
    public string Name { get; set; } = "";
    public string Category { get; set; } = "General";
    public string Path { get; set; } = "";
    public string Comments { get; set; } = "";
}

public sealed class PackageDefinition
{
    public string Name { get; set; } = "WindowsTriage";
    public string Description { get; set; } = "Пакет Windows triage";
    public string Author { get; set; } = "";
    public string Version { get; set; } = "1.0";
    public string PackageId { get; set; } = Guid.NewGuid().ToString();
    public bool RecreateDirectories { get; set; } = true;
    public List<SelectionEntry> Targets { get; set; } = new();
    public List<SelectionEntry> Modules { get; set; } = new();
    public string Tsource { get; set; } = "C:";
    public bool ZipOutput { get; set; } = true;
    public bool Flush { get; set; }
    public bool Vss { get; set; }
    public string Notes { get; set; } = "";

    /// <summary>
    /// Compound target name (= .tkape file name without extension).
    /// Do not auto-prefix '!': CMD delayed expansion eats it and breaks --target.
    /// </summary>
    public string TargetCompoundName
    {
        get
        {
            var clean = SafeName(Name).TrimStart('!');
            return string.IsNullOrEmpty(clean) ? "WindowsTriage" : clean;
        }
    }

    public string? ModuleCompoundName
    {
        get
        {
            if (Modules.Count == 0) return null;
            return TargetCompoundName + "_Modules";
        }
    }

    public static string SafeName(string name)
    {
        var cleaned = new string(name.Trim().Select(ch =>
            char.IsLetterOrDigit(ch) || ch is '_' or '-' or '!' ? ch : '_').ToArray());
        return string.IsNullOrEmpty(cleaned) ? "CustomPackage" : cleaned;
    }

    public static string SafeDir(string name)
    {
        var cleaned = SafeName(name).TrimStart('!');
        return string.IsNullOrEmpty(cleaned) ? "CustomPackage" : cleaned;
    }
}

public sealed class ExportResult
{
    public string PackageDir { get; init; } = "";
    public string TargetFile { get; init; } = "";
    public string? ModuleFile { get; init; }
    public string BatFile { get; init; } = "";
    public string Ps1File { get; init; } = "";
    public string ManifestFile { get; init; } = "";
    public string? ZipFile { get; init; }
    public string? StandaloneExe { get; init; }
    public string? InstalledTarget { get; init; }
    public string? InstalledModule { get; init; }
    public List<string> Warnings { get; init; } = new();
}

public sealed class SyncResult
{
    public bool Ok { get; init; }
    public string Message { get; init; } = "";
    public int TargetsCopied { get; init; }
    public int ModulesCopied { get; init; }
    public long ZipBytes { get; init; }
    public List<string> Errors { get; init; } = new();
    public string SyncedAt { get; init; } = "";
}

public sealed class OverlapStats
{
    public int Compounds { get; init; }
    public int UniqueLeaves { get; init; }
    public int SharedLeaves { get; init; }
}
