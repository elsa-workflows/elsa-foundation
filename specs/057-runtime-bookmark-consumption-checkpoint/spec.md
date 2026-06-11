# Feature Specification: Runtime Bookmark Consumption Checkpoint

**Feature Branch**: `codex/runtime-bookmark-consumption-checkpoint`
**Created**: 2026-06-11
**Status**: Draft
**Input**: Continue the Runtime Execution Seam after bookmark resume handler dispatch. A successful durable bookmark resume must consume the matched bookmark through a named runtime checkpoint before deterministic completion propagation continues.

## Scenarios & Tests

1. Given `ResumeBookmark` scheduler work for a suspended activity execution with a matching bookmark, when the activity resume target completes, then the runtime commits `BookmarkConsumed`, deletes the bookmark state, upserts completed activity state, and enqueues completion work afterward.
2. Given duplicate `ResumeBookmark` scheduler work for an already completed activity execution with a still-present bookmark, then the runtime consumes the bookmark without invoking the activity again and re-enqueues completion work.
3. Given stale `ResumeBookmark` scheduler work where the bookmark no longer exists and the activity execution is not completed, then the activity is not invoked and no completion work is enqueued.
4. Given the bookmark exists but no longer matches the resume payload identity, then the scheduler work fails clearly instead of consuming the wrong bookmark.

## Requirements

- **FR-001**: Bookmark consumption MUST use the named `RuntimeCheckpointNames.BookmarkConsumed` boundary.
- **FR-002**: Consumption MUST delete the matched `BookmarkState` and upsert the completed `ActivityExecutionState` in the same checkpoint commit envelope.
- **FR-003**: Completion propagation work MUST be enqueued only after the bookmark-consumption checkpoint commit succeeds.
- **FR-004**: Duplicate resume work for already completed state MUST be idempotent and MUST NOT invoke the activity body again.
- **FR-005**: Stale resume work for a missing bookmark and non-completed activity MUST NOT invoke the activity body.
- **FR-006**: Runtime execution code MUST continue to use bookmark `ResumeTargetId` and MUST NOT persist callback method names.

## Non-Goals

- Full durable bookmark indexing.
- Full database transaction/provider implementation.
- Bookmark inbox or unmatched stimulus retention.
- Outbox processor changes.
- Descriptor-time resume handler compilation.

## Acceptance Criteria

- Tests prove successful resume deletes bookmark state through a `BookmarkConsumed` checkpoint and then queues `CompleteActivity`.
- Tests prove already completed state consumes a still-present bookmark and does not re-invoke activity code.
- Tests prove stale missing-bookmark resume work does not invoke activity code.
- Focused activity/runtime and architecture tests pass.
