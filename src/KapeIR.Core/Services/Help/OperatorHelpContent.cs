using System.Reflection;
using System.Text;

namespace KapeIR.Core.Services.Help;

/// <summary>Builder operator help — embedded Markdown sections (not shipped in Triage UI).</summary>
public static class OperatorHelpContent
{
    public sealed record Section(string Id, string Title, string BodyMarkdown);

    private static readonly (string ResourceFile, string Title)[] Catalog =
    {
        ("01-overview.md", "Обзор"),
        ("02-builder.md", "Builder"),
        ("03-triage.md", "Triage и режимы"),
        ("04-estimate.md", "Оценка объёма"),
        ("05-results.md", "Папка RESULTS"),
        ("06-chainsaw-hayabusa.md", "Chainsaw / Hayabusa"),
        ("07-wrapup-files.md", "Файлы после сбора"),
        ("08-checklist.md", "Чеклист"),
    };

    private static IReadOnlyList<Section>? _cached;

    public static IReadOnlyList<Section> GetSections()
    {
        if (_cached is not null)
            return _cached;

        var asm = typeof(OperatorHelpContent).Assembly;
        var list = new List<Section>(Catalog.Length);
        foreach (var (file, title) in Catalog)
        {
            var body = ReadEmbedded(asm, file);
            var id = Path.GetFileNameWithoutExtension(file);
            list.Add(new Section(id, title, body));
        }

        _cached = list;
        return _cached;
    }

    /// <summary>Case-insensitive filter by title or markdown body.</summary>
    public static IReadOnlyList<Section> Filter(string? query)
    {
        var all = GetSections();
        if (string.IsNullOrWhiteSpace(query))
            return all;

        var q = query.Trim();
        return all
            .Where(s =>
                s.Title.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                s.BodyMarkdown.Contains(q, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private static string ReadEmbedded(Assembly asm, string fileName)
    {
        var name = asm.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("." + fileName, StringComparison.OrdinalIgnoreCase)
                                 || n.EndsWith(fileName, StringComparison.OrdinalIgnoreCase));
        if (name is null)
            return $"_Раздел не найден: {fileName}_";

        using var stream = asm.GetManifestResourceStream(name);
        if (stream is null)
            return $"_Не удалось открыть: {fileName}_";

        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd().Trim();
    }
}
