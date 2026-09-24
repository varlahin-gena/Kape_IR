namespace KapePack.Core.Services;

/// <summary>Maps per-phase local 0–100 progress into overall CollectPack bar range.</summary>
public sealed class CollectionProgressState
{
    public double PhaseFloor { get; private set; }
    public double PhaseCeil { get; private set; } = 99;
    public double LocalProgress { get; private set; }

    public void BeginPhase(double floor, double ceil)
    {
        PhaseFloor = floor;
        PhaseCeil = ceil;
        LocalProgress = 0;
    }

    public void ApplyUpdate(CollectPackProgressParser.ProgressUpdate update)
    {
        if (update.ResetPhase)
        {
            PhaseFloor = update.PhaseFloor ?? PhaseFloor;
            PhaseCeil = update.PhaseCeil ?? PhaseCeil;
            LocalProgress = 0;
        }

        SetLocal(update.Local0to100);
    }

    public void SetLocal(double local0to100)
        => LocalProgress = Math.Clamp(local0to100, 0, 100);

    public double OverallPercent
    {
        get
        {
            var span = Math.Max(0.1, PhaseCeil - PhaseFloor);
            return PhaseFloor + span * LocalProgress / 100.0;
        }
    }
}
