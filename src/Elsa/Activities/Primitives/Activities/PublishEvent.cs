using System.Text.Json;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Activities.Primitives.Activities;

/// <summary>
/// The message send surface (spec 135): publishes a named-event stimulus <b>durably-first</b> and continues
/// immediately (fire-and-continue). It is the sibling of <see cref="Event"/> — they share the exact
/// <see cref="EventStimulus"/> routing pair (type <c>"Event"</c>, name-only SHA-256 hash), so one send both
/// <b>starts</b> every published workflow whose message start listens on the name and <b>resumes</b> every waiting
/// catch (intermediate, boundary, event subprocess).
/// </summary>
/// <remarks>
/// <para>
/// Execution never calls the stimulus router. It validates the name, stages a publish request on the activity's own
/// commit through <see cref="IPublishStimulusStager"/>, and completes with the <c>Done</c> outcome. After the commit,
/// a post-commit intent handler routes the stimulus (<see cref="StimulusRoutingMode.StartAndResume"/>) with the
/// outbox's retry/poison discipline — so the send is durable and at-least-once, never lost on a crash between
/// completion and delivery.
/// </para>
/// <para>
/// A nonblank <see cref="CorrelationId"/> scopes resume fan-in to same-name Event waits that retained the same
/// authored value (#1001). A null or blank value preserves broadcast delivery, and correlation never filters the
/// published-trigger start fan-out. <see cref="Payload"/> rides as the stimulus input the way the API dispatch
/// surface carries one.
/// </para>
/// </remarks>
[ActivityOutcome(ActivityOutcomes.Done)]
public sealed class PublishEvent(IPublishStimulusStager stager) : Activity<ActivityUnit>
{
    /// <summary>The stable activity type key (mirrors <see cref="Event.ActivityType"/>'s convention).</summary>
    public const string ActivityType = "Elsa.PublishEvent";

    /// <summary>The name of the event to publish. Drives the stimulus hash; required and non-blank.</summary>
    [ActivityInput(Key = nameof(EventName))]
    [Required]
    public string EventName { get; set; } = null!;

    /// <summary>An optional correlation id that scopes matching Event wait resumes; null or blank preserves broadcast delivery.</summary>
    [ActivityInput(Key = nameof(CorrelationId))]
    public string? CorrelationId { get; set; }

    /// <summary>An optional stimulus payload delivered to started/resumed instances as their input.</summary>
    [ActivityInput(Key = nameof(Payload))]
    public JsonElement? Payload { get; set; }

    protected override ValueTask<ActivityTransition<ActivityUnit>> ExecuteAsync(ActivityExecutionContext context)
    {
        var eventName = EventName?.Trim();
        if (string.IsNullOrWhiteSpace(eventName))
            throw new InvalidOperationException("PublishEvent requires a non-blank EventName.");

        var correlationId = string.IsNullOrWhiteSpace(CorrelationId) ? null : CorrelationId.Trim();
        stager.StagePublishStimulus(new PublishStimulusRequest(
            context.WorkflowExecutionId,
            context.InvocationId,
            eventName,
            correlationId,
            Payload));

        return ValueTask.FromResult(ActivityTransition.Complete(ActivityUnit.Value));
    }
}
