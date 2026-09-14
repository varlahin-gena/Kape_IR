using System.Text.Json;

namespace KapePack.Core.Services;

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
        catch (Exception ex)
        {
            AppLog.Warn("AppSettings.Load failed: " + ex.Message);
        }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            AppLog.Warn("AppSettings.Save failed: " + ex.Message);
        }
    }

    /// <summary>
    /// Candidates for first launch only. Never walk parent folders of the EXE —
    /// that picks the wrong KAPE tree when several installs sit nearby.
    /// </summary>
    public static IEnumerable<string> CandidateRoots(AppSettings? settings)
    {
        if (!string.IsNullOrWhiteSpace(settings?.LastKapeRoot))
            yield return settings!.LastKapeRoot!;

        var env = Environment.GetEnvironmentVariable("KAPE_ROOT");
        if (!string.IsNullOrWhiteSpace(env))
            yield return env;

        // Only the EXE folder itself (common: Builder dropped into a KAPE root).
        var baseDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        yield return baseDir;
    }

    public static string ResolveDefaultKapeRoot(AppSettings settings)
    {
        foreach (var c in CandidateRoots(settings).Where(c => !string.IsNullOrWhiteSpace(c)))
        {
            if (KapeRootPaths.LooksLikeKapeRoot(c))
            {
                try { return Path.GetFullPath(c.Trim()); }
                catch { return c.Trim(); }
            }
        }

        // Last resort: remembered path or empty — UI will ask user to browse.
        var last = settings.LastKapeRoot?.Trim() ?? "";
        if (string.IsNullOrEmpty(last)) return "";
        try { return Path.GetFullPath(last); }
        catch { return last; }
    }
}
