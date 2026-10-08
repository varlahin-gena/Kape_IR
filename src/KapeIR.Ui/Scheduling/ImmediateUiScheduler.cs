namespace KapeIR.Ui.Scheduling;

/// <summary>Runs marshaled / debounced work inline — for unit tests without a WPF Application.</summary>
public sealed class ImmediateUiScheduler : IUiScheduler
{
    public static ImmediateUiScheduler Instance { get; } = new();

    public bool CheckAccess() => true;

    public Task InvokeAsync(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        action();
        return Task.CompletedTask;
    }

    public void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        action();
    }

    public IUiDebounce CreateDebounce(TimeSpan delay) => new ImmediateDebounce();

    private sealed class ImmediateDebounce : IUiDebounce
    {
        private bool _disposed;

        public void Schedule(Action action)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            ArgumentNullException.ThrowIfNull(action);
            action();
        }

        public void Dispose() => _disposed = true;
    }
}
