namespace KapeIR.Ui.Scheduling;

/// <summary>Deferred UI action; calling <see cref="Schedule"/> again restarts the delay (last wins).</summary>
public interface IUiDebounce : IDisposable
{
    void Schedule(Action action);
}

/// <summary>
/// Abstracts WPF dispatcher marshaling and search debounce so ViewModels stay testable without Application.
/// </summary>
public interface IUiScheduler
{
    /// <summary>Default debounce used by Builder search / root-reload fields.</summary>
    static readonly TimeSpan DefaultDebounceDelay = TimeSpan.FromMilliseconds(180);

    bool CheckAccess();

    /// <summary>Run on the UI thread and await completion (or inline when already on UI / no dispatcher).</summary>
    Task InvokeAsync(Action action);

    /// <summary>Fire-and-forget marshal (BeginInvoke) — or inline when already on UI / no dispatcher.</summary>
    void Post(Action action);

    IUiDebounce CreateDebounce(TimeSpan delay);
}
