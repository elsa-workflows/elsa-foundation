# Implementation Plan: Runtime Bookmark Resume Handler Boundary

**Branch**: `codex/runtime-bookmark-resume-handler-boundary` | **Date**: 2026-06-11 | **Spec**: [spec.md](./spec.md)

## Summary

Handle `ResumeBookmark` scheduler work in Activities.Runtime by constructing the runtime activity instance, invoking a method marked with the stable `ResumeTargetAttribute`, completing the owning `ActivityExecutionState`, and enqueueing deterministic completion propagation work.

## Technical Context

- `RuntimeResumeBookmarkCommandPayload` already carries the pinned executable identity, bookmark ID, activity execution ID, executable node ID, resume target ID, stimulus identity, and input.
- `ResumeTargetAttribute` already exists in Activities.Runtime.Core.
- `WorkflowInvokeActivitySchedulerWorkHandler` provides the current activity construction, state validation, faulting, and completion-work patterns.
- Workflows.Runtime has a missing-provider fallback for `ResumeBookmark`; Activities.Runtime should register before that fallback.

## Constitution Check

| Gate | Status | Notes |
| --- | --- | --- |
| Runtime must not depend on Design | PASS | Uses runtime executable nodes, activity runtime construction, and stable resume target IDs only. |
| Resume target IDs, not callback names | PASS | Method names are discovered from runtime activity type metadata and are not persisted in bookmark state or command payload. |
| Deterministic scheduler work | PASS | Completion propagation remains queued scheduler work, not recursive bubbling. |
| Scope control | PASS | No bookmark consumption, handler compilation, or durable provider changes. |

## Implementation Steps

1. Add `WorkflowResumeBookmarkSchedulerWorkHandler` in Activities.Runtime.
2. Deserialize/validate resume payload and pinned executable.
3. Construct activity from executable node descriptor and find `[ResumeTarget]` method.
4. Invoke supported handler signatures and record completion/fault state.
5. Enqueue deterministic `CompleteActivity` scheduler work after successful resume or replay of completed state.
6. Register handler in `ActivitiesRuntimeFeature`.
7. Add focused tests and validation.

## Risks

- Reflection invocation is deliberately narrow in this slice. Richer descriptor-time resume handler binding can replace it later without changing bookmark state.
- This slice does not delete consumed bookmarks; a future checkpoint slice should consume bookmark state atomically.
