# Data Model: Runtime Activity Invocation Boundary

## RuntimeInvokeActivityCommandPayload

Carries the deterministic scheduler payload needed to invoke one concrete running activity execution:

- `PinnedExecutable`: exact workflow executable snapshot identity.
- `ExecutableNodeId`: runtime-owned executable node ID in the pinned artifact.
- `ActivityExecutionId`: durable identity for this concrete activity execution.
- `Reason`: scheduler reason, initially `StartedActivity`.

## RuntimeMaterializedActivityInput

Represents one runtime materialized activity input:

- `Name`: executable input name and activity factory key.
- `Argument`: constructed `InputArgument` instance passed to the activity factory.
- `Value`: value seeded into the execution context memory block.

The first implementation supports literal bindings with `typeName` metadata. Expression, durable value, active output, and reference sources remain contract-level or future middleware behavior.

## Invocation State Transition

`InvokeActivity` consumes `ActivityExecutionState` in `Running` status. The handler:

- Leaves non-`Running` state unchanged for replay/idempotency.
- Records `Completed` with `CompletedAt` when the activity completes or cannot execute.
- Records `Faulted`, increments `FaultCount`/`AggregateFaultCount`, and captures metadata when input materialization, activity `CanExecuteAsync`, or activity `ExecuteAsync` throws.
- Propagates construction failures as scheduler faults so missing runtime activity support remains a runtime dependency error.
- Keeps state persistence failures outside the activity-exception catch scope so storage failures are not misclassified as activity faults.

This slice does not schedule downstream executable nodes.
