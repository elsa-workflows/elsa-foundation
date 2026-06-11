# Contract: Runtime Schedule Activity State Creation

`WorkflowScheduleActivitySchedulerWorkHandler` handles `WorkflowExecutionCommandKind.ScheduleActivity`.

## Handler Behavior

1. Deserialize `RuntimeScheduleActivityCommandPayload`.
2. Load the runtime executable artifact by pinned artifact ID.
3. Validate runtime-significant pinned artifact identity.
4. Resolve the executable node by `ExecutableNodeId`.
5. Record an `ActivityExecutionState` with `Scheduled` status.

## Guarantees

- Activity state uses `ActivityExecutionId`, not authored activity ID, as durable execution identity.
- Authored activity ID is copied only as trace metadata inside `ActivityExecution`.
- The handler does not construct or invoke activities.
- Invalid schedule work faults before state is recorded.
