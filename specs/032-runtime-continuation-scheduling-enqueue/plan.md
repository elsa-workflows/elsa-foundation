# Implementation Plan: Runtime Continuation Scheduling Enqueue

**Branch**: `codex/runtime-continuation-scheduling-enqueue` | **Date**: 2026-06-11 | **Spec**: [spec.md](./spec.md)

## Summary

Extend the completion-drain boundary one deterministic step. `CompleteActivity` work representing `ParentCompletionEvaluation` now enqueues a follow-up `CompleteActivity` work item representing `ContinuationScheduling`. The handler validates and accepts continuation-scheduling work, but the slice stops before executable edge traversal, downstream activity scheduling, workflow completion, and checkpoint behavior.

## Technical Context

- `RuntimeCompleteActivityCommandPayload` already carries the pinned executable identity, subject executable node, subject activity execution, optional parent execution, branch, outcomes, and completion kind.
- `SchedulerCompletionKind.ContinuationScheduling` already exists in the runtime scheduler vocabulary.
- `IWorkflowSchedulerWorkQueue` is idempotent by scoped work item ID.
- Parent-evaluation work is already represented as command kind `CompleteActivity`, so continuation-scheduling work stays in the same deterministic completion lane.

## Constitution Check

| Gate | Status | Notes |
|---|---|---|
| Runtime must not depend on Design | PASS | Uses runtime scheduler and activity execution contracts only. |
| Deterministic scheduler work | PASS | Continuation scheduling is queued, not recursive bubbling. |
| Scope control | PASS | No edge traversal, child activity scheduling, workflow completion, or checkpoints. |
| Durable identity | PASS | Subject activity execution and pinned executable artifact remain explicit. |

## Validation

- `dotnet test tests/Elsa/Workflows/Runtime/Tests/Elsa.Workflows.Runtime.Tests.csproj`
- `dotnet test tests/Elsa/Activities/Runtime/Tests/Elsa.Activities.Runtime.Tests.csproj`
- `dotnet test tests/Elsa/Architecture/Elsa.Architecture.Tests.csproj`
- `dotnet build src/Elsa/Workflows/Runtime/Core/Elsa.Workflows.Runtime.Core.csproj`
- `dotnet build src/Elsa/Activities/Runtime/Elsa.Activities.Runtime.csproj`
- `git diff --check`
