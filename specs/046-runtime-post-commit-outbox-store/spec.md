# Feature Specification: Runtime Post-Commit Outbox Store

**Feature Branch**: `codex/runtime-post-commit-outbox-store`
**Created**: 2026-06-11
**Status**: Draft
**Input**: Continue the Runtime Execution Seam after scheduler state projection. The runtime already defines post-commit outbox contracts; this slice adds the default in-memory store without implementing a full outbox processor.

## Scenarios & Tests

1. Given a pending post-commit outbox item, when it is saved, then it can be queried as deliverable after its available time.
2. Given multiple pending outbox items, when deliverable items are queried, then unavailable items and other workflow executions can be filtered out.
3. Given a delivery result is recorded as delivered, then the item no longer appears as deliverable.
4. Given a delivery result is retryable, then the item records retry state and becomes deliverable only after the retry delay.

## Requirements

- **FR-001**: Runtime.Core MUST provide an in-memory `IRuntimePostCommitOutboxStore` implementation for the current runtime composition.
- **FR-002**: The store MUST accept only pending outbox items through `SavePendingAsync`.
- **FR-003**: The store MUST return deliverable pending and retryable items ordered deterministically and bounded by query limit.
- **FR-004**: The store MUST support workflow-execution filtering through `RuntimePostCommitOutboxQuery.WorkflowExecutionId`.
- **FR-005**: The store MUST record delivery results without returning delivered, final-failed, or cancelled items as deliverable.
- **FR-006**: The store MUST preserve retry delay semantics for retryable failures.
- **FR-007**: Runtime execution projects MUST remain free of Design-owned authored workflow model dependencies.

## Non-Goals

- Full outbox processor.
- Delivery ownership/claiming.
- Durable provider implementation.
- Scheduler dispatch replacement.
- Wait registration matching.

## Acceptance Criteria

- `InMemoryRuntimePostCommitOutboxStore` can save pending outbox items and query deliverable items.
- Delivery results update item status for delivered, failed retryable, failed final, and cancelled outcomes.
- Retryable failures respect retry policy delay before redelivery.
- `WorkflowsRuntimeApiFeature` registers the in-memory store for `IRuntimePostCommitOutboxStore`.
- Focused runtime and architecture tests pass.
