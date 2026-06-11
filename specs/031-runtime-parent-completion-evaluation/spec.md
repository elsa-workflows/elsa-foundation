# Feature Specification: Runtime Parent Completion Evaluation Enqueue

**Feature Branch**: `codex/runtime-parent-completion-evaluation`
**Created**: 2026-06-11
**Status**: Draft
**Input**: Continue deterministic activity completion propagation after `CompleteActivity` work exists.

## Scenarios & Tests

1. Given `CompleteActivity` work for a completed child activity with a parent, when Workflows Runtime handles it, then it enqueues deterministic `ParentCompletionEvaluation` work.
2. Given `CompleteActivity` work for a root activity with no parent, when Workflows Runtime handles it, then no parent-evaluation work is enqueued.
3. Given parent-evaluation work is replayed, when Workflows Runtime handles it, then the payload is validated and no continuation scheduling is performed yet.
4. Given parent activity state is missing, when child completion work references it, then the handler faults clearly instead of guessing parent node identity.

## Requirements

- **FR-001**: Completion-drain work MUST distinguish `ActivityCompleted` from `ParentCompletionEvaluation`.
- **FR-002**: Activity-completed work with a parent MUST enqueue parent-completion-evaluation scheduler work.
- **FR-003**: Parent-completion-evaluation work MUST explicitly identify the parent activity execution and the completed child activity execution.
- **FR-004**: Parent-completion-evaluation work MUST be idempotently named from the activity-completed work item, parent activity execution ID, and child activity execution ID.
- **FR-005**: Root activity completion MUST not enqueue parent-evaluation work.
- **FR-006**: This slice MUST NOT implement edge traversal, continuation scheduling, joins, or workflow completion.
- **FR-007**: Runtime execution projects MUST remain free of Design-owned authored workflow model dependencies.

## Non-Goals

- Evaluating parent activity behavior.
- Scheduling continuations.
- Join prerequisite evaluation.
- Workflow completion propagation.

## Acceptance Criteria

- `RuntimeCompleteActivityCommandPayload` carries completion work kind and completed child identity where required.
- `WorkflowCompleteActivitySchedulerWorkHandler` enqueues parent-evaluation work for child completion with a parent.
- Parent-evaluation work is accepted and validated by the named handler.
- Focused runtime tests pass.
