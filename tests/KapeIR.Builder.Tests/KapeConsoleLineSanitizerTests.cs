using System.Text;
using KapeIR.Core.Services;

namespace KapeIR.Builder.Tests;

public sealed class KapeConsoleLineSanitizerTests
{
    public KapeConsoleLineSanitizerTests()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    [Fact]
    public void Sanitize_StripsAnsiColor()
    {
        var raw = "\u001b[38;5;11m[!] Loaded 3758 detection rules\u001b[0m";
        var cleaned = KapeConsoleLineSanitizer.Sanitize(raw, Encoding.UTF8);
        Assert.Equal("[!] Loaded 3758 detection rules", cleaned);
    }

    [Fact]
    public void Sanitize_RecoversUtf8BoxDrawingMisreadAsOem()
    {
        var utf8Banner = "┌─── Chainsaw ───┐";
        var oem = Encoding.GetEncoding(866);
        // Simulate Process redirect with OEM decoder on UTF-8 bytes.
        var utf8Bytes = Encoding.UTF8.GetBytes(utf8Banner);
        var mojibake = oem.GetString(utf8Bytes);
        Assert.NotEqual(utf8Banner, mojibake);

        var cleaned = KapeConsoleLineSanitizer.Sanitize(mojibake, oem);
        Assert.Equal(utf8Banner, cleaned);
        Assert.True(KapeConsoleLineSanitizer.IsDecorativeBanner(cleaned) || cleaned.Contains('┌'));
    }

    [Fact]
    public void Sanitize_RecoversBlockElementLogoMisreadAsOem()
    {
        // Chainsaw logo uses U+2588 FULL BLOCK + double-line box chars.
        var utf8Banner = "██████╗██╗  ██╗";
        var oem = Encoding.GetEncoding(866);
        var mojibake = oem.GetString(Encoding.UTF8.GetBytes(utf8Banner));
        var cleaned = KapeConsoleLineSanitizer.Sanitize(mojibake, oem);
        Assert.Equal(utf8Banner, cleaned);
    }

    [Fact]
    public void Sanitize_LeavesNormalOemRussianIntact()
    {
        var oem = Encoding.GetEncoding(866);
        // Typical KAPE English / path line — must not be "recovered" into garbage.
        const string line = "  Expanded --tdest to D:\\test\\RESULTS\\V_HONOR";
        Assert.Equal(line, KapeConsoleLineSanitizer.Sanitize(line, oem));
    }
}
