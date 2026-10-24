using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Constants;

namespace Elsa.Activities.Primitives.Activities;

/// <summary>
/// A named-event trigger with a dual role, start trigger and mid-flow wait:
/// as a start trigger, the publish-time trigger extractor reads its <see cref="EventName"/> and records a
/// durable trigger binding keyed by the event's stimulus so the stimulus router starts a new instance when a
/// matching event arrives; scheduled mid-flow, it suspends with a typed trigger registration on the same
/// stimulus identity and completes when the named event is raised (the message/signal catch child for BPMN
/// intermediate catch events).
/// </summary>
/// <remarks>
/// <para>
/// Deliberately minimal: the trigger is keyed by event name plus an optional authored
/// correlation value, with no payload schema or filters. Whatever arrives with the event (a Publish Event payload, the
/// stimulus API's input, a test-run delivery, or nothing) is its payload: any JSON shape is accepted and handed on as
/// the <see cref="EventResult.Payload"/> output (#3107). On a mid-flow wait, a nonblank correlation is retained
/// as bookmark metadata so correlated delivery narrows resume fan-in; null or blank preserves broadcast behavior.
/// When a workflow is started by the event, the activity returns the event name and payload in one atomic result so
/// the run is observable.
/// </para>
/// <para>
/// Role selection mirrors <c>HttpEndpoint</c>: the activity completes when the run's start trigger delivery
/// targeted this node, or on direct invocation while <see cref="CanStartWorkflow"/> is <c>true</c> (the
/// default, preserving the activity's start-first identity). In every other case it suspends until the named
/// event stimulus resumes it. The cross-execution fan-in resume path is exercised through the stimulus
/// router against waiting bookmarks.
/// </para>
/// </remarks>
[RefusesSecretBinding(nameof(EventName), SecretBindingRefusalReason.FixedAtPublish)]
[RefusesSecretBinding(nameof(CorrelationId), SecretBindingRefusalReason.FixedAtPublish)]
[RefusesSecretBinding(nameof(CanStartWorkflow), SecretBindingRefusalReason.FixedAtPublish)]
[Activity(Icon = "Bell")]
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
            return ValueTask.FromResult(Complete(new EventResult(EventName, context.TryGetTrigger(out var trigger) ? trigger.Payload : null)));

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
        ValueTask.FromResult(Complete(new EventResult(context.State.EventName, context.Trigger.Payload)));

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
    public EventResult(string eventName, JsonElement? payload = null)
    {
        EventName = eventName;
        Payload = payload;
    }

    /// <summary>The routed event name.</summary>
    [Output(Key = "Result", Path = "eventName")]
    public string EventName { get; }

    /// <summary>The payload delivered with the event, unchanged; null when the event carried none.</summary>
    [Output(Key = "Payload", Path = "payload", IsRequired = false)]
    public JsonElement? Payload { get; }
}

/// <summary>The complete private state of a mid-flow event wait.</summary>
public sealed record EventWaitState(string EventName);

/// <summary>
/// The typed trigger delivered when a matching named event resumes a waiting <see cref="Event"/> or starts a workflow
/// at one: the event's payload. Its JSON form is the payload itself, so any delivered JSON value, <c>null</c> included,
/// reads as a trigger. The event's name is not part of it; a wait keeps its own in <see cref="EventWaitState"/>.
/// </summary>
[JsonConverter(typeof(EventReceivedJsonConverter))]
public sealed record EventReceived(JsonElement? Payload = null);

/// <summary>Reads any JSON value as an <see cref="EventReceived"/> payload and writes the payload back unchanged.</summary>
internal sealed class EventReceivedJsonConverter : JsonConverter<EventReceived>
{
    public override bool HandleNull => true;

    public override EventReceived Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return new EventReceived();

        using var document = JsonDocument.ParseValue(ref reader);
        return new EventReceived(document.RootElement.Clone());
    }

    public override void Write(Utf8JsonWriter writer, EventReceived value, JsonSerializerOptions options)
    {
        if (value?.Payload is { ValueKind: not (JsonValueKind.Undefined or JsonValueKind.Null) } payload)
            payload.WriteTo(writer);
        else
            writer.WriteNullValue();
    }
}
