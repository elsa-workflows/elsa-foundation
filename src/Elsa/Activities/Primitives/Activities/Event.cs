using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Constants;

namespace Elsa.Activities.Primitives.Activities;

/// <summary>
/// A named-event trigger with a dual role (W7 start half; spec 116 adds the mid-flow wait form):
/// as a start trigger, the publish-time trigger extractor reads its <see cref="EventName"/> and records a
/// durable trigger binding keyed by the event's stimulus so the stimulus router starts a new instance when a
/// matching event arrives; scheduled mid-flow, it suspends with a typed trigger registration on the same
/// stimulus identity and completes when the named event is raised (the message/signal catch child for BPMN
/// intermediate catch events).
/// </summary>
/// <remarks>
/// <para>
/// Deliberately minimal per the W7 ruling: the trigger is keyed by event name plus an optional authored
/// correlation value, with no payload schema or filters. On a mid-flow wait, a nonblank correlation is retained
/// as bookmark metadata so correlated delivery narrows resume fan-in; null or blank preserves broadcast behavior.
/// When a workflow is started by the event, the activity returns the event name in one atomic result so the run
/// is observable.
/// </para>
/// <para>
/// Role selection mirrors <c>HttpEndpoint</c>: the activity completes when the run's start trigger delivery
/// targeted this node, or on direct invocation while <see cref="CanStartWorkflow"/> is <c>true</c> (the
/// default, preserving the activity's start-first identity). In every other case it suspends until the named
/// event stimulus resumes it. The cross-execution fan-in resume path (E3-5) is exercised through the stimulus
/// router against waiting bookmarks.
/// </para>
/// </remarks>
[TriggerActivity]
public sealed class Event : StatefulTriggerActivity<EventResult, EventWaitState, EventReceived>
{
    /// <summary>The stable activity type key the trigger extractor's provider matches on.</summary>
    public const string ActivityType = "Elsa.Event";

    /// <summary>The activity-local resume-target id of the mid-flow wait path.</summary>
    public const string ResumeTargetId = "resume-target:event";

    /// <summary>The name of the event that starts (or resumes) the workflow. Drives the stimulus hash.</summary>
    [ActivityInput(Key = nameof(EventName))]
    [Required]
    public string EventName { get; set; } = null!;

    /// <summary>
    /// An optional correlation value retained by mid-flow waits for exact resume matching. Null or blank keeps
    /// the wait unscoped; this does not filter start-trigger fan-out or introduce a separate correlation subsystem.
    /// </summary>
    [ActivityInput(Key = nameof(CorrelationId))]
    public string? CorrelationId { get; set; }

    /// <summary>
    /// Whether this node may start a workflow. Defaults to <c>true</c> (start-first identity); a mid-flow
    /// catch node sets it to <c>false</c> so a direct invocation waits instead of completing and the
    /// publish-time trigger extractor skips it.
    /// </summary>
    [ActivityInput(Key = nameof(CanStartWorkflow), DefaultValue = "true")]
    public bool CanStartWorkflow { get; set; } = true;

    protected override ValueTask<ActivityTransition<EventResult, EventWaitState>> ExecuteAsync(
        ActivityStartContext<EventReceived> context)
    {
        if (ShouldComplete(context))
            return ValueTask.FromResult(Complete(new EventResult(EventName)));

        var correlationId = string.IsNullOrWhiteSpace(CorrelationId) ? null : CorrelationId.Trim();
        var metadata = correlationId is null
            ? null
            : new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [RuntimeMetadataKeys.CorrelationId] = correlationId
            };
        var registration = new ActivityTriggerRegistration<EventReceived>(
            ResumeTargetId,
            EventStimulus.StimulusType,
            EventStimulus.Hash(EventName),
            metadata: metadata);
        return ValueTask.FromResult(Suspend(new EventWaitState(EventName), [registration]));
    }

    [ResumeTarget(ResumeTargetId)]
    protected override ValueTask<ActivityTransition<EventResult, EventWaitState>> ResumeAsync(
        ActivityResumeContext<EventWaitState, EventReceived> context) =>
        ValueTask.FromResult(Complete(new EventResult(context.State.EventName)));

    private bool ShouldComplete(ActivityStartContext<EventReceived> context)
    {
        if (!context.IsDirectInvocation)
            return context.IsTriggerDelivery;

        return CanStartWorkflow;
    }
}

/// <summary>The atomic result produced when a named event starts or resumes a workflow.</summary>
public sealed record EventResult
{
    public EventResult(string eventName) => EventName = eventName;

    /// <summary>The routed event name.</summary>
    [Output(Key = "Result", Path = "eventName")]
    public string EventName { get; }
}

/// <summary>The complete private state of a mid-flow event wait.</summary>
public sealed record EventWaitState(string EventName);

/// <summary>The typed payload delivered when a matching named event resumes a waiting <see cref="Event"/>.</summary>
public sealed record EventReceived(string? EventName = null);
