using System.Globalization;
using System.Xml;
using Cronos;
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
            RecurringScheduleKind.Cron => ComputeNextCron(expression, after),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown recurring schedule kind.")
        };
    }

    /// <summary>Parses the interval spec: an ISO-8601 duration (e.g. <c>PT5M</c>) or an invariant <see cref="TimeSpan"/> string.</summary>
    public static TimeSpan ParseInterval(string expression)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expression);

        // ISO-8601 durations start with 'P'; anything else is treated as a plain TimeSpan for authoring convenience.
        TimeSpan interval;
        if (expression.StartsWith('P') || expression.StartsWith("-P", StringComparison.Ordinal))
            interval = XmlConvert.ToTimeSpan(expression);
        else if (!TimeSpan.TryParse(expression, CultureInfo.InvariantCulture, out interval))
            throw new FormatException($"Interval expression '{expression}' is neither an ISO-8601 duration nor a TimeSpan.");

        if (interval <= TimeSpan.Zero)
            throw new FormatException($"Interval expression '{expression}' must be a positive duration.");

        return interval;
    }

    private static DateTimeOffset? ComputeNextCron(string expression, DateTimeOffset after)
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
        return cron.GetNextOccurrence(after, TimeZoneInfo.Utc, inclusive: false);
    }

    private static int CountFields(string expression) =>
        expression.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length;
}
