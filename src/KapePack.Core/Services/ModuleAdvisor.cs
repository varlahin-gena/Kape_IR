using KapePack.Core.Models;

namespace KapePack.Core.Services;

/// <summary>
/// Suggests modules for selected targets using FileMask overlap, curated aliases,
/// and name/description heuristics. Prefers EZTools leaf modules.
/// </summary>
public sealed class ModuleAdvisor
{
    private readonly KapeCatalog _catalog;

    /// <summary>Known Windows target → preferred leaf module names (without extension).</summary>
    private static readonly Dictionary<string, string[]> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Prefetch"] = new[] { "PECmd" },
        ["Amcache"] = new[] { "AmcacheParser" },
        ["RecentFileCache"] = new[] { "RecentFileCacheParser" },
        ["WindowsTimeline"] = new[] { "WxTCmd" },
        ["JumpLists"] = new[] { "JLECmd" },
        ["LNKFilesAndJumpLists"] = new[] { "LECmd", "JLECmd" },
        ["EventLogs"] = new[] { "EvtxECmd", "Chainsaw_Offline", "Hayabusa_Offline" },
        ["EventLogs-RDP"] = new[] { "EvtxECmd_RDP", "EvtxECmd", "Chainsaw_Offline" },
        ["CombinedLogs"] = new[] { "Chainsaw_Offline", "Hayabusa_Offline", "EvtxECmd" },
        ["SRUM"] = new[] { "SrumECmd" },
        ["SUM"] = new[] { "SumECmd" },
        ["RegistryHivesSystem"] = new[] { "AppCompatCacheParser", "RECmd_DFIRBatch" },
        ["RegistryHivesUser"] = new[] { "RECmd_UserActivity", "RECmd_DFIRBatch" },
        ["$MFT"] = new[] { "MFTECmd_$MFT" },
        ["$J"] = new[] { "MFTECmd_$J", "NTFSLogTracker_$J" },
        ["$Boot"] = new[] { "MFTECmd_$Boot" },
        ["$SDS"] = new[] { "MFTECmd_$SDS" },
        ["$LogFile"] = new[] { "NTFSLogTracker_$LogFile" },
        ["ThumbCache"] = new[] { "ThumbCacheViewer" },
        ["IconCacheDB"] = new[] { "ThumbCacheViewer" },
        ["ScheduledTasks"] = new[] { "EvtxECmd" },
        ["PowerShellTranscripts"] = new[] { "EvtxECmd" },
        ["USBDevicesLogs"] = new[] { "EvtxECmd" },
    };

    public ModuleAdvisor(KapeCatalog catalog) => _catalog = catalog;

    public List<ModuleSuggestion> Suggest(
        IEnumerable<SelectionEntry> selectedTargets,
        IEnumerable<SelectionEntry>? selectedModules = null,
        bool includeCompounds = false,
        int maxPerTarget = 4)
    {
        var leaves = _catalog.FlattenToLeaves(
            selectedTargets.Select(t => string.IsNullOrWhiteSpace(t.Path) ? t.Name : t.Path),
            ItemKind.Target);

        if (leaves.Count == 0)
            return new List<ModuleSuggestion>();

        var already = KapeCatalog.BuildSelectionKeys(selectedModules ?? Enumerable.Empty<SelectionEntry>());
        var scored = new Dictionary<string, Accumulator>(StringComparer.OrdinalIgnoreCase);

        foreach (var target in leaves)
        {
            var hits = new List<(CatalogItem mod, int score, string reason)>();

            // 1) Curated aliases
            if (Aliases.TryGetValue(target.Name, out var aliasNames))
            {
                foreach (var alias in aliasNames)
                {
                    var mod = _catalog.FindModule(alias);
                    if (mod is null || mod.IsCompound) continue;
                    hits.Add((mod, 95, $"алиас: {target.Name} → {mod.Name}"));
                }
            }

            // 2) FileMask overlap
            foreach (var mod in _catalog.Modules)
            {
                if (mod.IsCompound && !includeCompounds) continue;
                if (mod.FileMasks.Count == 0 || target.FileMasks.Count == 0) continue;

                var overlap = MaskOverlap(target.FileMasks, mod.FileMasks);
                if (overlap is null) continue;
                hits.Add((mod, 100, $"FileMask: {overlap}"));
            }

            // 3) Name / description heuristics (helps PECmd etc. without FileMask)
            foreach (var mod in _catalog.Modules)
            {
                if (mod.IsCompound && !includeCompounds) continue;
                var nameScore = NameHeuristic(target, mod);
                if (nameScore is null) continue;
                hits.Add((mod, nameScore.Value.score, nameScore.Value.reason));
            }

            // Rank and keep top N per target; prefer EZTools / higher path priority
            var best = hits
                .GroupBy(h => h.mod.AbsolutePath, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.OrderByDescending(h => h.score + PathBonus(h.mod)).First())
                .OrderByDescending(h => h.score + PathBonus(h.mod))
                .ThenBy(h => h.mod.Name, StringComparer.OrdinalIgnoreCase)
                .Take(maxPerTarget)
                .ToList();

            foreach (var (mod, score, reason) in best)
            {
                var key = mod.AbsolutePath;
                if (!scored.TryGetValue(key, out var acc))
                {
                    acc = new Accumulator { Module = mod };
                    scored[key] = acc;
                }
                acc.Score = Math.Max(acc.Score, score + PathBonus(mod));
                if (!acc.Reasons.Contains(reason))
                    acc.Reasons.Add(reason);
                if (!acc.Targets.Contains(target.Name, StringComparer.OrdinalIgnoreCase))
                    acc.Targets.Add(target.Name);
            }
        }

        return scored.Values
            .Select(a => new ModuleSuggestion
            {
                Module = a.Module,
                Score = a.Score,
                Reason = string.Join("; ", a.Reasons.Take(3)),
                MatchedTargets = a.Targets.OrderBy(t => t, StringComparer.OrdinalIgnoreCase).ToList(),
                AlreadySelected = KapeCatalog.IsSelected(a.Module, already)
            })
            .OrderByDescending(s => s.Score)
            .ThenBy(s => s.AlreadySelected)
            .ThenBy(s => s.Module.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static int PathBonus(CatalogItem mod)
    {
        var path = mod.RelativePath.Replace('\\', '/');
        if (path.Contains("/EZTools/", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("Modules/EZTools/", StringComparison.OrdinalIgnoreCase))
            return 10;
        if (path.Contains("/Compound/", StringComparison.OrdinalIgnoreCase))
            return -5;
        if (path.Contains("/KapeResearch/", StringComparison.OrdinalIgnoreCase))
            return -8;
        if (path.Contains("/TZWorks/", StringComparison.OrdinalIgnoreCase))
            return -3;
        return 0;
    }

    public static string? MaskOverlap(IEnumerable<string> targetMasks, IEnumerable<string> moduleMasks)
    {
        foreach (var tm in targetMasks)
        {
            var tNorm = NormalizeMask(tm);
            if (string.IsNullOrEmpty(tNorm.stem)) continue;
            foreach (var mm in moduleMasks)
            {
                var mNorm = NormalizeMask(mm);
                if (string.IsNullOrEmpty(mNorm.stem)) continue;

                if (string.Equals(tNorm.raw, mNorm.raw, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(tNorm.stem, mNorm.stem, StringComparison.OrdinalIgnoreCase))
                    return tm;

                // *.pf vs something ending with .pf
                if (tNorm.ext is not null && mNorm.ext is not null &&
                    string.Equals(tNorm.ext, mNorm.ext, StringComparison.OrdinalIgnoreCase) &&
                    (tNorm.isWildcard || mNorm.isWildcard))
                    return tm;

                // Amcache.hve.LOG* vs Amcache.hve
                if (tNorm.stem.StartsWith(mNorm.stem, StringComparison.OrdinalIgnoreCase) ||
                    mNorm.stem.StartsWith(tNorm.stem, StringComparison.OrdinalIgnoreCase))
                {
                    if (tNorm.stem.Length >= 3 && mNorm.stem.Length >= 3)
                        return tm;
                }
            }
        }
        return null;
    }

    private static (int score, string reason)? NameHeuristic(CatalogItem target, CatalogItem mod)
    {
        var tName = target.Name;
        if (tName.Length < 3) return null;

        if (string.Equals(mod.Name, tName, StringComparison.OrdinalIgnoreCase) ||
            mod.Name.Contains(tName, StringComparison.OrdinalIgnoreCase) ||
            tName.Contains(mod.Name, StringComparison.OrdinalIgnoreCase))
        {
            return (85, $"имя: {tName} ↔ {mod.Name}");
        }

        var blob = (mod.Description + " " + mod.Category).ToLowerInvariant();
        var key = tName.TrimStart('$').ToLowerInvariant();
        if (key.Length >= 4 && blob.Contains(key, StringComparison.Ordinal))
            return (55, $"описание упоминает {tName}");

        // Prefetch ↔ "prefetch files"
        if (key is "prefetch" && blob.Contains("prefetch", StringComparison.Ordinal))
            return (70, "описание: prefetch");

        return null;
    }

    private static (string raw, string stem, string? ext, bool isWildcard) NormalizeMask(string mask)
    {
        var raw = mask.Trim().Trim('"', '\'');
        var isWildcard = raw.Contains('*') || raw.Contains('?');
        var noWild = raw.Replace("*", "").Replace("?", "");
        var stem = Path.GetFileNameWithoutExtension(noWild);
        if (string.IsNullOrEmpty(stem))
            stem = noWild;
        // Strip trailing .LOG etc. noise for comparison
        if (stem.EndsWith(".LOG", StringComparison.OrdinalIgnoreCase))
            stem = stem[..^4];
        string? ext = null;
        var extPart = Path.GetExtension(noWild);
        if (!string.IsNullOrEmpty(extPart))
            ext = extPart;
        else if (raw.StartsWith("*.", StringComparison.Ordinal))
            ext = raw[1..]; // .pf
        return (raw, stem, ext, isWildcard);
    }

    private sealed class Accumulator
    {
        public CatalogItem Module { get; set; } = null!;
        public int Score { get; set; }
        public List<string> Reasons { get; } = new();
        public List<string> Targets { get; } = new();
    }
}
