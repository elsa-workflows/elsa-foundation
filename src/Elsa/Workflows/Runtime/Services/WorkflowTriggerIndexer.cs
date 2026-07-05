using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services;

/// <summary>
/// Default <see cref="IWorkflowTriggerIndexer"/> (W7, E3-1). It extracts the trigger bindings for a published
/// executable and replaces the artifact's prior bindings with the current set: it first deletes every binding
/// owned by the artifact, then writes the freshly extracted ones, so a republished version's triggers fully
/// supersede the previous version's and no stale trigger from an earlier version can still start a workflow.
/// </summary>
/// <remarks>
/// Invoked inside the publish flow; any failure propagates and fails the publish (no silently unindexed
/// trigger). The delete-then-write sequence is idempotent, so retrying a failed publish converges.
/// </remarks>
public sealed class WorkflowTriggerIndexer : IWorkflowTriggerIndexer
{
    private readonly IWorkflowTriggerBindingExtractor _extractor;
    private readonly IWorkflowTriggerBindingStore _store;

    public WorkflowTriggerIndexer(IWorkflowTriggerBindingExtractor extractor, IWorkflowTriggerBindingStore store)
    {
        ArgumentNullException.ThrowIfNull(extractor);
        ArgumentNullException.ThrowIfNull(store);
        _extractor = extractor;
        _store = store;
    }

    public async ValueTask<IReadOnlyCollection<WorkflowTriggerBinding>> IndexAsync(WorkflowExecutable executable, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(executable);

        // Extract first: an unroutable trigger throws here, before any write, so a bad publish fails cleanly.
        var bindings = _extractor.Extract(executable);

        await _store.DeleteByArtifactAsync(executable.Identity.ArtifactId, cancellationToken);

        foreach (var binding in bindings)
            await _store.SaveAsync(binding, cancellationToken);

        return bindings;
    }
}
