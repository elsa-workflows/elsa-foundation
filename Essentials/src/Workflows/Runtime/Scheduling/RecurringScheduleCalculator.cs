using Elsa.Tasks.Schedules;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Scheduling;

/// <inheritdoc />
public sealed class RecurringScheduleCalculator : IRecurringScheduleCalculator
{
    public DateTimeOffset? ComputeNext(RecurringScheduleKind kind, string expression, DateTimeOffset after)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expression);

        return kind switch
        {
            RecurringScheduleKind.Interval => after + ParseInterval(expression),
            RecurringScheduleKind.Cron => UtcRecurrence.NextCron(expression, after),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown recurring schedule kind.")
        };
    }

    /// <summary>Parses the interval spec: an ISO-8601 duration (e.g. <c>PT5M</c>) or an invariant <see cref="TimeSpan"/> string.</summary>
    public static TimeSpan ParseInterval(string expression) => UtcRecurrence.ParseInterval(expression);
}
