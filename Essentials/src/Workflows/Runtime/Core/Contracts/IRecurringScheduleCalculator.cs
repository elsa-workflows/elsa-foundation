using Elsa.Workflows.Runtime.Core.Models;

// Namespace retained for source and binary compatibility when lifting the contract to Runtime.Core.
namespace Elsa.Workflows.Runtime.Scheduling;

/// <summary>
/// Computes the next occurrence of a <see cref="RecurringTriggerSchedule"/> from its recurrence spec.
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
