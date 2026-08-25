using System.Text.Json;

namespace KapePackBuilder.Services;

public sealed class AppSettings
{
    public string? LastKapeRoot { get; set; }

    private static string SettingsPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "KapePackBuilder", "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath)) ?? new AppSettings();
        }
        catch { /* ignore */ }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* ignore */ }
    }

    public static IEnumerable<string> CandidateRoots(AppSettings? settings)
    {
        if (!string.IsNullOrWhiteSpace(settings?.LastKapeRoot))
            yield return settings!.LastKapeRoot!;

        var env = Environment.GetEnvironmentVariable("KAPE_ROOT");
        if (!string.IsNullOrWhiteSpace(env))
            yield return env;

        var baseDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        yield return baseDir;

        var parent = Directory.GetParent(baseDir)?.FullName;
        if (!string.IsNullOrEmpty(parent))
            yield return parent;

        var grand = string.IsNullOrEmpty(parent) ? null : Directory.GetParent(parent)?.FullName;
        if (!string.IsNullOrEmpty(grand))
            yield return grand;

        yield return Directory.GetCurrentDirectory();
    }

    public static string ResolveDefaultKapeRoot(AppSettings settings)
    {
        foreach (var c in CandidateRoots(settings).Where(c => !string.IsNullOrWhiteSpace(c)))
        {
            if (Directory.Exists(Path.Combine(c, "Targets")))
                return c;
        }

        // Last resort: remembered path or empty — UI will ask user to browse.
        return settings.LastKapeRoot?.Trim() ?? "";
    }
}
