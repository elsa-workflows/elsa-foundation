# Contract: Runtime Incident State Projection

`InMemoryRuntimeCheckpointWriter` can project `RuntimeCheckpointCommit.StateChanges.Incidents` into `IIncidentStateStore`.

## Store Boundary

`IIncidentStateStore` stores incident continuation state by workflow execution ID and incident ID.

The minimal operations are:

- save an `IncidentState`;
- insert a newly observed `IncidentState` only when the incident key does not already exist;
- find an incident by workflow execution ID and incident ID;
- list incident states for a workflow execution;
- list blocking incident states for a workflow execution.

Incident history persistence, diagnostic payload capture, retry, and recovery behavior are out of scope for this slice.

## Projection Rule

When the writer is constructed with an `IIncidentStateStore`, a newly accepted checkpoint commit projects each incident state change into the store before the write record is added.

Supported operations:

- `RuntimeStateChangeOperation.Append`: record a newly observed incident state and reject a different commit that appends the same incident key.
- `RuntimeStateChangeOperation.Upsert`: save or replace an incident state, for example when an incident transitions to a terminal status.

## Invariants

- Duplicate commit IDs return without validation or projection.
- Projection is serialized by the writer gate.
- `StateId` must equal `IncidentState.IncidentId`.
- `IncidentState.WorkflowExecutionId` must equal the checkpoint workflow execution ID.
- `Append` must not replace an existing incident state.
- If validation or projection fails, the write is not recorded.
- Incident history projections remain outside continuation state.
