using System.Text;

namespace KapeIR.Triage.ViewModels;

/// <summary>Timestamped run journal (buffer + UI line formatting).</summary>
public sealed class TriageLogJournal
{
    private readonly StringBuilder _buffer = new();

    public string FullText => _buffer.ToString();

    public bool HasContent => _buffer.Length > 0;

    /// <summary>
    /// Append a stamped line to the buffer.
    /// Returns the stamped fragment (including CRLF) for UI append, or null if skipped.
    /// </summary>
    public string? Append(string message)
    {
        if (string.IsNullOrEmpty(message)) return null;
        var stamp = DateTime.Now.ToString("HH:mm:ss");
        var line = $"[{stamp}] {message}";
        _buffer.AppendLine(line);
        return line + "\r\n";
    }

    public void Clear() => _buffer.Clear();

    /// <summary>Single-line banner detail (max ~220 chars); multiline → first line + ellipsis.</summary>
    public static string TrimBanner(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return "Подробности — в журнале.";
        var oneLine = message.Replace("\r\n", "\n").Trim();
        var first = oneLine.Split('\n')[0].Trim();
        if (first.Length > 220)
            return first[..217] + "…";
        if (oneLine.Contains('\n', StringComparison.Ordinal))
            return first + "…";
        return first;
    }
}
