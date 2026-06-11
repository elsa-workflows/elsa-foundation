# Implementation Plan: Runtime Parent Completion Evaluation Enqueue

**Branch**: `codex/runtime-parent-completion-evaluation` | **Date**: 2026-06-11 | **Spec**: [spec.md](./spec.md)

## Summary

Extend the completion-drain boundary one deterministic step. `CompleteActivity` work representing `ActivityCompleted` now enqueues a second `CompleteActivity` work item representing `ParentCompletionEvaluation` when the completed activity has a parent. The handler validates and accepts parent-evaluation work but intentionally stops before edge traversal or continuation scheduling.

## Technical Context

- `RuntimeCompleteActivityCommandPayload` already carries activity execution, optional parent execution, branch, outcomes, and reason.
- `SchedulerCompletionKind` already defines `ActivityCompleted`, `ParentCompletionEvaluation`, and `ContinuationScheduling` vocabulary.
- `IActivityExecutionStateStore` can resolve parent state and parent executable node identity.
- `IWorkflowSchedulerWorkQueue` is idempotent by scoped work item ID.

## Constitution Check

| Gate | Status | Notes |
|---|---|---|
| Runtime must not depend on Design | PASS | Uses runtime activity state and scheduler contracts only. |
| Deterministic scheduler work | PASS | Parent evaluation is queued, not recursive bubbling. |
| Scope control | PASS | No continuation scheduling, joins, or workflow completion. |
| Durable identity | PASS | Parent and completed child activity execution IDs are explicit. |

## Validation

- `dotnet test tests/Elsa/Workflows/Runtime/Tests/Elsa.Workflows.Runtime.Tests.csproj`
- `dotnet test tests/Elsa/Activities/Runtime/Tests/Elsa.Activities.Runtime.Tests.csproj`
- `dotnet test tests/Elsa/Architecture/Elsa.Architecture.Tests.csproj`
- `dotnet build src/Elsa/Workflows/Runtime/Core/Elsa.Workflows.Runtime.Core.csproj`
- `dotnet build src/Elsa/Activities/Runtime/Elsa.Activities.Runtime.csproj`
- `git diff --check`
