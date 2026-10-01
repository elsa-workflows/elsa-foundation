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

    /// <summary>
    /// Lists the items <paramref name="query"/> selects that are claimed for delivery
    /// (<see cref="RuntimePostCommitOutboxStatus.Delivering"/>), whoever owns the claim and whether or not it has lapsed,
    /// earliest visibility deadline first and at most <see cref="RuntimePostCommitOutboxClaimedQuery.Limit"/> of them.
    /// Nothing is written.
    /// </summary>
    /// <remarks>
    /// A live drain reads this when its own delivery step delivered nothing (#2225). The resumption sweep claims across
    /// every execution, so it can take a drain's continuation between the drain's commit and its delivery step. An item
    /// listed here is such a continuation: the drain is not quiescent until that delivery has finished and its work has
    /// been drained, and once the claim lapses the drain claims the item itself. A provider that left a claimed item out
    /// would let the drain report quiescence, and its command return, before the next step of its own work had run.
    /// </remarks>
    ValueTask<IReadOnlyCollection<RuntimePostCommitOutboxItem>> ListClaimedAsync(
        RuntimePostCommitOutboxClaimedQuery query,
        CancellationToken cancellationToken = default);
}
