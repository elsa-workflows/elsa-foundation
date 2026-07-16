# Extension points — DispatchWorkflow runtime

## `DispatchWorkflowRuntimeFeature`

- Shell feature: `ActivitiesDispatchWorkflowRuntime`.
- Depends on `WorkflowsRuntimeResumption`.
- Contributes `ChildStartExecutor` for the stable `Elsa.Activities.DispatchWorkflow.StartChild` post-commit intent kind.

## Shared runtime contracts

The module stages `WorkflowDispatchCheckpointRequest` through the additive `IWorkflowDispatchStagingContext` capability implemented by the Foundation runtime context. The runtime engine, rather than the activity, owns folding the dispatch record and intent into the activity-completed checkpoint. Providers replace `IWorkflowDispatchStore` alongside their checkpoint persistence implementation; a provider must never ignore non-empty `WorkflowDispatches` state changes.

The activity has no transport selector. `ChildStartExecutor` delegates to `IWorkflowStartDispatcher`, which retains authority over exact-source gating and actor-provider selection.
