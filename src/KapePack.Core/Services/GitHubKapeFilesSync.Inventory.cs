using System.Text.Json;
using KapePack.Core.Models;

namespace KapePack.Core.Services;

public static partial class GitHubKapeFilesSync
{
    public static string ComputeFileSha256(string path) => FileHash.Sha256Hex(path);

    public static string? ReadLastZipSha256(string kapeRoot)
    {
        var path = Path.Combine(kapeRoot, "PackBuilder", LastZipSha256FileName);
        if (!File.Exists(path)) return null;
        try
        {
            var text = File.ReadAllText(path).Trim();
            if (string.IsNullOrEmpty(text)) return null;
            // Allow "hash" or "hash  filename" lines
            var first = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)[0];
            return first.ToLowerInvariant();
        }
        catch
        {
            return null;
        }
    }

    public static Dictionary<string, string>? ReadLastSync(string kapeRoot)
    {
        var path = Path.Combine(kapeRoot, "PackBuilder", "last_kapefiles_sync.json");
        if (!File.Exists(path)) return null;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in doc.RootElement.EnumerateObject())
                dict[p.Name] = p.Value.ToString();
            return dict;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Paths from the last KapeFiles zip (<c>Targets/…</c>, <c>Modules/…</c>).
    /// Null if the inventory file is missing (run «Обновить с GitHub…» once).
    /// </summary>
    public static HashSet<string>? ReadUpstreamPathSet(string kapeRoot)
    {
        var path = Path.Combine(kapeRoot, "PackBuilder", LastUpstreamPathsFileName);
        if (!File.Exists(path)) return null;
        try
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var line in File.ReadLines(path))
            {
                var s = CatalogOriginLabels.NormalizeRelativePath(line.Trim());
                if (s.Length == 0 || s.StartsWith('#')) continue;
                set.Add(s);
            }
            return set.Count == 0 ? null : set;
        }
        catch
        {
            return null;
        }
    }

    public static void WriteUpstreamPaths(string kapeRoot, IEnumerable<string> relativePaths)
    {
        var metaDir = Path.Combine(kapeRoot, "PackBuilder");
        Directory.CreateDirectory(metaDir);
        var path = Path.Combine(metaDir, LastUpstreamPathsFileName);
        var lines = relativePaths
            .Select(CatalogOriginLabels.NormalizeRelativePath)
            .Where(s => s.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(s => s, StringComparer.OrdinalIgnoreCase);
        File.WriteAllLines(path, lines);
    }

    /// <summary>Build relative path set from extracted KapeFiles Targets/ and Modules/ trees.</summary>
    public static HashSet<string> CollectUpstreamRelativePaths(string srcTargets, string srcModules)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        CollectTreePaths(srcTargets, writingModules: false, set);
        CollectTreePaths(srcModules, writingModules: true, set);
        return set;
    }

    private static void CollectTreePaths(string src, bool writingModules, HashSet<string> set)
    {
        if (!Directory.Exists(src)) return;
        foreach (var path in Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories))
        {
            var name = Path.GetFileName(path);
            if (name.StartsWith('.') || name.Equals(".DS_Store", StringComparison.OrdinalIgnoreCase))
                continue;
            var rel = Path.GetRelativePath(src, path).Replace('\\', '/');
            var tree = writingModules ? "Modules" : "Targets";
            if (NameCollisionFixer.IsWrongTreeExtension(name, writingModules))
                tree = writingModules ? "Targets" : "Modules";
            set.Add(tree + "/" + rel);
        }
    }
}
