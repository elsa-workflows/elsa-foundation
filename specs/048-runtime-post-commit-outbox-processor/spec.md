# Feature Specification: Runtime Post-Commit Outbox Processor

**Feature Branch**: `codex/runtime-post-commit-outbox-processor`
**Created**: 2026-06-11
**Status**: Draft
**Input**: Continue the Runtime Execution Seam after recording post-commit intents into the outbox. Add a narrow single-run delivery processor without implementing background processing or delivery ownership.

## Scenarios & Tests

1. Given pending deliverable outbox items, when the processor runs, then it dispatches each intent and records delivered results.
2. Given an intent dispatch fails, when the processor runs, then it records a retryable delivery failure and preserves the dispatch failure if failure-result recording also fails.
3. Given a workflow execution filter and limit, when the processor runs, then it only processes matching deliverable items up to the requested limit.
4. Given no deliverable outbox items, when the processor runs, then it returns an empty result without dispatching.

## Requirements

- **FR-001**: Runtime.Core MUST expose an `IRuntimePostCommitOutboxProcessor` boundary.
- **FR-002**: The default processor MUST query `IRuntimePostCommitOutboxStore` for deliverable items using current runtime time.
- **FR-003**: The processor MUST dispatch deliverable intents through `IRuntimePostCommitIntentDispatcher`.
- **FR-004**: Successful dispatch MUST record `RuntimePostCommitOutboxStatus.Delivered`.
- **FR-005**: Failed dispatch MUST record `RuntimePostCommitOutboxStatus.FailedRetryable`; the store remains responsible for normalizing exhausted retries to terminal failure.
- **FR-006**: The processor MUST preserve dispatch failure as primary if failed-result recording also fails.
- **FR-007**: Runtime execution projects MUST remain free of Design-owned authored workflow model dependencies.

## Non-Goals

- Background hosted service or scheduler integration.
- Delivery ownership/claiming.
- Distributed outbox processing.
- Wait-dependent intent activation semantics.
- Durable provider implementation.

## Acceptance Criteria

- Tests prove pending items are dispatched and marked delivered.
- Tests prove failed dispatch records retryable failure.
- Tests prove query filter and limit are honored.
- Focused runtime and architecture tests pass.
