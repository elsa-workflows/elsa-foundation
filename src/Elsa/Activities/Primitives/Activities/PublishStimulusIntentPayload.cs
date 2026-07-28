using System.Text.Json;
using System.Text.Json.Serialization;

namespace Elsa.Activities.Primitives.Activities;

/// <summary>
/// The durable payload of a <c>PublishStimulusIntentKind</c> post-commit intent (spec 135 D1). It carries the resolved
/// stimulus routing pair (<see cref="StimulusType"/>/<see cref="StimulusHash"/>) plus the send's authored facets so the
/// handler can reconstruct the exact <c>StimulusDispatchRequest</c> after commit without re-deriving anything from
/// activity state. Built by the staging buffer (which owns the hash derivation), never by the activity.
/// </summary>
public sealed record PublishStimulusIntentPayload(
    [property: JsonPropertyName("stimulusType")] string StimulusType,
    [property: JsonPropertyName("stimulusHash")] string StimulusHash,
    [property: JsonPropertyName("eventName")] string EventName,
    [property: JsonPropertyName("correlationId")] string? CorrelationId,
    [property: JsonPropertyName("payload")] JsonElement? Payload);
