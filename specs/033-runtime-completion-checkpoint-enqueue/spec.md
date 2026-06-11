# Feature Specification: Runtime Completion Checkpoint Enqueue

**Feature Branch**: `codex/runtime-completion-checkpoint-enqueue`
**Created**: 2026-06-11
**Status**: Draft
**Input**: Continue deterministic activity completion propagation after continuation-scheduling work exists.

## Scenarios & Tests

1. Given `ContinuationScheduling` completion work is handled, when Workflows Runtime accepts it, then it enqueues deterministic `Checkpoint` scheduler work for the same activity execution.
2. Given `Checkpoint` scheduler work is replayed, when Workflows Runtime handles it, then the payload is validated through a named handler and no checkpoint commit is written yet.
3. Given the scheduler drains parent-evaluation work, when it continues through continuation scheduling, then the drain reaches the named checkpoint handler instead of the fallback no-op.

## Requirements

- **FR-001**: Continuation-scheduling completion work MUST enqueue checkpoint scheduler work instead of writing checkpoints inline.
- **FR-002**: Checkpoint scheduler work MUST use `WorkflowExecutionCommandKind.Checkpoint`.
- **FR-003**: Checkpoint scheduler work MUST carry a runtime checkpoint name separate from persistence policy.
- **FR-004**: Activity-completion checkpoint work MUST use `RuntimeCheckpointNames.ActivityCompleted`.
- **FR-005**: Checkpoint scheduler work MUST be idempotently named from the continuation-scheduling work item, subject activity execution ID, and checkpoint name.
- **FR-006**: Workflows Runtime MUST contribute a named checkpoint scheduler handler that validates payload shape and explicitly defers checkpoint commit/write behavior.
- **FR-007**: This slice MUST NOT implement checkpoint persistence, executable edge traversal, scheduling downstream activities, workflow completion, bookmarks, outbox processing, or retry behavior.
- **FR-008**: Runtime execution projects MUST remain free of Design-owned authored workflow model dependencies.

## Non-Goals

- Writing `RuntimeCheckpointCommit` envelopes.
- Applying checkpoint persistence policy.
- Traversing executable edges.
- Scheduling downstream executable nodes.
- Workflow completion propagation.

## Acceptance Criteria

- `RuntimeCheckpointCommandPayload` exists in Runtime.Core and validates checkpoint work identity.
- Continuation-scheduling work enqueues one `Checkpoint` scheduler work item for the completed activity boundary.
- The Workflows Runtime composition dispatches `Checkpoint` through a named handler, not the fallback no-op.
- Focused runtime and architecture tests pass.
