# Elsa.Workflows.Design.Reconciliation

Reconciliation lifecycle for the workflow definition catalog. Mirrors the activity reconciliation pattern: sources contribute desired workflow versions via `IWorkflowReconciliationSource`; the reconciler diffs against the stored catalog and upserts.

## Cross-domain contributions

- **`IStartupTask`** *(Core — `Elsa.Tasks.Core`)* — `WorkflowsVersionReconcilerStartupTask` runs the reconciliation pass at startup under a distributed lock. Catalog: [`Elsa.Tasks/EXTENSION_POINTS.md`](../Elsa.Tasks/EXTENSION_POINTS.md)
