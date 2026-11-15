namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>One coherent actual outbox claim and scope. Providers still verify physical content and authority.</summary>
public sealed class RuntimeDeliverySourceContext
{
    public RuntimeDeliverySourceContext(RuntimePostCommitOutboxClaim claim, PersistenceScope scope)
    {
        ArgumentNullException.ThrowIfNull(claim); ArgumentNullException.ThrowIfNull(scope);
        var item = claim.Item;
        if (item.Status != RuntimePostCommitOutboxStatus.Delivering || item.DeliveringOwnerId != claim.OwnerId ||
            item.DeliveryFencingToken != claim.FencingToken || item.DeliveryStartedAt != claim.ClaimedAt ||
            item.DeliveryVisibleAfter != claim.VisibleAfter || claim.FencingToken <= 0)
            throw new ArgumentException("Delivery context requires a coherent fenced outbox claim.", nameof(claim));
        if (item.SourceScope is { } committedScope && committedScope != scope)
            throw new ArgumentException("Delivery source belongs to a different committed scope.", nameof(scope));
        Claim = claim; Scope = scope;
    }
    public RuntimePostCommitOutboxClaim Claim { get; }
    public PersistenceScope Scope { get; }
    public string SourceOutboxItemId => Claim.OutboxItemId;
    public string SourceFingerprint => Claim.Item.SourceFingerprint;
    public RuntimeDeliverySourceKind SourceKind => Claim.Item.SourceKind;
    public string DeliveryKind => Claim.Item.DeliveryKind;
    public string LogicalIntentId => Claim.Item.LogicalIntentId;
}

/// <summary>Immutable sealed target origin; no expiring handler claim is carried by a queued target command.</summary>
public sealed class RuntimeCommittedRecipientAuthority
{
    public RuntimeCommittedRecipientAuthority(string sourceOutboxItemId, string sourceFingerprint, string selectionFingerprint,
        PersistenceScope scope, string recipientId, string sealFingerprint)
    {
        RuntimeDeliveryFingerprint.Text(sourceOutboxItemId, nameof(sourceOutboxItemId));
        RuntimeDeliveryFingerprint.Hash(sourceFingerprint, nameof(sourceFingerprint));
        RuntimeDeliveryFingerprint.Hash(selectionFingerprint, nameof(selectionFingerprint));
        RuntimeDeliveryFingerprint.Text(recipientId, nameof(recipientId)); RuntimeDeliveryFingerprint.Hash(sealFingerprint, nameof(sealFingerprint));
        ArgumentNullException.ThrowIfNull(scope);
        SourceOutboxItemId = sourceOutboxItemId; SourceFingerprint = sourceFingerprint; SelectionFingerprint = selectionFingerprint;
        Scope = scope; RecipientId = recipientId; SealFingerprint = sealFingerprint;
    }
    public string SourceOutboxItemId { get; }
    public string SourceFingerprint { get; }
    public string SelectionFingerprint { get; }
    public PersistenceScope Scope { get; }
    public string RecipientId { get; }
    public string SealFingerprint { get; }
}
