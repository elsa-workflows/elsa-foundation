# Feature Specification: Runtime Activity Bookmark Request

**Feature Branch**: `codex/runtime-activity-bookmark-request`
**Created**: 2026-06-11
**Status**: Draft
**Input**: Continue the Runtime Execution Seam after `CreateBookmark` checkpoint handling. Activities need a runtime-side way to request durable suspension/bookmark creation without directly persisting bookmarks or depending on Design-owned authored models.

## Scenarios & Tests

1. Given an invoked activity records a bookmark request on its execution context, when invocation returns, then the invoke handler enqueues deterministic `CreateBookmark` scheduler work and does not mark the activity completed.
2. Given an invoked activity records multiple bookmark requests with unique IDs, then deterministic `CreateBookmark` work is enqueued for each request in request order.
3. Given an activity attempts to record duplicate bookmark IDs in one execution context, then the context rejects the duplicate before scheduler work is enqueued.
4. Given an activity completes without bookmark requests, then existing completion behavior remains unchanged.

## Requirements

- **FR-001**: Activities.Runtime.Core MUST define an activity-owned bookmark request model that contains bookmark ID, resume target ID, stimulus type/hash, optional payload, optional expiry, and metadata.
- **FR-002**: `IActivityExecutionContext` MUST expose a method for activity code to request durable bookmark creation.
- **FR-003**: The runtime invoke handler MUST translate recorded activity bookmark requests into `WorkflowExecutionCommandKind.CreateBookmark` scheduler work.
- **FR-004**: The invoke handler MUST NOT save completed activity state or enqueue `CompleteActivity` work when durable bookmark requests are recorded.
- **FR-005**: Bookmark request scheduler work MUST carry the pinned executable identity and executable node identity from the current invocation payload.
- **FR-006**: Duplicate bookmark IDs in one activity execution context MUST be rejected.
- **FR-007**: Runtime execution MUST continue to use `ResumeTargetId`, not persisted callback method names.

## Non-Goals

- Full high-level wait API or activity base helper methods.
- Post-commit intents for outbound side-effect waits.
- Volatile wait API.
- Workflow-level suspended state policy.
- Bookmark persistence itself; that remains handled by `CreateBookmark` scheduler work.

## Acceptance Criteria

- Tests prove bookmark-requesting activity execution enqueues `CreateBookmark` work and no completion work.
- Tests prove multiple bookmark requests are ordered deterministically.
- Tests prove duplicate bookmark request IDs are rejected.
- Existing completion tests still pass.
- Focused activity/runtime and architecture tests pass.
