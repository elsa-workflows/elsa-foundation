# Implementation Plan: Runtime Bookmark Creation Checkpoint

**Branch**: `codex/runtime-bookmark-creation-checkpoint` | **Date**: 2026-06-11 | **Spec**: [spec.md](./spec.md)

## Summary

Add the narrow `CreateBookmark` scheduler command boundary: deserialize a runtime-owned payload, validate the pinned executable resume target, build bookmark state, mark the activity suspended, and commit both through the `BookmarkCreated` checkpoint.

## Technical Context

- `WorkflowExecutionCommandKind.CreateBookmark` already exists.
- `RuntimeCheckpointNames.BookmarkCreated` already exists.
- `BookmarkState` already stores `ResumeTargetId`, stimulus identity, payload, and expiry.
- `InMemoryRuntimeCheckpointWriter` already projects bookmark upserts and activity state upserts.

## Constitution Check

| Gate | Status | Notes |
| --- | --- | --- |
| Runtime must not depend on Design | PASS | Uses executable artifact, executable node, activity state, and bookmark state. |
| Checkpoint names separate from persistence policy | PASS | Handler emits `BookmarkCreated`; `RuntimeCheckpointCommitter` applies policy. |
| Resume target IDs, not callback names | PASS | Payload and bookmark state carry `ResumeTargetId` only. |
| Scope control | PASS | No authoring API, outbox, or full durable wait abstraction. |

## Implementation Steps

1. Add `RuntimeCreateBookmarkCommandPayload`.
2. Add `WorkflowCreateBookmarkSchedulerWorkHandler`.
3. Validate pinned executable, executable node, resume target, and activity execution state.
4. Build bookmark state and suspended activity state.
5. Commit both through `RuntimeCheckpointCommitter` as `BookmarkCreated`.
6. Register handler in `WorkflowsRuntimeApiFeature`.
7. Add focused handler, DI, and architecture validation.

## Risks

- This command boundary does not yet expose the activity-author convenience API for waits.
- Workflow-level suspended state policy remains a future slice.
