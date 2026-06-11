# Contract: Runtime Composed Activity Execution

## Service Composition

`WorkflowsRuntimeApiFeature` and `ActivitiesRuntimeFeature` can be composed in one service provider. The resulting provider exposes:

- `IWorkflowExecutionAgentProvider`
- `IWorkflowExecutableStore`
- `IWorkflowSchedulerWorkQueue`
- `IWorkflowSchedulerDrainer`
- `IActivityExecutionStateStore`
- `IWorkflowExecutionStateStore`
- provider-specific `WorkflowInvokeActivitySchedulerWorkHandler`

## Execution Flow

Starting a pinned one-node executable through the in-process agent drains:

1. `Start`
2. `Checkpoint` with `WorkflowStarted`
3. `ScheduleActivity`
4. `StartActivity`
5. `InvokeActivity`
6. `CompleteActivity` for activity completion
7. `CompleteActivity` for continuation scheduling
8. `Checkpoint` with `WorkflowCompleted`

The flow uses runtime-owned executable node identity and `ActivityExecution` identity. It does not load or depend on Design-owned authored workflow models.

## Request-Affine Constraint

The actor-style abstraction represents single-writer workflow execution ownership. It must continue to allow an in-process request-affine execution lane where the initiating HTTP request awaits inline scheduler drainage and request-bound activities can access the live response before a durable boundary. This contract does not implement that full HTTP scope propagation yet; it reserves the behavior as required for subsequent execution slices.
