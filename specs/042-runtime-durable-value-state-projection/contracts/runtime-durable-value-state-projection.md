# Contract: Runtime Durable Value State Projection

`InMemoryRuntimeCheckpointWriter` can project `RuntimeCheckpointCommit.StateChanges.DurableValues` into `IDurableValueStateStore`.

## Store Boundary

`IDurableValueStateStore` stores declared durable value continuation state by workflow execution ID and durable value ID.

The minimal operations are:

- save a `DurableValueState`;
- delete a durable value by workflow execution ID and durable value ID;
- find a durable value by workflow execution ID and durable value ID;
- list durable value states for a workflow execution.

Storage driver behavior and value capture middleware are out of scope for this slice.

## Projection Rule

When the writer is constructed with an `IDurableValueStateStore`, a newly accepted checkpoint commit projects each durable value state change into the store before the write record is added.

Supported operations:

- `RuntimeStateChangeOperation.Upsert`: save or replace the durable value state.
- `RuntimeStateChangeOperation.Delete`: remove the durable value state by workflow execution ID and durable value ID.

## Invariants

- Duplicate commit IDs return without validation or projection.
- Projection is serialized by the writer gate.
- `StateId` must equal `DurableValueState.DurableValueId`.
- `DurableValueState.WorkflowExecutionId` must equal the checkpoint workflow execution ID.
- If validation or projection fails, the write is not recorded.
