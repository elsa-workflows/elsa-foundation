# Contract: Runtime Activity Execution State Projection

`InMemoryRuntimeCheckpointWriter` can project `RuntimeCheckpointCommit.StateChanges.ActivityExecutions` into `IActivityExecutionStateStore`.

## Projection Rule

When the writer is constructed with an `IActivityExecutionStateStore`, a newly accepted checkpoint commit projects each activity execution state change into the store before the write record is added.

Only `RuntimeStateChangeOperation.Upsert` is supported by this in-memory projection slice.

## Invariants

- Duplicate commit IDs return without validation or projection.
- Projection is serialized by the writer gate.
- `StateId` must equal `ActivityExecution.ActivityExecutionId`.
- `ActivityExecution.WorkflowExecutionId` must equal the checkpoint workflow execution ID.
- If validation or projection fails, the write is not recorded.
