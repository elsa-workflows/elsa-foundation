using Elsa.Jobs.Core;
using Elsa.Tasks.Schedules;

namespace Elsa.Jobs;

/// <summary>Pure UTC adapter; the provider owns due time, occurrence identities, cursors and missed-work policy.</summary>
internal sealed class JobRecurrenceCalculator : IJobRecurrenceCalculator
{
    public DateTimeOffset? Next(JobRecurrence recurrence, DateTimeOffset after)
    {
        ArgumentNullException.ThrowIfNull(recurrence);
        try
        {
            var utc = after.ToUniversalTime();
            return recurrence.Kind switch
            {
                JobRecurrenceKind.Interval => NextInterval(UtcRecurrence.ParseInterval(recurrence.Expression), utc),
                JobRecurrenceKind.Cron => UtcRecurrence.NextCron(recurrence.Expression, utc, allowSeconds: true),
                _ => throw new ArgumentOutOfRangeException(nameof(recurrence), "The Jobs recurrence kind is unsupported.")
            };
        }
        catch (Exception error) when (error is FormatException or ArgumentException or OverflowException)
        {
            throw new JobException("jobs.recurrence.invalid", "The Jobs UTC recurrence expression is invalid or unsupported.", error);
        }
    }

    private static DateTimeOffset? NextInterval(TimeSpan interval, DateTimeOffset after) =>
        interval.Ticks > DateTimeOffset.MaxValue.UtcTicks - after.UtcTicks ? null : after + interval;
}
