# Feature Specification: Runtime Checkpoint Commit Dispatch

**Feature Branch**: `codex/runtime-checkpoint-commit-dispatch`
**Created**: 2026-06-11
**Status**: Draft
**Input**: Continue deterministic activity completion propagation after checkpoint scheduler work exists.

## Scenarios & Tests

1. Given `Checkpoint` scheduler work references completed activity executions, when Workflows Runtime handles it, then it builds a `RuntimeCheckpointCommit` for the named checkpoint and dispatches it through `RuntimeCheckpointCommitter`.
2. Given the checkpoint payload references an unknown activity execution, when Workflows Runtime handles it, then the scheduler work faults clearly and no checkpoint commit is written.
3. Given the Workflows Runtime API feature is composed, when scheduler services are resolved, then checkpoint persistence policy, writer, post-commit dispatcher, committer, and handler are available by default.

## Requirements

- **FR-001**: Checkpoint scheduler work MUST create a provider-facing `RuntimeCheckpointCommit` envelope instead of writing state directly.
- **FR-002**: The checkpoint envelope MUST preserve the runtime checkpoint name from `RuntimeCheckpointCommandPayload`.
- **FR-003**: The checkpoint envelope MUST include `ActivityExecutionState` changes for every activity execution ID listed by the payload.
- **FR-004**: Missing referenced activity execution state MUST fault the scheduler work before checkpoint write.
- **FR-005**: The checkpoint handler MUST call `RuntimeCheckpointCommitter` so persistence policy and post-commit intent ordering remain centralized.
- **FR-006**: The default runtime composition MUST provide an in-memory checkpoint writer and no-op post-commit intent dispatcher suitable for the current in-process runtime slice.
- **FR-007**: This slice MUST NOT add durable database providers, full workflow/scheduler/bookmark/durable-value/incident/operational state stores, edge traversal, downstream scheduling, workflow completion, outbox processing, or retry behavior.
- **FR-008**: Runtime execution projects MUST remain free of Design-owned authored workflow model dependencies.

## Non-Goals

- Durable checkpoint storage providers.
- Full split-state checkpoint aggregation beyond activity execution state.
- Post-commit outbox processing.
- Workflow completion propagation.
- Edge traversal or downstream activity scheduling.

## Acceptance Criteria

- `WorkflowCheckpointSchedulerWorkHandler` dispatches a `RuntimeCheckpointCommit` through `RuntimeCheckpointCommitter`.
- The commit contains activity state changes for payload activity execution IDs.
- Missing activity state faults through the named checkpoint handler.
- Workflows Runtime composition registers checkpoint commit services.
- Focused runtime and architecture tests pass.
