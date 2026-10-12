using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace KapeIR.Core.Services;

/// <summary>
/// Seeds first-party KapeIR catalog definitions (Phase1 + Hayabusa v41 mkape + helper scripts)
/// from embedded resources into any KAPE root so Builder always has a complete KapeIR set
/// without relying on a hand-maintained "Kape clean" tree.
/// </summary>
public static class Phase1CatalogSeeder
{
    public const string ResourcePrefix = "Phase1/";
    public const string VersionResourceName = "Phase1/VERSION";
    public const string VersionMarkerRelativePath = @"Modules\Apps\Local\Phase1\.kapeir_phase1_version";

    /// <summary>Fallback if VERSION resource is missing.</summary>
    public const string FallbackVersion = "1.0.0";

    public sealed class Result
    {
        public bool Ok { get; init; }
        public string EmbeddedVersion { get; init; } = "";
        public string? PreviousVersion { get; init; }
        public bool SkippedUpToDate { get; init; }
        public int FilesWritten { get; init; }
        public int FilesUnchanged { get; init; }
        public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
        public string Message { get; init; } = "";
    }

    public static string ReadEmbeddedVersion(Assembly? assembly = null)
    {
        assembly ??= typeof(Phase1CatalogSeeder).Assembly;
        using var stream = OpenEmbeddedResource(assembly, VersionResourceName);
        if (stream is null)
            return FallbackVersion;
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var v = reader.ReadToEnd().Trim();
        return string.IsNullOrEmpty(v) ? FallbackVersion : v;
    }

    /// <summary>
    /// Manifest LogicalName uses '\'; lookups may use '/'. Match either.
    /// </summary>
    internal static Stream? OpenEmbeddedResource(Assembly assembly, string logicalName)
    {
        var want = NormalizeResourceName(logicalName);
        foreach (var name in assembly.GetManifestResourceNames())
        {
            if (!string.Equals(NormalizeResourceName(name), want, StringComparison.OrdinalIgnoreCase))
                continue;
            return assembly.GetManifestResourceStream(name);
        }

        return null;
    }

    public static string? ReadInstalledVersion(string kapeRoot)
    {
        var path = Path.Combine(kapeRoot, VersionMarkerRelativePath);
        if (!File.Exists(path))
            return null;
        try
        {
            return File.ReadAllText(path).Trim();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Read-only status (no writes) for toolkit check UI.</summary>
    public static Result Peek(string kapeRoot)
    {
        if (string.IsNullOrWhiteSpace(kapeRoot) || !Directory.Exists(kapeRoot))
        {
            return new Result
            {
                Ok = false,
                Message = "Корень KAPE не найден — каталог KapeIR не засеян."
            };
        }

        var asm = typeof(Phase1CatalogSeeder).Assembly;
        var embeddedVersion = ReadEmbeddedVersion(asm);
        var previous = ReadInstalledVersion(kapeRoot);
        var present = AllManagedFilesPresent(kapeRoot, asm);
        var drifted = present && AnyManagedFileDrift(kapeRoot, asm);
        var upToDate = !string.IsNullOrEmpty(previous) &&
                       string.Equals(previous, embeddedVersion, StringComparison.OrdinalIgnoreCase) &&
                       present &&
                       !drifted;
        return new Result
        {
            Ok = true,
            EmbeddedVersion = embeddedVersion,
            PreviousVersion = previous,
            SkippedUpToDate = upToDate,
            Message = upToDate
                ? $"KapeIR каталог актуален (v{embeddedVersion})."
                : drifted
                    ? $"KapeIR каталог: файлы отличаются от встроенных v{embeddedVersion} — нужен засев."
                    : string.IsNullOrEmpty(previous)
                        ? $"KapeIR каталог не засеян (встроено v{embeddedVersion})."
                        : $"KapeIR каталог: локально v{previous}, встроено v{embeddedVersion} — нужен засев."
        };
    }

    /// <summary>
    /// Ensure KapeIR (Phase1 + Hayabusa v41) definitions exist under <paramref name="kapeRoot"/>.
    /// Overwrites managed files when embedded version differs or content hash differs.
    /// </summary>
    public static Result Ensure(string kapeRoot, bool force = false)
    {
        if (string.IsNullOrWhiteSpace(kapeRoot) || !Directory.Exists(kapeRoot))
        {
            return new Result
            {
                Ok = false,
                Message = "Корень KAPE не найден — каталог KapeIR не засеян."
            };
        }

        KapeRootPaths.EnsureLayout(kapeRoot);

        var asm = typeof(Phase1CatalogSeeder).Assembly;
        var embeddedVersion = ReadEmbeddedVersion(asm);
        var previous = ReadInstalledVersion(kapeRoot);
        if (!force)
        {
            var peek = Peek(kapeRoot);
            if (peek.SkippedUpToDate)
                return peek;
        }

        var written = 0;
        var unchanged = 0;
        var warnings = new List<string>();

        foreach (var resourceName in asm.GetManifestResourceNames())
        {
            var logical = NormalizeResourceName(resourceName);
            if (!logical.StartsWith(ResourcePrefix, StringComparison.OrdinalIgnoreCase))
                continue;
            if (logical.Equals(NormalizeResourceName(VersionResourceName), StringComparison.OrdinalIgnoreCase))
                continue;

            var relative = logical.Substring(ResourcePrefix.Length).Replace('/', Path.DirectorySeparatorChar);
            if (string.IsNullOrWhiteSpace(relative))
                continue;

            var dest = Path.Combine(kapeRoot, relative);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                using var src = asm.GetManifestResourceStream(resourceName);
                if (src is null)
                {
                    warnings.Add("Нет потока: " + resourceName);
                    continue;
                }

                using var ms = new MemoryStream();
                src.CopyTo(ms);
                var bytes = ms.ToArray();

                if (File.Exists(dest) && FilesEqual(dest, bytes))
                {
                    unchanged++;
                    continue;
                }

                File.WriteAllBytes(dest, bytes);
                written++;
            }
            catch (Exception ex)
            {
                warnings.Add($"{relative}: {ex.Message}");
            }
        }

        try
        {
            var marker = Path.Combine(kapeRoot, VersionMarkerRelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(marker)!);
            File.WriteAllText(marker, embeddedVersion + Environment.NewLine, Encoding.ASCII);
        }
        catch (Exception ex)
        {
            warnings.Add("marker: " + ex.Message);
        }

        var ok = warnings.Count == 0 || written > 0 || unchanged > 0;
        return new Result
        {
            Ok = ok,
            EmbeddedVersion = embeddedVersion,
            PreviousVersion = previous,
            FilesWritten = written,
            FilesUnchanged = unchanged,
            Warnings = warnings,
            Message = ok
                ? $"KapeIR каталог: v{embeddedVersion} (записано {written}, без изменений {unchanged})" +
                  (warnings.Count > 0 ? $"; предупреждений: {warnings.Count}" : "")
                : "KapeIR каталог: ошибки засева — " + string.Join("; ", warnings.Take(3))
        };
    }

    private static bool AllManagedFilesPresent(string kapeRoot, Assembly asm)
    {
        foreach (var resourceName in asm.GetManifestResourceNames())
        {
            var logical = NormalizeResourceName(resourceName);
            if (!logical.StartsWith(ResourcePrefix, StringComparison.OrdinalIgnoreCase))
                continue;
            if (logical.Equals(NormalizeResourceName(VersionResourceName), StringComparison.OrdinalIgnoreCase))
                continue;
            var relative = logical.Substring(ResourcePrefix.Length).Replace('/', Path.DirectorySeparatorChar);
            if (string.IsNullOrWhiteSpace(relative))
                continue;
            if (!File.Exists(Path.Combine(kapeRoot, relative)))
                return false;
        }

        return true;
    }

    /// <summary>True when any managed file exists but bytes differ from the embedded payload.</summary>
    private static bool AnyManagedFileDrift(string kapeRoot, Assembly asm)
    {
        foreach (var resourceName in asm.GetManifestResourceNames())
        {
            var logical = NormalizeResourceName(resourceName);
            if (!logical.StartsWith(ResourcePrefix, StringComparison.OrdinalIgnoreCase))
                continue;
            if (logical.Equals(NormalizeResourceName(VersionResourceName), StringComparison.OrdinalIgnoreCase))
                continue;
            var relative = logical.Substring(ResourcePrefix.Length).Replace('/', Path.DirectorySeparatorChar);
            if (string.IsNullOrWhiteSpace(relative))
                continue;
            var dest = Path.Combine(kapeRoot, relative);
            if (!File.Exists(dest))
                return true;
            try
            {
                using var src = asm.GetManifestResourceStream(resourceName);
                if (src is null)
                    continue;
                using var ms = new MemoryStream();
                src.CopyTo(ms);
                if (!FilesEqual(dest, ms.ToArray()))
                    return true;
            }
            catch
            {
                return true;
            }
        }

        return false;
    }

    private static bool FilesEqual(string path, byte[] expected)
    {
        try
        {
            var onDisk = File.ReadAllBytes(path);
            if (onDisk.Length != expected.Length)
                return false;
            return CryptographicOperations.FixedTimeEquals(onDisk, expected);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>MSBuild LogicalName uses '\'; manifests may expose '/' — normalize to '/'.</summary>
    internal static string NormalizeResourceName(string name)
        => (name ?? "").Replace('\\', '/');
}
