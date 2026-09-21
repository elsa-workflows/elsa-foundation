using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// Reads the pinned, content-addressed executable artifact by id (ADR 0038/0040), served through the burst-scoped
/// reconstructible cache when a burst owns the current drain (ADR 0031 item b, spec 111). The first consumer of the
/// burst cache: because the artifact is immutable within a drain, memoizing it by <c>ArtifactId</c> needs no
/// invalidation and returns byte-identical results to a direct store read.
/// </summary>
/// <remarks>
/// This is a read-only façade over <see cref="IWorkflowExecutableStore.FindAsync"/> for the drain path. Reference-GC,
/// lease/recovery, activation-entry (dispatcher), and API/inspection reads deliberately go straight to the store, so
/// they are NOT routed through this reader (spec 111 call-site enumeration).
/// </remarks>
public interface IWorkflowExecutableReader
{
    /// <summary>Finds the pinned executable artifact by id, or <see langword="null"/>. Burst-cached when a burst is active.</summary>
    ValueTask<WorkflowExecutable?> FindAsync(string artifactId, CancellationToken cancellationToken = default);
}
