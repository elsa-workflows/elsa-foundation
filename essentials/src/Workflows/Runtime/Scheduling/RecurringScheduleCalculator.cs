using Cronos;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Scheduling;

/// <inheritdoc />
public sealed class RecurringScheduleCalculator : IRecurringScheduleCalculator
{
    public DateTimeOffset? ComputeNext(RecurringScheduleKind kind, string expression, DateTimeOffset after)
        => ComputeNext(kind, expression, after, "UTC");

    public DateTimeOffset? ComputeNext(RecurringScheduleKind kind, string expression, DateTimeOffset after, string timeZoneId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expression);

        return kind switch
        {
            RecurringScheduleKind.Interval => after + ParseInterval(expression),
            RecurringScheduleKind.Cron => ComputeNextCron(expression, after, TimeZoneInfo.FindSystemTimeZoneById(timeZoneId)),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown recurring schedule kind.")
        };
    }

    /// <summary>Parses the interval spec: an ISO-8601 duration (e.g. <c>PT5M</c>) or an invariant <see cref="TimeSpan"/> string.</summary>
    public static TimeSpan ParseInterval(string expression) => RecurringScheduleInterval.ParsePositive(expression);

    private static DateTimeOffset? ComputeNextCron(string expression, DateTimeOffset after, TimeZoneInfo timezone)
    {
        var format = CountFields(expression) >= 6 ? CronFormat.IncludeSeconds : CronFormat.Standard;
        CronExpression cron;
        try
        {
            cron = CronExpression.Parse(expression, format);
        }
        catch (CronFormatException exception)
        {
            throw new FormatException($"Cron expression '{expression}' is invalid: {exception.Message}", exception);
        }

        // Non-inclusive so a fire at exactly the cursor advances past it rather than re-selecting the same instant.
        return cron.GetNextOccurrence(after, timezone, inclusive: false);
    }

    private static int CountFields(string expression) =>
        expression.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length;
}
