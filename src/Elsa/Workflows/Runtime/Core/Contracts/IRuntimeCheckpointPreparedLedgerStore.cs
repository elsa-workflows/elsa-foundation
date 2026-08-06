using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// Optional durable capability required by checkpoint coalescing. The contract provides durable preparation, stable
/// paging, exact-set authority adoption, and provider-atomic terminal folding. There is deliberately no sequential
/// compatibility implementation for adoption or folding.
/// </summary>
public interface IRuntimeCheckpointPreparedLedgerStore : IRuntimeCheckpointCommitStore
{
    /// <summary>Durably reserves canonical input and provider authority for one logical checkpoint.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> or one of its required values is null.</exception>
    /// <exception cref="ArgumentException">A required request identity is blank.</exception>
    new ValueTask<RuntimeCheckpointPreparationResult> PrepareAsync(
        RuntimeCheckpointPrepareRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Finalizes one prepared checkpoint when no provider-atomic multi-checkpoint fold is required.</summary>
    /// <exception cref="ArgumentNullException">A required argument is null.</exception>
    /// <exception cref="InvalidOperationException">The durable authority cannot finalize the preparation.</exception>
    new ValueTask<RuntimeCheckpointCommitStoreResult> CommitPreparedAsync(
        RuntimeCheckpointPreparationToken token,
        RuntimeCheckpointCommit commit,
        RuntimeCheckpointPersistenceDecision decision,
        CancellationToken cancellationToken = default);

    /// <summary>Returns one stable bounded page of durable <c>Prepared</c> reservations.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="query"/> is null.</exception>
    /// <exception cref="ArgumentException">The query or its bound opaque cursor is invalid.</exception>
    ValueTask<RuntimeCheckpointPreparedPage> PagePreparedAsync(
        RuntimeCheckpointPreparedQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically transfers one complete, ordered durable prepared set to a strictly newer recovery authority, or
    /// returns the receipt for an exact replay at the current authority. Implementations must leave every other
    /// checkpoint surface unchanged and reject incomplete, mixed, or stale sets without mutation. A new transfer
    /// must prove in the same provider transaction that the target fence is the durable active ownership lease;
    /// an exact replay needs no still-active old lease.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is null.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    ValueTask<RuntimeCheckpointPreparedAdoptionReceipt> AdoptPreparedAsync(
        RuntimeCheckpointPreparedAdoptionRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically validates the exact current authority fence/revision of every explicit member, terminalizes the
    /// ordered set, and persists the exact committed-only fold produced by
    /// <see cref="RuntimeCheckpointPreparedFoldBuilder"/>. Source-free recovery may combine a strictly-newer authority
    /// CAS with this fold in the same provider transaction; live folds retain the current binding. Explicit delivered
    /// outbox exclusions are the only effects a live fold may subtract from that aggregate.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is null.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    ValueTask<RuntimeCheckpointPreparedFoldResult> CommitPreparedFoldAsync(
        RuntimeCheckpointPreparedFoldRequest request,
        CancellationToken cancellationToken = default);
}
