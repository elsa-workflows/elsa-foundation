# Feature Specification: Runtime Workflow Start Checkpoint

**Feature Branch**: `codex/runtime-workflow-start-checkpoint`
**Created**: 2026-06-11
**Status**: Draft
**Input**: Continue the Runtime Execution Seam implementation after terminal workflow completion. Start-node scheduling must happen after a named `WorkflowStarted` checkpoint commits, not directly from the start command handler.

## Scenarios & Tests

1. Given a start command with a pinned executable artifact, when the scheduler handles it, then it enqueues a `WorkflowStarted` checkpoint instead of directly enqueueing start-node `ScheduleActivity` work.
2. Given the `WorkflowStarted` checkpoint commits successfully, when post-commit intents are dispatched, then start-node `ScheduleActivity` work is enqueued after the checkpoint commit.
3. Given the `WorkflowStarted` checkpoint commit is built, then it carries a `WorkflowExecutionState` upsert with the pinned executable, workflow execution ID, `Running` status, and start timestamps.
4. Given the checkpoint write fails, then start-node scheduling intents are not dispatched.

## Requirements

- **FR-001**: Runtime start handling MUST produce a named `WorkflowStarted` checkpoint boundary before start activity scheduling.
- **FR-002**: Start-node scheduling MUST be represented as wait-independent post-commit scheduler intents on the `WorkflowStarted` checkpoint.
- **FR-003**: `WorkflowStarted` checkpoint commits MUST include a `WorkflowExecutionState` upsert pinned to the executable artifact with `Running` status, `CreatedAt`, `StartedAt`, and `UpdatedAt` set to checkpoint time, and `CompletedAt` unset.
- **FR-004**: Start handling MUST continue to reject missing artifacts, pinned executable mismatches, malformed payloads, and artifacts with no start nodes before enqueueing checkpoint work.
- **FR-005**: Runtime execution projects MUST remain free of Design-owned authored workflow model dependencies.

## Non-Goals

- Durable workflow execution state store providers.
- Full checkpoint state application beyond the commit envelope.
- Full workflow result reporting.
- Join, fan-out, suspension, fault, cancellation, or bookmark behavior.
- Replacing the agent provider or scheduler drain architecture.

## Acceptance Criteria

- Start command handler enqueues `WorkflowStarted` checkpoint work with start-node scheduler intents.
- Checkpoint handler emits workflow execution state for `WorkflowStarted` and preserves existing `WorkflowCompleted` behavior.
- Failed checkpoint writes do not dispatch start-node scheduler intents.
- Focused runtime and architecture tests pass.
