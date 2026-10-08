using System.Net.Http;

namespace KapeIR.Core.Services;

/// <summary>
/// Fills gaps between RegRipper profile lists (system/software/…) and plugins\*.pl on disk.
/// Missing plugins are pulled from keydet89/RegRipper4.0; names that 404 are dropped from profiles
/// so rip.exe no longer emits "Can't locate …pl in @INC" for every hive.
/// </summary>
public static class RegRipperPluginRepair
{
    public const string PluginRawBase =
        "https://raw.githubusercontent.com/keydet89/RegRipper4.0/main/plugins/";

    private static readonly string[] ProfileNames =
    {
        "all", "amcache", "ntuser", "sam", "security", "software", "syscache", "system", "usrclass"
    };

    public static string GetPluginsDir(string kapeRoot)
        => Path.Combine(kapeRoot, "Modules", "bin", "regripper", "plugins");

    public static RegRipperRepairStatus Check(string kapeRoot)
    {
        var pluginsDir = GetPluginsDir(kapeRoot);
        if (!Directory.Exists(pluginsDir))
        {
            return new RegRipperRepairStatus
            {
                Ok = false,
                NeedsRepair = false,
                Message = "RegRipper plugins\\ отсутствует (нужен Modules\\bin\\regripper\\)."
            };
        }

        var missing = CollectMissingPluginNames(pluginsDir);
        return new RegRipperRepairStatus
        {
            Ok = missing.Count == 0,
            NeedsRepair = missing.Count > 0,
            MissingPlugins = missing,
            PluginsDir = pluginsDir,
            Message = missing.Count == 0
                ? $"RegRipper plugins OK ({pluginsDir})"
                : $"RegRipper: в профилях нет на диске {missing.Count} плагин(ов): " +
                  string.Join(", ", missing.Take(8)) +
                  (missing.Count > 8 ? "…" : "")
        };
    }

    public static async Task<RegRipperRepairResult> RepairAsync(
        string kapeRoot,
        IProgress<string>? progress = null,
        CancellationToken ct = default,
        HttpMessageHandler? httpHandler = null)
    {
        var pluginsDir = GetPluginsDir(kapeRoot);
        if (!Directory.Exists(pluginsDir))
        {
            return new RegRipperRepairResult
            {
                Ok = false,
                Message = "Нет Modules\\bin\\regripper\\plugins — сначала положите RegRipper (BinaryUrl)."
            };
        }

        var missing = CollectMissingPluginNames(pluginsDir);
        if (missing.Count == 0)
        {
            return new RegRipperRepairResult
            {
                Ok = true,
                Message = "RegRipper: все плагины из профилей уже на месте."
            };
        }

        progress?.Report($"RegRipper: докачка {missing.Count} недостающих плагинов…");
        using var http = httpHandler is null
            ? new HttpClient { Timeout = TimeSpan.FromMinutes(5) }
            : new HttpClient(httpHandler, disposeHandler: false) { Timeout = TimeSpan.FromMinutes(5) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("KapeIR/1.0");

        var downloaded = new List<string>();
        var notFound = new List<string>();
        foreach (var name in missing)
        {
            ct.ThrowIfCancellationRequested();
            var file = name.EndsWith(".pl", StringComparison.OrdinalIgnoreCase) ? name : name + ".pl";
            var url = PluginRawBase + file;
            try
            {
                using var resp = await http.GetAsync(url, ct).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode)
                {
                    notFound.Add(Path.GetFileNameWithoutExtension(file));
                    continue;
                }

                var bytes = await resp.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
                await File.WriteAllBytesAsync(Path.Combine(pluginsDir, file), bytes, ct).ConfigureAwait(false);
                downloaded.Add(file);
                progress?.Report($"RegRipper: +{file}");
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                notFound.Add(Path.GetFileNameWithoutExtension(file));
            }
        }

        var pruned = 0;
        if (notFound.Count > 0)
        {
            progress?.Report($"RegRipper: удаление {notFound.Count} ссылок из профилей (плагин не найден upstream)…");
            pruned = PruneMissingFromProfiles(pluginsDir, notFound);
        }

        var still = CollectMissingPluginNames(pluginsDir);
        var ok = still.Count == 0;
        var msg =
            $"RegRipper: скачано {downloaded.Count}, убрано из профилей {pruned}" +
            (still.Count > 0 ? $", осталось отсутствующих: {still.Count}" : ".");

        return new RegRipperRepairResult
        {
            Ok = ok,
            Downloaded = downloaded,
            PrunedFromProfiles = pruned,
            Message = msg
        };
    }

    /// <summary>
    /// After selective copy: drop profile lines whose .pl is still missing in the package.
    /// </summary>
    public static int SanitizeProfilesInPlace(string pluginsDir)
    {
        if (!Directory.Exists(pluginsDir))
            return 0;
        var missing = CollectMissingPluginNames(pluginsDir);
        return missing.Count == 0 ? 0 : PruneMissingFromProfiles(pluginsDir, missing);
    }

    public static List<string> CollectMissingPluginNames(string pluginsDir)
    {
        var missing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var profile in ProfileNames)
        {
            var path = Path.Combine(pluginsDir, profile);
            if (!File.Exists(path))
                continue;
            foreach (var line in File.ReadAllLines(path))
            {
                var name = line.Trim();
                if (name.Length == 0 || name.StartsWith('#'))
                    continue;
                var pl = Path.Combine(pluginsDir, name + ".pl");
                if (!File.Exists(pl))
                    missing.Add(name);
            }
        }

        return missing.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static int PruneMissingFromProfiles(string pluginsDir, IEnumerable<string> missingNames)
    {
        var drop = new HashSet<string>(missingNames, StringComparer.OrdinalIgnoreCase);
        var removed = 0;
        foreach (var profile in ProfileNames)
        {
            var path = Path.Combine(pluginsDir, profile);
            if (!File.Exists(path))
                continue;

            var lines = File.ReadAllLines(path);
            var kept = new List<string>(lines.Length);
            var changed = false;
            foreach (var line in lines)
            {
                var name = line.Trim();
                if (name.Length > 0 && !name.StartsWith('#') && drop.Contains(name))
                {
                    removed++;
                    changed = true;
                    continue;
                }

                kept.Add(line);
            }

            if (changed)
                File.WriteAllLines(path, kept);
        }

        return removed;
    }
}

public sealed class RegRipperRepairStatus
{
    public bool Ok { get; init; }
    public bool NeedsRepair { get; init; }
    public string PluginsDir { get; init; } = "";
    public IReadOnlyList<string> MissingPlugins { get; init; } = Array.Empty<string>();
    public string Message { get; init; } = "";
}

public sealed class RegRipperRepairResult
{
    public bool Ok { get; init; }
    public IReadOnlyList<string> Downloaded { get; init; } = Array.Empty<string>();
    public int PrunedFromProfiles { get; init; }
    public string Message { get; init; } = "";
}
