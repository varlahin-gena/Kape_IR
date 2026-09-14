using KapePack.Core.Models;

namespace KapePack.Core.Services;

/// <summary>
/// KAPE requires globally unique target/module file names (basename) across the whole tree.
/// Duplicate Apps\X.tkape + Compound\X.tkape (or Apps + Windows) fails validation.
/// </summary>
public static class NameCollisionFixer
{
    public sealed record CollisionGroup(string FileName, IReadOnlyList<CatalogItem> Items);

    public sealed class FixResult
    {
        public int Groups { get; init; }
        public int Removed { get; set; }
        public List<string> Kept { get; } = new();
        public List<string> RemovedPaths { get; } = new();
        public List<string> Errors { get; } = new();
        public string QuarantineDir { get; init; } = "";
    }

    public static List<CollisionGroup> FindCollisions(IEnumerable<CatalogItem> items)
    {
        return items
            .GroupBy(i => Path.GetFileName(i.AbsolutePath), StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => new CollisionGroup(
                g.Key,
                g.OrderByDescending(PreferScore).ThenBy(i => i.RelativePath, StringComparer.OrdinalIgnoreCase).ToList()))
            .OrderBy(g => g.FileName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Keep the preferred file in each collision group; move the rest under PackBuilder\name_collisions\.
    /// </summary>
    public static FixResult FixCollisions(string kapeRoot, IEnumerable<CatalogItem> items, bool dryRun = false)
    {
        var groups = FindCollisions(items);
        var quarantine = Path.Combine(kapeRoot, "PackBuilder", "name_collisions");
        var result = new FixResult { Groups = groups.Count, QuarantineDir = quarantine };

        if (groups.Count == 0) return result;
        if (!dryRun)
            Directory.CreateDirectory(quarantine);

        foreach (var group in groups)
        {
            var ordered = group.Items;
            var keep = ordered[0];
            result.Kept.Add(keep.RelativePath);
            foreach (var loser in ordered.Skip(1))
            {
                result.RemovedPaths.Add(loser.RelativePath);
                if (dryRun)
                {
                    result.Removed++;
                    continue;
                }

                try
                {
                    var stamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
                    var safeRel = loser.RelativePath.Replace('/', '_').Replace('\\', '_');
                    var dest = Path.Combine(quarantine, stamp + "__" + safeRel);
                    Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                    if (File.Exists(dest))
                        dest = Path.Combine(quarantine, stamp + "_" + Guid.NewGuid().ToString("N")[..8] + "__" + safeRel);
                    File.Move(loser.AbsolutePath, dest, overwrite: false);
                    result.Removed++;
                }
                catch (Exception ex)
                {
                    result.Errors.Add($"{loser.RelativePath}: {ex.Message}");
                }
            }
        }

        return result;
    }

    /// <summary>Among candidates with the same basename, pick which AbsolutePath to keep/copy.</summary>
    public static CatalogItem Prefer(IEnumerable<CatalogItem> items)
        => items.OrderByDescending(PreferScore)
            .ThenBy(i => i.RelativePath, StringComparer.OrdinalIgnoreCase)
            .First();

    /// <summary>
    /// Skip writing if another path under <paramref name="searchRoot"/> already has this basename.
    /// If the new path is preferred, the existing file is moved aside and write is allowed.
    /// </summary>
    public static bool ShouldWriteUniqueBasename(
        string searchRoot,
        string destPath,
        string srcPath,
        out string? skipReason)
        => ShouldWriteUniqueBasename(new[] { searchRoot }, destPath, srcPath, out skipReason);

    /// <summary>
    /// KAPE requires globally unique .tkape/.mkape basenames across Targets and Modules.
    /// Pass both trees as <paramref name="searchRoots"/> when syncing from GitHub.
    /// </summary>
    public static bool ShouldWriteUniqueBasename(
        IEnumerable<string> searchRoots,
        string destPath,
        string srcPath,
        out string? skipReason)
    {
        skipReason = null;
        var name = Path.GetFileName(destPath);
        if (string.IsNullOrEmpty(name)) return true;

        var roots = searchRoots
            .Where(r => !string.IsNullOrWhiteSpace(r) && Directory.Exists(r))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        string? existingOther = null;
        foreach (var root in roots)
        {
            foreach (var hit in Directory.EnumerateFiles(root, name, SearchOption.AllDirectories))
            {
                if (IsUnderDisabledFolder(hit)) continue;
                if (!PathsEqual(hit, destPath))
                {
                    existingOther = hit;
                    break;
                }
            }
            if (existingOther is not null) break;
        }

        if (existingOther is null)
            return true;

        var newRel = BestRelHint(roots, destPath);
        var oldRel = BestRelHint(roots, existingOther);
        var newScore = PathPreferScore(newRel);
        var oldScore = PathPreferScore(oldRel);

        if (File.Exists(existingOther) && FilesContentEqual(srcPath, existingOther))
        {
            // Identical bytes: keep the better-located copy; drop a misplaced duplicate.
            if (newScore <= oldScore)
            {
                skipReason = $"пропуск дубликата имени {name} (уже есть {oldRel})";
                return false;
            }
            // Prefer destPath — quarantine existingOther below.
        }
        else if (newScore <= oldScore)
        {
            skipReason =
                $"пропуск {newRel}: имя {name} уже занято ({oldRel})";
            return false;
        }

        try
        {
            var quarantineBase = roots.Count > 0
                ? Path.GetDirectoryName(roots[0]) ?? roots[0]
                : Path.GetDirectoryName(destPath) ?? destPath;
            var quarantine = Path.Combine(quarantineBase, "PackBuilder", "name_collisions");
            Directory.CreateDirectory(quarantine);
            var dest = Path.Combine(quarantine,
                DateTime.UtcNow.ToString("yyyyMMdd_HHmmss") + "__" +
                oldRel.Replace('/', '_').Replace('\\', '_'));
            File.Move(existingOther, dest, overwrite: false);
            return true;
        }
        catch (Exception ex)
        {
            skipReason =
                $"не удалось заменить {oldRel} на предпочтительный путь: {ex.Message}";
            return false;
        }
    }

    /// <summary>
    /// .tkape belong under Targets; .mkape under Modules. Upstream sometimes misplaces them.
    /// </summary>
    public static bool IsWrongTreeExtension(string relativeOrAbsolutePath, bool writingModules)
    {
        var name = Path.GetFileName(relativeOrAbsolutePath);
        if (string.IsNullOrEmpty(name)) return false;
        if (writingModules)
            return name.EndsWith(".tkape", StringComparison.OrdinalIgnoreCase);
        return name.EndsWith(".mkape", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>True when a .tkape sits under Modules or a .mkape under Targets.</summary>
    public static bool IsMisplacedKapeFile(string relativeOrAbsolutePath)
    {
        var rel = relativeOrAbsolutePath.Replace('\\', '/');
        var name = Path.GetFileName(rel);
        if (string.IsNullOrEmpty(name)) return false;
        var underModules = rel.Contains("/Modules/", StringComparison.OrdinalIgnoreCase) ||
                           rel.StartsWith("Modules/", StringComparison.OrdinalIgnoreCase);
        var underTargets = rel.Contains("/Targets/", StringComparison.OrdinalIgnoreCase) ||
                           rel.StartsWith("Targets/", StringComparison.OrdinalIgnoreCase);
        if (underModules && name.EndsWith(".tkape", StringComparison.OrdinalIgnoreCase))
            return true;
        if (underTargets && name.EndsWith(".mkape", StringComparison.OrdinalIgnoreCase))
            return true;
        return false;
    }

    /// <summary>
    /// KAPE keeps unused targets/modules under Targets\!Disabled and Modules\!Disabled
    /// (also legacy _Disabled). These must not be treated as active catalog items.
    /// </summary>
    public static bool IsUnderDisabledFolder(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        foreach (var part in path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (part.Equals("!Disabled", StringComparison.OrdinalIgnoreCase) ||
                part.Equals("_Disabled", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    public static int PathPreferScore(string relativePath)
    {
        var rel = relativePath.Replace('\\', '/');
        var score = 100;
        if (IsUnderDisabledFolder(rel))
            score -= 80;
        if (IsMisplacedKapeFile(rel))
            score -= 90;
        if (rel.Contains("/Compound/", StringComparison.OrdinalIgnoreCase) ||
            rel.StartsWith("Compound/", StringComparison.OrdinalIgnoreCase))
            score -= 40;
        score -= Math.Min(rel.Count(c => c == '/'), 5);
        return score;
    }

    public static int PreferScore(CatalogItem item)
    {
        var rel = item.RelativePath.Replace('\\', '/');
        var score = 100;
        if (IsUnderDisabledFolder(rel))
            score -= 80;
        if (IsMisplacedKapeFile(rel))
            score -= 90;

        var underCompound = rel.Contains("/Compound/", StringComparison.OrdinalIgnoreCase) ||
                            rel.StartsWith("Targets/Compound/", StringComparison.OrdinalIgnoreCase) ||
                            rel.StartsWith("Modules/Compound/", StringComparison.OrdinalIgnoreCase);

        if (item.IsCompound)
        {
            // Real compounds belong in Compound/
            if (underCompound) score += 40;
            else score -= 10;
        }
        else
        {
            // Leaf targets wrongly sitting in Compound/ are common duplicates of Apps/Windows copies
            if (underCompound) score -= 50;
            else score += 20;
        }

        // Prefer shallower paths slightly
        score -= Math.Min(rel.Count(c => c == '/'), 5);
        return score;
    }

    private static string BestRelHint(IReadOnlyList<string> roots, string absolute)
    {
        foreach (var root in roots)
        {
            try
            {
                var rel = Path.GetRelativePath(root, absolute);
                if (!rel.StartsWith("..", StringComparison.Ordinal) && !Path.IsPathRooted(rel))
                {
                    var tree = Path.GetFileName(root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                    return string.IsNullOrEmpty(tree)
                        ? rel.Replace('\\', '/')
                        : tree + "/" + rel.Replace('\\', '/');
                }
            }
            catch
            {
                /* try next */
            }
        }

        try
        {
            if (roots.Count > 0)
            {
                var kape = Path.GetDirectoryName(roots[0]);
                if (!string.IsNullOrEmpty(kape))
                    return Path.GetRelativePath(kape, absolute).Replace('\\', '/');
            }
        }
        catch
        {
            /* fall through */
        }

        return Path.GetFileName(absolute);
    }

    private static string RelHint(string root, string absolute)
    {
        try
        {
            return Path.GetRelativePath(root, absolute).Replace('\\', '/');
        }
        catch
        {
            return absolute;
        }
    }

    private static bool PathsEqual(string a, string b)
        => string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);

    private static bool FilesContentEqual(string a, string b)
    {
        var fa = new FileInfo(a);
        var fb = new FileInfo(b);
        if (fa.Length != fb.Length) return false;
        if (fa.Length == 0) return true;
        using var sa = File.OpenRead(a);
        using var sb = File.OpenRead(b);
        var bufA = new byte[64 * 1024];
        var bufB = new byte[64 * 1024];
        while (true)
        {
            var na = sa.Read(bufA, 0, bufA.Length);
            var nb = sb.Read(bufB, 0, bufB.Length);
            if (na != nb) return false;
            if (na == 0) return true;
            if (!bufA.AsSpan(0, na).SequenceEqual(bufB.AsSpan(0, nb)))
                return false;
        }
    }
}
