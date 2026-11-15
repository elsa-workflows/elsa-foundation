using Cronos;
using Elsa.Tasks.Core;
using Elsa.Tasks.Schedules;
using Elsa.Testing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Elsa.Tasks.Tests;

/// <summary>The schedules a recurring task that is not a backoff sweep returns from <c>GetSchedule</c>.</summary>
public sealed class TaskScheduleTests
{
    private readonly FakeSystemClock _clock = new(new DateTimeOffset(2026, 1, 1, 0, 0, 59, TimeSpan.Zero));
    private readonly RecordingLogger _logger = new();

    [Fact]
    public async Task Every_waits_the_interval_before_each_run()
    {
        await using var execution = TaskSchedule.Every(TimeSpan.FromMinutes(5), NullLogger.Instance).ScheduleExecution(NoWork);

        Assert.Equal(TimeSpan.FromMinutes(5), NextWait(execution));
    }

    // A timer truncates a due time to whole milliseconds, so a shorter interval would run the task back to back.
    [Theory]
    [InlineData(0L)]
    [InlineData(-TimeSpan.TicksPerSecond)]
    [InlineData(1L)]
    [InlineData(999 * TimeSpan.TicksPerMicrosecond)]
    public void Every_refuses_an_interval_shorter_than_a_millisecond(long ticks) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => TaskSchedule.Every(TimeSpan.FromTicks(ticks), NullLogger.Instance));

    [Fact]
    public async Task Every_accepts_an_interval_of_one_millisecond()
    {
        await using var execution = TaskSchedule.Every(TimeSpan.FromMilliseconds(1), NullLogger.Instance).ScheduleExecution(NoWork);

        Assert.Equal(TimeSpan.FromMilliseconds(1), NextWait(execution));
    }

    // System.Threading.Timer refuses a due time above about 49.7 days.
    [Fact]
    public async Task Every_schedules_an_interval_longer_than_a_timer_can_hold()
    {
        await using var execution = TaskSchedule.Every(TimeSpan.FromDays(60), NullLogger.Instance).ScheduleExecution(NoWork);

        Assert.Equal(TimeSpan.FromDays(60), NextWait(execution));
    }

    [Fact]
    public async Task Cron_waits_until_the_next_occurrence_after_the_clocks_now()
    {
        await using var execution = TaskSchedule.Cron("*/15 * * * *", _clock, NullLogger.Instance).ScheduleExecution(NoWork);

        // 00:00:59 to 00:15:00.
        Assert.Equal(new TimeSpan(0, 14, 1), NextWait(execution));
    }

    [Fact]
    public async Task Cron_schedules_a_yearly_expression()
    {
        await using var execution = TaskSchedule.Cron("0 0 1 1 *", _clock, NullLogger.Instance).ScheduleExecution(NoWork);

        // 2026-01-01 00:00:59 to 2027-01-01 00:00:00.
        Assert.Equal(TimeSpan.FromDays(365) - TimeSpan.FromSeconds(59), NextWait(execution));
    }

    [Theory]
    [InlineData("every minute")]
    [InlineData("0 * * * * *")] // six fields: seconds are not part of the standard format
    public void Cron_refuses_an_expression_that_is_not_five_field_cron(string expression) =>
        Assert.ThrowsAny<FormatException>(() => TaskSchedule.Cron(expression, _clock, NullLogger.Instance));

    [Fact]
    public void Cron_refuses_an_expression_that_never_occurs() =>
        Assert.Throws<ArgumentException>(() => TaskSchedule.Cron("0 0 30 2 *", _clock, NullLogger.Instance));

    // TaskSchedule.Cron refuses such an expression up front, so the schedule is built directly.
    [Fact]
    public async Task Cron_with_no_further_occurrence_stops_scheduling_and_says_so()
    {
        var schedule = new CronSchedule(_clock, CronExpression.Parse("0 0 30 2 *"), _logger);
        await using var execution = schedule.ScheduleExecution(NoWork);

        var warning = Assert.Single(_logger.Warnings);
        Assert.Contains("0 0 30 2 *", warning.Message);
        Assert.Equal(Timeout.InfiniteTimeSpan, NextWait(execution));
    }

    [Fact]
    public async Task A_failure_to_schedule_the_next_run_is_logged()
    {
        var reads = 0;
        var ran = new TaskCompletionSource();
        await using var execution = new ScheduledTaskExecution(
            () => { ran.TrySetResult(); return Task.CompletedTask; },
            () => ++reads == 1 ? TimeSpan.FromMilliseconds(1) : throw new InvalidOperationException("no next run"),
            _logger);

        await ran.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var error = await WaitForErrorAsync();

        Assert.IsType<InvalidOperationException>(error.Exception);
    }

    [Fact]
    public void A_wait_longer_than_a_timer_can_hold_is_armed_in_parts() =>
        Assert.Equal(ScheduledTaskExecution.MaxTimerWait, ScheduledTaskExecution.NextArm(TimeSpan.FromDays(60)));

    // A non-null arm means the timer is re-armed instead of the action running.
    [Theory]
    [InlineData(1L)]
    [InlineData(TimeSpan.TicksPerMillisecond)]
    [InlineData(TimeSpan.TicksPerDay * 11)]
    public void A_wait_with_time_left_never_runs_early(long remainingTicks)
    {
        var arm = ScheduledTaskExecution.NextArm(TimeSpan.FromTicks(remainingTicks));

        Assert.NotNull(arm);
        Assert.InRange(arm.Value, TimeSpan.FromTicks(remainingTicks), ScheduledTaskExecution.MaxTimerWait);
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    public void A_wait_with_no_time_left_runs_now(long remainingTicks) =>
        Assert.Null(ScheduledTaskExecution.NextArm(TimeSpan.FromTicks(remainingTicks)));

    [Fact]
    public void A_wait_that_never_ends_never_arms() =>
        Assert.Equal(Timeout.InfiniteTimeSpan, ScheduledTaskExecution.NextArm(Timeout.InfiniteTimeSpan));

    [Fact]
    public async Task A_finite_tick_exactly_one_millisecond_late_runs_and_schedules_its_next_run()
    {
        var clock = new ControlledTimestampProvider();
        var ranTwice = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var actions = 0;
        var intervals = 0;
        await using var execution = new ScheduledTaskExecution(
            () => { if (Interlocked.Increment(ref actions) == 2) ranTwice.TrySetResult(); return Task.CompletedTask; },
            () =>
            {
                if (Interlocked.Increment(ref intervals) == 3) return Timeout.InfiniteTimeSpan;
                // ScheduleNextRun captured the start first. Both real timer callbacks therefore
                // observe 101ms elapsed for their finite 100ms wait, without an OS timing guess.
                clock.Advance(TimeSpan.FromMilliseconds(101));
                return TimeSpan.FromMilliseconds(100);
            }, timeProvider: clock);

        await ranTwice.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(2, Volatile.Read(ref actions));
    }

    [Fact]
    public async Task A_configured_infinite_wait_does_not_run_when_the_clock_advances()
    {
        var clock = new ControlledTimestampProvider();
        var ran = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var execution = new ScheduledTaskExecution(
            () => { ran.TrySetResult(); return Task.CompletedTask; },
            () => Timeout.InfiniteTimeSpan, timeProvider: clock);

        clock.Advance(TimeSpan.FromDays(100));
        var observation = Task.Delay(TimeSpan.FromMilliseconds(200));
        Assert.Same(observation, await Task.WhenAny(ran.Task, observation));
    }

    [Fact]
    public async Task Early_finite_callbacks_keep_waiting_until_the_original_deadline()
    {
        var clock = new ControlledTimestampProvider();
        var ran = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reads = 0;
        await using var execution = new ScheduledTaskExecution(
            () => { ran.TrySetResult(); return Task.CompletedTask; },
            () => Interlocked.Increment(ref reads) == 1 ? TimeSpan.FromMilliseconds(100) : Timeout.InfiniteTimeSpan,
            timeProvider: clock);

        await clock.TwoCallbacks.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(ran.Task.IsCompleted);
        clock.Advance(TimeSpan.FromMilliseconds(101));
        await ran.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    private sealed class ControlledTimestampProvider : TimeProvider
    {
        private long _timestamp;
        private int _reads;
        public TaskCompletionSource TwoCallbacks { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp()
        {
            var value = Interlocked.Read(ref _timestamp);
            if (Interlocked.Increment(ref _reads) == 3) TwoCallbacks.TrySetResult();
            return value;
        }
        public void Advance(TimeSpan elapsed) => Interlocked.Add(ref _timestamp, elapsed.Ticks);
    }

    private async Task<LogEntry> WaitForErrorAsync()
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            if (_logger.Entries.FirstOrDefault(entry => entry.Level == LogLevel.Error) is { } error)
                return error;
            await Task.Delay(10);
        }

        throw new TimeoutException("No error was logged.");
    }

    private static TimeSpan NextWait(IScheduledTaskExecution execution) => Assert.IsType<ScheduledTaskExecution>(execution).Interval();

    private static Task NoWork() => Task.CompletedTask;
}
