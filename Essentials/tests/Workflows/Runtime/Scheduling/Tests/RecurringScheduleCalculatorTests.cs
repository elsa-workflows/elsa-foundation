using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Workflows.Runtime.Scheduling.Tests;

public sealed class RecurringScheduleCalculatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 1, 12, 0, 0, TimeSpan.Zero);
    private readonly RecurringScheduleCalculator _calculator = new();

    [Fact]
    public void Interval_Iso8601_ReturnsAfterPlusInterval()
    {
        var next = _calculator.ComputeNext(RecurringScheduleKind.Interval, "PT5M", Now);

        Assert.Equal(Now.AddMinutes(5), next);
    }

    [Fact]
    public void Interval_TimeSpanString_ReturnsAfterPlusInterval()
    {
        var next = _calculator.ComputeNext(RecurringScheduleKind.Interval, "00:00:30", Now);

        Assert.Equal(Now.AddSeconds(30), next);
    }

    [Fact]
    public void Interval_NeverReplaysBacklog_AdvancesFromNow_NotFromPastAnchor()
    {
        // The pump passes "now" as after; an interval always advances one interval past now, so a long downtime
        // yields a single next occurrence rather than a queue of missed ones (missed-occurrence policy).
        var afterLongDowntime = Now.AddHours(10);

        var next = _calculator.ComputeNext(RecurringScheduleKind.Interval, "PT1M", afterLongDowntime);

        Assert.Equal(afterLongDowntime.AddMinutes(1), next);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("PT0S")]
    [InlineData("-PT5M")]
    [InlineData("not-a-duration")]
    public void Interval_InvalidOrNonPositive_Throws(string expression)
    {
        Assert.ThrowsAny<Exception>(() => _calculator.ComputeNext(RecurringScheduleKind.Interval, expression, Now));
    }

    [Fact]
    public void Cron_FiveField_ReturnsNextMinuteBoundary()
    {
        // Every minute; first occurrence strictly after 12:00:00 is 12:01:00.
        var next = _calculator.ComputeNext(RecurringScheduleKind.Cron, "* * * * *", Now);

        Assert.Equal(Now.AddMinutes(1), next);
    }

    [Fact]
    public void Cron_SixField_EnablesSeconds()
    {
        // Every 30 seconds; first occurrence strictly after 12:00:00 is 12:00:30.
        var next = _calculator.ComputeNext(RecurringScheduleKind.Cron, "0,30 * * * * *", Now);

        Assert.Equal(Now.AddSeconds(30), next);
    }

    [Fact]
    public void Cron_IsStrictlyAfter_SoAFireAtCursorAdvancesPastIt()
    {
        // At exactly a matching instant the next occurrence must be the following one, never the same instant,
        // so the pump's claim-advance cannot loop on one occurrence.
        var next = _calculator.ComputeNext(RecurringScheduleKind.Cron, "* * * * *", Now);

        Assert.True(next > Now);
    }

    [Fact]
    public void Cron_Invalid_Throws()
    {
        Assert.Throws<FormatException>(() => _calculator.ComputeNext(RecurringScheduleKind.Cron, "not a cron", Now));
    }

    [Fact]
    public void Cron_Exhausted_ReturnsNull()
    {
        // The 30th of February never occurs, so Cronos reports no future occurrence.
        var next = _calculator.ComputeNext(RecurringScheduleKind.Cron, "0 0 30 2 *", Now);

        Assert.Null(next);
    }
}
