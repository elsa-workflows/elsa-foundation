# Feature Specification: Runtime Bookmark Creation Checkpoint

**Feature Branch**: `codex/runtime-bookmark-creation-checkpoint`
**Created**: 2026-06-11
**Status**: Draft
**Input**: Continue the Runtime Execution Seam after bookmark resume consumption. Runtime has bookmark state projection and resume consumption; it now needs a narrow scheduler command boundary that creates durable bookmark state and suspends the owning activity through a named checkpoint.

## Scenarios & Tests

1. Given `CreateBookmark` scheduler work for a running activity execution, when the pinned executable declares the resume target, then the runtime commits `BookmarkCreated`, upserts bookmark state, marks the activity suspended, and does not enqueue completion work.
2. Given the executable artifact does not declare the resume target, then the scheduler work fails clearly and does not persist bookmark or activity state changes.
3. Given the activity execution already contains the bookmark ID, then replaying the same command keeps the activity bookmark list deterministic and duplicate-free.
4. Given the activity execution state is missing or belongs to a different executable node, then the scheduler work fails clearly.

## Requirements

- **FR-001**: Runtime.Core MUST define a `RuntimeCreateBookmarkCommandPayload`.
- **FR-002**: Runtime.Core MUST provide a scheduler handler for `WorkflowExecutionCommandKind.CreateBookmark`.
- **FR-003**: Bookmark creation MUST validate the pinned executable, executable node, resume target ID, and activity execution state before committing.
- **FR-004**: Bookmark creation MUST use `RuntimeCheckpointNames.BookmarkCreated`.
- **FR-005**: The checkpoint commit MUST upsert exactly one `BookmarkState` and one suspended `ActivityExecutionState`.
- **FR-006**: Bookmark creation MUST NOT enqueue activity completion propagation work.
- **FR-007**: Bookmark state MUST store `ResumeTargetId`; callback method names remain out of durable state.

## Non-Goals

- Activity author API for declaring waits.
- Full durable wait registration abstraction.
- Bookmark lookup indexes beyond the current state store.
- Workflow-level suspension policy.
- Post-commit intents or outbound side-effect waits.

## Acceptance Criteria

- Tests prove `CreateBookmark` persists bookmark state and suspended activity state through a `BookmarkCreated` checkpoint.
- Tests prove invalid resume target and missing/mismatched activity state fail before writes.
- Tests prove replay keeps bookmark IDs duplicate-free.
- Focused runtime and architecture tests pass.
