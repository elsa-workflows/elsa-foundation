using Elsa.Primitives.Contracts;
using Elsa.Tasks.Schedules;
using Microsoft.Extensions.Logging.Abstractions;

namespace Elsa.Tasks.Tests;

/// <summary>The schedules a recurring task that is not a backoff sweep returns from <c>GetSchedule</c>.</summary>
public sealed class TaskScheduleTests
{
    private static readonly ISystemClock Clock = new FixedClock(new DateTimeOffset(2026, 1, 1, 0, 0, 59, TimeSpan.Zero));

    [Fact]
    public async Task Every_waits_the_interval_before_each_run()
    {
        await using var execution = TaskSchedule.Every(TimeSpan.FromMinutes(5), NullLogger.Instance).ScheduleExecution(NoWork);

        Assert.Equal(TimeSpan.FromMinutes(5), ScheduledExecutions.NextWait(execution)());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Every_refuses_an_interval_that_is_not_positive(int seconds) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => TaskSchedule.Every(TimeSpan.FromSeconds(seconds), NullLogger.Instance));

    [Fact]
    public async Task Cron_waits_until_the_next_occurrence_after_the_clocks_now()
    {
        await using var execution = TaskSchedule.Cron("*/15 * * * *", Clock, NullLogger.Instance).ScheduleExecution(NoWork);

        // 00:00:59 to 00:15:00.
        Assert.Equal(new TimeSpan(0, 14, 1), ScheduledExecutions.NextWait(execution)());
    }

    [Theory]
    [InlineData("every minute")]
    [InlineData("0 * * * * *")] // six fields: seconds are not part of the standard format
    public void Cron_refuses_an_expression_that_is_not_five_field_cron(string expression) =>
        Assert.ThrowsAny<FormatException>(() => TaskSchedule.Cron(expression, Clock, NullLogger.Instance));

    [Fact]
    public void Cron_refuses_an_expression_that_never_occurs() =>
        Assert.Throws<ArgumentException>(() => TaskSchedule.Cron("0 0 30 2 *", Clock, NullLogger.Instance));

    private static Task NoWork() => Task.CompletedTask;

    private sealed class FixedClock(DateTimeOffset utcNow) : ISystemClock
    {
        public DateTimeOffset UtcNow => utcNow;
    }
}
