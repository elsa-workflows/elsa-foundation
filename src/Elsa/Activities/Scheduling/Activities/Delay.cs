using System.Text.Json;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Activities.Scheduling.Activities;

/// <summary>
/// Suspends one typed invocation for a relative duration and completes when its durable timer fires.
/// Workflow data is a plain hydrated property; timer services are constructor-injected into each
/// transient activation.
/// </summary>
/// <remarks>
/// The timer is persisted before the suspension transition is returned. Its deterministic identity makes
/// retry idempotent, while the activity state and trigger registration are committed atomically by the
/// runtime after this method returns.
/// </remarks>
[ActivityOutcome("Done")]
public sealed class Delay(IDurableTimerScheduler scheduler, TimeProvider timeProvider) :
    StatefulActivity<ActivityUnit, DelayState, DurableTimerElapsed>
{
    public const string TimerTriggerProviderId = "provider.durable-timer";
    private const string ResumeTargetId = "resume-target:durable-timer";

    /// <summary>The relative duration to suspend for, measured from the initial attempt.</summary>
    [ActivityInput(Key = "Duration", DefaultValue = "00:00:00", DefaultSyntax = "Literal")]
    public TimeSpan Duration { get; set; }

    protected override async ValueTask<ActivityTransition<ActivityUnit, DelayState>> ExecuteAsync(
        ActivityExecutionContext context)
    {
        var now = timeProvider.GetUtcNow();
        var dueTime = Duration <= TimeSpan.Zero ? now : now + Duration;
        var timerId = DeriveTimerId(context.InvocationId);
        var trigger = new DurableTimerElapsed(timerId);
        var registration = new ActivityTriggerRegistration<DurableTimerElapsed>(
            ResumeTargetId,
            DurableTimerConstants.TimerStimulusType,
            timerId,
            ActivityTriggerDeduplicationMode.Once);

        await scheduler.ScheduleAsync(
            new DurableTimer(
                TimerId: timerId,
                WorkflowExecutionId: context.WorkflowExecutionId,
                StimulusType: DurableTimerConstants.TimerStimulusType,
                StimulusHash: timerId,
                DueTime: dueTime,
                CreatedAt: now,
                Input: JsonSerializer.SerializeToElement(trigger),
                PayloadType: registration.PayloadType,
                ProviderId: TimerTriggerProviderId),
            context.CancellationToken);

        return Suspend(new DelayState(timerId), [registration]);
    }

    [ResumeTarget(ResumeTargetId)]
    protected override ValueTask<ActivityTransition<ActivityUnit, DelayState>> ResumeAsync(
        ActivityResumeContext<DelayState, DurableTimerElapsed> context)
    {
        if (!StringComparer.Ordinal.Equals(context.State.TimerId, context.Trigger.TimerId))
            throw new InvalidOperationException("The delivered durable timer does not match the committed delay state.");

        return ValueTask.FromResult(Complete(ActivityUnit.Value));
    }

    private static string DeriveTimerId(string invocationId) => $"timer:{invocationId}";
}

/// <summary>The complete private state required to validate a resumed delay.</summary>
public sealed record DelayState(string TimerId);

/// <summary>The typed payload delivered by the durable timer provider.</summary>
public sealed record DurableTimerElapsed(string TimerId);
