using System.Text;
using System.Text.RegularExpressions;
using YamlDotNet.Serialization;
using KapePackBuilder.Models;

namespace KapePackBuilder.Services;

public static class KapeFileIo
{
    private static readonly Regex GuidRe = new(
        @"^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$",
        RegexOptions.Compiled);

    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .IgnoreUnmatchedProperties()
        .Build();

    public static Dictionary<string, object?> LoadKapeFile(string path)
    {
        var raw = File.ReadAllText(path, Encoding.UTF8);
        var cleaned = SanitizeYamlText(raw);
        var data = Deserializer.Deserialize<object>(cleaned);
        if (data is not Dictionary<object, object> map)
            throw new InvalidDataException($"Invalid KAPE file (expected mapping): {path}");
        return ToStringKeyed(map);
    }

    public static bool TryLoadKapeFile(string path, out Dictionary<string, object?> data)
    {
        try
        {
            data = LoadKapeFile(path);
            return true;
        }
        catch
        {
            data = new Dictionary<string, object?>();
            return false;
        }
    }

    public static bool IsCompoundTarget(Dictionary<string, object?> data)
    {
        foreach (var entry in EnumMaps(GetList(data, "Targets")))
        {
            var p = GetString(entry, "Path");
            if (p.EndsWith(".tkape", StringComparison.OrdinalIgnoreCase) ||
                p.EndsWith(".mkape", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    public static bool IsCompoundModule(Dictionary<string, object?> data)
    {
        foreach (var entry in EnumMaps(GetList(data, "Processors")))
        {
            var exe = GetString(entry, "Executable");
            if (exe.EndsWith(".mkape", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    public static List<string> ExtractTargetChildren(Dictionary<string, object?> data)
    {
        var children = new List<string>();
        foreach (var entry in EnumMaps(GetList(data, "Targets")))
        {
            var p = GetString(entry, "Path").Trim();
            if (p.EndsWith(".tkape", StringComparison.OrdinalIgnoreCase))
                children.Add(Path.GetFileName(p));
        }
        return children;
    }

    public static List<string> ExtractModuleChildren(Dictionary<string, object?> data)
    {
        var children = new List<string>();
        foreach (var entry in EnumMaps(GetList(data, "Processors")))
        {
            var exe = GetString(entry, "Executable").Trim();
            if (exe.EndsWith(".mkape", StringComparison.OrdinalIgnoreCase))
                children.Add(Path.GetFileName(exe));
        }
        return children;
    }

    /// <summary>Collect FileMask values from a target (.tkape) Targets: entries.</summary>
    public static List<string> ExtractTargetFileMasks(Dictionary<string, object?> data)
    {
        var masks = new List<string>();
        foreach (var entry in EnumMaps(GetList(data, "Targets")))
        {
            var mask = GetString(entry, "FileMask");
            if (string.IsNullOrWhiteSpace(mask)) continue;
            masks.AddRange(SplitMaskTokens(mask));
        }
        return DedupMasks(masks);
    }

    /// <summary>Collect FileMask from a module (.mkape) root (and processor overrides if present).</summary>
    public static List<string> ExtractModuleFileMasks(Dictionary<string, object?> data)
    {
        var masks = new List<string>();
        var root = GetString(data, "FileMask");
        if (!string.IsNullOrWhiteSpace(root))
            masks.AddRange(SplitMaskTokens(root));
        foreach (var entry in EnumMaps(GetList(data, "Processors")))
        {
            var mask = GetString(entry, "FileMask");
            if (string.IsNullOrWhiteSpace(mask)) continue;
            masks.AddRange(SplitMaskTokens(mask));
        }
        return DedupMasks(masks);
    }

    public static IEnumerable<string> SplitMaskTokens(string raw)
    {
        var s = raw.Trim().Trim('"', '\'');
        if (s.StartsWith("regex:", StringComparison.OrdinalIgnoreCase))
            s = s[6..].Trim();
        // Drop surrounding parentheses used by some modules: (a|b)
        if (s.StartsWith('(') && s.EndsWith(')'))
            s = s[1..^1];
        foreach (var part in s.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var cleaned = part.Trim().Trim('"', '\'');
            if (string.IsNullOrWhiteSpace(cleaned) || cleaned is "*" or "*.*")
                continue;
            yield return cleaned;
        }
    }

    private static List<string> DedupMasks(IEnumerable<string> masks)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var list = new List<string>();
        foreach (var m in masks)
        {
            if (seen.Add(m))
                list.Add(m);
        }
        return list;
    }

    public static PackageDefinition PackageFromCompoundTarget(string path, Dictionary<string, object?>? data = null)
    {
        data ??= LoadKapeFile(path);
        var entries = new List<SelectionEntry>();
        foreach (var item in EnumMaps(GetList(data, "Targets")))
        {
            var refPath = GetString(item, "Path").Trim();
            if (!refPath.EndsWith(".tkape", StringComparison.OrdinalIgnoreCase))
                continue;
            var comments = GetString(item, "Comments");
            if (string.IsNullOrEmpty(comments))
                comments = GetString(item, "Comment");
            entries.Add(new SelectionEntry
            {
                Name = NullIfEmpty(GetString(item, "Name")) ?? Path.GetFileNameWithoutExtension(refPath),
                Category = NullIfEmpty(GetString(item, "Category")) ?? "General",
                Path = Path.GetFileName(refPath),
                Comments = comments
            });
        }

        return new PackageDefinition
        {
            Name = Path.GetFileNameWithoutExtension(path),
            Description = GetString(data, "Description"),
            Author = GetString(data, "Author"),
            Version = NullIfEmpty(GetString(data, "Version")) ?? "1.0",
            PackageId = NullIfEmpty(GetString(data, "Id")) ?? Guid.NewGuid().ToString(),
            RecreateDirectories = GetBool(data, "RecreateDirectories", true),
            Targets = entries
        };
    }

    public static string RenderCompoundTarget(PackageDefinition pkg)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Description: {(string.IsNullOrWhiteSpace(pkg.Description) ? pkg.Name : pkg.Description)}");
        sb.AppendLine($"Author: {(string.IsNullOrWhiteSpace(pkg.Author) ? "KAPE Pack Builder" : pkg.Author)}");
        sb.AppendLine($"Version: {pkg.Version}");
        sb.AppendLine($"Id: {EnsureGuid(pkg.PackageId)}");
        sb.AppendLine($"RecreateDirectories: {(pkg.RecreateDirectories ? "true" : "false")}");
        sb.AppendLine("Targets:");
        foreach (var entry in pkg.Targets)
        {
            sb.AppendLine("    -");
            sb.AppendLine($"        Name: {entry.Name}");
            sb.AppendLine($"        Category: {(string.IsNullOrWhiteSpace(entry.Category) ? "General" : entry.Category)}");
            sb.AppendLine($"        Path: {entry.Path}");
            if (!string.IsNullOrWhiteSpace(entry.Comments))
            {
                var escaped = entry.Comments.Replace("\"", "\\\"");
                sb.AppendLine($"        Comments: \"{escaped}\"");
            }
        }
        sb.AppendLine();
        if (!string.IsNullOrWhiteSpace(pkg.Notes))
        {
            foreach (var line in pkg.Notes.Split('\n'))
                sb.AppendLine($"# {line.TrimEnd('\r')}");
            sb.AppendLine();
        }
        return sb.ToString();
    }

    public static string RenderCompoundModule(PackageDefinition pkg)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Description: {(string.IsNullOrWhiteSpace(pkg.Description) ? pkg.Name : pkg.Description)} Modules");
        sb.AppendLine("Category: Compound");
        sb.AppendLine($"Author: {(string.IsNullOrWhiteSpace(pkg.Author) ? "KAPE Pack Builder" : pkg.Author)}");
        sb.AppendLine($"Version: {pkg.Version}");
        sb.AppendLine($"Id: {Guid.NewGuid()}");
        sb.AppendLine("ExportFormat: ");
        sb.AppendLine("FileMask: \"\"");
        sb.AppendLine("Processors:");
        foreach (var entry in pkg.Modules)
        {
            sb.AppendLine("    -");
            sb.AppendLine($"        Executable: {entry.Path}");
            sb.AppendLine("        CommandLine: \"\"");
            sb.AppendLine("        ExportFormat: \"\"");
        }
        sb.AppendLine();
        sb.AppendLine("# Generated by KAPE Pack Builder");
        sb.AppendLine();
        return sb.ToString();
    }

    public static string RenderRunBat(PackageDefinition pkg, string kapeRel = ".\\kape.exe")
    {
        var target = pkg.TargetCompoundName;
        var moduleArg = "";
        if (pkg.ModuleCompoundName is not null)
            moduleArg = $" --mdest RESULTS\\%m\\ModuleOutput --zm true --module {pkg.ModuleCompoundName}";
        else if (pkg.Modules.Count > 0)
        {
            var names = string.Join(",", pkg.Modules.Select(m => Path.GetFileNameWithoutExtension(m.Path)));
            moduleArg = $" --mdest RESULTS\\%m\\ModuleOutput --zm true --module {names}";
        }

        var zipArg = pkg.ZipOutput ? " --zip %m" : "";
        var flushArg = pkg.Flush ? " --flush" : "";
        var vssArg = pkg.Vss ? " --vss" : "";

        return string.Join('\n', new[]
        {
            "@echo off",
            "setlocal",
            "cd /d \"%~dp0\"",
            "echo [*] KAPE Pack Builder launch script",
            $"echo [*] Package: {pkg.Name}",
            $"echo [*] Target:  {target}",
            $"\"{kapeRel}\" --tsource {pkg.Tsource} --tdest RESULTS\\%m --target {target}{zipArg}{moduleArg}{flushArg}{vssArg}",
            "echo [*]",
            "echo [*] Done. Check RESULTS\\%%COMPUTERNAME%%",
            "pause",
            ""
        });
    }

    public static string RenderRunPs1(PackageDefinition pkg)
    {
        var args = new List<string>
        {
            "--tsource", pkg.Tsource,
            "--tdest", "RESULTS\\%m",
            "--target", pkg.TargetCompoundName
        };
        if (pkg.ZipOutput)
            args.AddRange(new[] { "--zip", "%m" });
        if (pkg.ModuleCompoundName is not null)
        {
            args.AddRange(new[]
            {
                "--mdest", "RESULTS\\%m\\ModuleOutput", "--zm", "true",
                "--module", pkg.ModuleCompoundName
            });
        }
        else if (pkg.Modules.Count > 0)
        {
            var names = string.Join(",", pkg.Modules.Select(m => Path.GetFileNameWithoutExtension(m.Path)));
            args.AddRange(new[]
            {
                "--mdest", "RESULTS\\%m\\ModuleOutput", "--zm", "true",
                "--module", names
            });
        }
        if (pkg.Flush) args.Add("--flush");
        if (pkg.Vss) args.Add("--vss");

        var argLiteral = string.Join(", ", args.Select(a => $"'{a}'"));
        return string.Join('\n', new[]
        {
            "#Requires -RunAsAdministrator",
            "$ErrorActionPreference = 'Stop'",
            "Set-Location -Path $PSScriptRoot",
            $"Write-Host '[*] Package: {pkg.Name}'",
            "$kape = Join-Path $PSScriptRoot 'kape.exe'",
            "if (-not (Test-Path $kape)) { throw 'kape.exe not found next to this script' }",
            $"& $kape {argLiteral}",
            "Write-Host '[*] Done.'",
            ""
        });
    }

    public static string EnsureGuid(string? value)
    {
        if (!string.IsNullOrWhiteSpace(value) && GuidRe.IsMatch(value.Trim()))
            return value.Trim();
        return Guid.NewGuid().ToString();
    }

    public static string GetString(Dictionary<string, object?> data, string key)
    {
        if (!data.TryGetValue(key, out var val) || val is null) return "";
        return Convert.ToString(val)?.Trim() ?? "";
    }

    public static string SanitizeYamlText(string text)
    {
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var sb = new StringBuilder();
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (i == 0 && line.StartsWith('\ufeff'))
                line = line.TrimStart('\ufeff');
            sb.Append(line.Replace("\t", "    "));
            if (i < lines.Length - 1 || text.EndsWith('\n') || text.EndsWith("\r\n"))
                sb.Append('\n');
        }
        return sb.ToString();
    }

    private static bool GetBool(Dictionary<string, object?> data, string key, bool defaultValue)
    {
        if (!data.TryGetValue(key, out var val) || val is null) return defaultValue;
        if (val is bool b) return b;
        var s = Convert.ToString(val)?.Trim().ToLowerInvariant();
        return s is "true" or "yes" or "1";
    }

    private static List<object?> GetList(Dictionary<string, object?> data, string key)
    {
        if (!data.TryGetValue(key, out var val) || val is null)
            return new List<object?>();
        if (val is List<object?> list) return list;
        if (val is IEnumerable<object> en)
            return en.Cast<object?>().ToList();
        return new List<object?>();
    }

    private static IEnumerable<Dictionary<string, object?>> EnumMaps(List<object?> list)
    {
        foreach (var item in list)
        {
            if (item is Dictionary<object, object> map)
                yield return ToStringKeyed(map);
            else if (item is Dictionary<string, object?> sm)
                yield return sm;
        }
    }

    private static Dictionary<string, object?> ToStringKeyed(Dictionary<object, object> map)
    {
        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in map)
        {
            var key = Convert.ToString(kv.Key) ?? "";
            result[key] = Normalize(kv.Value);
        }
        return result;
    }

    private static object? Normalize(object? value)
    {
        if (value is Dictionary<object, object> map)
            return ToStringKeyed(map);
        if (value is List<object> list)
            return list.Select(Normalize).Cast<object?>().ToList();
        if (value is IList<object> ilist)
            return ilist.Cast<object>().Select(Normalize).Cast<object?>().ToList();
        return value;
    }

    private static string? NullIfEmpty(string s) => string.IsNullOrWhiteSpace(s) ? null : s;
}
