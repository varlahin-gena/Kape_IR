using System.Text;
using System.Text.RegularExpressions;
using KapeIR.Core.Models;

namespace KapeIR.Core.Services;

/// <summary>
/// Compound/leaf analysis of parsed KAPE YAML and render of generated compound .tkape / .mkape.
/// </summary>
public static class KapeCompoundIo
{
    private static readonly Regex GuidRe = new(
        @"^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$",
        RegexOptions.Compiled);

    // Stock kapefiles often escape path separators as \\ inside PowerShell -c strings.
    private static readonly Regex ModulesBinRefRe = new(
        @"(?i)(?:%kapeDirectory%(?:\\)+)?(?:\.(?:\\)+)?Modules(?:\\)+bin(?:\\)+(?<rel>[^\s""'<>|]+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static bool IsCompoundTarget(Dictionary<string, object?> data)
    {
        foreach (var entry in KapeYamlReader.EnumMaps(KapeYamlReader.GetList(data, "Targets")))
        {
            var p = KapeYamlReader.GetString(entry, "Path");
            if (p.EndsWith(".tkape", StringComparison.OrdinalIgnoreCase) ||
                p.EndsWith(".mkape", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    public static bool IsCompoundModule(Dictionary<string, object?> data)
    {
        foreach (var entry in KapeYamlReader.EnumMaps(KapeYamlReader.GetList(data, "Processors")))
        {
            var exe = KapeYamlReader.GetString(entry, "Executable");
            if (exe.EndsWith(".mkape", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    public static List<string> ExtractTargetChildren(Dictionary<string, object?> data)
    {
        var children = new List<string>();
        foreach (var entry in KapeYamlReader.EnumMaps(KapeYamlReader.GetList(data, "Targets")))
        {
            var p = KapeYamlReader.GetString(entry, "Path").Trim();
            if (p.EndsWith(".tkape", StringComparison.OrdinalIgnoreCase))
                children.Add(Path.GetFileName(p));
        }
        return children;
    }

    public static List<string> ExtractModuleChildren(Dictionary<string, object?> data)
    {
        var children = new List<string>();
        foreach (var entry in KapeYamlReader.EnumMaps(KapeYamlReader.GetList(data, "Processors")))
        {
            var exe = KapeYamlReader.GetString(entry, "Executable").Trim();
            if (exe.EndsWith(".mkape", StringComparison.OrdinalIgnoreCase))
                children.Add(Path.GetFileName(exe));
        }
        return children;
    }

    /// <summary>Leaf-module processor Executable values that are real binaries (not .mkape children).</summary>
    public static List<string> ExtractModuleExecutables(Dictionary<string, object?> data)
    {
        var list = new List<string>();
        foreach (var entry in KapeYamlReader.EnumMaps(KapeYamlReader.GetList(data, "Processors")))
        {
            var exe = KapeYamlReader.GetString(entry, "Executable").Trim();
            if (string.IsNullOrEmpty(exe) ||
                exe.EndsWith(".mkape", StringComparison.OrdinalIgnoreCase))
                continue;
            list.Add(exe);
        }
        return list;
    }

    /// <summary>
    /// Relative paths under Modules\bin referenced from processor CommandLine
    /// (scripts wrappers, nested tools: Invoke-Utf8Capture.ps1, CrowdResponse\…, …).
    /// </summary>
    public static List<string> ExtractModuleBinCommandLineRefs(Dictionary<string, object?> data)
    {
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in KapeYamlReader.EnumMaps(KapeYamlReader.GetList(data, "Processors")))
        {
            foreach (var rel in ExtractBinRefsFromCommandLine(KapeYamlReader.GetString(entry, "CommandLine")))
                found.Add(rel);
        }

        return found.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Parse Modules\bin\… fragments out of a single CommandLine string.</summary>
    public static IEnumerable<string> ExtractBinRefsFromCommandLine(string? commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine))
            yield break;

        foreach (Match m in ModulesBinRefRe.Matches(commandLine))
        {
            var rel = m.Groups["rel"].Value
                .Replace('/', '\\')
                .Replace("\\\\", "\\")
                .Trim()
                .TrimEnd('\\', '/', '.', ',', ';', '`');
            // Collapse residual doubled separators from %%kapeDirectory%%\\Modules\\bin\\…
            while (rel.Contains("\\\\", StringComparison.Ordinal))
                rel = rel.Replace("\\\\", "\\", StringComparison.Ordinal);
            if (string.IsNullOrWhiteSpace(rel))
                continue;
            yield return rel.TrimStart('\\');
        }
    }

    /// <summary>Collect FileMask values from a target (.tkape) Targets: entries.</summary>
    public static List<string> ExtractTargetFileMasks(Dictionary<string, object?> data)
    {
        var masks = new List<string>();
        foreach (var entry in KapeYamlReader.EnumMaps(KapeYamlReader.GetList(data, "Targets")))
        {
            var mask = KapeYamlReader.GetString(entry, "FileMask");
            if (string.IsNullOrWhiteSpace(mask)) continue;
            masks.AddRange(SplitMaskTokens(mask));
        }
        return DedupMasks(masks);
    }

    /// <summary>Collect FileMask from a module (.mkape) root (and processor overrides if present).</summary>
    public static List<string> ExtractModuleFileMasks(Dictionary<string, object?> data)
    {
        var masks = new List<string>();
        var root = KapeYamlReader.GetString(data, "FileMask");
        if (!string.IsNullOrWhiteSpace(root))
            masks.AddRange(SplitMaskTokens(root));
        foreach (var entry in KapeYamlReader.EnumMaps(KapeYamlReader.GetList(data, "Processors")))
        {
            var mask = KapeYamlReader.GetString(entry, "FileMask");
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

    public static PackageDefinition PackageFromCompoundTarget(string path, Dictionary<string, object?>? data = null)
    {
        data ??= KapeYamlReader.LoadKapeFile(path);
        var entries = new List<SelectionEntry>();
        foreach (var item in KapeYamlReader.EnumMaps(KapeYamlReader.GetList(data, "Targets")))
        {
            var refPath = KapeYamlReader.GetString(item, "Path").Trim();
            if (!refPath.EndsWith(".tkape", StringComparison.OrdinalIgnoreCase))
                continue;
            var comments = KapeYamlReader.GetString(item, "Comments");
            if (string.IsNullOrEmpty(comments))
                comments = KapeYamlReader.GetString(item, "Comment");
            entries.Add(new SelectionEntry
            {
                Name = NullIfEmpty(KapeYamlReader.GetString(item, "Name")) ?? Path.GetFileNameWithoutExtension(refPath),
                Category = NullIfEmpty(KapeYamlReader.GetString(item, "Category")) ?? "General",
                Path = Path.GetFileName(refPath),
                Comments = comments
            });
        }

        return new PackageDefinition
        {
            Name = Path.GetFileNameWithoutExtension(path),
            Description = KapeYamlReader.GetString(data, "Description"),
            Author = KapeYamlReader.GetString(data, "Author"),
            Version = NullIfEmpty(KapeYamlReader.GetString(data, "Version")) ?? "1.0",
            PackageId = NullIfEmpty(KapeYamlReader.GetString(data, "Id")) ?? Guid.NewGuid().ToString(),
            RecreateDirectories = KapeYamlReader.GetBool(data, "RecreateDirectories", true),
            Targets = entries
        };
    }

    public static string RenderCompoundTarget(PackageDefinition pkg)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Description: {(string.IsNullOrWhiteSpace(pkg.Description) ? pkg.Name : pkg.Description)}");
        sb.AppendLine($"Author: {(string.IsNullOrWhiteSpace(pkg.Author) ? "KapeIR" : pkg.Author)}");
        sb.AppendLine($"Version: {pkg.Version}");
        sb.AppendLine($"Id: {EnsureGuid(pkg.PackageId)}");
        sb.AppendLine($"RecreateDirectories: {(pkg.RecreateDirectories ? "true" : "false")}");
        sb.AppendLine("Targets:");
        foreach (var entry in pkg.Targets)
        {
            sb.AppendLine("    -");
            sb.AppendLine($"        Name: {FormatYamlScalar(entry.Name)}");
            sb.AppendLine(
                $"        Category: {FormatYamlScalar(string.IsNullOrWhiteSpace(entry.Category) ? "General" : entry.Category)}");
            sb.AppendLine($"        Path: {FormatYamlScalar(entry.Path)}");
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
        => RenderCompoundModule(pkg, pkg.Modules);

    public static string RenderCompoundModule(PackageDefinition pkg, IEnumerable<SelectionEntry> modules)
    {
        var list = modules.ToList();
        var sb = new StringBuilder();
        sb.AppendLine($"Description: {(string.IsNullOrWhiteSpace(pkg.Description) ? pkg.Name : pkg.Description)} Modules");
        sb.AppendLine("Category: Compound");
        sb.AppendLine($"Author: {(string.IsNullOrWhiteSpace(pkg.Author) ? "KapeIR" : pkg.Author)}");
        sb.AppendLine($"Version: {pkg.Version}");
        sb.AppendLine($"Id: {Guid.NewGuid()}");
        // KAPE validates every .mkape under Modules\; empty ExportFormat fails the whole run.
        sb.AppendLine("ExportFormat: csv");
        sb.AppendLine("Processors:");
        foreach (var entry in list)
        {
            sb.AppendLine("    -");
            // Quote paths starting with ! / !! — otherwise YAML treats them as tags
            // (e.g. !!ToolSync.mkape → unresolved tag tag:yaml.org,2002:ToolSync.mkape).
            sb.AppendLine($"        Executable: {FormatYamlScalar(entry.Path)}");
            sb.AppendLine("        CommandLine: \"\"");
            sb.AppendLine("        ExportFormat: \"\"");
        }
        sb.AppendLine();
        sb.AppendLine("# Generated by KapeIR");
        sb.AppendLine();
        return sb.ToString();
    }

    /// <summary>
    /// Format a YAML plain scalar, quoting when needed so values like
    /// <c>!!ToolSync.mkape</c> / <c>!EZParser.mkape</c> are not parsed as tags.
    /// </summary>
    public static string FormatYamlScalar(string? value)
    {
        var s = value ?? "";
        if (s.Length == 0)
            return "\"\"";

        var needsQuote =
            char.IsWhiteSpace(s[0]) ||
            char.IsWhiteSpace(s[^1]) ||
            s[0] is '!' or '#' or '&' or '*' or '?' or '|' or '>' or '@' or '`'
                or '\'' or '"' or '%' or '{' or '}' or '[' or ']' or ',' or ':' or '-' ||
            s.Contains(':') ||
            s.Contains('#') ||
            s.Contains('\n') ||
            s.Contains('\r') ||
            LooksLikeYamlBoolOrNull(s);

        if (!needsQuote)
            return s;

        return "'" + s.Replace("'", "''", StringComparison.Ordinal) + "'";
    }

    public static string EnsureGuid(string? value)
    {
        if (!string.IsNullOrWhiteSpace(value) && GuidRe.IsMatch(value.Trim()))
            return value.Trim();
        return Guid.NewGuid().ToString();
    }

    private static bool LooksLikeYamlBoolOrNull(string s) =>
        s.Equals("true", StringComparison.OrdinalIgnoreCase) ||
        s.Equals("false", StringComparison.OrdinalIgnoreCase) ||
        s.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
        s.Equals("no", StringComparison.OrdinalIgnoreCase) ||
        s.Equals("on", StringComparison.OrdinalIgnoreCase) ||
        s.Equals("off", StringComparison.OrdinalIgnoreCase) ||
        s.Equals("null", StringComparison.OrdinalIgnoreCase) ||
        s.Equals("~", StringComparison.Ordinal);

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

    private static string? NullIfEmpty(string s) => string.IsNullOrWhiteSpace(s) ? null : s;
}
