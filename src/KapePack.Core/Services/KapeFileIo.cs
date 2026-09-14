using System.Text;
using System.Text.RegularExpressions;
using YamlDotNet.Serialization;
using KapePack.Core.Models;

namespace KapePack.Core.Services;

public static class KapeFileIo
{
    private static readonly Regex GuidRe = new(
        @"^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$",
        RegexOptions.Compiled);

    private static readonly Regex DocUrlRe = new(
        @"https?://[^\s<>""']+",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .IgnoreUnmatchedProperties()
        .Build();

    /// <summary>
    /// Collect documentation URLs from # comment lines (YAML parser strips comments).
    /// </summary>
    public static List<string> ExtractDocumentationLinks(string path)
    {
        if (!File.Exists(path)) return new List<string>();
        return ExtractDocumentationLinksFromText(File.ReadAllText(path, Encoding.UTF8));
    }

    public static List<string> ExtractDocumentationLinksFromText(string text)
    {
        var urls = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            var trimmed = line.TrimStart();
            if (!trimmed.StartsWith('#')) continue;
            foreach (Match m in DocUrlRe.Matches(trimmed))
            {
                var url = m.Value.TrimEnd('.', ',', ';', ')', ']');
                if (url.Length < 12) continue;
                if (seen.Add(url))
                    urls.Add(url);
            }
        }
        return urls;
    }

    public static Dictionary<string, object?> LoadKapeFile(string path)
    {
        if (!TryReadKapeFile(path, out var data, out _))
            throw new InvalidDataException($"Invalid KAPE file: {path}");
        return data;
    }

    /// <summary>Single disk read: YAML map + raw text (for documentation URLs in comments).</summary>
    public static bool TryReadKapeFile(string path, out Dictionary<string, object?> data, out string rawText)
    {
        rawText = "";
        data = new Dictionary<string, object?>();
        try
        {
            rawText = File.ReadAllText(path, Encoding.UTF8);
            var cleaned = SanitizeYamlText(rawText);
            var parsed = Deserializer.Deserialize<object>(cleaned);
            if (parsed is not Dictionary<object, object> map)
                return false;
            data = ToStringKeyed(map);
            return true;
        }
        catch
        {
            data = new Dictionary<string, object?>();
            return false;
        }
    }

    public static bool TryLoadKapeFile(string path, out Dictionary<string, object?> data)
        => TryReadKapeFile(path, out data, out _);

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

    /// <summary>Leaf-module processor Executable values that are real binaries (not .mkape children).</summary>
    public static List<string> ExtractModuleExecutables(Dictionary<string, object?> data)
    {
        var list = new List<string>();
        foreach (var entry in EnumMaps(GetList(data, "Processors")))
        {
            var exe = GetString(entry, "Executable").Trim();
            if (string.IsNullOrEmpty(exe) ||
                exe.EndsWith(".mkape", StringComparison.OrdinalIgnoreCase))
                continue;
            list.Add(exe);
        }
        return list;
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
        sb.AppendLine($"Author: {(string.IsNullOrWhiteSpace(pkg.Author) ? "KAPE Pack Builder" : pkg.Author)}");
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
        sb.AppendLine("# Generated by KAPE Pack Builder");
        sb.AppendLine();
        return sb.ToString();
    }

    public static string RenderRunBat(PackageDefinition pkg, string kapeRel = ".\\kape.exe")
    {
        // Thin wrapper: all args live in PowerShell where "!" in names is safe.
        _ = kapeRel;
        return string.Join("\r\n", new[]
        {
            "@echo off",
            "setlocal",
            "chcp 65001 >nul",
            "cd /d \"%~dp0\"",
            "echo [*] Запуск через PowerShell…",
            "powershell.exe -NoProfile -ExecutionPolicy Bypass -File \"%~dp0run_collection.ps1\"",
            "set \"KAPE_ERR=%ERRORLEVEL%\"",
            "if not \"%KAPE_ERR%\"==\"0\" echo [!] Код выхода: %KAPE_ERR%",
            "pause",
            ""
        });
    }

    public static string RenderKapeCli(PackageDefinition pkg)
    {
        if (!pkg.IsTwoPhase)
            return KapeCliArgs.RenderCliLine(KapeCliArgs.FromPackage(pkg, fleetCliVars: true)) + "\r\n";

        var manifest = CollectionPlan.FromPackage(pkg);
        var phases = CollectionPlan.BuildPhases(manifest, new CollectionPlan.RuntimeOptions(pkg.Tsource));
        var sb = new StringBuilder();
        sb.AppendLine("# two_phase IR — CollectPack.exe runs both phases; fleet may use one line at a time");
        foreach (var phase in phases)
        {
            var fleetOpts = phase.Options with { FleetCliVars = true };
            // Rewrite paths for fleet %%d / %%m
            if (phase.Name == "1")
            {
                fleetOpts = fleetOpts with
                {
                    Mdest = @"%%d\RESULTS\%%m\Phase1_Volatile",
                    FleetCliVars = true
                };
            }
            else if (phase.Name == "2")
            {
                fleetOpts = fleetOpts with
                {
                    Tdest = @"%%d\RESULTS\%%m\Phase2_Disk",
                    Mdest = @"%%d\RESULTS\%%m\Phase2_Disk\ModuleOutput",
                    FleetCliVars = true
                };
            }

            sb.AppendLine("# " + phase.Label);
            sb.AppendLine(KapeCliArgs.RenderCliLine(fleetOpts));
        }

        return sb.ToString();
    }

    public static string RenderRunPs1(PackageDefinition pkg)
    {
        if (pkg.IsTwoPhase)
            return RenderRunPs1TwoPhase(pkg);

        var module = pkg.ModuleCompoundName;
        if (module is null && pkg.Modules.Count > 0)
            module = string.Join(",", pkg.Modules.Select(m => Path.GetFileNameWithoutExtension(m.Path)));

        var args = KapeCliArgs.Build(new KapeCliArgs.Options(
            pkg.Tsource,
            pkg.TargetCompoundName,
            module,
            pkg.ZipOutput,
            pkg.Flush,
            pkg.Vss,
            FleetCliVars: false));

        var argLiteral = string.Join(", ", args.Select(a => $"'{a.Replace("'", "''")}'"));
        return string.Join("\r\n", new[]
        {
            "$ErrorActionPreference = 'Continue'",
            "try { [Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false) } catch {}",
            "Set-Location -LiteralPath $PSScriptRoot",
            $"Write-Host '[*] Package: {pkg.Name}'",
            $"Write-Host '[*] Target:  {pkg.TargetCompoundName}'",
            "$kape = Join-Path $PSScriptRoot 'kape.exe'",
            "if (-not (Test-Path -LiteralPath $kape)) {",
            "  Write-Host '[!] kape.exe не найден рядом со скриптом' -ForegroundColor Red",
            "  exit 2",
            "}",
            // Active _kape.cli forces KAPE batch mode and ignores @kapeArgs.
            "$cli = Join-Path $PSScriptRoot '_kape.cli'",
            "$cliHold = $cli + '.packhold'",
            "if (Test-Path -LiteralPath $cli) {",
            "  if (Test-Path -LiteralPath $cliHold) { Remove-Item -LiteralPath $cliHold -Force }",
            "  Move-Item -LiteralPath $cli -Destination $cliHold -Force",
            "  Write-Host '[i] _kape.cli временно отключён (иначе batch mode)'",
            "}",
            "$code = 0",
            "try {",
            $"  $kapeArgs = @({argLiteral})",
            "  Write-Host ('[*] Command: kape.exe ' + ($kapeArgs -join ' '))",
            "  & $kape @kapeArgs",
            "  $code = $LASTEXITCODE",
            "} finally {",
            "  if (Test-Path -LiteralPath $cliHold) {",
            "    if (Test-Path -LiteralPath $cli) { Remove-Item -LiteralPath $cli -Force }",
            "    Move-Item -LiteralPath $cliHold -Destination $cli -Force",
            "  }",
            "}",
            "$results = Join-Path $PSScriptRoot ('RESULTS\\' + $env:COMPUTERNAME)",
            "if (Test-Path -LiteralPath $results) {",
            "  Write-Host \"[*] Готово. Результаты: $results\" -ForegroundColor Green",
            "} else {",
            "  Write-Host '[!] Папка RESULTS не создана — сбор не выполнен или упал.' -ForegroundColor Red",
            "  if ($code -eq 0) { $code = 1 }",
            "}",
            "exit $code",
            ""
        });
    }

    private static string RenderRunPs1TwoPhase(PackageDefinition pkg)
    {
        var manifest = CollectionPlan.FromPackage(pkg);
        var phases = CollectionPlan.BuildPhases(manifest, new CollectionPlan.RuntimeOptions(pkg.Tsource));
        var lines = new List<string>
        {
            "$ErrorActionPreference = 'Continue'",
            "try { [Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false) } catch {}",
            "Set-Location -LiteralPath $PSScriptRoot",
            $"Write-Host '[*] Package: {pkg.Name} (two_phase)'",
            "$kape = Join-Path $PSScriptRoot 'kape.exe'",
            "if (-not (Test-Path -LiteralPath $kape)) {",
            "  Write-Host '[!] kape.exe не найден рядом со скриптом' -ForegroundColor Red",
            "  exit 2",
            "}",
            "$cli = Join-Path $PSScriptRoot '_kape.cli'",
            "$cliHold = $cli + '.packhold'",
            "if (Test-Path -LiteralPath $cli) {",
            "  if (Test-Path -LiteralPath $cliHold) { Remove-Item -LiteralPath $cliHold -Force }",
            "  Move-Item -LiteralPath $cli -Destination $cliHold -Force",
            "  Write-Host '[i] _kape.cli временно отключён (иначе batch mode)'",
            "}",
            "$code = 0",
            "try {"
        };

        foreach (var phase in phases)
        {
            var argLiteral = string.Join(", ",
                KapeCliArgs.Build(phase.Options).Select(a => $"'{a.Replace("'", "''")}'"));
            lines.Add($"  Write-Host '[*] {phase.Label.Replace("'", "''")}'");
            lines.Add($"  $kapeArgs = @({argLiteral})");
            lines.Add("  Write-Host ('[*] Command: kape.exe ' + ($kapeArgs -join ' '))");
            lines.Add("  & $kape @kapeArgs");
            lines.Add("  if ($LASTEXITCODE -ne 0) { $code = $LASTEXITCODE }");
        }

        lines.AddRange(new[]
        {
            "} finally {",
            "  if (Test-Path -LiteralPath $cliHold) {",
            "    if (Test-Path -LiteralPath $cli) { Remove-Item -LiteralPath $cli -Force }",
            "    Move-Item -LiteralPath $cliHold -Destination $cli -Force",
            "  }",
            "}",
            "$results = Join-Path $PSScriptRoot ('RESULTS\\' + $env:COMPUTERNAME)",
            "if (Test-Path -LiteralPath $results) {",
            "  Write-Host \"[*] Готово. Результаты: $results\" -ForegroundColor Green",
            "} else {",
            "  Write-Host '[!] Папка RESULTS не создана — сбор не выполнен или упал.' -ForegroundColor Red",
            "  if ($code -eq 0) { $code = 1 }",
            "}",
            "exit $code",
            ""
        });
        return string.Join("\r\n", lines);
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

    private static bool LooksLikeYamlBoolOrNull(string s) =>
        s.Equals("true", StringComparison.OrdinalIgnoreCase) ||
        s.Equals("false", StringComparison.OrdinalIgnoreCase) ||
        s.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
        s.Equals("no", StringComparison.OrdinalIgnoreCase) ||
        s.Equals("on", StringComparison.OrdinalIgnoreCase) ||
        s.Equals("off", StringComparison.OrdinalIgnoreCase) ||
        s.Equals("null", StringComparison.OrdinalIgnoreCase) ||
        s.Equals("~", StringComparison.Ordinal);

    /// <summary>UTF-8 with BOM — needed so cmd.exe + chcp 65001 / PowerShell show Russian correctly.</summary>
    public static Encoding BatEncoding { get; } = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);

    private static string EscapeBatSetValue(string value)
    {
        return (value ?? "").Replace("\"", "\"\"");
    }

    private static string QuoteBatArg(string value)
    {
        if (string.IsNullOrEmpty(value)) return "\"\"";
        if (value.Contains(' ') || value.Contains('!') || value.Contains('&') || value.Contains('^'))
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        return value;
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
