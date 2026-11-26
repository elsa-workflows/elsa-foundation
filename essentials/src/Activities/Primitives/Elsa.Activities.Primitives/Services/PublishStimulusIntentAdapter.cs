using System.Text.Json;
using Elsa.Activities.Primitives.Activities;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;

namespace Elsa.Activities.Primitives.Services;

/// <summary>The owning payload decoder for canonical persisted named-event sends; performs no delivery effects.</summary>
internal sealed class PublishStimulusIntentAdapter : IStimulusDeliveryIntentAdapter
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
    internal static readonly StandaloneTriggerSourceDescriptor SourceDescriptor = new("event", 1, "Elsa.Event.Broadcast",
        new ValueTypeDescriptor("Elsa.EventPayload", schemaVersion: 1));
    public PublishStimulusIntentAdapter() { }
    public string IntentKind => PublishStimulusConstants.PublishStimulusIntentKind;

    public StimulusDispatchRequest CreateRequest(RuntimePostCommitIntent intent)
    {
        ArgumentNullException.ThrowIfNull(intent);
        try { return Decode(intent); }
        catch (Exception exception)
        { throw new StimulusDeliveryIntentException(intent.IntentId, intent.Kind, exception); }
    }

    public RuntimeWorkflowSubscriptionSource? CreateSubscriptionSource(RuntimePostCommitIntent intent)
    {
        _ = CreateRequest(intent); // The owning routing decoder validates every persisted field before matching resources.
        var payload = intent.Payload!.Value.Deserialize<PublishStimulusIntentPayload>(Options)!;
        if (payload.IsLocalEvent) return null;
        return new(SourceDescriptor.TypeKey, SourceDescriptor.SchemaVersion, "eventName", payload.EventName, SourceDescriptor.SourceFamily, SourceDescriptor.PayloadSchema,
            payload.Payload ?? JsonSerializer.SerializeToElement<object?>(null),
            JsonSerializer.SerializeToElement(new { eventName = payload.EventName, correlationId = payload.CorrelationId }, Options));
    }

    private StimulusDispatchRequest Decode(RuntimePostCommitIntent intent)
    {
        if (intent.Kind != IntentKind) throw new ArgumentException("The intent is not a named-event send.", nameof(intent));
        if (intent.Payload is not { ValueKind: JsonValueKind.Object } element) throw new JsonException("The named-event send has no object payload.");
        var fields = element.EnumerateObject().ToArray();
        var allowed = new[] { "stimulusType", "stimulusHash", "eventName", "correlationId", "payload", "isLocalEvent" };
        if (fields.Any(field => !allowed.Contains(field.Name, StringComparer.OrdinalIgnoreCase)) ||
            fields.GroupBy(field => field.Name, StringComparer.OrdinalIgnoreCase).Any(group => group.Count() != 1) ||
            allowed.Take(5).Any(name => !fields.Any(field => field.Name.Equals(name, StringComparison.OrdinalIgnoreCase))))
            throw new JsonException("The named-event send has incomplete, unknown or duplicated routing fields.");
        var payload = element.Deserialize<PublishStimulusIntentPayload>(Options) ?? throw new JsonException("The named-event send payload is invalid.");
        ArgumentException.ThrowIfNullOrWhiteSpace(payload.EventName);
        if (payload.StimulusType != EventStimulus.StimulusType || payload.StimulusHash != EventStimulus.Hash(payload.EventName))
            throw new JsonException("The named-event send differs from its staged event identity.");
        return new(payload.StimulusType, payload.StimulusHash, payload.Payload, payload.CorrelationId,
            payload.IsLocalEvent ? StimulusRoutingMode.ResumeOnly : StimulusRoutingMode.StartAndResume, intent.IdempotencyKey,
            PublishStimulusConstants.RequestedBy, targetWorkflowExecutionId: payload.IsLocalEvent ? intent.WorkflowExecutionId : null)
            { ResumeAllMatchingBookmarks = true };
    }
}
