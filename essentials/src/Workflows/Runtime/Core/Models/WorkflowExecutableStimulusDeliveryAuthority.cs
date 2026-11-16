using System.Text.Json;
using System.Text.Json.Serialization;

namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>Compact provenance for one saved start binding on an exact claimed stimulus source.</summary>
/// <remarks>This is data, not an independent authorization token. Admission must reread the physical source,
/// current owner/fence/expiry, saved cohort and authoritative scope before using the selected publication.</remarks>
[JsonConverter(typeof(WorkflowExecutableStimulusDeliveryAuthorityJsonConverter))]
public sealed class WorkflowExecutableStimulusDeliveryAuthority
{
    [JsonConstructor]
    public WorkflowExecutableStimulusDeliveryAuthority(string sourceOutboxItemId, string intentFingerprint,
        PersistenceScope scope, string ownerId, long fencingToken, string triggerBindingId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceOutboxItemId);
        ArgumentException.ThrowIfNullOrWhiteSpace(intentFingerprint);
        if (intentFingerprint.Length != 64 || intentFingerprint.Any(character => character is not (>= '0' and <= '9' or >= 'a' and <= 'f')))
            throw new ArgumentException("Saved-start provenance requires the complete canonical intent SHA256.", nameof(intentFingerprint));
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        if (fencingToken <= 0) throw new ArgumentOutOfRangeException(nameof(fencingToken));
        WorkflowTriggerBinding.ValidateId(triggerBindingId);
        SourceOutboxItemId = sourceOutboxItemId; IntentFingerprint = intentFingerprint; Scope = scope;
        OwnerId = ownerId; FencingToken = fencingToken; TriggerBindingId = triggerBindingId;
    }

    public string SourceOutboxItemId { get; }
    public string IntentFingerprint { get; }
    public PersistenceScope Scope { get; }
    public string OwnerId { get; }
    public long FencingToken { get; }
    public string TriggerBindingId { get; }
}

internal sealed class WorkflowExecutableStimulusDeliveryAuthorityJsonConverter : JsonConverter<WorkflowExecutableStimulusDeliveryAuthority>
{
    public WorkflowExecutableStimulusDeliveryAuthorityJsonConverter() { }
    public override WorkflowExecutableStimulusDeliveryAuthority Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var wire = JsonSerializer.Deserialize<Wire>(ref reader, options) ?? throw new JsonException("Saved-start source provenance must be an object.");
        try { return new(wire.SourceOutboxItemId, wire.IntentFingerprint, wire.Scope, wire.OwnerId, wire.FencingToken, wire.TriggerBindingId); }
        catch (ArgumentException exception) { throw new JsonException("Saved-start source provenance is invalid.", exception); }
    }

    public override void Write(Utf8JsonWriter writer, WorkflowExecutableStimulusDeliveryAuthority value, JsonSerializerOptions options) =>
        JsonSerializer.Serialize(writer, new Wire(value.SourceOutboxItemId, value.IntentFingerprint, value.Scope,
            value.OwnerId, value.FencingToken, value.TriggerBindingId), options);

    private sealed record Wire([property: JsonRequired] string SourceOutboxItemId,
        [property: JsonRequired] string IntentFingerprint, [property: JsonRequired] PersistenceScope Scope,
        [property: JsonRequired] string OwnerId, [property: JsonRequired] long FencingToken,
        [property: JsonRequired] string TriggerBindingId);
}
