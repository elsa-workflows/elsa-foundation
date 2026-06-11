# Implementation Plan: Runtime Activity Bookmark Request

**Branch**: `codex/runtime-activity-bookmark-request` | **Date**: 2026-06-11 | **Spec**: [spec.md](./spec.md)

## Summary

Add an activity runtime context contract for durable bookmark requests and update activity invocation to enqueue `CreateBookmark` scheduler work when an activity requests durable suspension.

## Technical Context

- `WorkflowExecutionCommandKind.CreateBookmark` and `RuntimeCreateBookmarkCommandPayload` already exist.
- `WorkflowCreateBookmarkSchedulerWorkHandler` owns `BookmarkCreated` checkpoint persistence.
- `WorkflowInvokeActivitySchedulerWorkHandler` currently completes every successfully executed activity.
- `Activities.Runtime.Core` cannot depend on `Workflows.Runtime.Core`; activity-facing request types must remain workflow-runtime-neutral.

## Constitution Check

| Gate | Status | Notes |
| --- | --- | --- |
| Runtime must not depend on Design | PASS | Uses runtime activity context and runtime scheduler payloads only. |
| Checkpoints separate from policy | PASS | Activity invocation only enqueues `CreateBookmark`; persistence remains in the checkpoint handler. |
| Durable wait distinct from volatile wait | PASS | This slice handles durable bookmark requests only. |
| Resume target IDs, not callback names | PASS | Activity request carries `ResumeTargetId`; no callback method name is persisted. |

## Implementation Steps

1. Add `ActivityBookmarkRequest` in Activities.Runtime.Core.
2. Extend `IActivityExecutionContext` and `SimpleActivityExecutionContext` to record bookmark requests.
3. Update `WorkflowInvokeActivitySchedulerWorkHandler` to enqueue `CreateBookmark` work after activity execution when requests are present.
4. Preserve normal completion behavior when no bookmark requests are present.
5. Add focused activity invocation tests for one request, multiple requests, and duplicate rejection.
6. Run focused validation and self-review.

## Risks

- This is a low-level runtime request contract, not the final ergonomic activity-author API.
- The request still requires caller-provided bookmark IDs until a later ID-generation/helper slice.
