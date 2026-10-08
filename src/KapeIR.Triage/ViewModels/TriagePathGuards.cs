namespace KapeIR.Triage.ViewModels;

/// <summary>Path validation for Triage GUI (tsource + RESULTS folder).</summary>
public static class TriagePathGuards
{
    public sealed record PathApplyResult(bool Ok, string? Error, string Tsource, string ResultsRoot);

    public static PathApplyResult TryResolve(
        string selectedTsource,
        string? selectedDriveRoot,
        string? resultsPath)
    {
        var tsource = (selectedTsource ?? "").Trim();
        if (!string.IsNullOrWhiteSpace(selectedDriveRoot))
            tsource = selectedDriveRoot.Trim();

        if (string.IsNullOrWhiteSpace(tsource))
            return new PathApplyResult(false, "Выберите диск-источник для сбора.", "", "");

        var results = (resultsPath ?? "").Trim();
        if (string.IsNullOrWhiteSpace(results))
            return new PathApplyResult(false, "Укажите папку для результатов.", tsource, "");

        try
        {
            results = Path.GetFullPath(results);
        }
        catch (Exception ex)
        {
            return new PathApplyResult(false, "Некорректный путь к папке результатов: " + ex.Message, tsource, "");
        }

        try
        {
            Directory.CreateDirectory(results);
        }
        catch (Exception ex)
        {
            return new PathApplyResult(
                false,
                "Не удалось создать папку результатов:\n" + ex.Message,
                tsource,
                "");
        }

        return new PathApplyResult(true, null, tsource, results);
    }

    public static string? GuessInitialDir(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        try
        {
            var full = Path.GetFullPath(path.Trim());
            if (Directory.Exists(full)) return full;
            var parent = Path.GetDirectoryName(full);
            return Directory.Exists(parent) ? parent : null;
        }
        catch
        {
            return null;
        }
    }

    public static string QuoteCliArg(string s)
        => s.Contains(' ') || s.Contains('%') ? "\"" + s + "\"" : s;
}
