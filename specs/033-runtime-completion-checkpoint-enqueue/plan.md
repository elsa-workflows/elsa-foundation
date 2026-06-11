# Implementation Plan: Runtime Completion Checkpoint Enqueue

**Branch**: `codex/runtime-completion-checkpoint-enqueue` | **Date**: 2026-06-11 | **Spec**: [spec.md](./spec.md)

## Summary

Extend the completion-drain boundary to the checkpoint scheduling step. `CompleteActivity` work representing `ContinuationScheduling` now queues a `Checkpoint` scheduler work item for the `ActivityCompleted` runtime checkpoint boundary. Workflows Runtime contributes a named checkpoint scheduler handler that validates the command payload and stops before checkpoint commit/persistence.

## Technical Context

- `WorkflowExecutionCommandKind.Checkpoint` already exists and must keep ordinal `11`.
- `RuntimeCheckpointNames.ActivityCompleted` already names the activity completion checkpoint boundary.
- `RuntimeCheckpointCommit`, `IRuntimeCheckpointPersistencePolicy`, `IRuntimeCheckpointWriter`, and `RuntimeCheckpointCommitter` already exist, but this slice intentionally does not call them.
- `IWorkflowSchedulerWorkQueue` is idempotent by scoped work item ID.

## Constitution Check

| Gate | Status | Notes |
|---|---|---|
| Runtime must not depend on Design | PASS | Uses runtime scheduler/checkpoint contracts only. |
| Deterministic scheduler work | PASS | Checkpoint is queued after continuation scheduling. |
| Checkpoint names separate from policy | PASS | Payload carries checkpoint name; handler does not choose persistence mode. |
| Scope control | PASS | No checkpoint commit/write, edge traversal, workflow completion, bookmarks, or retry. |

## Validation

- `dotnet test tests/Elsa/Workflows/Runtime/Tests/Elsa.Workflows.Runtime.Tests.csproj`
- `dotnet test tests/Elsa/Activities/Runtime/Tests/Elsa.Activities.Runtime.Tests.csproj`
- `dotnet test tests/Elsa/Architecture/Elsa.Architecture.Tests.csproj`
- `dotnet build src/Elsa/Workflows/Runtime/Core/Elsa.Workflows.Runtime.Core.csproj`
- `dotnet build src/Elsa/Activities/Runtime/Elsa.Activities.Runtime.csproj`
- `git diff --check`
