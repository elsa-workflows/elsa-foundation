using Elsa.Tasks.Core;
using Microsoft.Extensions.Logging;

namespace Elsa.Workflows.Runtime.Scheduling;

internal sealed class DurableTimerDeadlineSchedule(DurableTimerWakeup wakeup, Func<TimeSpan> recoveryInterval,
    Func<bool> healthy, TimeProvider clock, ILogger logger) : ITaskSchedule
{
    public IScheduledTaskExecution ScheduleExecution(Func<Task> action) =>
        new Execution(wakeup, recoveryInterval, healthy, clock, logger, action);

    internal sealed class Execution : IScheduledTaskExecution
    {
        private static readonly TimeSpan MaxWait = TimeSpan.FromMilliseconds(uint.MaxValue - 1);
        private readonly object _gate = new();
        private readonly DurableTimerWakeup _wakeup;
        private readonly Func<TimeSpan> _recoveryInterval;
        private readonly Func<bool> _healthy;
        private readonly TimeProvider _clock;
        private readonly ILogger _logger;
        private readonly Func<Task> _action;
        private readonly ITimer _timer;
        private DateTimeOffset _recoveryAt;
        private bool _running;
        private bool _disposed;
        private TaskCompletionSource _idle = Completed();
        internal Task WhenIdle { get { lock (_gate) return _idle.Task; } }

        internal Execution(DurableTimerWakeup wakeup, Func<TimeSpan> recoveryInterval,
            Func<bool> healthy, TimeProvider clock, ILogger logger, Func<Task> action)
        {
            _wakeup = wakeup;
            _recoveryInterval = recoveryInterval;
            _healthy = healthy;
            _clock = clock;
            _logger = logger;
            _action = action;
            _recoveryAt = clock.GetUtcNow(); // Discover persisted deadlines at startup.
            _timer = clock.CreateTimer(Callback, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            _wakeup.Changed += Rearm;
            Rearm();
        }

        private void Rearm()
        {
            lock (_gate)
            {
                if (_disposed || _running) return;
                var wait = NextWakeup() - _clock.GetUtcNow();
                // A positive minimum bounds a due backlog without a zero-delay callback spin.
                wait = wait <= TimeSpan.Zero ? TimeSpan.FromMilliseconds(1)
                    : wait > MaxWait ? MaxWait : TimeSpan.FromMilliseconds(Math.Ceiling(wait.TotalMilliseconds));
                _timer.Change(wait, Timeout.InfiniteTimeSpan);
            }
        }

        private DateTimeOffset NextWakeup() =>
            _healthy() && _wakeup.Snapshot.Deadline is { } deadline && deadline < _recoveryAt
                ? deadline : _recoveryAt;

        private void Callback(object? state)
        {
            lock (_gate)
            {
                if (_disposed || _running) return;
                if (NextWakeup() > _clock.GetUtcNow()) { Rearm(); return; }
                _running = true;
                _idle = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            }
            _ = RunAsync();
        }

        private async Task RunAsync()
        {
            try { await _action(); }
            catch (Exception exception) { _logger.LogError(exception, "Unhandled exception in durable timer task"); }
            finally
            {
                lock (_gate)
                {
                    _running = false;
                    try
                    {
                        var interval = _recoveryInterval();
                        var now = _clock.GetUtcNow();
                        interval = interval > TimeSpan.Zero ? interval : TimeSpan.FromMilliseconds(1);
                        _recoveryAt = interval >= DateTimeOffset.MaxValue - now ? DateTimeOffset.MaxValue : now.Add(interval);
                        Rearm();
                    }
                    catch (Exception exception)
                    {
                        _logger.LogError(exception, "Could not rearm the durable timer task");
                    }
                    finally { _idle.TrySetResult(); }
                }
            }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed) return;
                _disposed = true;
                _wakeup.Changed -= Rearm;
                _timer.Dispose();
            }
        }

        public async ValueTask DisposeAsync()
        {
            Task idle;
            lock (_gate) { Dispose(); idle = _idle.Task; }
            await _timer.DisposeAsync();
            await idle;
        }

        private static TaskCompletionSource Completed()
        {
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            completion.SetResult();
            return completion;
        }
    }
}
