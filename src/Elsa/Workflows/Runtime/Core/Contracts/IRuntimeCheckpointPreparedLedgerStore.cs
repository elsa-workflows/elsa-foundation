using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// Optional durable capability required by checkpoint coalescing. The current contract provides durable preparation,
/// stable paging, and an explicit extension point for a separately reviewed terminal fold. Shipped implementations
/// fail closed at that extension point; there is deliberately no sequential compatibility implementation.
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
    /// Reserved extension point for terminally folding an ordered prepared-checkpoint set. Current implementations
    /// fail closed without mutating durable state until the terminal-fold semantics are reviewed and enabled.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is null.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    /// <exception cref="NotSupportedException">Terminal prepared-checkpoint folding is not enabled.</exception>
    ValueTask<RuntimeCheckpointPreparedFoldResult> CommitPreparedFoldAsync(
        RuntimeCheckpointPreparedFoldRequest request,
        CancellationToken cancellationToken = default);
}
