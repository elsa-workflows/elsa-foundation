namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>
/// Exact in-process source identity supplied by a durable outbox processor. It is never an independent
/// authorization token: a provider must revalidate source content, scope and current claim authority.
/// </summary>
public sealed class PostCommitDeliveryContext
{
    public PostCommitDeliveryContext(RuntimePostCommitOutboxClaim claim, PersistenceScope scope)
    {
        ArgumentNullException.ThrowIfNull(claim);
        ArgumentNullException.ThrowIfNull(scope);
        var item = claim.Item;
        if (item.Status != RuntimePostCommitOutboxStatus.Delivering ||
            !StringComparer.Ordinal.Equals(item.DeliveringOwnerId, claim.OwnerId) ||
            item.DeliveryFencingToken != claim.FencingToken ||
            item.DeliveryStartedAt != claim.ClaimedAt || item.DeliveryVisibleAfter != claim.VisibleAfter)
            throw new ArgumentException("Delivery context requires one coherent durable outbox claim.", nameof(claim));
        var suffix = ":" + item.Intent.IntentId;
        if (!item.OutboxItemId.EndsWith(suffix, StringComparison.Ordinal) || item.OutboxItemId.Length <= suffix.Length)
            throw new ArgumentException("Delivery context requires the complete commit-specific outbox identity.", nameof(claim));
        var sourceCommitId = item.OutboxItemId[..^suffix.Length];
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceCommitId, nameof(claim));

        Claim = claim;
        Scope = scope;
        SourceCommitId = sourceCommitId;
        IntentFingerprint = RuntimeCheckpointCommitFingerprint.ComputeIntent(item.Intent);
    }

    public RuntimePostCommitOutboxClaim Claim { get; }
    public PersistenceScope Scope { get; }
    public string SourceCommitId { get; }
    public string SourceOutboxItemId => Claim.OutboxItemId;
    public RuntimePostCommitIntent Intent => Claim.Item.Intent;
    public string IntentFingerprint { get; }
}
