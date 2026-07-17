# Feature Specification: Runtime Activity Completion Work Enqueue

**Feature Branch**: `codex/runtime-completion-propagation`
**Created**: 2026-06-11
**Status**: Draft
**Input**: Continue the Runtime Execution Seam after activity invocation. Activity completion propagation must be deterministic scheduler work, not recursive bubbling.

> **Superseded behavior (2026-07-16)**: Spec 095 removed `IActivity.CanExecuteAsync` and the hidden
> skipped-invocation path. Conditional execution is now represented explicitly in the executable graph, while
> invoked CLR activities return a declared terminal transition or suspend. The completion-enqueue contract below
> reflects that current model.

## Scenarios & Tests

1. Given an invoked activity completes successfully, when its terminal activity execution state is saved, then runtime enqueues `CompleteActivity` scheduler work for the same activity execution.
2. Given an invoked activity returns a successful declared outcome, when its terminal state is saved, then completion-drain work carries that declared outcome.
3. Given activity invocation faults before a completed state is saved, then no completion-drain work is enqueued.
4. Given completion-drain work reaches Workflows Runtime, then a named handler accepts and validates the payload instead of letting fallback no-op handling silently acknowledge it.

## Requirements

- **FR-001**: Runtime MUST enqueue completion-drain scheduler work after successful activity completion state persistence.
- **FR-002**: Completion work MUST use the existing `WorkflowExecutionCommandKind.CompleteActivity` command vocabulary to preserve enum ordinals.
- **FR-003**: Completion work payload MUST identify the pinned executable artifact, executable node ID, activity execution ID, optional parent activity execution ID, optional branch ID, outcome names, and reason.
- **FR-004**: Completion work MUST be idempotently named from the invoke scheduler work item and activity execution ID.
- **FR-005**: Completion work MUST not be enqueued when activity execution remains running or is recorded as faulted.
- **FR-006**: Workflows Runtime MUST contribute a named `CompleteActivity` scheduler handler that validates payload shape and explicitly defers edge traversal.
- **FR-007**: Runtime execution projects MUST remain free of Design-owned authored workflow model dependencies.

## Non-Goals

- Implementing parent activity evaluation.
- Implementing edge traversal or continuation scheduling.
- Implementing joins, cancellation, compensation, or incidents.
- Implementing durable scheduler lane persistence beyond the current scheduler work queue.

## Acceptance Criteria

- Successful activity invocation queues one `CompleteActivity` work item after the completed state is stored.
- Successful activity invocation queues completion work with its declared outcome names.
- Activity and materialization failures do not queue completion work.
- Workflows Runtime composition dispatches `CompleteActivity` through a named handler, not `NoopWorkflowSchedulerWorkHandler`.
