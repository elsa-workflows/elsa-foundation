using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// A narrow trigger-serving-projection notification seam (spec 089 B). The trigger indexer invokes every
/// registered observer after it has replaced an artifact's bindings. Publication reconciliation also invokes
/// observers after prepared bindings change authority. Observers exist so a projection derived from the trigger
/// index (e.g. the per-shell HTTP route table) can be refreshed without either producer taking a dependency on
/// any consumer.
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
    /// Called after the durable trigger serving set changed. <paramref name="snapshot"/> carries the affected
    /// artifact id and binding set; authority transitions request an unconditional derived-projection refresh.
    /// Throwing propagates and fails the publication transition.
    /// </summary>
    ValueTask OnTriggersIndexedAsync(WorkflowTriggerIndexSnapshot snapshot, CancellationToken cancellationToken = default);
}
