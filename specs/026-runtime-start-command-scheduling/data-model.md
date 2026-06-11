# Data Model: Runtime Start Command Scheduling

## `WorkflowExecutionStartCommandPayload`

Existing start command payload emitted by `IWorkflowExecutionStartDispatcher`.

- `PinnedExecutable`: exact executable artifact identity snapshot that this workflow execution is starting from.
- `RequestedArtifactId`: original artifact lookup key used by ingress.

## `RuntimeScheduleActivityCommandPayload`

Typed scheduler-work payload for a scheduled executable node.

- `PinnedExecutable`: exact executable artifact identity inherited from the start command.
- `ExecutableNodeId`: runtime executable node to schedule.
- `ActivityExecutionId`: optional durable activity execution identity when a later slice creates it before queueing.
- `SchedulingActivityExecutionId`: optional parent/scheduling activity execution.
- `Reason`: scheduler reason such as workflow start.

## `WorkflowStartSchedulerWorkHandler`

Default scheduler work handler for `WorkflowExecutionCommandKind.Start`.

- Reads the start command payload from `RuntimeSchedulerWorkItem.Payload`.
- Loads the `WorkflowExecutable` from `IWorkflowExecutableStore`.
- Verifies loaded identity matches the pinned payload identity.
- Enqueues `WorkflowExecutionCommandKind.ScheduleActivity` work for each artifact start node.
