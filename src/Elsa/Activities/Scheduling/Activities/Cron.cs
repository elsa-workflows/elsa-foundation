using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Models;

namespace Elsa.Activities.Scheduling.Activities;

/// <summary>
/// A recurring cron start trigger (W16). Authored as a start trigger, it starts a <i>new</i> workflow instance
/// on each occurrence of its cron <see cref="Expression"/> — the calendar-aligned counterpart to
/// <see cref="Timer"/> (a drift-tolerant "every N"). Publishing a workflow whose start trigger is a
/// <see cref="Cron"/> records both a trigger binding and a recurring schedule; the hosted recurring-trigger pump
/// starts an instance on each due occurrence.
/// </summary>
/// <remarks>
/// <para>
/// The <see cref="Expression"/> is an authored literal cron string, evaluated in UTC. Five fields use standard
/// cron; six or seven fields enable the seconds field. It is a literal because a start trigger's schedule is
/// fixed at publish time, before any run exists — an expression-bound value has nothing to schedule and fails
/// the publish.
/// </para>
/// <para>
/// Fires <b>at most once</b> per due window: a pump that wakes after downtime advances to the next occurrence
/// and fires once rather than replaying the elapsed backlog. When a run is started by the schedule the activity
/// surfaces the cron expression as its result so the run is observable.
/// </para>
/// <para>
/// Scope note: this ships the START form only. Delivering per-fire start input (e.g. the scheduled instant) is a
/// named follow-up shared with the other start triggers ("activity trigger start-input delivery").
/// </para>
/// </remarks>
[TriggerActivity]
public sealed class Cron : Activity<CronResult>
{
    /// <summary>The stable activity type key the trigger/schedule providers match on.</summary>
    public const string ActivityType = "Elsa.Cron";

    /// <summary>
    /// The cron expression, as an authored literal (UTC). Five fields = standard cron; six/seven fields enable
    /// the seconds field. Drives the stimulus hash and the recurring schedule.
    /// </summary>
    [ActivityInput(Key = nameof(Expression))]
    [Required]
    public string Expression { get; set; } = null!;

    protected override ValueTask<ActivityTransition<CronResult>> ExecuteAsync(ActivityExecutionContext context) =>
        ValueTask.FromResult(ActivityTransition.Complete(new CronResult(Expression)));
}

/// <summary>The atomic result produced when a recurring cron schedule starts a workflow.</summary>
public sealed record CronResult(
    [property: Output(Key = "Result", Path = "expression")] string Expression);
