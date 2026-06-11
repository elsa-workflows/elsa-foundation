# Data Model: Runtime Activity Start State Transition

## `RuntimeStartActivityCommandPayload`

Scheduler work payload for starting one previously scheduled activity execution.

- `PinnedExecutable`: exact executable artifact identity.
- `ExecutableNodeId`: runtime executable node expected by the activity execution state.
- `ActivityExecutionId`: durable identity for this concrete activity execution.
- `Reason`: scheduler reason such as scheduled activity start.

## `ActivityExecutionState`

This slice transitions existing state:

- From `Status`: `Scheduled`.
- To `Status`: `Running`.
- `StartedAt`: scheduler handler timestamp.
- Durable identity and relationship fields remain unchanged.
- Metadata is extended with the start reason and scheduler work item ID.

## Idempotency

Repeated `ScheduleActivity` work for an existing `Scheduled` state re-enqueues `StartActivity` work. Repeated `StartActivity` work for an activity execution that is no longer `Scheduled` is a no-op. It must not regress `Running`, `Waiting`, `Suspended`, `Completed`, `Faulted`, or `Cancelled` states.
