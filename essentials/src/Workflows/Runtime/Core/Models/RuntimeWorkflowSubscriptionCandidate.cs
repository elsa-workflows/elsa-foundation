using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;

namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>Frozen resource attribution for the complete evaluated association set, including refusals.</summary>
public sealed class RuntimeSubscriptionResourceCapture
{
    [JsonConstructor]
    public RuntimeSubscriptionResourceCapture(string triggerId, long triggerRevision, long configurationRevision,
        long associationRevision, IReadOnlyList<string> subscriptionIds, JsonElement triggerEnvelope)
    {
        RuntimeDeliveryFingerprint.Text(triggerId, nameof(triggerId));
        if (triggerRevision <= 0 || configurationRevision <= 0 || associationRevision < 0 ||
            configurationRevision > triggerRevision || associationRevision > triggerRevision)
            throw new ArgumentException("Resource capture requires coherent aggregate revisions.");
        ArgumentNullException.ThrowIfNull(subscriptionIds);
        var ids = subscriptionIds.Order(StringComparer.Ordinal).ToArray();
        foreach (var id in ids) RuntimeDeliveryFingerprint.Text(id, nameof(subscriptionIds));
        if (ids.Distinct(StringComparer.Ordinal).Count() != ids.Length || triggerEnvelope.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("Resource capture requires distinct association identities and a detached trigger envelope.");
        TriggerId = triggerId; TriggerRevision = triggerRevision; ConfigurationRevision = configurationRevision;
        AssociationRevision = associationRevision; SubscriptionIds = Array.AsReadOnly(ids); TriggerEnvelope = triggerEnvelope.Clone();
    }
    public string TriggerId { get; }
    public long TriggerRevision { get; }
    public long ConfigurationRevision { get; }
    public long AssociationRevision { get; }
    public IReadOnlyList<string> SubscriptionIds { get; }
    public JsonElement TriggerEnvelope { get; }
}

/// <summary>Structured module-owned matching rule over one canonical string configuration field.</summary>
public sealed record RuntimeWorkflowSubscriptionSource(string TriggerTypeKey, int TriggerSchemaVersion, string ConfigurationField, string ConfigurationValue,
    string SourceFamily, ValueTypeDescriptor PayloadSchema, JsonElement Payload, JsonElement Metadata);

/// <summary>Prepared known resource and target observations; infrastructure failures must propagate instead.</summary>
public sealed record RuntimeIngressTriggerObservation(TriggerAggregate Resource, IReadOnlyList<RuntimeIngressTargetObservation> Targets);

/// <summary>One complete first workflow-source selection, installed with its ordinary starts/waits before any recipient effect.</summary>
public sealed record RuntimeWorkflowSubscriptionCandidate(TriggerScope Scope, RuntimeWorkflowSubscriptionSource? Source,
    RuntimeSubscriptionDelivery Delivery, IReadOnlyList<RuntimeIngressTriggerObservation> Triggers);
