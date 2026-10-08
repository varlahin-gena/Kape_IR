namespace KapeIR.Core.Services;

/// <summary>
/// Combines <see cref="CollectPackProgressParser"/> + <see cref="CollectionProgressState"/>
/// for GUI/silent progress. During silent KAPE copy (no % lines) stays indeterminate —
/// no fake bar creep.
/// </summary>
public sealed class CollectionProgressFacade
{
    private readonly CollectPackProgressParser _parser = new();
    private readonly CollectionProgressState _state = new();

    public bool IsCopyWaiting { get; private set; }

    public sealed record Snapshot(
        double OverallPercent,
        string Status,
        bool IsIndeterminate,
        bool IsCopyWaiting);

    public Snapshot Current
    {
        get
        {
            if (IsCopyWaiting)
            {
                return new Snapshot(
                    Math.Clamp(_state.OverallPercent, 0, 99),
                    "Копирование…",
                    IsIndeterminate: true,
                    IsCopyWaiting: true);
            }

            return new Snapshot(
                Math.Clamp(_state.OverallPercent, 0, 99),
                Status: "",
                IsIndeterminate: false,
                IsCopyWaiting: false);
        }
    }

    public void Reset()
    {
        _parser.ResetCounters();
        IsCopyWaiting = false;
        _state.BeginPhase(0, 99);
    }

    public Snapshot BeginPhase(double floor, double ceil, string status)
    {
        IsCopyWaiting = false;
        _parser.ResetCounters();
        _state.BeginPhase(floor, ceil);
        return new Snapshot(
            Math.Clamp(_state.OverallPercent, 0, 99),
            status,
            IsIndeterminate: false,
            IsCopyWaiting: false);
    }

    /// <summary>Parse a KAPE/log line; null if it does not affect progress.</summary>
    public Snapshot? ApplyLine(string line)
    {
        var update = _parser.TryParse(line);
        if (update is null) return null;

        if (update.ResetPhase || update.StopCopyPulse)
            IsCopyWaiting = false;

        if (update.StartCopyPulse)
        {
            IsCopyWaiting = true;
            _state.ApplyUpdate(update);
            _parser.SetLocalProgress(update.Local0to100);
            return new Snapshot(
                Math.Clamp(_state.OverallPercent, 0, 99),
                "Копирование…",
                IsIndeterminate: true,
                IsCopyWaiting: true);
        }

        _state.ApplyUpdate(update);
        _parser.SetLocalProgress(update.Local0to100);
        return new Snapshot(
            Math.Clamp(_state.OverallPercent, 0, 99),
            update.Status,
            IsIndeterminate: false,
            IsCopyWaiting: false);
    }

    /// <summary>
    /// Called by the view DispatcherTimer while copy-wait is active.
    /// Does not advance the bar — only reasserts indeterminate + status.
    /// </summary>
    public Snapshot? TickCopyWait()
    {
        if (!IsCopyWaiting) return null;
        return Current with { Status = "Копирование…" };
    }

    public void EndCopyWait() => IsCopyWaiting = false;
}
