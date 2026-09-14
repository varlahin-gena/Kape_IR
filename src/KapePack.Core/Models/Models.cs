namespace KapePack.Core.Models;

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
    /// <summary>GitHub KapeFiles vs local-only (see CatalogOriginLabels).</summary>
    public CatalogOrigin Origin { get; init; }

    public string DisplayName => IsCompound ? $"[C] {Name}" : Name;
    public string OriginLabel => CatalogOriginLabels.Display(Origin);
    public string OriginTag => CatalogOriginLabels.Short(Origin);

    public string SearchBlob =>
        string.Join(' ', new[] { Name, Category, Description, Author, RelativePath, CatalogOriginLabels.SearchToken(Origin) }
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

/// <summary>How CollectPack / Runner invokes kape.exe.</summary>
public enum IrCollectionMode
{
    /// <summary>One kape run: --target then --module (legacy).</summary>
    Single = 0,
    /// <summary>Phase1 modules (volatile) then Phase2 disk targets.</summary>
    TwoPhase = 1
}

public sealed class PackageDefinition
{
    public const string DefaultPhase1Module = "VolatileFirst";
    public const string DefaultPhase1ModuleNoMemory = "VolatileFirst_NoMemory";

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

    /// <summary>Single (default) or TwoPhase volatile-then-disk IR.</summary>
    public IrCollectionMode CollectionMode { get; set; } = IrCollectionMode.Single;

    /// <summary>Case / ticket id written into chain-of-custody on the host.</summary>
    public string CaseId { get; set; } = "";

    /// <summary>Phase 1 compound module name (two_phase). Default VolatileFirst.</summary>
    public string Phase1ModuleName { get; set; } = DefaultPhase1Module;

    /// <summary>
    /// Optional phase-2 module compound (parsers after disk copy).
    /// When null at export time, derived from non-VolatileFirst selected modules.
    /// </summary>
    public string? Phase2ModuleName { get; set; }

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

    /// <summary>Module name passed to kape --module (phase 1 in two_phase mode).</summary>
    public string? ModuleCompoundName
    {
        get
        {
            if (CollectionMode == IrCollectionMode.TwoPhase)
            {
                var p1 = string.IsNullOrWhiteSpace(Phase1ModuleName)
                    ? DefaultPhase1Module
                    : Phase1ModuleName.Trim();
                return p1;
            }

            if (Modules.Count == 0) return null;
            return TargetCompoundName + "_Modules";
        }
    }

    public bool IsTwoPhase => CollectionMode == IrCollectionMode.TwoPhase;

    /// <summary>Default generated name for phase-2 parser compound.</summary>
    public string DefaultPhase2ModuleCompoundName => TargetCompoundName + "_Modules";

    public static bool IsVolatileFirstPlaybookModule(string? nameOrPath)
    {
        if (string.IsNullOrWhiteSpace(nameOrPath)) return false;
        var bare = Path.GetFileNameWithoutExtension(nameOrPath.Trim());
        return bare.Equals(DefaultPhase1Module, StringComparison.OrdinalIgnoreCase) ||
               bare.Equals(DefaultPhase1ModuleNoMemory, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Modules that run in phase 2 (anything except VolatileFirst playbooks).</summary>
    public List<SelectionEntry> GetPhase2ModuleEntries()
        => Modules
            .Where(m => !IsVolatileFirstPlaybookModule(m.Path) && !IsVolatileFirstPlaybookModule(m.Name))
            .ToList();

    /// <summary>Resolve phase2 module compound name from selection (or explicit Phase2ModuleName).</summary>
    public string? ResolvePhase2ModuleName()
    {
        if (!IsTwoPhase) return null;
        if (!string.IsNullOrWhiteSpace(Phase2ModuleName))
            return Phase2ModuleName.Trim();
        return GetPhase2ModuleEntries().Count > 0 ? DefaultPhase2ModuleCompoundName : null;
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
    /// <summary>Added + updated (actual writes; same counts for dry-run would-change).</summary>
    public int TargetsCopied { get; init; }
    public int ModulesCopied { get; init; }
    public int TargetsAdded { get; init; }
    public int TargetsUpdated { get; init; }
    public int TargetsUnchanged { get; init; }
    public int ModulesAdded { get; init; }
    public int ModulesUpdated { get; init; }
    public int ModulesUnchanged { get; init; }
    public long ZipBytes { get; init; }
    public List<string> Errors { get; init; } = new();
    public string SyncedAt { get; init; } = "";
    public string? BackupDir { get; init; }
    public string? ZipSha256 { get; init; }
    public bool IsDryRun { get; init; }
    /// <summary>Path to a persisted/reused zip for a subsequent apply without re-download.</summary>
    public string? CachedZipPath { get; init; }
    public List<string> AddedSamples { get; init; } = new();
    public List<string> UpdatedSamples { get; init; } = new();
    /// <summary>.tkape under Modules or .mkape under Targets from upstream — skipped.</summary>
    public List<string> MisplacedSamples { get; init; } = new();
}

public sealed class OverlapStats
{
    public int Compounds { get; init; }
    public int UniqueLeaves { get; init; }
    public int SharedLeaves { get; init; }
}
