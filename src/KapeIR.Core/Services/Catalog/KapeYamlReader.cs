using System.Text;
using System.Text.RegularExpressions;
using YamlDotNet.Serialization;

namespace KapeIR.Core.Services;

/// <summary>Load / sanitize KAPE YAML (.tkape / .mkape) and extract comment-level metadata.</summary>
public static class KapeYamlReader
{
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

    internal static bool GetBool(Dictionary<string, object?> data, string key, bool defaultValue)
    {
        if (!data.TryGetValue(key, out var val) || val is null) return defaultValue;
        if (val is bool b) return b;
        var s = Convert.ToString(val)?.Trim().ToLowerInvariant();
        return s is "true" or "yes" or "1";
    }

    internal static List<object?> GetList(Dictionary<string, object?> data, string key)
    {
        if (!data.TryGetValue(key, out var val) || val is null)
            return new List<object?>();
        if (val is List<object?> list) return list;
        if (val is IEnumerable<object> en)
            return en.Cast<object?>().ToList();
        return new List<object?>();
    }

    internal static IEnumerable<Dictionary<string, object?>> EnumMaps(List<object?> list)
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
}
