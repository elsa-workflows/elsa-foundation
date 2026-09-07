using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// The single content-addressed artifact store (ADR 0038/0040). Artifacts are immutable and one row per distinct
/// behavior; scope, expiry and deletion are reference facts (see <see cref="IWorkflowExecutableSourceReferenceStore"/>),
/// so this store keeps only the minimal artifact surface. Saving is idempotent by <c>ArtifactId</c>.
/// </summary>
public interface IWorkflowExecutableStore
{
    /// <summary>Saves the artifact. Idempotent by <c>ArtifactId</c>: an existing artifact is left untouched.</summary>
    ValueTask SaveAsync(WorkflowExecutable executable, CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves one closure's immutable artifacts as a single atomic unit. Either every previously absent artifact
    /// becomes visible together or none does; already-present content-addressed artifacts are left untouched.
    /// </summary>
    /// <remarks>
    /// Reconciliation requires this stronger boundary because a closure is its isolation unit. Custom stores that
    /// do not support atomic multi-artifact writes fail closed instead of silently degrading to sequential saves.
    /// </remarks>
    ValueTask SaveBatchAsync(
        IReadOnlyList<WorkflowExecutable> executables,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromException(new NotSupportedException(
            $"{GetType().Name} does not support atomic workflow executable batches."));

    /// <summary>
    /// Unconditionally deletes an artifact for exceptional privileged administration and low-level store tests.
    /// Garbage collection must use the guarded overload.
    /// </summary>
    ValueTask<bool> DeleteAsync(string artifactId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Attempts to acquire a provider-backed lease before establishing a new durable retention root.
    /// Returns <see langword="null"/> while an unexpired deletion guard owns the artifact.
    /// </summary>
    ValueTask<WorkflowExecutableRootWriteLease?> TryAcquireRootWriteLeaseAsync(
        string artifactId,
        string leaseId,
        DateTimeOffset expiresAt,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);

    /// <summary>Renews a matching, unexpired root-write lease.</summary>
    ValueTask<bool> RenewRootWriteLeaseAsync(
        WorkflowExecutableRootWriteLease lease,
        DateTimeOffset expiresAt,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);

    /// <summary>Releases a matching root-write lease. Stale and already-released leases are ignored.</summary>
    ValueTask ReleaseRootWriteLeaseAsync(
        WorkflowExecutableRootWriteLease lease,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Attempts to reserve an artifact for deletion. Returns <see langword="null"/> while any unexpired root-write
    /// lease exists or another deletion operation owns the guard.
    /// </summary>
    ValueTask<WorkflowExecutableDeletionGuard?> TryBeginDeletionAsync(
        string artifactId,
        string operationId,
        DateTimeOffset expiresAt,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);

    /// <summary>Cancels a matching deletion guard so root writers may proceed.</summary>
    ValueTask<bool> CancelDeletionAsync(
        WorkflowExecutableDeletionGuard guard,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes only when the supplied deletion guard is current and unexpired and no root-write lease is live.
    /// </summary>
    ValueTask<bool> DeleteAsync(
        WorkflowExecutableDeletionGuard guard,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);

    /// <summary>Finds an artifact by id, or null.</summary>
    ValueTask<WorkflowExecutable?> FindAsync(string artifactId, CancellationToken cancellationToken = default);

    /// <summary>Reads one finite, forward-only page of stored artifacts.</summary>
    ValueTask<RuntimeStorePage<WorkflowExecutable>> ListPageAsync(
        RuntimeStorePageRequest request,
        CancellationToken cancellationToken = default);
}
