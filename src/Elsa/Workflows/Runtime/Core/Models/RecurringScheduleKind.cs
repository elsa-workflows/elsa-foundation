namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>
/// The kind of recurrence a <see cref="RecurringTriggerSchedule"/> follows. Persisted as its integer ordinal
/// (matching every other Groundwork runtime enum), so the member <b>order</b> is the wire contract: append new
/// members at the end and never reorder or remove one without a schema migration. The golden fixture pins the
/// current mapping.
/// </summary>
public enum RecurringScheduleKind
{
    /// <summary>A fixed interval between fires (the <see cref="RecurringTriggerSchedule.Expression"/> is an ISO-8601 duration).</summary>
    Interval,

    /// <summary>A cron expression (the <see cref="RecurringTriggerSchedule.Expression"/> is a Cronos-parseable cron string).</summary>
    Cron
}
