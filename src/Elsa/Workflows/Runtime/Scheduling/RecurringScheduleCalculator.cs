using System.Globalization;
using System.Xml;
using Cronos;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Scheduling;

/// <summary>
/// Computes the next occurrence of a <see cref="RecurringTriggerSchedule"/> from its recurrence spec (W16).
/// This is the one place that understands the two <see cref="RecurringScheduleKind"/>s — the schedule indexer
/// uses it to seed the initial <see cref="RecurringTriggerSchedule.NextOccurrence"/> at publish time, and the
/// recurring-trigger pump uses it to advance the cursor after a fire.
/// </summary>
/// <remarks>
/// <para>
/// <b>Interval.</b> An interval schedule has no anchor: the "next occurrence strictly after <c>after</c>" is
/// simply <c>after + interval</c>. That is what makes the missed-occurrence policy hold — when the pump wakes
/// after downtime it advances to one interval past <i>now</i>, never replaying the elapsed backlog.
/// </para>
/// <para>
/// <b>Cron.</b> Cron occurrences are absolute wall-clock instants; the calculator returns the first cron
/// occurrence strictly after <c>after</c> (in UTC), again skipping any occurrences that elapsed while the pump
/// was down. Both 5-field (minute precision) and 6-field (with seconds) cron expressions are supported.
/// </para>
/// </remarks>
public interface IRecurringScheduleCalculator
{
    /// <summary>
    /// Returns the first occurrence strictly after <paramref name="after"/> for the given recurrence spec, or
    /// <c>null</c> for a cron expression that has no further occurrences (e.g. a fixed year in the past).
    /// </summary>
    /// <exception cref="FormatException">The expression is not a valid interval or cron string.</exception>
    DateTimeOffset? ComputeNext(RecurringScheduleKind kind, string expression, DateTimeOffset after);
}

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
