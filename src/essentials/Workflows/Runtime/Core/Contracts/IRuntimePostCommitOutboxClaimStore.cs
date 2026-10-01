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

    ValueTask RecordDeliveryResultAsync(
        RuntimePostCommitOutboxClaim claim,
        RuntimePostCommitOutboxDeliveryResult result,
        CancellationToken cancellationToken = default);
}
