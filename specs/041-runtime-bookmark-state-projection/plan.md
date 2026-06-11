# Implementation Plan: Runtime Bookmark State Projection

**Branch**: `codex/runtime-bookmark-state-projection` | **Date**: 2026-06-11 | **Spec**: [spec.md](./spec.md)

## Summary

Extend the checkpoint-state projection sequence to bookmark state. Add a minimal `IBookmarkStateStore` boundary, an in-memory implementation, and projection from `RuntimeCheckpointCommit.StateChanges.Bookmarks` under the same serialized checkpoint writer gate used for workflow and activity state projection.

## Technical Context

- `BookmarkState` already stores runtime-owned durable resume handles and `ResumeTargetId`.
- `RuntimeCheckpointCommit.StateChanges.Bookmarks` already carries bookmark state changes.
- `InMemoryRuntimeCheckpointWriter` already serializes state projection and commit recording with an async gate.
- Bookmark lookup by stimulus type/hash is intentionally deferred; this slice only stores continuation state by workflow execution and bookmark ID.

## Constitution Check

| Gate | Status | Notes |
| --- | --- | --- |
| Runtime must not depend on Design | PASS | Uses Runtime.Core bookmark and executable identity contracts only. |
| Runtime state remains split | PASS | Bookmark state receives its own store boundary. |
| Checkpoint-driven state | PASS | Projection happens from accepted checkpoint commits. |
| Scope control | PASS | No durable provider, resume dispatcher, stimulus index, or recovery scanner. |

## Implementation Steps

1. Add `IBookmarkStateStore` and `InMemoryBookmarkStateStore`.
2. Register the in-memory bookmark store in the runtime API feature.
3. Extend the in-memory checkpoint writer to accept an optional bookmark state store.
4. Validate bookmark state projection operations and identities before recording writes.
5. Project bookmark upserts/deletes under the writer gate.
6. Add focused store, writer, DI, and architecture validation tests.
7. Update active Speckit pointers and run validation.

## Risks

- The in-memory writer is not a durable transaction provider. Durable providers still need to implement atomic commit semantics across split state stores.
- This slice deliberately does not expose bookmark stimulus lookup APIs, so resume behavior remains a later slice.
