# Contract: Runtime Activity Completion Work Enqueue

## `RuntimeCompleteActivityCommandPayload`

Carries the minimum deterministic data needed for the completion-drain boundary:

- `PinnedExecutable`
- `ExecutableNodeId`
- `ActivityExecutionId`
- `ParentActivityExecutionId`
- `BranchId`
- `OutcomeNames`
- `Reason`

The payload is runtime-owned and must not carry authored workflow document models or callback method names.

## Dispatch

`WorkflowExecutionCommandKind.CompleteActivity` is the command kind for activity-completion scheduler work. Workflows Runtime accepts the work with a named handler that validates the payload and explicitly defers parent evaluation and continuation scheduling to later slices.
