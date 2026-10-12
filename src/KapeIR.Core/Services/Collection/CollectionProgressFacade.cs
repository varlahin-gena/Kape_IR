namespace KapeIR.Core.Services;

/// <summary>
/// Combines <see cref="CollectPackProgressParser"/> + <see cref="CollectionProgressState"/>
/// for GUI/silent progress. During silent KAPE copy (no % lines) stays indeterminate —
/// no fake bar creep. Phase label is sticky across action updates within a phase.
/// </summary>
public sealed class CollectionProgressFacade
{
    private readonly CollectPackProgressParser _parser = new();
    private readonly CollectionProgressState _state = new();
    private string _phase = "";
    private string _action = "";

    public bool IsCopyWaiting { get; private set; }

    public sealed record Snapshot(
        double OverallPercent,
        string Phase,
        string Action,
        bool IsIndeterminate,
        bool IsCopyWaiting);

    public Snapshot Current
    {
        get
        {
            if (IsCopyWaiting)
            {
                return MakeSnapshot(
                    phase: _phase,
                    action: "Копирование…",
                    indeterminate: true,
                    copyWaiting: true);
            }

            return MakeSnapshot(_phase, _action, indeterminate: false, copyWaiting: false);
        }
    }

    public void Reset()
    {
        _parser.ResetCounters();
        IsCopyWaiting = false;
        _phase = "";
        _action = "";
        _state.BeginPhase(0, 99);
    }

    public Snapshot BeginPhase(double floor, double ceil, string status)
    {
        IsCopyWaiting = false;
        _parser.ResetCounters();
        _state.BeginPhase(floor, ceil);
        AssignStatus(status);
        return MakeSnapshot(_phase, _action, indeterminate: false, copyWaiting: false);
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
            _action = "Копирование…";
            return MakeSnapshot(_phase, _action, indeterminate: true, copyWaiting: true);
        }

        _state.ApplyUpdate(update);
        _parser.SetLocalProgress(update.Local0to100);
        AssignStatus(update.Status);
        return MakeSnapshot(_phase, _action, indeterminate: false, copyWaiting: false);
    }

    /// <summary>
    /// Called by the view DispatcherTimer while copy-wait is active.
    /// Does not advance the bar — only reasserts indeterminate + status.
    /// </summary>
    public Snapshot? TickCopyWait()
    {
        if (!IsCopyWaiting) return null;
        return Current with { Action = "Копирование…" };
    }

    public void EndCopyWait() => IsCopyWaiting = false;

    private Snapshot MakeSnapshot(string phase, string action, bool indeterminate, bool copyWaiting)
        => new(
            Math.Clamp(_state.OverallPercent, 0, 99),
            phase,
            action,
            indeterminate,
            copyWaiting);

    private void AssignStatus(string status)
    {
        if (string.IsNullOrEmpty(status))
            return;

        if (LooksLikePhase(status))
        {
            _phase = status;
            _action = "";
        }
        else
        {
            _action = status;
        }
    }

    internal static bool LooksLikePhase(string status)
        => status.StartsWith("Фаза", StringComparison.Ordinal);
}
