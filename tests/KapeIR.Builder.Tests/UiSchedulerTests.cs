using KapeIR.Ui.Scheduling;

namespace KapeIR.Builder.Tests;

public sealed class UiSchedulerTests
{
    [Fact]
    public async Task Immediate_InvokeAndPost_RunInline()
    {
        var ui = ImmediateUiScheduler.Instance;
        var n = 0;
        Assert.True(ui.CheckAccess());
        await ui.InvokeAsync(() => n++);
        ui.Post(() => n++);
        Assert.Equal(2, n);
    }

    [Fact]
    public void Immediate_Debounce_RunsImmediately()
    {
        var ui = ImmediateUiScheduler.Instance;
        using var debounce = ui.CreateDebounce(TimeSpan.FromSeconds(30));
        var n = 0;
        debounce.Schedule(() => n++);
        debounce.Schedule(() => n++);
        Assert.Equal(2, n);
    }

    [Fact]
    public void Immediate_Debounce_ThrowsAfterDispose()
    {
        var debounce = ImmediateUiScheduler.Instance.CreateDebounce(TimeSpan.FromMilliseconds(1));
        debounce.Dispose();
        Assert.Throws<ObjectDisposedException>(() => debounce.Schedule(() => { }));
    }
}
