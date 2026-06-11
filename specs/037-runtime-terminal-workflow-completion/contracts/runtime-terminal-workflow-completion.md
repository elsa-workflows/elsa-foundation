# Contract: Runtime Terminal Workflow Completion

`WorkflowCompleteActivitySchedulerWorkHandler` handles terminal continuation classification during `SchedulerCompletionKind.ContinuationScheduling`.

When outgoing executable edges match the completed activity outcomes:

1. The handler enqueues `ActivityCompleted` checkpoint work.
2. The checkpoint payload carries downstream scheduler post-commit intents.
3. No workflow execution terminal state is emitted.

When no outgoing executable edge matches:

1. The handler enqueues `WorkflowCompleted` checkpoint work.
2. The checkpoint payload carries the completed activity execution ID.
3. The checkpoint payload carries no downstream scheduler post-commit intents.

`WorkflowCheckpointSchedulerWorkHandler` commits `WorkflowCompleted` checkpoint work by adding a `WorkflowExecutionState` upsert:

- `WorkflowExecutionId`: the scheduler work item workflow execution ID.
- `PinnedExecutable`: the checkpoint payload pinned executable.
- `Status`: `Completed`.
- `UpdatedAt` and `CompletedAt`: checkpoint occurrence time.

This contract does not define workflow output/result mapping, durable provider writes outside the checkpoint envelope, branch joins, cancellation, or fault handling.
