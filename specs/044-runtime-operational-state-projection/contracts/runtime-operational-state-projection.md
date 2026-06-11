# Contract: Runtime Operational State Projection

`InMemoryRuntimeCheckpointWriter` can project `RuntimeCheckpointCommit.StateChanges.Operational` into `IOperationalStateStore`.

## Store Boundary

`IOperationalStateStore` stores operational continuation/coordination state by workflow execution ID and operational state ID.

The minimal operations are:

- save an `OperationalState`;
- find operational state by workflow execution ID and operational state ID;
- list operational states for a workflow execution.

Recovery scanning, actor lease enforcement, outbox delivery processing, and domain retry behavior are out of scope for this slice.

## Projection Rule

When the writer is constructed with an `IOperationalStateStore`, a newly accepted checkpoint commit projects each operational state change into the store before the write record is added.

Supported operations:

- `RuntimeStateChangeOperation.Upsert`: save or replace operational coordination state.

## Invariants

- Duplicate commit IDs return without validation or projection.
- Projection is serialized by the writer gate.
- `StateId` must equal `OperationalState.OperationalStateId`.
- `OperationalState.WorkflowExecutionId` must equal the checkpoint workflow execution ID.
- If validation or projection fails, the write is not recorded.
- Operational recovery and domain retry remain separate.
