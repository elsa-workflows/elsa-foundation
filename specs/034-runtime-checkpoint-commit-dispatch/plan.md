# Implementation Plan: Runtime Checkpoint Commit Dispatch

**Branch**: `codex/runtime-checkpoint-commit-dispatch` | **Date**: 2026-06-11 | **Spec**: [spec.md](./spec.md)

## Summary

Move checkpoint scheduler work from validation-only handling to narrow commit dispatch. `WorkflowCheckpointSchedulerWorkHandler` will deserialize checkpoint payloads, resolve the referenced activity execution states, create a `RuntimeCheckpointCommit` with activity-state changes and empty unsupported state lanes, and call `RuntimeCheckpointCommitter`. The default composition gains an immediate persistence policy, in-memory writer, no-op post-commit dispatcher, and committer registration.

## Technical Context

- `RuntimeCheckpointCommitter` already centralizes persistence policy, writer, and post-commit intent ordering.
- `IActivityExecutionStateStore` is the only concrete split-state store in the current runtime slice.
- `RuntimeCheckpointCommandPayload` carries pinned executable identity, checkpoint name, activity execution IDs, and reason.
- Durable providers and full checkpoint aggregation remain later slices.

## Constitution Check

| Gate | Status | Notes |
|---|---|---|
| Runtime must not depend on Design | PASS | Uses runtime scheduler/checkpoint/activity-state contracts only. |
| Checkpoint names separate from policy | PASS | Handler preserves checkpoint name and delegates policy to `RuntimeCheckpointCommitter`. |
| Deterministic scheduler work | PASS | Checkpoint work remains ordered scheduler work. |
| Scope control | PASS | No durable provider, outbox processor, workflow completion, or edge traversal. |

## Validation

- `dotnet test tests/Elsa/Workflows/Runtime/Tests/Elsa.Workflows.Runtime.Tests.csproj`
- `dotnet test tests/Elsa/Activities/Runtime/Tests/Elsa.Activities.Runtime.Tests.csproj`
- `dotnet test tests/Elsa/Architecture/Elsa.Architecture.Tests.csproj`
- `dotnet build src/Elsa/Workflows/Runtime/Core/Elsa.Workflows.Runtime.Core.csproj`
- `dotnet build src/Elsa/Activities/Runtime/Elsa.Activities.Runtime.csproj`
- `git diff --check`
