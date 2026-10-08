using System.Text.RegularExpressions;

namespace KapeIR.Core.Services;

/// <summary>
/// go-winpmem requires <c>acquire &lt;file&gt;</c>. Stock kapefiles / GitHub sync often ship
/// the legacy mini CLI (<c>"%destinationDirectory%\memory.raw"</c>), which fails with
/// "expected command but got …" and produces no dump.
/// </summary>
public static partial class WinPmemMkapePatch
{
    public const string ModuleFileName = "Velocidex_WinPmem.mkape";
    public const string AcquireCommandLine = @"acquire --progress ""%destinationDirectory%\\memory.raw""";

    /// <summary>Rewrite CommandLine in place when it lacks the <c>acquire</c> subcommand.</summary>
    public static bool TryEnsureAcquireCommandLine(string mkapePath)
    {
        if (string.IsNullOrWhiteSpace(mkapePath) || !File.Exists(mkapePath))
            return false;
        if (!Path.GetFileName(mkapePath).Equals(ModuleFileName, StringComparison.OrdinalIgnoreCase))
            return false;

        var text = File.ReadAllText(mkapePath);
        if (text.Contains("acquire", StringComparison.OrdinalIgnoreCase) &&
            CommandLineAcquireRegex().IsMatch(text))
            return false;

        var updated = CommandLineAnyRegex().Replace(text, $"        CommandLine: {AcquireCommandLine}", 1);
        if (string.Equals(updated, text, StringComparison.Ordinal))
            return false;

        File.WriteAllText(mkapePath, updated);
        return true;
    }

    [GeneratedRegex(
        @"^\s*CommandLine:\s*.*acquire\b.*$",
        RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CommandLineAcquireRegex();

    [GeneratedRegex(
        @"^\s*CommandLine:\s*.+$",
        RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CommandLineAnyRegex();
}
