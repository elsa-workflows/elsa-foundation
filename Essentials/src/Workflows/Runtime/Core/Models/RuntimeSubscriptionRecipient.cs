using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>One root's versioned identity, independent of artifact replacement and shared material.</summary>
public sealed class RuntimeSubscriptionStartIdentity
{
    public const int CurrentVersion = 1;

    [JsonConstructor]
    public RuntimeSubscriptionStartIdentity(int version, PersistenceScope scope, string sourceOutboxItemId, string subscriptionId)
    {
        if (version != CurrentVersion) throw new NotSupportedException($"Subscription start identity version '{version}' is unsupported.");
        ArgumentNullException.ThrowIfNull(scope);
        RuntimeDeliveryFingerprint.Text(scope.Value, nameof(scope));
        RuntimeDeliveryFingerprint.Text(sourceOutboxItemId, nameof(sourceOutboxItemId));
        RuntimeDeliveryFingerprint.Text(subscriptionId, nameof(subscriptionId));
        Version = version; Scope = scope; SourceOutboxItemId = sourceOutboxItemId; SubscriptionId = subscriptionId;
    }

    public int Version { get; }
    public PersistenceScope Scope { get; }
    public string SourceOutboxItemId { get; }
    public string SubscriptionId { get; }
    [JsonIgnore] public string Digest => RuntimeDeliveryFingerprint.Compute("elsa.runtime.subscription-start.v1", this);
    [JsonIgnore] public string WorkflowExecutionId => "wfexec:ingress:v1:" + Digest;
    [JsonIgnore] public string CommandId => "command:ingress:v1:" + Digest;
    [JsonIgnore] public string EnvelopeId => "envelope:ingress:v1:" + Digest;
    [JsonIgnore] public string InputId => "input:ingress:v1:" + Digest;
    [JsonIgnore] public string IdempotencyKey => "start:ingress:v1:" + Digest;
    public bool IsEquivalentTo(RuntimeSubscriptionStartIdentity? other) => other is not null &&
        Version == other.Version && Scope == other.Scope && SourceOutboxItemId == other.SourceOutboxItemId && SubscriptionId == other.SubscriptionId;
}

/// <summary>Complete exact published target and normalized input capture for one standalone subscription.</summary>
public sealed class RuntimeSubscriptionRecipient
{
    [JsonConstructor]
    public RuntimeSubscriptionRecipient(string recipientId, string subscriptionId, long subscriptionRevision, long grantRevision,
        string logicalDefinitionId, string modelKey, string slotId, long slotRevision, string activationId,
        WorkflowExecutableIdentity material, WorkflowExecutableSourceProvenance sourceProvenance,
        string inputContractFingerprint, long mappingRevision, int mappingVersion,
        IReadOnlyDictionary<string, JsonElement> inputs, WorkflowExecutionAuthoritySnapshot authority,
        string? tenantId, WorkflowExecutionPartition partition, RuntimeSubscriptionStartIdentity startIdentity,
        RuntimeExecutionProtocol? modelProtocol = null, string? modelProfile = null, string? materialIncarnation = null)
    {
        foreach (var text in new[] { recipientId, subscriptionId, logicalDefinitionId, modelKey, slotId, activationId })
            RuntimeDeliveryFingerprint.Text(text, nameof(text));
        if (subscriptionRevision <= 0) throw new ArgumentOutOfRangeException(nameof(subscriptionRevision));
        if (grantRevision <= 0) throw new ArgumentOutOfRangeException(nameof(grantRevision));
        if (slotRevision <= 0) throw new ArgumentOutOfRangeException(nameof(slotRevision));
        if (mappingRevision <= 0) throw new ArgumentOutOfRangeException(nameof(mappingRevision));
        if (mappingVersion <= 0) throw new ArgumentOutOfRangeException(nameof(mappingVersion));
        ArgumentNullException.ThrowIfNull(material); ArgumentNullException.ThrowIfNull(sourceProvenance);
        foreach (var text in new[] { material.ArtifactId, material.DefinitionId, material.DefinitionVersionId,
                     material.ArtifactVersion, material.ArtifactHash, sourceProvenance.SourceReferenceId,
                     sourceProvenance.SourceKind, sourceProvenance.SourceId, sourceProvenance.DefinitionId,
                     sourceProvenance.DefinitionVersionId, sourceProvenance.ArtifactVersion }) RuntimeDeliveryFingerprint.Text(text, nameof(text));
        if (sourceProvenance.SlotId != slotId || sourceProvenance.PublicationId != activationId || sourceProvenance.DefinitionId != logicalDefinitionId)
            throw new ArgumentException("Captured publication provenance must name the logical target, slot and activation.", nameof(sourceProvenance));
        RuntimeDeliveryFingerprint.Hash(inputContractFingerprint, nameof(inputContractFingerprint));
        ArgumentNullException.ThrowIfNull(inputs); ArgumentNullException.ThrowIfNull(authority);
        ArgumentNullException.ThrowIfNull(partition); ArgumentNullException.ThrowIfNull(startIdentity);
        if (startIdentity.SubscriptionId != subscriptionId) throw new ArgumentException("Start identity must name this subscription.", nameof(startIdentity));
        if (tenantId is not null) RuntimeDeliveryFingerprint.Text(tenantId, nameof(tenantId));
        if ((modelProtocol is null) != (modelProfile is null) || (modelProtocol is null) != (materialIncarnation is null))
            throw new ArgumentException("A model capture requires its protocol, profile and material incarnation together.", nameof(modelProtocol));
        if (modelProfile is not null) RuntimeDeliveryFingerprint.Text(modelProfile, nameof(modelProfile));
        if (materialIncarnation is not null) RuntimeDeliveryFingerprint.Text(materialIncarnation, nameof(materialIncarnation));
        var snapshot = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var pair in inputs)
        {
            RuntimeDeliveryFingerprint.Text(pair.Key, nameof(inputs));
            if (pair.Value.ValueKind == JsonValueKind.Undefined) throw new ArgumentException("Captured workflow inputs must be defined.", nameof(inputs));
            if (!snapshot.TryAdd(pair.Key, pair.Value.Clone())) throw new ArgumentException("Captured workflow input names must be unique.", nameof(inputs));
        }
        RecipientId = recipientId; SubscriptionId = subscriptionId; SubscriptionRevision = subscriptionRevision;
        GrantRevision = grantRevision; LogicalDefinitionId = logicalDefinitionId; ModelKey = modelKey; SlotId = slotId;
        SlotRevision = slotRevision; ActivationId = activationId; Material = material; SourceProvenance = sourceProvenance;
        InputContractFingerprint = inputContractFingerprint; MappingRevision = mappingRevision; MappingVersion = mappingVersion;
        Inputs = new ReadOnlyDictionary<string, JsonElement>(snapshot); Authority = authority; TenantId = tenantId;
        Partition = partition; StartIdentity = startIdentity; ModelProtocol = modelProtocol; ModelProfile = modelProfile;
        MaterialIncarnation = materialIncarnation;
        _ = Fingerprint;
    }

    public string RecipientId { get; }
    public string SubscriptionId { get; }
    public long SubscriptionRevision { get; }
    public long GrantRevision { get; }
    public string LogicalDefinitionId { get; }
    public string ModelKey { get; }
    public string SlotId { get; }
    public long SlotRevision { get; }
    public string ActivationId { get; }
    public WorkflowExecutableIdentity Material { get; }
    public WorkflowExecutableSourceProvenance SourceProvenance { get; }
    public string InputContractFingerprint { get; }
    public long MappingRevision { get; }
    public int MappingVersion { get; }
    public IReadOnlyDictionary<string, JsonElement> Inputs { get; }
    public WorkflowExecutionAuthoritySnapshot Authority { get; }
    public string? TenantId { get; }
    public WorkflowExecutionPartition Partition { get; }
    public RuntimeSubscriptionStartIdentity StartIdentity { get; }
    public RuntimeExecutionProtocol? ModelProtocol { get; }
    public string? ModelProfile { get; }
    public string? MaterialIncarnation { get; }
    [JsonIgnore] public string Fingerprint => RuntimeDeliveryFingerprint.Compute("elsa.runtime.subscription-recipient.v1", this);
    public bool IsEquivalentTo(RuntimeSubscriptionRecipient? other) => other is not null && Fingerprint == other.Fingerprint;
}

/// <summary>A known evaluated refusal, never an accepted target or an unknown provider failure.</summary>
public sealed class RuntimeSubscriptionRefusal
{
    [JsonConstructor]
    public RuntimeSubscriptionRefusal(string subscriptionId, long subscriptionRevision, string stage, string reasonCode)
    {
        RuntimeDeliveryFingerprint.Text(subscriptionId, nameof(subscriptionId));
        RuntimeDeliveryFingerprint.Text(stage, nameof(stage)); RuntimeDeliveryFingerprint.Text(reasonCode, nameof(reasonCode));
        if (subscriptionRevision <= 0) throw new ArgumentOutOfRangeException(nameof(subscriptionRevision));
        SubscriptionId = subscriptionId; SubscriptionRevision = subscriptionRevision; Stage = stage; ReasonCode = reasonCode;
    }
    public string SubscriptionId { get; }
    public long SubscriptionRevision { get; }
    public string Stage { get; }
    public string ReasonCode { get; }
}
