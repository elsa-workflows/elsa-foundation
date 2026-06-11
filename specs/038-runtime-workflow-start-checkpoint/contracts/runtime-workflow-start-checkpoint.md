# Runtime Workflow Start Checkpoint Contract

## Start Command Handling

`WorkflowStartSchedulerWorkHandler` handles `WorkflowExecutionCommandKind.Start` by validating the pinned executable artifact and enqueueing one `Checkpoint` work item named `WorkflowStarted`.

The checkpoint payload carries:

- the pinned executable identity from the start command payload
- checkpoint name `WorkflowStarted`
- no activity execution IDs
- start propagation reason
- one `EnqueueSchedulerWork` post-commit intent per executable start node

The handler must not enqueue start-node `ScheduleActivity` work directly.

## Checkpoint Commit

`WorkflowCheckpointSchedulerWorkHandler` emits a workflow execution state change for `WorkflowStarted`:

- operation: `Upsert`
- status: `Running`
- pinned executable: checkpoint payload identity
- `CreatedAt`, `StartedAt`, and `UpdatedAt`: checkpoint occurrence time
- `CompletedAt`: `null`

This slice does not introduce a durable workflow execution state store. The checkpoint commit envelope remains the provider-facing persistence boundary.

## Post-Commit Scheduling

Start-node scheduler work is dispatched through existing wait-independent post-commit scheduler intents only after the checkpoint writer succeeds. If checkpoint writing fails, no start-node work is enqueued.
