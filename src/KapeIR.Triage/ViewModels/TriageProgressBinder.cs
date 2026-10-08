using System.Globalization;
using KapeIR.Core.Services;

namespace KapeIR.Triage.ViewModels;

/// <summary>
/// Wraps <see cref="CollectionProgressFacade"/> and prepare-phase status parsing
/// so <see cref="TriageViewModel"/> only applies snapshots to bindable properties.
/// </summary>
public sealed class TriageProgressBinder
{
    private readonly CollectionProgressFacade _progress = new();

    public bool IsCopyWaiting => _progress.IsCopyWaiting;

    public void Reset() => _progress.Reset();

    public void EndCopyWait() => _progress.EndCopyWait();

    public CollectionProgressFacade.Snapshot BeginPhase(double floor, double ceil, string status)
        => _progress.BeginPhase(floor, ceil, status);

    public CollectionProgressFacade.Snapshot? ApplyLine(string line) => _progress.ApplyLine(line);

    public CollectionProgressFacade.Snapshot? TickCopyWait() => _progress.TickCopyWait();

    /// <summary>Interpret a prepare-phase log line (SHA256 / extract %) for status bar + journal.</summary>
    public static PrepareLogEffect InterpretPrepareLog(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return PrepareLogEffect.Empty;

        // "Проверка SHA256… 42%" / "Извлечение архива… 80%"
        var pctIdx = message.LastIndexOf('%');
        if (pctIdx > 0)
        {
            var start = pctIdx - 1;
            while (start >= 0 && (char.IsDigit(message[start]) || message[start] == '.' || message[start] == ' '))
                start--;
            var num = message[(start + 1)..pctIdx].Trim();
            if (double.TryParse(num, NumberStyles.Float, CultureInfo.InvariantCulture, out var pct))
            {
                var label = message;
                var dots = message.IndexOf('…');
                if (dots < 0) dots = message.IndexOf("...", StringComparison.Ordinal);
                if (dots > 0)
                    label = message[..(dots + (message[dots] == '…' ? 1 : 3))].TrimEnd();

                var rounded = (int)Math.Round(pct);
                var journal = rounded is 0 or 100 || rounded % 20 == 0;
                return new PrepareLogEffect(
                    AppendToJournal: journal,
                    JournalMessage: journal ? message : null,
                    StatusText: label,
                    Indeterminate: false,
                    Percent: Math.Clamp(pct, 0, 100));
            }
        }

        var statusWorthy =
            message.StartsWith("Проверка SHA256", StringComparison.Ordinal)
            || message.StartsWith("Извлечение", StringComparison.Ordinal)
            || message.StartsWith("Распаковка", StringComparison.Ordinal)
            || message.StartsWith("Очистка", StringComparison.Ordinal);

        return new PrepareLogEffect(
            AppendToJournal: true,
            JournalMessage: message,
            StatusText: statusWorthy ? message : null,
            Indeterminate: statusWorthy
                ? !message.Contains('%', StringComparison.Ordinal)
                : null,
            Percent: null);
    }

    public readonly record struct PrepareLogEffect(
        bool AppendToJournal,
        string? JournalMessage,
        string? StatusText,
        bool? Indeterminate,
        double? Percent)
    {
        public static PrepareLogEffect Empty { get; } = new(false, null, null, null, null);
    }
}
