using Elsa.Workflows.Runtime.Reconciliation.Core.Models;

namespace Elsa.Workflows.Runtime.Reconciliation.Contracts;

/// <summary>
/// Runs one reconciliation pass over every registered <c>IWorkflowArtifactReconciliationSource</c>, importing and
/// activating the closures they offer (FR-B-002).
/// </summary>
/// <remarks>
/// <para>
/// The pipeline, per closure unit and in this order: parse and format gate → closure validation against the
/// envelope alone → content-hash recompute → requirements gate → idempotency and supersession → one activation
/// request to <c>IWorkflowActivationCoordinator</c>. The isolation unit is the closure file: every gate runs for
/// the whole unit before any write, so a failing member rejects the unit and the unit writes nothing — and one
/// unit's failure never fails another.
/// </para>
/// <para>
/// <b>It has no journal and needs none.</b> Its recovery unit is the next pass: every step is idempotent by
/// content-addressed identity, and the coordinator compensates its own partial activations. Introducing
/// importer-side bookkeeping would create a second record of what is live, which is the duplicated authority
/// FR-B-006 exists to remove.
/// </para>
/// </remarks>
public interface IWorkflowArtifactReconciler
{
    /// <summary>
    /// Reconciles every source. Returns a report — per-artifact rejections are entries on the result, never
    /// throws. Only a pass-aborting condition (a configured mount that does not exist) propagates, as
    /// <c>WorkflowArtifactReconciliationException</c>.
    /// </summary>
    ValueTask<WorkflowArtifactReconciliationResult> ReconcileAsync(CancellationToken cancellationToken = default);
}
