using System.Text.Json;
using KapePackBuilder.Models;

namespace KapePackBuilder.Services;

/// <summary>
/// Persist builder sessions as package.json-shaped JSON under PackBuilder/sessions/.
/// </summary>
public static class PackageSessionStore
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public static string SessionsDir(string kapeRoot)
        => Path.Combine(kapeRoot, "PackBuilder", "sessions");

    public static string SessionPath(string kapeRoot, string name)
        => Path.Combine(SessionsDir(kapeRoot), SafeSessionFileName(name) + ".json");

    public static void Save(string kapeRoot, string name, PackageDefinition pkg)
    {
        var dir = SessionsDir(kapeRoot);
        Directory.CreateDirectory(dir);
        var path = SessionPath(kapeRoot, name);
        var payload = new
        {
            name = pkg.Name,
            description = pkg.Description,
            author = pkg.Author,
            version = pkg.Version,
            package_id = pkg.PackageId,
            recreate_directories = pkg.RecreateDirectories,
            targets = pkg.Targets.Select(t => new
            {
                name = t.Name,
                category = t.Category,
                path = t.Path,
                comments = t.Comments
            }),
            modules = pkg.Modules.Select(m => new
            {
                name = m.Name,
                category = m.Category,
                path = m.Path,
                comments = m.Comments
            }),
            tsource = pkg.Tsource,
            zip_output = pkg.ZipOutput,
            flush = pkg.Flush,
            vss = pkg.Vss,
            notes = pkg.Notes,
            target_compound = pkg.TargetCompoundName,
            module_compound = pkg.ModuleCompoundName
        };
        File.WriteAllText(path, JsonSerializer.Serialize(payload, JsonOpts));
    }

    public static PackageDefinition Load(string kapeRoot, string name)
    {
        var path = SessionPath(kapeRoot, name);
        if (!File.Exists(path))
            throw new FileNotFoundException($"Сессия не найдена: {name}", path);
        return PackageExporter.LoadPackageJson(path);
    }

    public static IReadOnlyList<string> ListSessionNames(string kapeRoot)
    {
        var dir = SessionsDir(kapeRoot);
        if (!Directory.Exists(dir))
            return Array.Empty<string>();
        return Directory.EnumerateFiles(dir, "*.json")
            .Select(f => Path.GetFileNameWithoutExtension(f)!)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static bool Delete(string kapeRoot, string name)
    {
        var path = SessionPath(kapeRoot, name);
        if (!File.Exists(path)) return false;
        File.Delete(path);
        return true;
    }

    public static string SafeSessionFileName(string name)
    {
        // SafeDir keeps '!' for compound names; strip for session filenames.
        var cleaned = PackageDefinition.SafeDir(name).Replace("!", "", StringComparison.Ordinal);
        return string.IsNullOrEmpty(cleaned) ? "session" : cleaned;
    }
}
