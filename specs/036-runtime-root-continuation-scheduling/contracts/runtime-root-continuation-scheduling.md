# Contract: Runtime Root Continuation Scheduling

`WorkflowCompleteActivitySchedulerWorkHandler` handles `SchedulerCompletionKind.ActivityCompleted`.

When `ParentActivityExecutionId` is present:

1. The handler loads the parent activity execution state.
2. The handler enqueues `ParentCompletionEvaluation` work for the parent activity execution.
3. Existing child completion behavior remains unchanged.

When `ParentActivityExecutionId` is absent:

1. The completed activity is a root activity execution for this completion-propagation path.
2. The handler enqueues `ContinuationScheduling` work for the same activity execution.
3. The continuation payload preserves the pinned executable identity, executable node ID, activity execution ID, branch ID, and outcome names from the original completion payload.
4. The continuation payload does not carry a completed-child activity execution identity.
5. The handler does not create `ParentCompletionEvaluation` work.

Continuation scheduling remains deterministic scheduler work. It may later create checkpoint work and downstream scheduler post-commit intents through existing behavior, but this slice does not add workflow completion or join semantics.
