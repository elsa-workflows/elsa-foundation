using System.Diagnostics;
using Elsa.Tasks.Core;
using Microsoft.Extensions.Logging;

namespace Elsa.Tasks.Schedules;

internal sealed class ScheduledTaskExecution : IScheduledTaskExecution, IDisposable, IAsyncDisposable
{
    /// <summary>The longest due time <see cref="Timer"/> accepts: 4294967294 milliseconds, about 49.7 days.</summary>
    internal static readonly TimeSpan MaxTimerWait = TimeSpan.FromMilliseconds(uint.MaxValue - 1);

    private readonly Func<Task> _action;
    private readonly Timer _timer;
    private readonly ILogger? _logger;
    private long _armedAt;
    private TimeSpan _wait;

    public ScheduledTaskExecution(Func<Task> action, Func<TimeSpan> interval, ILogger? logger = null)
    {
        _action = action;
        Interval = interval;
        _logger = logger;
        _timer = new Timer(Callback, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        ScheduleNextRun();
    }

    /// <summary>What to wait before the next run, read once now and again after every run.</summary>
    internal Func<TimeSpan> Interval { get; }

    /// <summary>
    /// The due time to arm the timer with when <paramref name="remaining"/> is left before the next run, or
    /// <see langword="null"/> when the run is due now. A wait longer than <see cref="MaxTimerWait"/> is armed in parts.
    /// </summary>
    internal static TimeSpan? NextArm(TimeSpan remaining)
    {
        if (remaining == Timeout.InfiniteTimeSpan)
            return Timeout.InfiniteTimeSpan;
        if (remaining <= TimeSpan.Zero)
            return null;

        // Rounded up, because the timer truncates to whole milliseconds and would otherwise fire before the due time.
        var milliseconds = Math.Ceiling(remaining.TotalMilliseconds);
        return milliseconds >= MaxTimerWait.TotalMilliseconds ? MaxTimerWait : TimeSpan.FromMilliseconds(milliseconds);
    }

    private void ScheduleNextRun()
    {
        _armedAt = Stopwatch.GetTimestamp();
        _wait = Interval();
        _timer.Change(NextArm(_wait) ?? TimeSpan.Zero, Timeout.InfiniteTimeSpan);
    }

    private async void Callback(object? state)
    {
        var remaining = _wait == Timeout.InfiniteTimeSpan ? _wait : _wait - Stopwatch.GetElapsedTime(_armedAt);
        if (NextArm(remaining) is { } rest)
        {
            // The timer fired on one part of a wait longer than it can hold: arm the rest without running.
            Rearm(() => _timer.Change(rest, Timeout.InfiniteTimeSpan));
            return;
        }

        try
        {
            await _action();
        }
        catch (Exception e)
        {
            // Swallow exception to prevent async void from crashing the process.
            // Log unhandled exceptions here as a safeguard; calling code may have its own exception handling.
            _logger?.LogError(e, "Unhandled exception in scheduled timer action");
        }
        finally
        {
            Rearm(ScheduleNextRun);
        }
    }

    // Runs on the timer thread of an async void callback, so nothing may escape it.
    private void Rearm(Action arm)
    {
        try
        {
            arm();
        }
        catch (ObjectDisposedException)
        {
            // Timer was disposed, ignore.
        }
        catch (Exception e)
        {
            _logger?.LogError(e, "Could not schedule the next run of the scheduled timer action; it will not run again");
        }
    }

    public void Dispose()
    {
        _timer.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        await _timer.DisposeAsync();
    }
}
