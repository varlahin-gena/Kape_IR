using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace KapeIR.Core.Services;

/// <summary>
/// Cleans kape.exe / child-tool console lines for the Triage journal:
/// strips ANSI color, recovers UTF-8 banners (Chainsaw box art) mis-decoded as OEM.
/// </summary>
public static class KapeConsoleLineSanitizer
{
    private static readonly Regex AnsiCsi = new(
        @"\x1B\[[0-9;?]*[ -/]*[@-~]|\x1B\][^\x07\x1B]*(?:\x07|\x1B\\)|\x1B[@-Z\\-_]",
        RegexOptions.Compiled);

    /// <summary>
    /// Returns a journal-friendly line. Empty after sanitize → caller may skip.
    /// </summary>
    public static string Sanitize(string? line, Encoding? oemEncoding = null)
    {
        if (string.IsNullOrEmpty(line))
            return line ?? "";

        var s = AnsiCsi.Replace(line, "");
        if (s.Length == 0)
            return s;

        var oem = oemEncoding ?? SafeOemEncoding();
        if (TryRecoverUtf8MisreadAsOem(s, oem, out var recovered) && LooksLikeRecoveredToolBanner(recovered))
            return recovered.TrimEnd();

        return s;
    }

    /// <summary>True when the line is only decorative banner noise (optional skip).</summary>
    public static bool IsDecorativeBanner(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return false;
        var box = 0;
        var other = 0;
        foreach (var c in line)
        {
            if (IsBoxOrBlock(c) || c == ' ')
                box++;
            else if (!char.IsWhiteSpace(c))
                other++;
        }

        return box >= 8 && other <= 2;
    }

    /// <summary>Recovered UTF-8 looks like Chainsaw / box-drawing tool art (not random Cyrillic).</summary>
    internal static bool LooksLikeRecoveredToolBanner(string recovered)
    {
        if (ContainsBoxOrBlock(recovered))
            return true;
        // Chainsaw logo lines after recovery are mostly █╗╚═ and spaces; also credit line.
        if (recovered.Contains("WithSecure", StringComparison.OrdinalIgnoreCase) ||
            recovered.Contains("Countercept", StringComparison.OrdinalIgnoreCase) ||
            recovered.Contains("Chainsaw", StringComparison.OrdinalIgnoreCase))
            return true;
        return false;
    }

    internal static bool TryRecoverUtf8MisreadAsOem(string line, Encoding oem, out string recovered)
    {
        recovered = line;
        try
        {
            var bytes = oem.GetBytes(line);
            // UTF-8 box art / logos use multi-byte sequences starting with 0xE2 (U+2500 block).
            if (bytes.Length < 3 || !bytes.Any(b => b == 0xE2))
                return false;

            recovered = Encoding.UTF8.GetString(bytes);
            if (ReferenceEquals(recovered, line) || recovered == line)
                return false;
            // Reject if recovery produced lots of replacement chars.
            var bad = recovered.Count(c => c == '\uFFFD');
            return bad == 0 && recovered.Length > 0;
        }
        catch
        {
            return false;
        }
    }

    private static bool ContainsBoxOrBlock(string s)
    {
        foreach (var c in s)
        {
            if (IsBoxOrBlock(c))
                return true;
        }

        return false;
    }

    /// <summary>Box Drawing (U+2500+) and Block Elements (U+2580+) used in tool ASCII/UTF-8 logos.</summary>
    private static bool IsBoxOrBlock(char c)
        => c is (>= '\u2500' and <= '\u259F');

    private static Encoding SafeOemEncoding()
    {
        try
        {
            return Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage);
        }
        catch
        {
            return Encoding.Default;
        }
    }
}
