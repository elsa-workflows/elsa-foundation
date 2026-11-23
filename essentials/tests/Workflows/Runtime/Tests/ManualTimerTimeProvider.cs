namespace Elsa.Workflows.Runtime.Tests;

/// <summary>
/// A controllable <see cref="TimeProvider"/> for deterministic tests of background renewal loops: time only moves
/// when the test calls <see cref="Advance"/> or <see cref="AdvanceAndFire"/>, and a pending timer only fires when the
/// test fires it. Overriding <see cref="CreateTimer"/> is what makes it drive
/// <c>Task.Delay(cadence, timeProvider, token)</c> — a clock that only overrides <see cref="GetUtcNow"/> would leave
/// those delays on the wall clock.
/// </summary>
internal sealed class ManualTimerTimeProvider(DateTimeOffset now) : TimeProvider
{
    private readonly object _syncRoot = new();
    private readonly TaskCompletionSource _timerCreated =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private ManualTimer? _pendingTimer;
    private DateTimeOffset _now = now;

    /// <summary>Completes once the code under test has parked on its first timer.</summary>
    public Task TimerCreated => _timerCreated.Task;

    public override DateTimeOffset GetUtcNow() => _now;

    public override ITimer CreateTimer(
        TimerCallback callback,
        object? state,
        TimeSpan dueTime,
        TimeSpan period)
    {
        var timer = new ManualTimer(callback, state);
        lock (_syncRoot)
            _pendingTimer = timer;
        _timerCreated.TrySetResult();
        return timer;
    }

    /// <summary>
    /// Moves the clock without firing the pending timer. Use this to observe state at a virtual instant that the
    /// renewal loop must not act on yet — for example past an original lease expiry but before the renewed one.
    /// </summary>
    public void Advance(TimeSpan delta)
    {
        lock (_syncRoot)
            _now = _now.Add(delta);
    }

    public void AdvanceAndFire(TimeSpan delta)
    {
        ManualTimer timer;
        lock (_syncRoot)
        {
            _now = _now.Add(delta);
            timer = _pendingTimer ?? throw new InvalidOperationException("No timer was created.");
            _pendingTimer = null;
        }

        timer.Fire();
    }

    private sealed class ManualTimer(TimerCallback callback, object? state) : ITimer
    {
        private bool _disposed;

        public bool Change(TimeSpan dueTime, TimeSpan period) => !_disposed;

        public void Fire()
        {
            if (!_disposed)
                callback(state);
        }

        public void Dispose() => _disposed = true;

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
