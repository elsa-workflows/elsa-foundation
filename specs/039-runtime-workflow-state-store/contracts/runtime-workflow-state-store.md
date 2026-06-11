# Contract: Runtime Workflow Execution State Store

`IWorkflowExecutionStateStore` is the runtime continuation-state boundary for workflow execution state.

## Operations

- `SaveAsync(WorkflowExecutionState state)`: inserts or replaces the state for `WorkflowExecutionId`.
- `FindAsync(string workflowExecutionId)`: returns state for one workflow execution, if present.
- `ListAsync()`: returns all states known to the in-memory store.

## Projection Rule

`InMemoryRuntimeCheckpointWriter` may be constructed with an `IWorkflowExecutionStateStore`. When present, the writer projects `RuntimeCheckpointCommit.StateChanges.WorkflowExecution` into that store only when the commit ID is first accepted by the writer.

Only `RuntimeStateChangeOperation.Upsert` is supported by this in-memory projection slice. Other workflow execution state operations remain out of scope until a durable state application contract is introduced.

## Invariants

- Scheduler handlers build checkpoint commits; they do not save workflow execution state directly.
- Projected workflow execution state must have a `WorkflowExecutionId` matching the state-change `StateId`.
- Replaying the same commit ID does not reapply a conflicting state change.
