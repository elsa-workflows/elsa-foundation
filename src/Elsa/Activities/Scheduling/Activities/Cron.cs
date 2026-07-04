using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Contracts;
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
public sealed class Cron : CodeActivity<string>
{
    /// <summary>The stable activity type key the trigger/schedule providers match on.</summary>
    public const string ActivityType = "Elsa.Cron";

    public Cron() : base(ActivityType)
    {
    }

    /// <summary>
    /// The cron expression, as an authored literal (UTC). Five fields = standard cron; six/seven fields enable
    /// the seconds field. Drives the stimulus hash and the recurring schedule.
    /// </summary>
    public InputArgument<string> Expression { get; set; } = null!;

    protected override void Execute(IActivityExecutionContext context)
    {
        var expression = context.Get(Expression);
        context.Set(Result, expression);
    }
}
