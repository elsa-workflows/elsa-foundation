# Contract: Runtime Scheduler State Projection

`InMemoryRuntimeCheckpointWriter` can project `RuntimeCheckpointCommit.StateChanges.Scheduler` into `ISchedulerStateStore`.

## Store Boundary

`ISchedulerStateStore` stores the scheduler continuation-state snapshot by workflow execution ID.

The minimal operations are:

- save a `SchedulerState`;
- find scheduler state by workflow execution ID;
- list scheduler states.

Scheduler work queueing, draining, activity execution, recovery scanning, and durable provider behavior are out of scope for this slice.

## Projection Rule

When the writer is constructed with an `ISchedulerStateStore`, a newly accepted checkpoint commit projects the scheduler state change into the store before the write record is added.

Supported operations:

- `RuntimeStateChangeOperation.Upsert`: save or replace scheduler continuation state.

## Invariants

- Duplicate commit IDs return without validation or projection.
- Projection is serialized by the writer gate.
- `StateId` must equal `SchedulerState.WorkflowExecutionId`.
- `SchedulerState.WorkflowExecutionId` must equal the checkpoint workflow execution ID.
- If validation or projection fails, the write is not recorded.
- Scheduler snapshot storage remains distinct from `IWorkflowSchedulerWorkQueue`.
