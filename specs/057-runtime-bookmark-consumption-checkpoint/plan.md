# Implementation Plan: Runtime Bookmark Consumption Checkpoint

**Branch**: `codex/runtime-bookmark-consumption-checkpoint` | **Date**: 2026-06-11 | **Spec**: [spec.md](./spec.md)

## Summary

Add a small runtime bookmark-consumption checkpoint service and use it from the Activities runtime `ResumeBookmark` handler so a successful durable resume consumes the owning bookmark before completion propagation work is scheduled.

## Technical Context

- `RuntimeCheckpointNames.BookmarkConsumed` already exists.
- `RuntimeCheckpointStateChangeSet` already supports bookmark state changes.
- `InMemoryRuntimeCheckpointWriter` already projects bookmark `Delete` changes.
- `WorkflowResumeBookmarkSchedulerWorkHandler` currently completes activity execution and enqueues completion work, but deliberately left bookmark consumption out of scope.

## Constitution Check

| Gate | Status | Notes |
| --- | --- | --- |
| Runtime must not depend on Design | PASS | Uses `BookmarkState`, `ActivityExecutionState`, and checkpoint contracts only. |
| Checkpoint names separate from persistence policy | PASS | Adds a named checkpoint service that delegates persistence decisions to `RuntimeCheckpointCommitter`. |
| Resume target IDs, not callback names | PASS | Consumption validates `ResumeTargetId`; no method name is persisted. |
| Scope control | PASS | No durable provider, bookmark inbox, or outbox processor changes. |

## Implementation Steps

1. Add bookmark consumption checkpoint request/result/service contracts.
2. Implement the service with `RuntimeCheckpointCommitter`.
3. Register the service in `WorkflowsRuntimeApiFeature`.
4. Update `WorkflowResumeBookmarkSchedulerWorkHandler` to validate the active bookmark before invocation.
5. Commit completed activity state plus bookmark delete before enqueuing completion work.
6. Add focused resume-handler and DI tests.
7. Run focused validation and self-review.

## Risks

- This slice does not provide provider-level transaction semantics beyond the existing checkpoint writer boundary.
- Stale resume work with missing bookmark is treated as duplicate/stale work and does not invoke activity code.
