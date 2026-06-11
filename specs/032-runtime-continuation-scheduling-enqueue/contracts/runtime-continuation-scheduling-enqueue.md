# Contract: Runtime Continuation Scheduling Enqueue

## Completion Work Kinds

`RuntimeCompleteActivityCommandPayload.CompletionKind` uses `SchedulerCompletionKind`.

- `ParentCompletionEvaluation`: subject is the parent activity execution. `CompletedChildActivityExecutionId` identifies the completed child that caused evaluation.
- `ContinuationScheduling`: subject is the activity execution whose continuations should be considered. It does not carry `CompletedChildActivityExecutionId`.

## Dispatch

Workflows Runtime handles all completion payload kinds through `WorkflowCompleteActivitySchedulerWorkHandler`. `ParentCompletionEvaluation` enqueues `ContinuationScheduling`; `ContinuationScheduling` is validated and accepted only. Executable edge traversal and downstream activity scheduling remain later slices.
