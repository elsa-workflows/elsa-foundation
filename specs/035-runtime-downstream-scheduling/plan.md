# Implementation Plan: Runtime Downstream Scheduling

**Branch**: `codex/runtime-downstream-scheduling` | **Date**: 2026-06-11 | **Spec**: [spec.md](./spec.md)

## Summary

Add the next deterministic completion-propagation step without letting downstream work escape before checkpoint commit. Continuation scheduling will resolve matching outgoing executable edges from the pinned runtime artifact and attach downstream scheduler work as checkpoint post-commit intents. `WorkflowCheckpointSchedulerWorkHandler` will copy payload intents into the commit envelope, and the default post-commit dispatcher will enqueue scheduler work after `IRuntimeCheckpointWriter` succeeds.

## Technical Context

- `WorkflowExecutable` already owns runtime `ExecutableEdge` records.
- `RuntimeCheckpointCommitter` already enforces writer-before-post-commit-dispatch ordering.
- `RuntimeScheduleActivityCommandPayload` already records the scheduled executable node and scheduling activity execution ID.
- This slice deliberately stops before workflow completion and join/branch semantics.

## Constitution Check

| Gate | Status | Notes |
|---|---|---|
| Runtime must not depend on Design | PASS | Traversal uses runtime-owned `WorkflowExecutable` and `ExecutableEdge` only. |
| Checkpoint before downstream advance | PASS | Downstream work is dispatched as post-commit intent after writer success. |
| Deterministic scheduler work | PASS | Downstream activity starts as queued `ScheduleActivity` work. |
| Scope control | PASS | No workflow completion, joins, bookmarks, retry, durable outbox, or activity invocation provider. |

## Validation

- `dotnet test tests/Elsa/Workflows/Runtime/Tests/Elsa.Workflows.Runtime.Tests.csproj`
- `dotnet test tests/Elsa/Architecture/Elsa.Architecture.Tests.csproj`
- `dotnet build src/Elsa/Workflows/Runtime/Core/Elsa.Workflows.Runtime.Core.csproj`
- `git diff --check`
