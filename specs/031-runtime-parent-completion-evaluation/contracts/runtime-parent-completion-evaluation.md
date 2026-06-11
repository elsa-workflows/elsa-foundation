# Contract: Runtime Parent Completion Evaluation Enqueue

## Completion Work Kinds

`RuntimeCompleteActivityCommandPayload.CompletionKind` uses `SchedulerCompletionKind`.

- `ActivityCompleted`: subject is the completed child activity execution. `ParentActivityExecutionId` may point to the parent.
- `ParentCompletionEvaluation`: subject is the parent activity execution. `CompletedChildActivityExecutionId` identifies the completed child.

## Dispatch

Workflows Runtime handles both payload kinds through `WorkflowCompleteActivitySchedulerWorkHandler`. `ActivityCompleted` may enqueue `ParentCompletionEvaluation`; `ParentCompletionEvaluation` is validated and accepted only. Continuation scheduling remains a later slice.
