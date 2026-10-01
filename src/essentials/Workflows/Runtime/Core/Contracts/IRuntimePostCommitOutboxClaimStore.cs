using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>Additive atomic claim capability for restart-safe post-commit delivery.</summary>
public interface IRuntimePostCommitOutboxClaimStore
{
    /// <remarks>
    /// When <see cref="RuntimePostCommitOutboxClaimRequest.DeferContinuationsToExecutionOwner"/> is set, implementations
    /// MUST NOT claim an <c>EnqueueSchedulerWork</c> item whose workflow execution holds an unexpired ownership lease,
    /// and MUST read that lease after reading the item: a drain acquires its lease before it commits a continuation, so
    /// only that order keeps a live drain's own continuations away from the claimer (#2225). Other intent kinds, and
    /// continuations of executions without a live lease, are claimed as usual.
    /// </remarks>
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
