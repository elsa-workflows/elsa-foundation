using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Models;

namespace Elsa.Activities.Scheduling.Activities;

/// <summary>
/// A recurring interval start trigger (W16). Authored as a start trigger, it starts a <i>new</i> workflow
/// instance every <see cref="Interval"/> — the recurring counterpart to <see cref="Delay"/> (which suspends and
/// resumes one existing run). Publishing a workflow whose start trigger is a <see cref="Timer"/> records both a
/// trigger binding (so the recurring-trigger pump's stimulus routes to a start) and a recurring schedule (so the
/// pump knows when to fire); the hosted pump then starts an instance on each due occurrence.
/// </summary>
/// <remarks>
/// <para>
/// The <see cref="Interval"/> is an authored literal — an ISO-8601 duration (e.g. <c>PT5M</c>) or a
/// <see cref="TimeSpan"/> string (e.g. <c>00:05:00</c>) — because a start trigger's schedule is fixed at publish
/// time, before any run exists. The interval is anchored to each fire (next = fired-at + interval), so it is a
/// drift-tolerant "every N", not a wall-clock-aligned cron; use <see cref="Cron"/> for calendar alignment.
/// </para>
/// <para>
/// Fires <b>at most once</b> per due window: a pump that wakes after downtime advances to the next occurrence
/// and fires once rather than replaying the elapsed backlog. When a run is started by the timer the activity
/// surfaces the interval as its result so the run is observable.
/// </para>
/// <para>
/// Scope note: this ships the START form only. Delivering per-fire start input (e.g. the scheduled instant) is a
/// named follow-up shared with the other start triggers ("activity trigger start-input delivery").
/// </para>
/// </remarks>
[TriggerActivity]
public sealed class Timer : Activity<TimerResult>
{
    /// <summary>The stable activity type key the trigger/schedule providers match on.</summary>
    public const string ActivityType = "Elsa.Timer";

    public Timer() => Type = ActivityType;

    /// <summary>
    /// The recurrence interval, as an authored literal: an ISO-8601 duration (<c>PT5M</c>) or a
    /// <see cref="TimeSpan"/> string (<c>00:05:00</c>). Drives the stimulus hash and the recurring schedule.
    /// </summary>
    [ActivityInput(Key = nameof(Interval))]
    [Required]
    public string Interval { get; set; } = null!;

    protected override ValueTask<ActivityTransition<TimerResult>> ExecuteAsync(ActivityExecutionContext context) =>
        ValueTask.FromResult(ActivityTransition.Complete(new TimerResult(Interval)));
}

/// <summary>The atomic result produced when a recurring timer starts a workflow.</summary>
public sealed record TimerResult(
    [property: Output(Key = "Result", Path = "interval")] string Interval);
