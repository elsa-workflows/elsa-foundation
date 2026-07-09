using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// A narrow post-index notification seam (spec 089 B). The trigger indexer invokes every registered observer
/// after it has deleted an artifact's prior trigger bindings and saved the current ones — inside the publish
/// flow, before <see cref="IWorkflowTriggerIndexer.IndexAsync"/> returns. Observers exist so a projection
/// derived from the trigger index (e.g. the per-shell HTTP route table) can be refreshed as an atomic part of
/// publishing, without the indexer taking a dependency on any consumer.
/// </summary>
/// <remarks>
/// <para>
/// This is a contribution (fan-in) seam: register implementations with
/// <c>services.TryAddEnumerable(ServiceDescriptor.Scoped&lt;IWorkflowTriggerIndexObserver, MyObserver&gt;())</c>;
/// the indexer resolves them as <c>IEnumerable&lt;IWorkflowTriggerIndexObserver&gt;</c>. There is no default
/// implementation — an unobserved index is valid.
/// </para>
/// <para>
/// <b>Failure policy:</b> observer exceptions are NOT swallowed. An observer that throws fails the whole
/// publish, matching the indexer's existing "indexing failure fails the publish" rule — a stale projection is
/// treated as an unindexed trigger, not tolerated silently. Keep observer work idempotent so a retried publish
/// converges.
/// </para>
/// </remarks>
public interface IWorkflowTriggerIndexObserver
{
    /// <summary>
    /// Called after the indexer wrote the artifact's current bindings. <paramref name="snapshot"/> carries the
    /// artifact id and its new binding set. Throwing propagates and fails the publish.
    /// </summary>
    ValueTask OnTriggersIndexedAsync(WorkflowTriggerIndexSnapshot snapshot, CancellationToken cancellationToken = default);
}
