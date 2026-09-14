using System.Text.Json;
using KapePack.Core.Models;

namespace KapePack.Core.Services;

/// <summary>Shared package.json → <see cref="CollectionPlan.LaunchManifest"/> parsing for Builder and Runner.</summary>
public static class LaunchManifestIo
{
    public static CollectionPlan.LaunchManifest? TryReadFromPackageDir(string packageDir)
    {
        var path = Path.Combine(packageDir, "package.json");
        return File.Exists(path) ? TryReadFile(path) : null;
    }

    public static CollectionPlan.LaunchManifest? TryReadFile(string path)
    {
        try
        {
            return ReadFile(path);
        }
        catch
        {
            return null;
        }
    }

    public static CollectionPlan.LaunchManifest ReadFile(string path)
        => Parse(JsonDocument.Parse(File.ReadAllText(path)).RootElement);

    public static CollectionPlan.LaunchManifest Parse(JsonElement root)
    {
        var mode = ParseCollectionMode(root);
        var phase1 = NullIfEmpty(GetStr(root, "phase1_module"))
                     ?? NullIfEmpty(GetStr(root, "phase1_module_name"))
                     ?? NullIfEmpty(GetStr(root, "module_compound"))
                     ?? PackageDefinition.DefaultPhase1Module;

        return new CollectionPlan.LaunchManifest
        {
            Name = NullIfEmpty(GetStr(root, "name")) ?? "Package",
            Tsource = NullIfEmpty(GetStr(root, "tsource")) ?? "",
            Target = GetStr(root, "target_compound") ?? "",
            Module = NullIfEmpty(GetStr(root, "module_compound")),
            ZipOutput = !root.TryGetProperty("zip_output", out var z) || z.ValueKind != JsonValueKind.False,
            Flush = IsTrue(root, "flush"),
            Vss = IsTrue(root, "vss"),
            CollectionMode = mode,
            CaseId = GetStr(root, "case_id") ?? "",
            Phase1Module = phase1,
            Phase2Module = NullIfEmpty(GetStr(root, "phase2_module"))
                           ?? NullIfEmpty(GetStr(root, "phase2_module_name"))
        };
    }

    public static IrCollectionMode ParseCollectionMode(JsonElement root)
    {
        if (!root.TryGetProperty("collection_mode", out var p))
            return IrCollectionMode.Single;

        if (p.ValueKind == JsonValueKind.String)
        {
            var s = p.GetString() ?? "";
            if (s.Equals("two_phase", StringComparison.OrdinalIgnoreCase) ||
                s.Equals("twophase", StringComparison.OrdinalIgnoreCase) ||
                s.Equals("2", StringComparison.Ordinal))
                return IrCollectionMode.TwoPhase;
        }

        if (p.ValueKind == JsonValueKind.Number && p.TryGetInt32(out var n) && n == 1)
            return IrCollectionMode.TwoPhase;

        return IrCollectionMode.Single;
    }

    private static bool IsTrue(JsonElement root, string name)
        => root.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.True;

    private static string? GetStr(JsonElement root, string name)
        => root.TryGetProperty(name, out var p) ? p.GetString() : null;

    private static string? NullIfEmpty(string? s)
        => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
