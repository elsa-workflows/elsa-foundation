# Implementation Plan: Runtime Composed Activity Execution

**Branch**: `codex/runtime-composed-activity-execution` | **Date**: 2026-06-11 | **Spec**: [spec.md](./spec.md)

## Summary

Add the first composed execution regression for the new runtime seam: a pinned runtime executable starts through the in-process workflow execution agent, drains scheduler work inline, invokes a real activity through Activities Runtime, and checkpoints terminal workflow completion.

## Technical Context

- `WorkflowsRuntimeApiFeature` owns runtime stores, checkpoint committer, scheduler queue/drainer, command processor, execution agent provider, and fallback handlers.
- `ActivitiesRuntimeFeature` contributes the provider-specific activity invocation and bookmark resume scheduler handlers plus activity construction services.
- `WorkflowSchedulerDrainer` dispatches non-fallback handlers before fallback handlers, so composing Activities Runtime should route `InvokeActivity` to `WorkflowInvokeActivitySchedulerWorkHandler`.
- `WorkflowCompleteActivitySchedulerWorkHandler` already classifies a completed node with `Done` outcome and no outgoing edges as terminal and enqueues a `WorkflowCompleted` checkpoint.
- The current invocation handler creates an activity execution scope. This slice proves inline agent drainage and activity service resolution, while a later request-affine slice must carry HTTP request scope/context explicitly for live response writing.

## Constitution Check

| Gate | Status | Notes |
| --- | --- | --- |
| Actor-style execution agents | PASS | Starts through `IWorkflowExecutionAgentProvider`; no direct executor path returns. |
| Runtime executes pinned artifacts | PASS | Test saves and starts a runtime-owned `WorkflowExecutable` snapshot. |
| Runtime state split | PASS | Assertions use workflow/activity state stores and checkpointed state. |
| Runtime must not depend on Design | PASS | Test composes runtime projects and activity descriptor constructors only. |
| Request-affine synchronous execution | TRACKED | Preserved as an explicit requirement for a later inline request-scope slice. |

## Implementation Steps

1. Add slice artifacts and update active Speckit pointers.
2. Mark the previous slice PR-loop task complete.
3. Add Runtime API as a test reference for Activities Runtime tests.
4. Add composed runtime+activity execution regression test.
5. Run focused activity/runtime/architecture validation.
6. Self-review and fix actionable findings.
7. Commit, open PR, run PR loop, and merge when clean.

## Risks

- The test may expose the existing scope boundary as insufficient for true HTTP response activities. That is a required follow-up, not a reason to reintroduce the direct executor.
- Service composition order must keep provider-specific handlers ahead of fallback behavior through the existing drainer priority rules.
