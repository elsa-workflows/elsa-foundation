# Data Model: Runtime Completion Propagation Contract

## `SchedulerCompletionWorkItem`

Ordered internal scheduler work for activity completion propagation.

- `WorkItemId`
- `WorkflowExecutionId`
- `SubjectActivityExecutionId`
- `CompletedChildActivityExecutionId`
- `BranchId`
- `Kind`
- `Sequence`
- `EnqueuedAt`
- `Reason`
- `OutcomeNames`
- `RequiredCompletedActivityExecutionIds`
- `Metadata`

## `SchedulerCompletionKind`

Completion-drain work vocabulary:

- `ActivityCompleted`
- `ParentCompletionEvaluation`
- `ContinuationScheduling`

## `SchedulerState.PendingCompletionWork`

A separate scheduler state lane for completion propagation work. This lane is distinct from:

- `PendingWork`: ordinary scheduled activity executions.
- `PendingContinuations`: volatile wait and internal continuation work.
