# Implementation Plan: Runtime Activity Completion Work Enqueue

**Branch**: `codex/runtime-completion-propagation` | **Date**: 2026-06-11 | **Spec**: [spec.md](./spec.md)

## Summary

Add the first behavior slice for completion propagation after `InvokeActivity`: when Activities Runtime records a completed/skipped activity execution, it enqueues deterministic `CompleteActivity` scheduler work. Workflows Runtime contributes a narrow handler that validates the completion payload and intentionally stops before parent evaluation or continuation scheduling.

## Technical Context

- `SchedulerCompletionWorkItem` and `SchedulerState.PendingCompletionWork` already define the durable contract shape.
- `WorkflowExecutionCommandKind.CompleteActivity` already exists and must keep ordinal `8`.
- `WorkflowInvokeActivitySchedulerWorkHandler` is the only current activity body invocation path.
- `NoopWorkflowSchedulerWorkHandler` currently accepts command kinds other than `InvokeActivity`; this slice adds a named handler so `CompleteActivity` is not silently swallowed.

## Constitution Check

| Gate | Status | Notes |
|---|---|---|
| Runtime must not depend on Design | PASS | Changes use runtime executable and activity execution contracts only. |
| Deterministic scheduler work | PASS | Completion propagation is queued as scheduler work after terminal state persistence. |
| Artifact/runtime-owned execution | PASS | Payload pins executable artifact and executable node identity. |
| Scope control | PASS | No parent evaluation, edge traversal, joins, or continuation scheduling. |

## Scope

- Add `RuntimeCompleteActivityCommandPayload`.
- Add `WorkflowCompleteActivitySchedulerWorkHandler`.
- Register the handler in Workflows Runtime before fallback handlers.
- Enqueue `CompleteActivity` from activity invocation after completed/skipped state save.
- Add focused tests for success, skipped, fault, and named dispatch.

## Out of Scope

- Scheduler completion lane persistence.
- Completion propagation drain behavior beyond payload validation.
- Activity output capture and outcome routing.
- Full workflow completion behavior.

## Validation

- `dotnet test tests/Elsa/Activities/Runtime/Tests/Elsa.Activities.Runtime.Tests.csproj`
- `dotnet test tests/Elsa/Workflows/Runtime/Tests/Elsa.Workflows.Runtime.Tests.csproj`
- `dotnet test tests/Elsa/Architecture/Elsa.Architecture.Tests.csproj`
- `dotnet build src/Elsa/Workflows/Runtime/Core/Elsa.Workflows.Runtime.Core.csproj`
- `dotnet build src/Elsa/Activities/Runtime/Elsa.Activities.Runtime.csproj`
- `git diff --check`
