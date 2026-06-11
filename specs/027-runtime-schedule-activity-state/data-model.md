# Data Model: Runtime Schedule Activity State Creation

## `RuntimeScheduleActivityCommandPayload`

Scheduler work payload for one executable node.

- `PinnedExecutable`: exact executable artifact identity.
- `ExecutableNodeId`: runtime executable node to schedule.
- `ActivityExecutionId`: durable identity for this concrete execution.
- `SchedulingActivityExecutionId`: optional parent/scheduling activity execution.
- `Reason`: scheduler reason such as workflow start.

## `ActivityExecutionState`

Existing split runtime state record. This slice creates records with:

- `Status`: `Scheduled`.
- `ScheduledAt`: scheduler handler timestamp.
- `Execution`: concrete `ActivityExecution` derived from the executable node.
- Relationship fields copied from the schedule payload.
- Empty bookmark/incident collections and metadata containing the scheduling reason.

## `IActivityExecutionStateStore`

Runtime-owned state boundary for activity execution continuation state.

- `SaveAsync(ActivityExecutionState state, ...)`
- `FindAsync(string workflowExecutionId, string activityExecutionId, ...)`
- `ListAsync(string workflowExecutionId, ...)`
