using KapeIR.Core.Models;

namespace KapeIR.Core.Services;

/// <summary>
/// Obsolete session API — use <see cref="PackageAssemblyStore"/> (compounds + assemblies sidecar).
/// Kept as thin wrappers so older call sites compile until removed.
/// </summary>
[Obsolete("Use PackageAssemblyStore (ready assemblies under Targets/Compound + PackBuilder/assemblies).")]
public static class PackageSessionStore
{
    public static string SessionsDir(string kapeRoot)
        => PackageAssemblyStore.AssembliesDir(kapeRoot);

    public static string SessionPath(string kapeRoot, string name)
        => PackageAssemblyStore.SidecarPath(kapeRoot, name);

    public static void Save(string kapeRoot, string name, PackageDefinition pkg)
    {
        ArgumentNullException.ThrowIfNull(pkg);
        var clone = pkg.Clone();
        if (!string.IsNullOrWhiteSpace(name))
            clone.Name = name;
        PackageAssemblyStore.WriteSidecar(kapeRoot, clone);
    }

    public static PackageDefinition Load(string kapeRoot, string name)
    {
        if (!PackageAssemblyStore.TryLoadSidecar(kapeRoot, name, out var pkg) || pkg is null)
            throw new FileNotFoundException($"Сборка (sidecar) не найдена: {name}",
                PackageAssemblyStore.SidecarPath(kapeRoot, name));
        return pkg;
    }

    public static IReadOnlyList<string> ListSessionNames(string kapeRoot)
    {
        var dir = PackageAssemblyStore.AssembliesDir(kapeRoot);
        if (!Directory.Exists(dir))
            return Array.Empty<string>();
        return Directory.EnumerateFiles(dir, "*.json")
            .Select(f => Path.GetFileNameWithoutExtension(f)!)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static bool Delete(string kapeRoot, string name)
    {
        var path = PackageAssemblyStore.SidecarPath(kapeRoot, name);
        if (!File.Exists(path)) return false;
        File.Delete(path);
        return true;
    }

    public static string SafeSessionFileName(string name)
        => PackageAssemblyStore.SafeAssemblyFileName(name);
}
