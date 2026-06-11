# Contract: Runtime Activity Start State Transition

`WorkflowStartActivitySchedulerWorkHandler` handles `WorkflowExecutionCommandKind.StartActivity`.

## Handler Behavior

1. Deserialize `RuntimeStartActivityCommandPayload`.
2. Load the runtime executable artifact by pinned artifact ID.
3. Validate runtime-significant pinned artifact identity.
4. Load `ActivityExecutionState` by workflow execution ID and activity execution ID.
5. Validate the activity state belongs to the payload executable node.
6. If the state is `Scheduled`, save a copy with `Running` status and `StartedAt`.
7. If the state is already beyond `Scheduled`, return without changing it.

## Guarantees

- Activity state uses `ActivityExecutionId`, not authored activity ID, as durable execution identity.
- `ScheduleActivity` replay for an existing `Scheduled` state still enqueues `StartActivity` work.
- The handler does not construct or invoke activities.
- Invalid start work faults before state is changed.
- Repeated start work does not overwrite later lifecycle state.
