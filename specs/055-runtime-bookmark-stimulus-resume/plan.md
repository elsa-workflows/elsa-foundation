# Implementation Plan: Runtime Bookmark Stimulus Resume Dispatch

**Branch**: `codex/runtime-bookmark-stimulus-resume` | **Date**: 2026-06-11 | **Spec**: [spec.md](./spec.md)

## Summary

Add the first durable bookmark stimulus/resume seam. Runtime can find a bookmark by workflow execution, stimulus type, and stimulus hash, resolve the bookmark through the workflow execution's pinned executable artifact, and dispatch a `ResumeBookmark` command into the actor-style workflow execution agent.

## Technical Context

- `BookmarkState` already stores `ResumeTargetId`, stimulus type/hash, activity execution ID, executable node ID, payload, and expiration.
- `IBookmarkStateStore` currently lists bookmarks by workflow execution; this slice builds a contract on top of that store and does not add provider-specific indexes.
- `BookmarkResumeResolver` already validates pinned executable identity, resume target table, node identity, and missing targets.
- `IWorkflowExecutionAgentProvider` already exposes activation reason `ResumeBookmark`.

## Constitution Check

| Gate | Status | Notes |
| --- | --- | --- |
| Runtime must not depend on Design | PASS | Uses Runtime.Core artifact, state, bookmark, and agent contracts only. |
| Bookmarks use resume target IDs | PASS | Dispatcher payload carries `ResumeTargetId`, not method names. |
| Actor-style execution | PASS | Dispatch goes through `IWorkflowExecutionAgentProvider`, preserving one mailbox per workflow execution. |
| Scope control | PASS | No bookmark consumption, handler invocation, ingress adapter, or durable provider index. |

## Implementation Steps

1. Add bookmark stimulus lookup request/result models and `IBookmarkStimulusLookup`.
2. Add default lookup implementation over `IBookmarkStateStore`.
3. Add resume command payload and dispatch request/result models.
4. Add `IBookmarkResumeDispatcher` default implementation using workflow state, executable store, resolver, ID generator, and agent provider.
5. Register lookup, resolver, and dispatcher in `WorkflowsRuntimeApiFeature`.
6. Document extension points and add focused tests.
7. Run validation and self-review.

## Risks

- Store-backed lookup is list-based for the current in-memory slice. Durable providers should implement indexed lookup behind the same contract later.
- This slice dispatches resume work but deliberately does not consume the bookmark or call activity-specific resume handlers.
