# Implementation Plan: Runtime Completion Propagation Contract

**Branch**: `codex/runtime-completion-propagation-contract` | **Date**: 2026-06-10 | **Spec**: [spec.md](./spec.md)

## Summary

Add the next Runtime Execution Seam addendum slice after volatile waits. This slice codifies activity completion propagation as deterministic scheduler completion-drain work, preserving inspectable queue-shaped ordering without implementing the scheduler loop.

## Technical Context

- `SchedulerState` already separates ordinary scheduled activity work from volatile wait/internal continuation work.
- `ActivityExecutionState` already carries parent, branch, iteration, lifecycle, and completion status.
- Checkpoint names already include `ActivityCompleted`; this slice does not change checkpoint persistence policy.
- Runtime architecture tests already guard against Design-owned authored workflow model dependencies.

## Constitution Check

| Gate | Status | Notes |
|---|---|---|
| Runtime must not depend on Design | PASS | This slice changes Runtime.Core models and runtime tests only. |
| Artifact/runtime-owned execution state | PASS | Completion work references activity execution IDs, not authored workflow nodes. |
| Focused tests for logic-bearing contracts | PASS | Add dedicated completion propagation contract tests. |
| Scope control | PASS | Scheduler execution behavior remains out of scope. |

## Scope

- Add typed scheduler completion-drain work item contract.
- Add completion-drain work kind vocabulary for activity completed, parent completion evaluation, and continuation scheduling.
- Extend `SchedulerState` with a separate `PendingCompletionWork` collection.
- Preserve scheduler collection normalization/snapshot behavior.
- Add focused tests for completion propagation contracts.

## Out of Scope

- Full scheduler drain behavior.
- Parent activity implementation.
- Join runtime execution.
- Cancellation/incident interruption behavior beyond carrying contract data.
- Checkpoint writer changes.

## Validation

- `dotnet test tests/Elsa/Workflows/Runtime/Tests/Elsa.Workflows.Runtime.Tests.csproj`
- `dotnet test tests/Elsa/Architecture/Elsa.Architecture.Tests.csproj`
- `dotnet build src/Elsa/Workflows/Runtime/Core/Elsa.Workflows.Runtime.Core.csproj`
- `git diff --check`
