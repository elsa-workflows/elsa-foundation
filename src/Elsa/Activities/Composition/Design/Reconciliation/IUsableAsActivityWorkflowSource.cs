namespace Elsa.Activities.Composition.Design.Reconciliation;

/// <summary>
/// Narrow port describing what the Workflow activity kind needs from the workflow world: the workflow
/// definition versions marked usable-as-activity, as provider-neutral records. It keeps
/// <see cref="WorkflowActivityReconciliationSource"/> free of any Workflows Design dependency — the reach
/// into the Workflows Design read ports is isolated to the adapter that implements this port (§2.7
/// bridge/adapter, connecting the Activities and Workflows design sub-domains without either owning the
/// other). The discovery mechanism (today a full scan; later a persisted usable-as-activity index) is an
/// adapter concern and can change without touching the reconciliation source.
/// </summary>
public interface IUsableAsActivityWorkflowSource
{
    ValueTask<IEnumerable<UsableAsActivityWorkflow>> Read(CancellationToken cancellationToken);
}
