# Feature Specification: Runtime Terminal Workflow Completion

**Feature Branch**: `codex/runtime-terminal-workflow-completion`
**Created**: 2026-06-11
**Status**: Draft
**Input**: Continue deterministic completion propagation after root completions reach continuation scheduling.

## Scenarios & Tests

1. Given continuation scheduling can inspect the pinned executable and finds no outgoing edge matching the completed activity outcomes, when it enqueues checkpoint work, then the checkpoint is `WorkflowCompleted`.
2. Given `WorkflowCompleted` checkpoint work is committed, when the commit envelope is built, then it carries a `WorkflowExecutionState` upsert with status `Completed` and `CompletedAt` set to the checkpoint time.
3. Given matching outgoing executable edges exist, when continuation scheduling runs, then existing downstream scheduling behavior remains unchanged and no workflow-completed state change is emitted.

## Requirements

- **FR-001**: Terminal continuation scheduling MUST be detected from the runtime-owned pinned `WorkflowExecutable`, not from authored Design models.
- **FR-002**: A continuation with no matching outgoing executable edge MUST enqueue a `WorkflowCompleted` checkpoint.
- **FR-003**: A `WorkflowCompleted` checkpoint commit MUST include a `WorkflowExecutionState` upsert with the pinned executable, workflow execution ID, `Completed` status, and terminal timestamps.
- **FR-004**: Matching-edge continuation scheduling MUST continue to enqueue downstream scheduler work after `ActivityCompleted` checkpoint commit.
- **FR-005**: Runtime execution projects MUST remain free of Design-owned authored workflow model dependencies.
- **FR-006**: This slice MUST NOT implement joins, branch merge policy, workflow output/result mapping, durable providers, bookmark behavior, outbox delivery state, retry policy, or activity invocation providers.

## Non-Goals

- Workflow output/result mapping.
- Cancellation, fault, or suspension terminal semantics.
- Join or branch synchronization semantics.
- Durable workflow execution state store provider.

## Acceptance Criteria

- No-match continuation scheduling produces a `WorkflowCompleted` checkpoint.
- `WorkflowCompleted` checkpoint commits carry workflow execution continuation state.
- Existing matching-edge downstream scheduling tests keep passing.
- Focused runtime and architecture tests pass.
