namespace Elsa.Workflows.Design.Reconciliation.Core;

/// <summary>
/// Idempotent reconciliation lifecycle for the workflow-definition catalog. Each pass publishes
/// <see cref="WorkflowVersionsReconciling"/> to gather candidate versions from source modules,
/// then upserts the catalog. Mirrors the Activities-side reconciler contract per the unified
/// Model X reconciliation pattern.
/// </summary>
public interface IWorkflowVersionReconciler
{
    Task Reconcile(CancellationToken cancellationToken);
}
