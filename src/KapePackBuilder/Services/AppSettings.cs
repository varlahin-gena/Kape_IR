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

    public static string ResolveDefaultKapeRoot(AppSettings settings)
    {
        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(settings.LastKapeRoot))
            candidates.Add(settings.LastKapeRoot!);

        var baseDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        candidates.Add(baseDir);
        candidates.Add(Directory.GetParent(baseDir)?.FullName ?? "");
        candidates.Add(Directory.GetParent(Directory.GetParent(baseDir)?.FullName ?? "")?.FullName ?? "");
        candidates.Add(@"D:\Distr\HACK\Kape");
        candidates.Add(Directory.GetCurrentDirectory());

        foreach (var c in candidates.Where(c => !string.IsNullOrWhiteSpace(c)))
        {
            if (Directory.Exists(Path.Combine(c, "Targets")))
                return c;
        }
        return @"D:\Distr\HACK\Kape";
    }
}
