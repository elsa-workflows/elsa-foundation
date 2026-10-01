using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>Additive atomic claim capability for restart-safe post-commit delivery.</summary>
public interface IRuntimePostCommitOutboxClaimStore
{
    ValueTask<IReadOnlyCollection<RuntimePostCommitOutboxClaim>> ClaimAsync(
        RuntimePostCommitOutboxClaimRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Extends a claim's visibility to <paramref name="now"/> plus <paramref name="visibilityTimeout"/>, atomically and
    /// only while the presented owner and fencing token still hold the item
    /// (<see cref="RuntimePostCommitOutboxClaimTransitions.Renew"/>). Returns the renewed claim, or <see langword="null"/>
    /// when the claim no longer holds the item; nothing is written then.
    /// </summary>
    /// <remarks>
    /// The processor renews every claim immediately before it dispatches the claimed intent (#2195), so a claim that lapsed
    /// during a long batch and was re-claimed by a peer is skipped rather than dispatched a second time. The renewal must
    /// therefore be a compare-and-set against concurrent claimers: a provider whose snapshot of the item may be stale has
    /// to detect the conflict when it writes, not trust the snapshot.
    /// </remarks>
    ValueTask<RuntimePostCommitOutboxClaim?> RenewClaimAsync(
        RuntimePostCommitOutboxClaim claim,
        DateTimeOffset now,
        TimeSpan visibilityTimeout,
        CancellationToken cancellationToken = default);

    ValueTask RecordDeliveryResultAsync(
        RuntimePostCommitOutboxClaim claim,
        RuntimePostCommitOutboxDeliveryResult result,
        CancellationToken cancellationToken = default);
}
