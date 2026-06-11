# Feature Specification: Runtime Workflow Execution State Store

**Feature Branch**: `codex/runtime-workflow-state-store`
**Created**: 2026-06-11
**Status**: Draft
**Input**: Continue the Runtime Execution Seam after workflow start/completion checkpoints. Checkpoint commits now carry `WorkflowExecutionState` changes; the next slice makes those changes queryable as runtime continuation state without introducing a full durable provider.

## Scenarios & Tests

1. Given a checkpoint commit with a workflow execution state upsert, when the in-memory checkpoint writer accepts the commit, then the workflow execution state is saved in a workflow execution state store.
2. Given a `WorkflowStarted` checkpoint is handled, when its commit succeeds, then callers can read `Running` workflow execution state by workflow execution ID.
3. Given a `WorkflowCompleted` checkpoint is handled, when its commit succeeds, then callers can read `Completed` workflow execution state by workflow execution ID with preserved start timestamps.
4. Given the same checkpoint commit is replayed, then the in-memory checkpoint writer remains idempotent by commit ID.

## Requirements

- **FR-001**: Runtime.Core MUST expose a workflow execution state store contract separate from activity execution state storage.
- **FR-002**: Runtime.Core MUST provide an in-memory workflow execution state store for current runtime tests and default API composition.
- **FR-003**: The default in-memory checkpoint writer MUST project successful workflow execution state upserts into the workflow execution state store.
- **FR-004**: Workflow start and completion checkpoint handling MUST remain checkpoint-driven; scheduler handlers MUST NOT write workflow execution state directly outside the checkpoint writer path.
- **FR-005**: Runtime execution projects MUST remain free of Design-owned authored workflow model dependencies.

## Non-Goals

- Durable database/provider implementation.
- Applying scheduler, bookmark, durable value, incident, or operational state changes.
- Full checkpoint replay/recovery scanner.
- Outbox processing changes.
- New workflow result APIs.

## Acceptance Criteria

- `IWorkflowExecutionStateStore` and `InMemoryWorkflowExecutionStateStore` exist and are registered by `WorkflowsRuntimeApiFeature`.
- In-memory checkpoint writes apply `WorkflowExecutionState` upserts only after accepting the commit.
- Focused tests prove `WorkflowStarted` and `WorkflowCompleted` checkpoints become queryable workflow execution state.
- Focused runtime and architecture tests pass.
