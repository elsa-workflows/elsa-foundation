using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// Store for <see cref="WorkflowExecutableSourceReference"/> records — the per-publish pointers into the single
/// content-addressed artifact store (ADR 0038/0039/0040). Provides the query and retirement primitives the future
/// GC sweep (a two-query prune of expired/retired references then unreferenced artifacts) is built from; the GC
/// service itself is a separate slice.
/// </summary>
public interface IWorkflowExecutableSourceReferenceStore
{
    /// <summary>Saves (upserts by <c>SourceReferenceId</c>) a source reference.</summary>
    ValueTask SaveAsync(WorkflowExecutableSourceReference reference, CancellationToken cancellationToken = default);

    /// <summary>Finds a reference by id, or null.</summary>
    ValueTask<WorkflowExecutableSourceReference?> FindAsync(string sourceReferenceId, CancellationToken cancellationToken = default);

    /// <summary>Lists every reference pointing at the given artifact.</summary>
    ValueTask<IReadOnlyCollection<WorkflowExecutableSourceReference>> ListByArtifactAsync(string artifactId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists references, optionally filtered by <paramref name="scope"/> and liveness. When <paramref name="liveOnly"/>
    /// is true, retired and (as of <paramref name="now"/>) expired references are excluded.
    /// </summary>
    ValueTask<IReadOnlyCollection<WorkflowExecutableSourceReference>> ListAsync(
        WorkflowExecutableReferenceScope? scope = null,
        bool liveOnly = false,
        DateTimeOffset? now = null,
        CancellationToken cancellationToken = default);

    /// <summary>Retires a reference (soft-delete), stamping <paramref name="deletedAt"/> and an optional reason. Returns false when it did not exist.</summary>
    ValueTask<bool> RetireAsync(string sourceReferenceId, DateTimeOffset deletedAt, string? reason = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// GC primitive: hard-deletes references that are retired or expired at <paramref name="now"/> and returns their
    /// ids. Does not touch artifacts — the caller sweeps unreferenced artifacts afterwards.
    /// </summary>
    ValueTask<IReadOnlyCollection<string>> DeleteExpiredOrRetiredAsync(DateTimeOffset now, CancellationToken cancellationToken = default);

    /// <summary>
    /// GC primitive: returns the ids of artifacts among <paramref name="artifactIds"/> that no live reference points
    /// at (as of <paramref name="now"/>) — the set safe to sweep from the artifact store.
    /// </summary>
    ValueTask<IReadOnlyCollection<string>> ListUnreferencedArtifactIdsAsync(
        IEnumerable<string> artifactIds,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);
}
