using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Primitives.Models;

namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>Actual origin of durable work, distinct from workflow checkpoint provenance.</summary>
public enum RuntimeDeliverySourceKind
{
    WorkflowCheckpoint,
    StandaloneIngress
}

public enum RuntimeIngressAdmissionPolicy
{
    RequireAll,
    AcceptAvailable,
    Independent
}

/// <summary>Immutable, physically accepted external origin. It never invents a workflow publisher or checkpoint.</summary>
public sealed class RuntimeStandaloneIngressSource
{
    public const int CurrentVersion = 1;
    public const string StandaloneTriggerDeliveryKind = "Elsa.StandaloneTrigger.Dispatch";

    [JsonConstructor]
    public RuntimeStandaloneIngressSource(int version, string admissionId, string logicalIntentId, DateTimeOffset recordedAt,
        PersistenceScope scope, string? tenantId, WorkflowExecutionPartition partition, string triggerId,
        long triggerRevision, long associationGeneration, string sourceFamily, string sourceKey, string requestFingerprint,
        ValueTypeDescriptor payloadSchema, JsonElement canonicalPayload, RuntimeIngressAdmissionPolicy admissionPolicy,
        WorkflowExecutionAuthoritySnapshot sourceActorAttribution, string deliveryKind = StandaloneTriggerDeliveryKind)
    {
        if (version != CurrentVersion) throw new NotSupportedException($"Standalone ingress source version '{version}' is unsupported.");
        foreach (var text in new[] { admissionId, logicalIntentId, triggerId, sourceFamily, sourceKey, deliveryKind })
            RuntimeDeliveryFingerprint.Text(text, nameof(text));
        ArgumentNullException.ThrowIfNull(scope); ArgumentNullException.ThrowIfNull(partition);
        ArgumentNullException.ThrowIfNull(payloadSchema); ArgumentNullException.ThrowIfNull(sourceActorAttribution);
        RuntimeDeliveryFingerprint.Text(scope.Value, nameof(scope)); RuntimeDeliveryFingerprint.Text(partition.Value, nameof(partition));
        RuntimeDeliveryFingerprint.Text(sourceActorAttribution.SystemIdentity, nameof(sourceActorAttribution));
        RuntimeDeliveryFingerprint.Text(sourceActorAttribution.RootInitiator, nameof(sourceActorAttribution));
        foreach (var pair in sourceActorAttribution.Metadata)
        { RuntimeDeliveryFingerprint.Text(pair.Key, nameof(sourceActorAttribution)); RuntimeDeliveryFingerprint.Text(pair.Value, nameof(sourceActorAttribution), allowEmpty: true); }
        if (tenantId is not null) RuntimeDeliveryFingerprint.Text(tenantId, nameof(tenantId));
        if (recordedAt == default || recordedAt == DateTimeOffset.MaxValue) throw new ArgumentOutOfRangeException(nameof(recordedAt));
        if (triggerRevision <= 0) throw new ArgumentOutOfRangeException(nameof(triggerRevision));
        if (associationGeneration < 0) throw new ArgumentOutOfRangeException(nameof(associationGeneration));
        if (!Enum.IsDefined(admissionPolicy)) throw new ArgumentOutOfRangeException(nameof(admissionPolicy));
        RuntimePostCommitIntent.ValidateKind(deliveryKind, nameof(deliveryKind));
        RuntimeDeliveryFingerprint.Hash(requestFingerprint, nameof(requestFingerprint));
        if (canonicalPayload.ValueKind == JsonValueKind.Undefined) throw new ArgumentException("Ingress payload must be defined.", nameof(canonicalPayload));
        Version = version; AdmissionId = admissionId; LogicalIntentId = logicalIntentId; RecordedAt = recordedAt;
        Scope = scope; TenantId = tenantId; Partition = partition; TriggerId = triggerId; TriggerRevision = triggerRevision;
        AssociationGeneration = associationGeneration; SourceFamily = sourceFamily; SourceKey = sourceKey;
        RequestFingerprint = requestFingerprint; PayloadSchema = payloadSchema; CanonicalPayload = canonicalPayload.Clone();
        AdmissionPolicy = admissionPolicy; SourceActorAttribution = sourceActorAttribution; DeliveryKind = deliveryKind;
        // Eager validation rejects duplicate JSON fields and a wire that cannot preserve the accepted source.
        _ = Fingerprint;
    }

    public int Version { get; }
    public string AdmissionId { get; }
    public string LogicalIntentId { get; }
    public DateTimeOffset RecordedAt { get; }
    public PersistenceScope Scope { get; }
    public string? TenantId { get; }
    public WorkflowExecutionPartition Partition { get; }
    public string TriggerId { get; }
    public long TriggerRevision { get; }
    public long AssociationGeneration { get; }
    public string SourceFamily { get; }
    public string SourceKey { get; }
    public string RequestFingerprint { get; }
    public ValueTypeDescriptor PayloadSchema { get; }
    public JsonElement CanonicalPayload { get; }
    public RuntimeIngressAdmissionPolicy AdmissionPolicy { get; }
    public WorkflowExecutionAuthoritySnapshot SourceActorAttribution { get; }
    public string DeliveryKind { get; }

    [JsonIgnore]
    public string Fingerprint => RuntimeDeliveryFingerprint.Compute("elsa.runtime.standalone-ingress.v1", this);

    [JsonIgnore]
    public string OutboxItemId => BuildOutboxItemId(Scope, TenantId, TriggerId, SourceFamily, SourceKey);

    public static string BuildOutboxItemId(PersistenceScope scope, string? tenantId, string triggerId, string sourceFamily, string sourceKey)
    {
        ArgumentNullException.ThrowIfNull(scope);
        foreach (var text in new[] { scope.Value, triggerId, sourceFamily, sourceKey }) RuntimeDeliveryFingerprint.Text(text, nameof(text));
        if (tenantId is not null) RuntimeDeliveryFingerprint.Text(tenantId, nameof(tenantId));
        return "runtime-ingress:v1:" + RuntimeDeliveryFingerprint.Compute("elsa.runtime.ingress-key.v1", new
        {
            scope = scope.Value,
            tenant = new { present = tenantId is not null, value = tenantId },
            triggerId, sourceFamily, sourceKey
        });
    }

    /// <summary>Identifies submitted intent independently of later target selection, time, configuration and progress.</summary>
    public static string ComputeRequestFingerprint(PersistenceScope scope, string? tenantId, string triggerId, string sourceFamily, string sourceKey,
        ValueTypeDescriptor payloadSchema, JsonElement canonicalPayload, WorkflowExecutionAuthoritySnapshot sourceActorAttribution)
    {
        ArgumentNullException.ThrowIfNull(scope);
        foreach (var text in new[] { scope.Value, triggerId, sourceFamily, sourceKey }) RuntimeDeliveryFingerprint.Text(text, nameof(text));
        if (tenantId is not null) RuntimeDeliveryFingerprint.Text(tenantId, nameof(tenantId));
        ArgumentNullException.ThrowIfNull(payloadSchema); ArgumentNullException.ThrowIfNull(sourceActorAttribution);
        if (canonicalPayload.ValueKind == JsonValueKind.Undefined) throw new ArgumentException("Ingress payload must be defined.", nameof(canonicalPayload));
        return RuntimeDeliveryFingerprint.Compute("elsa.runtime.ingress-request.v1", new
        {
            scope = scope.Value,
            tenant = new { present = tenantId is not null, value = tenantId },
            triggerId, sourceFamily, sourceKey, payloadSchema, canonicalPayload, sourceActorAttribution
        });
    }

    public bool IsEquivalentTo(RuntimeStandaloneIngressSource? other) => other is not null && Fingerprint == other.Fingerprint;
}
