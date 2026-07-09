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
/// trigger). The delete-then-write sequence is idempotent, so retrying a failed publish converges. After the
/// write succeeds — and before returning — it notifies every registered <see cref="IWorkflowTriggerIndexObserver"/>
/// with the artifact's new bindings so index-derived projections (e.g. the HTTP route table) refresh as part
/// of the same publish; an observer that throws also fails the publish (same "indexing failure fails the
/// publish" rule).
/// </remarks>
public sealed class WorkflowTriggerIndexer : IWorkflowTriggerIndexer
{
    private readonly IWorkflowTriggerBindingExtractor _extractor;
    private readonly IWorkflowTriggerBindingStore _store;
    private readonly IEnumerable<IWorkflowTriggerIndexObserver> _observers;

    public WorkflowTriggerIndexer(
        IWorkflowTriggerBindingExtractor extractor,
        IWorkflowTriggerBindingStore store,
        IEnumerable<IWorkflowTriggerIndexObserver>? observers = null)
    {
        ArgumentNullException.ThrowIfNull(extractor);
        ArgumentNullException.ThrowIfNull(store);
        _extractor = extractor;
        _store = store;
        _observers = observers ?? [];
    }

    public async ValueTask<IReadOnlyCollection<WorkflowTriggerBinding>> IndexAsync(WorkflowExecutable executable, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(executable);

        // Extract first: an unroutable trigger throws here, before any write, so a bad publish fails cleanly.
        var bindings = _extractor.Extract(executable);

        await _store.DeleteByArtifactAsync(executable.Identity.ArtifactId, cancellationToken);

        foreach (var binding in bindings)
            await _store.SaveAsync(binding, cancellationToken);

        // Notify projections after the write, before returning. Exceptions propagate and fail the publish.
        var snapshot = new WorkflowTriggerIndexSnapshot(executable.Identity.ArtifactId, bindings);

        foreach (var observer in _observers)
            await observer.OnTriggersIndexedAsync(snapshot, cancellationToken);

        return bindings;
    }
}
