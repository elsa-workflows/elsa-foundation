using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Models;

namespace Elsa.Activities.Primitives.Activities;

/// <summary>
/// A minimal named-event start trigger (W7, E3-1). Authored with <see cref="ActivityExecutionType.Trigger"/>,
/// it lets an external event start a workflow with no explicit execution id: the publish-time trigger extractor
/// reads its <see cref="EventName"/> and records a durable trigger binding keyed by the event's stimulus, and
/// the stimulus router starts a new instance when a matching event arrives.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately minimal per the W7 ruling: the trigger is keyed by event name plus an optional passive
/// correlation value, with no payload schema or filters. When a workflow is started by the event, the activity
/// returns the event name in one atomic result so the run is observable.
/// </para>
/// <para>
/// Scope note: this ships the START half of an event trigger (E3-1's acceptance: "an event-driven workflow
/// starts from a stimulus"), which is W7's approved scope. A mid-flow WAIT form (suspend until a named event
/// arrives) is a straightforward follow-up now that the publisher compiles resume targets (W8): add a
/// <c>[ResumeTarget]</c> resume path to this activity. It is intentionally out of W7's start-only scope. The
/// cross-execution fan-in resume path (E3-5) is exercised through the stimulus router against waiting bookmarks.
/// </para>
/// </remarks>
[TriggerActivity]
public sealed class Event : Activity<EventResult>
{
    /// <summary>The stable activity type key the trigger extractor's provider matches on.</summary>
    public const string ActivityType = "Elsa.Event";

    public Event() => Type = ActivityType;

    /// <summary>The name of the event that starts (or targets) the workflow. Drives the stimulus hash.</summary>
    [ActivityInput(Key = nameof(EventName))]
    [Required]
    public string EventName { get; set; } = null!;

    /// <summary>An optional passive correlation value threaded through routing; it does not own a correlation subsystem.</summary>
    [ActivityInput(Key = nameof(CorrelationId))]
    public string? CorrelationId { get; set; }

    protected override ValueTask<ActivityTransition<EventResult>> ExecuteAsync(ActivityExecutionContext context) =>
        ValueTask.FromResult(ActivityTransition.Complete(new EventResult(EventName)));
}

/// <summary>The atomic result produced when a named event starts a workflow.</summary>
public sealed record EventResult
{
    public EventResult(string eventName) => EventName = eventName;

    /// <summary>The routed event name.</summary>
    [Output(Key = "Result", Path = "eventName")]
    public string EventName { get; }
}
