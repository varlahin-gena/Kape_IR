using System.IO.Compression;
using System.Text.Json;
using KapePackBuilder.Models;

namespace KapePackBuilder.Services;

public sealed class PackageExporter
{
    private readonly KapeCatalog _catalog;

    public PackageExporter(KapeCatalog catalog) => _catalog = catalog;

    public ExportResult Export(
        PackageDefinition pkg,
        string outputDir,
        bool installIntoKape = true,
        bool makeZip = true,
        bool copyDependencies = true)
    {
        var warnings = new List<string>();
        pkg.PackageId = KapeFileIo.EnsureGuid(pkg.PackageId);

        var packageDir = Path.Combine(outputDir, PackageDefinition.SafeDir(pkg.Name));
        if (Directory.Exists(packageDir))
            Directory.Delete(packageDir, true);
        Directory.CreateDirectory(packageDir);

        var targetsOut = Path.Combine(packageDir, "Targets", "Compound");
        Directory.CreateDirectory(targetsOut);

        var targetName = pkg.TargetCompoundName + ".tkape";
        var targetFile = Path.Combine(targetsOut, targetName);
        File.WriteAllText(targetFile, KapeFileIo.RenderCompoundTarget(pkg));

        string? moduleFile = null;
        if (pkg.Modules.Count > 0)
        {
            var modulesOut = Path.Combine(packageDir, "Modules", "Compound");
            Directory.CreateDirectory(modulesOut);
            var moduleName = pkg.ModuleCompoundName + ".mkape";
            moduleFile = Path.Combine(modulesOut, moduleName!);
            File.WriteAllText(moduleFile, KapeFileIo.RenderCompoundModule(pkg));
        }

        if (copyDependencies)
        {
            warnings.AddRange(CopyTargetDeps(pkg, packageDir));
            if (pkg.Modules.Count > 0)
                warnings.AddRange(CopyModuleDeps(pkg, packageDir));
        }

        var batFile = Path.Combine(packageDir, "run_collection.bat");
        var ps1File = Path.Combine(packageDir, "run_collection.ps1");
        File.WriteAllText(batFile, KapeFileIo.RenderRunBat(pkg));
        File.WriteAllText(ps1File, KapeFileIo.RenderRunPs1(pkg));

        var manifest = BuildManifest(pkg, warnings);
        var manifestFile = Path.Combine(packageDir, "package.json");
        File.WriteAllText(manifestFile, JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));

        File.WriteAllText(Path.Combine(packageDir, "README.txt"), ReadmeText(pkg, installIntoKape));

        string? installedTarget = null;
        string? installedModule = null;
        if (installIntoKape)
            (installedTarget, installedModule) = InstallIntoKape(pkg, targetFile, moduleFile);

        string? zipFile = null;
        if (makeZip)
        {
            zipFile = Path.Combine(outputDir, PackageDefinition.SafeDir(pkg.Name) + ".zip");
            if (File.Exists(zipFile)) File.Delete(zipFile);
            ZipFile.CreateFromDirectory(packageDir, zipFile, CompressionLevel.Optimal, false);
        }

        return new ExportResult
        {
            PackageDir = packageDir,
            TargetFile = targetFile,
            ModuleFile = moduleFile,
            BatFile = batFile,
            Ps1File = ps1File,
            ManifestFile = manifestFile,
            ZipFile = zipFile,
            InstalledTarget = installedTarget,
            InstalledModule = installedModule,
            Warnings = warnings
        };
    }

    public static PackageDefinition LoadPackageJson(string path)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var root = doc.RootElement;
        var pkg = new PackageDefinition
        {
            Name = root.TryGetProperty("name", out var n) ? n.GetString() ?? "Package" : "Package",
            Description = GetStr(root, "description"),
            Author = GetStr(root, "author"),
            Version = NullIfEmpty(GetStr(root, "version")) ?? "1.0",
            PackageId = GetStr(root, "package_id"),
            RecreateDirectories = !root.TryGetProperty("recreate_directories", out var rd) || rd.GetBoolean(),
            Tsource = NullIfEmpty(GetStr(root, "tsource")) ?? "C:",
            ZipOutput = !root.TryGetProperty("zip_output", out var zo) || zo.GetBoolean(),
            Flush = root.TryGetProperty("flush", out var fl) && fl.GetBoolean(),
            Vss = root.TryGetProperty("vss", out var vs) && vs.GetBoolean(),
            Notes = GetStr(root, "notes")
        };
        pkg.Targets = ReadEntries(root, "targets");
        pkg.Modules = ReadEntries(root, "modules");
        return pkg;
    }

    private (string, string?) InstallIntoKape(PackageDefinition pkg, string targetFile, string? moduleFile)
    {
        var destT = Path.Combine(_catalog.KapeRoot, "Targets", "Compound", Path.GetFileName(targetFile));
        Directory.CreateDirectory(Path.GetDirectoryName(destT)!);
        File.Copy(targetFile, destT, true);

        string? destM = null;
        if (moduleFile is not null)
        {
            destM = Path.Combine(_catalog.KapeRoot, "Modules", "Compound", Path.GetFileName(moduleFile));
            Directory.CreateDirectory(Path.GetDirectoryName(destM)!);
            File.Copy(moduleFile, destM, true);
        }

        var safe = PackageDefinition.SafeDir(pkg.Name);
        File.WriteAllText(Path.Combine(_catalog.KapeRoot, $"run_{safe}.bat"), KapeFileIo.RenderRunBat(pkg));
        File.WriteAllText(Path.Combine(_catalog.KapeRoot, $"run_{safe}.ps1"), KapeFileIo.RenderRunPs1(pkg));
        return (destT, destM);
    }

    private List<string> CopyTargetDeps(PackageDefinition pkg, string packageDir)
    {
        var warnings = new List<string>();
        var refs = pkg.Targets.Select(t => t.Path).ToList();
        var items = _catalog.ResolveClosure(refs, ItemKind.Target);
        var known = items.Select(i => Path.GetFileName(i.AbsolutePath).ToLowerInvariant()).ToHashSet();
        foreach (var r in refs)
        {
            if (!known.Contains(Path.GetFileName(r).ToLowerInvariant()) &&
                items.All(i => !i.Name.Equals(r, StringComparison.OrdinalIgnoreCase)))
                warnings.Add($"Target not found in catalog: {r}");
        }

        var generated = (pkg.TargetCompoundName + ".tkape").ToLowerInvariant();
        foreach (var item in items)
        {
            var dest = ResolveDest(packageDir, item.AbsolutePath, "Targets");
            if (File.Exists(dest) && Path.GetFileName(dest).Equals(generated, StringComparison.OrdinalIgnoreCase))
                continue;
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(item.AbsolutePath, dest, true);
        }
        return warnings;
    }

    private List<string> CopyModuleDeps(PackageDefinition pkg, string packageDir)
    {
        var warnings = new List<string>();
        var refs = pkg.Modules.Select(m => m.Path).ToList();
        var items = _catalog.ResolveClosure(refs, ItemKind.Module);
        var known = items.Select(i => Path.GetFileName(i.AbsolutePath).ToLowerInvariant()).ToHashSet();
        foreach (var r in refs)
        {
            if (!known.Contains(Path.GetFileName(r).ToLowerInvariant()) &&
                items.All(i => !i.Name.Equals(r, StringComparison.OrdinalIgnoreCase)))
                warnings.Add($"Module not found in catalog: {r}");
        }

        var generated = (pkg.ModuleCompoundName + ".mkape")?.ToLowerInvariant();
        foreach (var item in items)
        {
            var dest = ResolveDest(packageDir, item.AbsolutePath, "Modules");
            if (generated is not null &&
                File.Exists(dest) &&
                Path.GetFileName(dest).Equals(generated, StringComparison.OrdinalIgnoreCase))
                continue;
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(item.AbsolutePath, dest, true);
        }

        if (Directory.Exists(Path.Combine(_catalog.KapeRoot, "Modules", "bin")))
        {
            warnings.Add(
                "Modules\\bin exists in KAPE root but was not copied (can be large). " +
                "Keep package next to full KAPE or copy bin manually if needed.");
        }
        return warnings;
    }

    private string ResolveDest(string packageDir, string absolutePath, string fallbackRoot)
    {
        try
        {
            var rel = Path.GetRelativePath(_catalog.KapeRoot, absolutePath);
            if (!rel.StartsWith(".."))
                return Path.Combine(packageDir, rel);
        }
        catch { /* ignore */ }
        return Path.Combine(packageDir, fallbackRoot, Path.GetFileName(absolutePath));
    }

    private static object BuildManifest(PackageDefinition pkg, List<string> warnings) => new
    {
        name = pkg.Name,
        description = pkg.Description,
        author = pkg.Author,
        version = pkg.Version,
        package_id = pkg.PackageId,
        recreate_directories = pkg.RecreateDirectories,
        targets = pkg.Targets.Select(t => new { name = t.Name, category = t.Category, path = t.Path, comments = t.Comments }),
        modules = pkg.Modules.Select(m => new { name = m.Name, category = m.Category, path = m.Path, comments = m.Comments }),
        tsource = pkg.Tsource,
        zip_output = pkg.ZipOutput,
        flush = pkg.Flush,
        vss = pkg.Vss,
        notes = pkg.Notes,
        target_compound = pkg.TargetCompoundName,
        module_compound = pkg.ModuleCompoundName,
        warnings
    };

    private static string ReadmeText(PackageDefinition pkg, bool installed)
    {
        var lines = new List<string>
        {
            $"KAPE Pack: {pkg.Name}",
            $"Description: {pkg.Description}",
            $"Author: {pkg.Author}",
            $"Version: {pkg.Version}",
            "",
            "Contents:",
            $"  - Compound target: {pkg.TargetCompoundName}.tkape"
        };
        if (pkg.ModuleCompoundName is not null)
            lines.Add($"  - Compound module: {pkg.ModuleCompoundName}.mkape");
        lines.AddRange(new[]
        {
            "  - run_collection.bat / run_collection.ps1",
            "  - package.json manifest",
            "  - Copied dependent Targets/Modules (when exported with dependencies)",
            "",
            "Usage:",
            "  1. Prefer running from a full KAPE directory that contains kape.exe."
        });
        lines.Add(installed
            ? $"  2. Installed into local KAPE. Use target {pkg.TargetCompoundName}."
            : "  2. Copy Targets\\ and Modules\\ from this pack into your KAPE root, then run the scripts.");
        lines.AddRange(new[]
        {
            "  3. Run PowerShell/CMD as Administrator.",
            "",
            "Generated by KAPE Pack Builder",
            ""
        });
        return string.Join('\n', lines);
    }

    private static List<SelectionEntry> ReadEntries(JsonElement root, string prop)
    {
        var list = new List<SelectionEntry>();
        if (!root.TryGetProperty(prop, out var arr) || arr.ValueKind != JsonValueKind.Array)
            return list;
        foreach (var el in arr.EnumerateArray())
        {
            list.Add(new SelectionEntry
            {
                Name = GetStr(el, "name"),
                Category = NullIfEmpty(GetStr(el, "category")) ?? "General",
                Path = GetStr(el, "path"),
                Comments = GetStr(el, "comments")
            });
        }
        return list;
    }

    private static string GetStr(JsonElement el, string name)
        => el.TryGetProperty(name, out var p) ? p.GetString() ?? "" : "";

    private static string? NullIfEmpty(string s) => string.IsNullOrWhiteSpace(s) ? null : s;
}
