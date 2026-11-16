using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace Elsa.Workflows.Runtime.Core.StandaloneTriggers;

/// <summary>The single live association used by trigger and workflow management surfaces.</summary>
public sealed class TriggerSubscription
{
    [JsonConstructor]
    public TriggerSubscription(string id, string triggerId, string definitionId, string slotName, string modelKey,
        long revision, long mappingRevision, bool enabled, IReadOnlyList<TriggerInputMapping> mappings,
        TriggerManagementOrigin origin, TriggerInvocationGrant? grant = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(triggerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(definitionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(slotName);
        ArgumentException.ThrowIfNullOrWhiteSpace(modelKey);
        if (id.Length > 128 || triggerId.Length > 128)
            throw new ArgumentException("A trigger association identity exceeds its supported length.");
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(revision);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(mappingRevision);
        ArgumentNullException.ThrowIfNull(mappings);
        ArgumentNullException.ThrowIfNull(origin);
        if (mappings.Any(mapping => mapping is null) ||
            mappings.Select(mapping => mapping.InputName).Distinct(StringComparer.Ordinal).Count() != mappings.Count)
            throw new ArgumentException("Input mappings must have distinct, nonnull destinations.", nameof(mappings));
        if (grant is not null && (grant.TriggerId != triggerId || grant.SubscriptionId != id ||
            grant.DefinitionId != definitionId || grant.SlotName != slotName || grant.ModelKey != modelKey))
            throw new ArgumentException("The invocation grant must belong to this exact resource and target.", nameof(grant));
        Id = id; TriggerId = triggerId; DefinitionId = definitionId; SlotName = slotName; ModelKey = modelKey;
        Revision = revision; MappingRevision = mappingRevision; Enabled = enabled;
        Mappings = new ReadOnlyCollection<TriggerInputMapping>(mappings.ToArray()); Origin = origin; Grant = grant;
    }

    public string Id { get; }
    public string TriggerId { get; }
    public string DefinitionId { get; }
    public string SlotName { get; }
    public string ModelKey { get; }
    public long Revision { get; }
    public long MappingRevision { get; }
    public bool Enabled { get; }
    public IReadOnlyList<TriggerInputMapping> Mappings { get; }
    public TriggerManagementOrigin Origin { get; }
    public TriggerInvocationGrant? Grant { get; }
}

public enum TriggerGrantStatus { Granted, Revoked }

/// <summary>Resource-owned invocation authority. Granting actor identity is audit history, not an ongoing user credential.</summary>
public sealed class TriggerInvocationGrant
{
    [JsonConstructor]
    public TriggerInvocationGrant(string id, string triggerId, string subscriptionId, string definitionId,
        string slotName, string modelKey, string sourceTypeKey, long revision, TriggerGrantStatus status,
        string grantedBy, DateTimeOffset grantedAt, string? revokedBy = null, DateTimeOffset? revokedAt = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(triggerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(subscriptionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(definitionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(slotName);
        ArgumentException.ThrowIfNullOrWhiteSpace(modelKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceTypeKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(grantedBy);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(revision);
        if (!Enum.IsDefined(status)) throw new ArgumentOutOfRangeException(nameof(status));
        if (status == TriggerGrantStatus.Revoked)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(revokedBy);
            if (revokedAt is null || revokedAt < grantedAt)
                throw new ArgumentException("Revocation requires a timestamp at or after the grant.", nameof(revokedAt));
        }
        else if (revokedBy is not null || revokedAt is not null)
            throw new ArgumentException("An active grant cannot carry a revocation.", nameof(revokedBy));
        Id = id; TriggerId = triggerId; SubscriptionId = subscriptionId; DefinitionId = definitionId;
        SlotName = slotName; ModelKey = modelKey; SourceTypeKey = sourceTypeKey; Revision = revision;
        Status = status; GrantedBy = grantedBy; GrantedAt = grantedAt; RevokedBy = revokedBy; RevokedAt = revokedAt;
    }

    public string Id { get; }
    public string TriggerId { get; }
    public string SubscriptionId { get; }
    public string DefinitionId { get; }
    public string SlotName { get; }
    public string ModelKey { get; }
    public string SourceTypeKey { get; }
    public long Revision { get; }
    public TriggerGrantStatus Status { get; }
    public string GrantedBy { get; }
    public DateTimeOffset GrantedAt { get; }
    public string? RevokedBy { get; }
    public DateTimeOffset? RevokedAt { get; }
}
