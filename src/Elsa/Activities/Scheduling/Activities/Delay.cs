using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Activities.Scheduling.Activities;

/// <summary>
/// Suspends the workflow durably for a relative <see cref="Duration"/>, then resumes on schedule. Ported in
/// spirit from elsa-core's <c>Delay</c> activity and adapted to this repo's durable-timer model: the activity
/// registers a durable timer and creates a matching bookmark, then the hosted timer pump fires the bookmark
/// through the existing resume dispatcher at due time.
/// </summary>
/// <remarks>
/// <para>
/// <b>Ordering invariant.</b> The timer is written strictly <i>before</i> the bookmark is requested, so the
/// dangerous "bookmark committed but timer missing → hang forever" state is structurally excluded: a
/// timer-write failure faults the activity and no bookmark is created.
/// </para>
/// <para>
/// <b>Determinism.</b> The timer id, bookmark id and stimulus hash are all derived from the replay-stable
/// <c>ActivityExecutionId</c>, so a crash-replay re-invoke of the same activity execution upserts the same
/// timer and re-requests the same bookmark rather than creating duplicates.
/// </para>
/// <para>
/// <b>Bookmark expiry.</b> The bookmark is created with no <c>ExpiresAt</c>: the <i>timer</i> owns the
/// deadline, not the bookmark. Setting <c>ExpiresAt = dueTime</c> would make the bookmark unmatchable exactly
/// at fire time (the stimulus lookup filters out bookmarks whose expiry is at/behind the evaluation instant),
/// producing a permanent hang.
/// </para>
/// <para>
/// <b>Durability.</b> Restart-durable resumption requires a durable timer store (the Groundwork store). In a
/// shell configured with the in-memory default store, <c>Delay</c> still suspends and resumes within the
/// process but does <i>not</i> survive a process restart.
/// </para>
/// </remarks>
public sealed class Delay : CodeActivity
{
    /// <summary>The relative duration to suspend for, measured from when the activity executes.</summary>
    public InputArgument<TimeSpan>? Duration { get; set; }

    protected override async ValueTask ExecuteAsync(IActivityExecutionContext context)
    {
        var runtimeContext = RequireRuntimeContext(context);
        var duration = context.Get(Duration);

        var timeProvider = context.GetRequiredService<TimeProvider>();
        var scheduler = context.GetRequiredService<IDurableTimerScheduler>();

        var now = timeProvider.GetUtcNow();
        var dueTime = duration <= TimeSpan.Zero ? now : now + duration;

        var activityExecutionId = runtimeContext.ActivityExecutionState.Execution.ActivityExecutionId;
        var timerId = DeriveTimerId(activityExecutionId);
        var stimulusHash = timerId;
        var bookmarkId = timerId;

        // Timer FIRST — a write failure faults the activity before any bookmark exists (ordering invariant).
        await scheduler.ScheduleAsync(
            new DurableTimer(
                TimerId: timerId,
                WorkflowExecutionId: runtimeContext.WorkflowExecutionId,
                StimulusType: DurableTimerConstants.TimerStimulusType,
                StimulusHash: stimulusHash,
                DueTime: dueTime,
                CreatedAt: now,
                ActivityExecutionId: activityExecutionId,
                ExecutionScopeId: runtimeContext.ActivityExecutionState.ExecutionScopeId ?? runtimeContext.ActivityExecutionState.Provenance.ExecutionScopeId),
            context.CancellationToken);

        // ExpiresAt is intentionally null: the timer owns the deadline, not the bookmark expiry.
        context.CreateBookmark(new ActivityBookmarkRequest(
            bookmarkId: bookmarkId,
            resumeTargetId: ResumeTargetId,
            stimulusType: DurableTimerConstants.TimerStimulusType,
            stimulusHash: stimulusHash,
            expiresAt: null));
    }

    /// <summary>
    /// Runtime resume target invoked by the timer pump's dispatched <c>ResumeBookmark</c>. Completing the
    /// activity is handled by the resume work handler after this returns, so the body is a no-op.
    /// </summary>
    [ResumeTarget(ResumeTargetId)]
    private ValueTask OnTimerElapsedAsync() => ValueTask.CompletedTask;

    private const string ResumeTargetId = "resume-target:durable-timer";

    private static string DeriveTimerId(string activityExecutionId) => $"timer:{activityExecutionId}";

    private static IRuntimeActivityExecutionContext RequireRuntimeContext(IActivityExecutionContext context)
    {
        if (context is IRuntimeActivityExecutionContext runtimeContext)
            return runtimeContext;

        throw new InvalidOperationException("Delay requires an Elsa runtime activity execution context.");
    }
}
