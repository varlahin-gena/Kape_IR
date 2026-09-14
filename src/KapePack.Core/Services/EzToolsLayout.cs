namespace KapePack.Core.Services;

/// <summary>
/// Get-ZimmermanTools installs into Modules\bin\net{N}\. KAPE resolves module binaries from
/// Modules\bin (root) or Modules\&lt;ModuleName&gt; — not netN. Promote net folder contents to bin root.
/// </summary>
public static class EzToolsLayout
{
    /// <summary>Prefer newest net runtime folder under Modules\bin (net9 &gt; net8 &gt; …).</summary>
    public static string? FindPreferredNetDir(string modulesBin)
    {
        if (!Directory.Exists(modulesBin))
            return null;

        try
        {
            return Directory.EnumerateDirectories(modulesBin, "net*")
                .Select(d => (Path: d, Ver: TryParseNetVersion(Path.GetFileName(d))))
                .Where(x => x.Ver is not null)
                .OrderByDescending(x => x.Ver)
                .Select(x => x.Path)
                .FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    private static int? TryParseNetVersion(string? folderName)
    {
        if (string.IsNullOrEmpty(folderName) ||
            folderName.Length < 4 ||
            !folderName.StartsWith("net", StringComparison.OrdinalIgnoreCase))
            return null;
        return int.TryParse(folderName.AsSpan(3), out var n) && n > 0 ? n : null;
    }

    public static bool IsNetRuntimeFolder(string directoryName)
        => TryParseNetVersion(directoryName) is not null;

    /// <summary>
    /// Copy files/dirs from preferred net{N} into Modules\bin root so kape.exe finds parsers.
    /// Existing root files are overwritten by net copies (EZ update wins). Skips nested net* folders.
    /// </summary>
    /// <returns>Number of files copied.</returns>
    public static int PromoteNetFolderToBinRoot(string modulesBin, IProgress<string>? progress = null)
    {
        var netDir = FindPreferredNetDir(modulesBin);
        return netDir is null ? 0 : CopyNetRuntimeIntoBinRoot(netDir, modulesBin, progress);
    }

    /// <summary>Copy a specific net{N} tree into Modules\bin root (does not nest as netN\).</summary>
    public static int CopyNetRuntimeIntoBinRoot(
        string netDir,
        string modulesBin,
        IProgress<string>? progress = null)
    {
        if (!Directory.Exists(netDir))
            return 0;

        var label = Path.GetFileName(netDir);
        progress?.Report($"Раскладка {label} → Modules\\bin (KAPE ищет парсеры в корне bin)…");

        var copied = 0;
        foreach (var dir in Directory.EnumerateDirectories(netDir, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(netDir, dir);
            if (rel.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    .Any(IsNetRuntimeFolder))
                continue;
            Directory.CreateDirectory(Path.Combine(modulesBin, rel));
        }

        foreach (var file in Directory.EnumerateFiles(netDir, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(netDir, file);
            var parts = rel.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (parts.Any(IsNetRuntimeFolder))
                continue;

            var dest = Path.Combine(modulesBin, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(file, dest, overwrite: true);
            copied++;
        }

        if (copied > 0)
            progress?.Report($"Раскладка {label}: скопировано {copied:N0} файлов в корень Modules\\bin");

        return copied;
    }

    /// <summary>
    /// Delete Modules\bin\net{N} folders after promote so packages are not ~2× size
    /// (tools already live at bin root for KAPE).
    /// </summary>
    public static int RemoveNetRuntimeFolders(string modulesBin, IProgress<string>? progress = null)
    {
        if (!Directory.Exists(modulesBin))
            return 0;

        var removed = 0;
        foreach (var dir in Directory.EnumerateDirectories(modulesBin, "net*").ToList())
        {
            if (!IsNetRuntimeFolder(Path.GetFileName(dir)))
                continue;
            try
            {
                progress?.Report($"Удаление дубля {Path.GetFileName(dir)} из Modules\\bin…");
                Directory.Delete(dir, recursive: true);
                removed++;
            }
            catch (Exception ex)
            {
                progress?.Report($"Не удалось удалить {dir}: {ex.Message}");
            }
        }

        return removed;
    }

    /// <summary>True if relative path under Modules\bin is inside a net{N} folder.</summary>
    public static bool IsUnderNetRuntimeFolder(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            return false;
        var parts = relativePath.Split(
            new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar, '/', '\\' },
            StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 0 && IsNetRuntimeFolder(parts[0]);
    }

    /// <summary>True when a key binary is visible to KAPE (bin root, not only under netN).</summary>
    public static string? FindKapeVisibleBinary(string modulesBin, string fileName)
    {
        if (!Directory.Exists(modulesBin))
            return null;

        var direct = Path.Combine(modulesBin, fileName);
        if (File.Exists(direct))
            return direct;

        try
        {
            foreach (var dir in Directory.EnumerateDirectories(modulesBin))
            {
                var name = Path.GetFileName(dir);
                if (IsNetRuntimeFolder(name))
                    continue;
                if (name.Equals("chainsaw", StringComparison.OrdinalIgnoreCase))
                    continue;

                var candidate = Path.Combine(dir, fileName);
                if (File.Exists(candidate))
                    return candidate;
            }
        }
        catch
        {
            /* ignore */
        }

        return null;
    }
}
