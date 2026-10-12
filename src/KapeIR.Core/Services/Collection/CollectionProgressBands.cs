namespace KapeIR.Core.Services;

/// <summary>
/// Overall progress bands for two-phase IR collection (local 0–100 mapped into these floors/ceils).
/// Overlaps keep the bar from jumping backward when a phase starts.
/// </summary>
public static class CollectionProgressBands
{
    public const double Phase1aFloor = 0;
    public const double Phase1aCeil = 30;

    public const double Phase1bFloor = 25;
    public const double Phase1bCeil = 40;

    public const double Phase2Floor = 35;
    public const double Phase2Ceil = 99;
}
