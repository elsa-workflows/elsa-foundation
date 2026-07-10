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

    /// <summary>Deletes the artifact (GC of an unreferenced artifact). Returns false when it did not exist.</summary>
    ValueTask<bool> DeleteAsync(string artifactId, CancellationToken cancellationToken = default);

    /// <summary>Finds an artifact by id, or null.</summary>
    ValueTask<WorkflowExecutable?> FindAsync(string artifactId, CancellationToken cancellationToken = default);

    /// <summary>Lists every stored artifact.</summary>
    ValueTask<IReadOnlyCollection<WorkflowExecutable>> ListAsync(CancellationToken cancellationToken = default);
}
