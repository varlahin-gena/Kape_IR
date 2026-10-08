using System.Windows;
using System.Windows.Threading;

namespace KapeIR.Ui.Scheduling;

/// <summary>Production scheduler over <see cref="Application.Current"/>.Dispatcher.</summary>
public sealed class WpfUiScheduler : IUiScheduler
{
    public bool CheckAccess()
    {
        var dispatcher = Application.Current?.Dispatcher;
        return dispatcher is null || dispatcher.CheckAccess();
    }

    public Task InvokeAsync(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
            return Task.CompletedTask;
        }

        return dispatcher.InvokeAsync(action).Task;
    }

    public void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
            return;
        }

        _ = dispatcher.BeginInvoke(action, DispatcherPriority.Normal);
    }

    public IUiDebounce CreateDebounce(TimeSpan delay) => new WpfDebounce(delay);

    private sealed class WpfDebounce : IUiDebounce
    {
        private readonly TimeSpan _delay;
        private DispatcherTimer? _timer;
        private Action? _pending;
        private bool _disposed;

        public WpfDebounce(TimeSpan delay) => _delay = delay <= TimeSpan.Zero
            ? IUiScheduler.DefaultDebounceDelay
            : delay;

        public void Schedule(Action action)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            ArgumentNullException.ThrowIfNull(action);

            _pending = action;
            _timer?.Stop();
            var t = new DispatcherTimer { Interval = _delay };
            _timer = t;
            t.Tick += OnTick;
            t.Start();
        }

        private void OnTick(object? sender, EventArgs e)
        {
            if (_timer is not null)
            {
                _timer.Stop();
                _timer.Tick -= OnTick;
            }

            var action = _pending;
            _pending = null;
            action?.Invoke();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_timer is not null)
            {
                _timer.Stop();
                _timer.Tick -= OnTick;
                _timer = null;
            }

            _pending = null;
        }
    }
}
