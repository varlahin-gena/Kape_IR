namespace KapePack.Core.Services;

public static partial class GitHubKapeFilesSync
{
    private static MergeStats MergeTree(
        string src,
        string dest,
        string kapeRoot,
        bool writingModules,
        IProgress<string>? progress,
        CancellationToken ct,
        bool dryRun,
        string? backupRoot,
        string samplePrefix)
    {
        var stats = new MergeStats();
        var processed = 0;
        var uniquenessRoots = new[]
        {
            Path.Combine(kapeRoot, "Targets"),
            Path.Combine(kapeRoot, "Modules")
        };
        foreach (var path in Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();
            var name = Path.GetFileName(path);
            if (name.StartsWith('.') || name.Equals(".DS_Store", StringComparison.OrdinalIgnoreCase))
                continue;
            var rel = Path.GetRelativePath(src, path);
            var target = Path.Combine(dest, rel);
            var samplePath = (samplePrefix + "/" + rel.Replace('\\', '/')).TrimStart('/');
            string? wrongTreePath = null;
            try
            {
                if (NameCollisionFixer.IsWrongTreeExtension(name, writingModules))
                {
                    var otherTree = writingModules ? "Targets" : "Modules";
                    wrongTreePath = target;
                    var redirected = Path.Combine(kapeRoot, otherTree, rel);
                    if (stats.MisplacedSamples.Count < SampleCap)
                        stats.MisplacedSamples.Add($"{samplePath} → {otherTree}/{rel.Replace('\\', '/')}");
                    stats.SkippedMisplaced++;
                    target = redirected;
                    samplePath = otherTree + "/" + rel.Replace('\\', '/');
                }

                if (!dryRun)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    if (!NameCollisionFixer.ShouldWriteUniqueBasename(uniquenessRoots, target, path, out _))
                    {
                        stats.SkippedDuplicates++;
                        continue;
                    }
                }
                else if (ShouldSkipDuplicateDryRun(uniquenessRoots, target, path))
                {
                    stats.SkippedDuplicates++;
                    continue;
                }

                if (File.Exists(target) && FilesContentEqual(path, target))
                {
                    stats.Unchanged++;
                    // Still remove a leftover copy in the wrong tree from an earlier bad sync.
                    if (!dryRun && wrongTreePath is not null && File.Exists(wrongTreePath) &&
                        !PathsEqual(wrongTreePath, target))
                    {
                        try { File.Delete(wrongTreePath); } catch { /* ignore */ }
                    }
                }
                else
                {
                    var isNew = !File.Exists(target);
                    if (!dryRun)
                    {
                        if (!isNew && backupRoot is not null)
                        {
                            var backupTree = samplePath.StartsWith("Modules/", StringComparison.OrdinalIgnoreCase)
                                ? "Modules"
                                : "Targets";
                            var backupPath = Path.Combine(
                                Path.GetDirectoryName(backupRoot) ?? backupRoot,
                                backupTree,
                                rel);
                            Directory.CreateDirectory(Path.GetDirectoryName(backupPath)!);
                            File.Copy(target, backupPath, overwrite: true);
                        }
                        File.Copy(path, target, overwrite: true);
                        if (wrongTreePath is not null && File.Exists(wrongTreePath) &&
                            !PathsEqual(wrongTreePath, target))
                        {
                            try { File.Delete(wrongTreePath); } catch { /* ignore */ }
                        }
                    }

                    if (isNew)
                    {
                        if (wrongTreePath is not null) stats.RedirectedAdded++;
                        else stats.Added++;
                        if (stats.AddedSamples.Count < SampleCap)
                            stats.AddedSamples.Add(samplePath);
                    }
                    else
                    {
                        if (wrongTreePath is not null) stats.RedirectedUpdated++;
                        else stats.Updated++;
                        if (stats.UpdatedSamples.Count < SampleCap)
                            stats.UpdatedSamples.Add(samplePath);
                    }
                }

                processed++;
                if (processed % 50 == 0)
                    progress?.Report(
                        $"Сверка {Path.GetFileName(dest)}… +{stats.Added} / ~{stats.Updated} / ={stats.Unchanged}");
            }
            catch (Exception ex)
            {
                stats.Errors.Add($"{rel.Replace('\\', '/')}: {ex.Message}");
            }
        }
        return stats;
    }

    /// <summary>Read-only duplicate-basename check for dry-run (never quarantines/moves).</summary>
    private static bool ShouldSkipDuplicateDryRun(
        IReadOnlyList<string> searchRoots,
        string destPath,
        string srcPath)
    {
        var name = Path.GetFileName(destPath);
        if (string.IsNullOrEmpty(name)) return false;

        string? existingOther = null;
        foreach (var destRoot in searchRoots)
        {
            if (!Directory.Exists(destRoot)) continue;
            foreach (var hit in Directory.EnumerateFiles(destRoot, name, SearchOption.AllDirectories))
            {
                if (NameCollisionFixer.IsUnderDisabledFolder(hit)) continue;
                if (!PathsEqual(hit, destPath))
                {
                    existingOther = hit;
                    break;
                }
            }
            if (existingOther is not null) break;
        }

        if (existingOther is null) return false;

        var newScore = NameCollisionFixer.PathPreferScore(RelHintAny(searchRoots, destPath));
        var oldScore = NameCollisionFixer.PathPreferScore(RelHintAny(searchRoots, existingOther));

        if (File.Exists(existingOther) && FilesContentEqual(srcPath, existingOther))
            return newScore <= oldScore;

        // Prefer incoming → would write (do not skip). Else skip as occupied.
        return newScore <= oldScore;
    }

    private static string RelHintAny(IReadOnlyList<string> roots, string path)
    {
        foreach (var root in roots)
        {
            try
            {
                var rel = Path.GetRelativePath(root, path);
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
                /* next */
            }
        }
        return RelHint(roots.Count > 0 ? roots[0] : Path.GetDirectoryName(path) ?? ".", path);
    }

    private static bool PathsEqual(string a, string b)
        => string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);

    private static string RelHint(string root, string path)
    {
        try { return Path.GetRelativePath(root, path).Replace('\\', '/'); }
        catch { return Path.GetFileName(path); }
    }

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

    private sealed class MergeStats
    {
        public int Added { get; set; }
        public int Updated { get; set; }
        public int Unchanged { get; set; }
        public int SkippedDuplicates { get; set; }
        public int SkippedMisplaced { get; set; }
        public int RedirectedAdded { get; set; }
        public int RedirectedUpdated { get; set; }
        public List<string> Errors { get; } = new();
        public List<string> AddedSamples { get; } = new();
        public List<string> UpdatedSamples { get; } = new();
        public List<string> MisplacedSamples { get; } = new();
    }
}
