using KapeIR.Core.Services;

namespace KapeIR.Builder.Tests;

public sealed class CollectionProgressFacadeTests
{
    [Fact]
    public void BeginningCopy_GoesIndeterminate_WithoutCreepingOnTick()
    {
        var f = new CollectionProgressFacade();
        f.BeginPhase(0, 99, "Сбор…");

        var start = f.ApplyLine("Beginning copy of files…");
        Assert.NotNull(start);
        Assert.True(start!.IsIndeterminate);
        Assert.True(start.IsCopyWaiting);
        Assert.True(f.IsCopyWaiting);
        var atStart = start.OverallPercent;

        var tick1 = f.TickCopyWait();
        var tick2 = f.TickCopyWait();
        Assert.NotNull(tick1);
        Assert.NotNull(tick2);
        Assert.True(tick1!.IsIndeterminate);
        Assert.Equal(atStart, tick2!.OverallPercent);
        Assert.Equal("Копирование…", tick2.Action);
    }

    [Fact]
    public void RealCopyCount_EndsWait_AndShowsDeterminate()
    {
        var f = new CollectionProgressFacade();
        f.BeginPhase(0, 99, "Сбор…");
        f.ApplyLine("Found 100 files");
        f.ApplyLine("Beginning copy of files…");
        Assert.True(f.IsCopyWaiting);

        var done = f.ApplyLine("Copied 50 out of 100");
        Assert.NotNull(done);
        Assert.False(done!.IsIndeterminate);
        Assert.False(f.IsCopyWaiting);
        Assert.Contains("50", done.Action);
        Assert.Null(f.TickCopyWait());
    }

    [Fact]
    public void PhaseReset_ClearsCopyWait()
    {
        var f = new CollectionProgressFacade();
        f.BeginPhase(CollectionProgressBands.Phase1aFloor, CollectionProgressBands.Phase1aCeil, "Фаза 1…");
        f.ApplyLine("Beginning copy of files…");
        Assert.True(f.IsCopyWaiting);

        var phase2 = f.ApplyLine("Фаза 2 — disk triage");
        Assert.NotNull(phase2);
        Assert.False(f.IsCopyWaiting);
        Assert.False(phase2!.IsIndeterminate);
        Assert.Contains("2", phase2.Phase);
        Assert.Equal("", phase2.Action);
    }

    [Fact]
    public void Phase_IsSticky_AcrossModuleActions()
    {
        var f = new CollectionProgressFacade();
        f.BeginPhase(CollectionProgressBands.Phase1aFloor, CollectionProgressBands.Phase1aCeil, "Фаза 1…");

        var phase = f.ApplyLine("Фаза 1 — оперативный сбор");
        Assert.NotNull(phase);
        Assert.Contains("1a", phase!.Phase, StringComparison.Ordinal);
        Assert.Equal("", phase.Action);

        f.ApplyLine("Discovered 29 processors");
        var modules = f.ApplyLine("Running bar.exe");
        Assert.NotNull(modules);
        Assert.Contains("1a", modules!.Phase, StringComparison.Ordinal);
        Assert.Contains("Модули", modules.Action, StringComparison.Ordinal);
    }

    [Fact]
    public void Phase2_ReplacesStickyPhase()
    {
        var f = new CollectionProgressFacade();
        f.BeginPhase(CollectionProgressBands.Phase1aFloor, CollectionProgressBands.Phase1aCeil, "Фаза 1…");
        f.ApplyLine("Фаза 1 — оперативный");
        f.ApplyLine("Found 10 files");

        var phase2 = f.ApplyLine("Фаза 2 — дисковый сбор");
        Assert.NotNull(phase2);
        Assert.Contains("2", phase2!.Phase, StringComparison.Ordinal);
        Assert.Equal("", phase2.Action);
    }

    [Fact]
    public void CopyWait_DoesNotClearPhase()
    {
        var f = new CollectionProgressFacade();
        f.BeginPhase(CollectionProgressBands.Phase1aFloor, CollectionProgressBands.Phase1aCeil, "Фаза 1…");
        f.ApplyLine("Фаза 1 — оперативный");

        var copy = f.ApplyLine("Beginning copy of files…");
        Assert.NotNull(copy);
        Assert.Contains("1a", copy!.Phase, StringComparison.Ordinal);
        Assert.Equal("Копирование…", copy.Action);
        Assert.True(copy.IsIndeterminate);

        var tick = f.TickCopyWait();
        Assert.NotNull(tick);
        Assert.Contains("1a", tick!.Phase, StringComparison.Ordinal);
        Assert.Equal("Копирование…", tick.Action);
    }

    [Fact]
    public void BeginPhase_NonPhaseStatus_GoesToAction()
    {
        var f = new CollectionProgressFacade();
        var snap = f.BeginPhase(0, 99, "Сбор…");
        Assert.Equal("", snap.Phase);
        Assert.Equal("Сбор…", snap.Action);
    }
}
