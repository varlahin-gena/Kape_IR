using System.Text.Json;
using KapeIR.Core.Models;

namespace KapeIR.Core.Services;

/// <summary>
/// Persist ready assemblies as compound .tkape/.mkape under Targets|Modules\Compound
/// plus PackBuilder/assemblies/*.json sidecars for IR/build fields YAML cannot hold.
/// </summary>
public static class PackageAssemblyStore
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public static string AssembliesDir(string kapeRoot)
        => KapeRootPaths.AssembliesDir(kapeRoot);

    public static string LegacySessionsDir(string kapeRoot)
        => Path.Combine(KapeRootPaths.PackBuilderDir(kapeRoot), "sessions");

    public static string SidecarPath(string kapeRoot, string name)
        => Path.Combine(AssembliesDir(kapeRoot), SafeAssemblyFileName(name) + ".json");

    public static string TargetCompoundPath(string kapeRoot, string compoundName)
        => Path.Combine(KapeRootPaths.Normalize(kapeRoot), "Targets", "Compound", compoundName + ".tkape");

    public static string ModulesCompoundPath(string kapeRoot, string compoundName)
        => Path.Combine(KapeRootPaths.Normalize(kapeRoot), "Modules", "Compound", compoundName + "_Modules.mkape");

    public static string TargetRelativePath(string compoundName)
        => CatalogOriginLabels.NormalizeRelativePath($"Targets/Compound/{compoundName}.tkape");

    public static string ModulesRelativePath(string compoundName)
        => CatalogOriginLabels.NormalizeRelativePath($"Modules/Compound/{compoundName}_Modules.mkape");

    public static string SafeAssemblyFileName(string name)
    {
        var cleaned = PackageDefinition.SafeDir(name).Replace("!", "", StringComparison.Ordinal);
        return string.IsNullOrEmpty(cleaned) ? "assembly" : cleaned;
    }

    /// <summary>
    /// One-shot: copy PackBuilder/sessions/*.json → assemblies/ when the target is missing.
    /// </summary>
    public static int MigrateSessionsIfNeeded(string kapeRoot)
    {
        var sessions = LegacySessionsDir(kapeRoot);
        if (!Directory.Exists(sessions)) return 0;

        var destDir = AssembliesDir(kapeRoot);
        Directory.CreateDirectory(destDir);
        var copied = 0;
        foreach (var src in Directory.EnumerateFiles(sessions, "*.json"))
        {
            var dest = Path.Combine(destDir, Path.GetFileName(src));
            if (File.Exists(dest)) continue;
            File.Copy(src, dest);
            copied++;
        }
        return copied;
    }

    public static bool IsUpstreamRelativePath(
        string kapeRoot,
        string relativePath,
        IReadOnlySet<string>? upstreamPaths = null)
    {
        upstreamPaths ??= GitHubKapeFilesSync.ReadUpstreamPathSet(kapeRoot);
        if (upstreamPaths is null) return false;
        return upstreamPaths.Contains(CatalogOriginLabels.NormalizeRelativePath(relativePath));
    }

    public static bool WouldOverwriteUpstream(
        string kapeRoot,
        PackageDefinition pkg,
        IReadOnlySet<string>? upstreamPaths = null)
    {
        var compound = pkg.TargetCompoundName;
        return IsUpstreamRelativePath(kapeRoot, TargetRelativePath(compound), upstreamPaths);
    }

    /// <summary>
    /// Write local compounds + sidecar. Never overwrites a GitHub upstream path:
    /// renames to {Name}_Local / _Local2 / … and returns <see cref="SaveAssemblyResult.CreatedLocalCopy"/>.
    /// </summary>
    public static SaveAssemblyResult SaveLocal(
        string kapeRoot,
        PackageDefinition pkg,
        IReadOnlySet<string>? upstreamPaths = null,
        bool forceLocalCopy = false)
    {
        ArgumentNullException.ThrowIfNull(pkg);
        kapeRoot = KapeRootPaths.Normalize(kapeRoot);
        upstreamPaths ??= GitHubKapeFilesSync.ReadUpstreamPathSet(kapeRoot);

        pkg = pkg.Clone();
        EnsurePackBuilderAuthor(pkg);

        var previousName = pkg.Name;
        var createdCopy = false;
        if (forceLocalCopy || WouldOverwriteUpstream(kapeRoot, pkg, upstreamPaths))
        {
            pkg.Name = AllocateLocalCopyName(kapeRoot, pkg.Name, upstreamPaths);
            createdCopy = true;
        }

        var compound = pkg.TargetCompoundName;
        var targetsDir = Path.Combine(kapeRoot, "Targets", "Compound");
        var modulesDir = Path.Combine(kapeRoot, "Modules", "Compound");
        Directory.CreateDirectory(targetsDir);
        Directory.CreateDirectory(modulesDir);

        var targetFile = Path.Combine(targetsDir, compound + ".tkape");
        File.WriteAllText(targetFile, KapeCompoundIo.RenderCompoundTarget(pkg));

        string? moduleFile = null;
        var moduleEntries = ResolveModuleEntriesForCompanion(pkg);
        var modulesPath = Path.Combine(modulesDir, compound + "_Modules.mkape");
        if (moduleEntries.Count > 0)
        {
            File.WriteAllText(modulesPath, KapeCompoundIo.RenderCompoundModule(pkg, moduleEntries));
            moduleFile = modulesPath;
        }
        else if (File.Exists(modulesPath) &&
                 !IsUpstreamRelativePath(kapeRoot, ModulesRelativePath(compound), upstreamPaths))
        {
            File.Delete(modulesPath);
        }

        var sidecar = WriteSidecar(kapeRoot, pkg);

        return new SaveAssemblyResult
        {
            Package = pkg,
            CreatedLocalCopy = createdCopy,
            PreviousName = createdCopy ? previousName : null,
            TargetFile = targetFile,
            ModuleFile = moduleFile,
            SidecarFile = sidecar
        };
    }

    public static string WriteSidecar(string kapeRoot, PackageDefinition pkg)
    {
        var dir = AssembliesDir(kapeRoot);
        Directory.CreateDirectory(dir);
        var path = SidecarPath(kapeRoot, pkg.Name);
        var payload = BuildSidecarPayload(pkg);
        File.WriteAllText(path, JsonSerializer.Serialize(payload, JsonOpts));
        return path;
    }

    public static bool TryLoadSidecar(string kapeRoot, string name, out PackageDefinition? sidecar)
    {
        sidecar = null;
        var path = SidecarPath(kapeRoot, name);
        if (!File.Exists(path))
        {
            var alt = SidecarPath(kapeRoot, PackageDefinition.SafeDir(name));
            if (!File.Exists(alt)) return false;
            path = alt;
        }

        sidecar = PackageExporter.LoadPackageJson(path);
        return true;
    }

    /// <summary>
    /// Overlay IR/build fields from sidecar onto a package loaded from compound YAML.
    /// Composition (targets/modules) stays from the compound.
    /// </summary>
    public static void MergeSidecarIntoPackage(PackageDefinition pkg, string kapeRoot)
    {
        ArgumentNullException.ThrowIfNull(pkg);
        if (!TryLoadSidecar(kapeRoot, pkg.Name, out var side) || side is null)
        {
            if (!TryLoadSidecar(kapeRoot, pkg.TargetCompoundName, out side) || side is null)
                return;
        }

        pkg.Tsource = side.Tsource;
        pkg.ZipOutput = side.ZipOutput;
        pkg.Flush = side.Flush;
        pkg.Vss = side.Vss;
        pkg.Notes = side.Notes;
        pkg.CollectionMode = side.CollectionMode;
        pkg.CaseId = side.CaseId;
        pkg.Phase1ModuleName = side.Phase1ModuleName;
        pkg.Phase2ModuleName = side.Phase2ModuleName;
        if (!string.IsNullOrWhiteSpace(side.Version))
            pkg.Version = side.Version;
        if (!string.IsNullOrWhiteSpace(side.Description) && string.IsNullOrWhiteSpace(pkg.Description))
            pkg.Description = side.Description;
        if (!string.IsNullOrWhiteSpace(side.Author))
            pkg.Author = side.Author;
        if (!string.IsNullOrWhiteSpace(side.PackageId))
            pkg.PackageId = side.PackageId;
    }

    /// <summary>
    /// Delete a local assembly. Refuses GitHub upstream paths.
    /// </summary>
    public static bool DeleteLocal(
        string kapeRoot,
        string name,
        IReadOnlySet<string>? upstreamPaths = null)
    {
        kapeRoot = KapeRootPaths.Normalize(kapeRoot);
        upstreamPaths ??= GitHubKapeFilesSync.ReadUpstreamPathSet(kapeRoot);

        var compound = PackageDefinition.SafeName(name).TrimStart('!');
        if (string.IsNullOrEmpty(compound))
            compound = "WindowsTriage";

        if (IsUpstreamRelativePath(kapeRoot, TargetRelativePath(compound), upstreamPaths))
            throw new InvalidOperationException(
                $"Нельзя удалить сборку с GitHub: {compound}. Создайте локальную копию и удалите её.");

        var deleted = false;
        var target = TargetCompoundPath(kapeRoot, compound);
        if (File.Exists(target))
        {
            File.Delete(target);
            deleted = true;
        }

        var modules = ModulesCompoundPath(kapeRoot, compound);
        if (File.Exists(modules) &&
            !IsUpstreamRelativePath(kapeRoot, ModulesRelativePath(compound), upstreamPaths))
        {
            File.Delete(modules);
            deleted = true;
        }

        var sidecar = SidecarPath(kapeRoot, name);
        if (File.Exists(sidecar))
        {
            File.Delete(sidecar);
            deleted = true;
        }
        else
        {
            var alt = SidecarPath(kapeRoot, compound);
            if (File.Exists(alt))
            {
                File.Delete(alt);
                deleted = true;
            }
        }

        return deleted;
    }

    public static string AllocateLocalCopyName(
        string kapeRoot,
        string baseName,
        IReadOnlySet<string>? upstreamPaths = null)
    {
        upstreamPaths ??= GitHubKapeFilesSync.ReadUpstreamPathSet(kapeRoot);
        var root = string.IsNullOrWhiteSpace(baseName) ? "CustomPackage" : baseName.Trim();
        // Strip trailing _Local / _LocalN so we do not produce Name_Local_Local.
        root = System.Text.RegularExpressions.Regex.Replace(
            root, @"_Local\d*$", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (string.IsNullOrWhiteSpace(root))
            root = "CustomPackage";

        for (var i = 0; i < 1000; i++)
        {
            var candidate = i == 0 ? root + "_Local" : root + "_Local" + (i + 1);
            var compound = PackageDefinition.SafeName(candidate).TrimStart('!');
            var targetPath = TargetCompoundPath(kapeRoot, compound);
            if (File.Exists(targetPath)) continue;
            if (IsUpstreamRelativePath(kapeRoot, TargetRelativePath(compound), upstreamPaths))
                continue;
            return candidate;
        }

        return root + "_Local" + Guid.NewGuid().ToString("N")[..6];
    }

    public static void EnsurePackBuilderAuthor(PackageDefinition pkg)
    {
        if (CatalogOriginLabels.LooksLikePackBuilderAuthor(pkg.Author))
            return;
        pkg.Author = string.IsNullOrWhiteSpace(pkg.Author)
            ? CatalogOriginLabels.PackBuilderAuthorPrefix
            : $"{CatalogOriginLabels.PackBuilderAuthorPrefix} / {pkg.Author.Trim()}";
    }

    private static List<SelectionEntry> ResolveModuleEntriesForCompanion(PackageDefinition pkg)
    {
        if (pkg.IsTwoPhase)
            return pkg.GetPhase2ModuleEntries();
        return pkg.Modules.ToList();
    }

    private static object BuildSidecarPayload(PackageDefinition pkg) => new
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
        module_compound = pkg.ModuleCompoundName,
        collection_mode = pkg.IsTwoPhase ? "two_phase" : "single",
        case_id = pkg.CaseId ?? "",
        phase1_module = pkg.Phase1ModuleName,
        phase2_module = pkg.ResolvePhase2ModuleName()
    };
}

public sealed class SaveAssemblyResult
{
    public required PackageDefinition Package { get; init; }
    public bool CreatedLocalCopy { get; init; }
    public string? PreviousName { get; init; }
    public required string TargetFile { get; init; }
    public string? ModuleFile { get; init; }
    public required string SidecarFile { get; init; }
}
