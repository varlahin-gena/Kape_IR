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
        Assert.Equal("Копирование…", tick2.Status);
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
        Assert.Contains("50", done.Status);
        Assert.Null(f.TickCopyWait());
    }

    [Fact]
    public void PhaseReset_ClearsCopyWait()
    {
        var f = new CollectionProgressFacade();
        f.BeginPhase(0, 15, "Фаза 1…");
        f.ApplyLine("Beginning copy of files…");
        Assert.True(f.IsCopyWaiting);

        var phase2 = f.ApplyLine("Фаза 2 — disk triage");
        Assert.NotNull(phase2);
        Assert.False(f.IsCopyWaiting);
        Assert.False(phase2!.IsIndeterminate);
    }
}
