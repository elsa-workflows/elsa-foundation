# Implementation Plan: Runtime Root Continuation Scheduling

**Branch**: `codex/runtime-root-continuation-scheduling` | **Date**: 2026-06-11 | **Spec**: [spec.md](./spec.md)

## Summary

Close the root-completion gap in the completion propagation chain. `WorkflowCompleteActivitySchedulerWorkHandler` already sends child completion through `ParentCompletionEvaluation -> ContinuationScheduling -> Checkpoint -> downstream scheduling`. Root activity completion currently exits before continuation scheduling, which prevents ordinary start-node completions from traversing executable outgoing edges. This slice routes root `ActivityCompleted` work directly to `ContinuationScheduling` while preserving the existing no-parent-evaluation invariant.

This is a deliberate follow-up to slice 032's explicit deferral for root continuation scheduling, not a reinterpretation of parent-evaluation semantics.

## Technical Context

**Language/Version**: C# / .NET 10
**Primary Projects**: `src/Elsa/Workflows/Runtime/Core`, runtime tests
**Storage**: Existing in-memory scheduler queue/checkpoint writer only
**Constraints**: Runtime execution code remains Design-free; no workflow completion or join policy in this slice

## Constitution Check

| Gate | Result | Notes |
|---|---|---|
| Runtime must not depend on Design | PASS | Runtime.Core and runtime tests only. |
| Runtime executes pinned executable artifacts | PASS | Continuation scheduling preserves the pinned artifact and downstream traversal keeps using the executable store. |
| Deterministic scheduler work | PASS | Root completion becomes queued continuation work, not recursive bubbling. |
| Scope control | PASS | No workflow completion, joins, bookmarks, retry, durable providers, or activity invocation provider. |

## Implementation Tasks

- Add Speckit slice artifacts and update active Speckit pointers.
- Change `ActivityCompleted` handling with no parent to enqueue continuation-scheduling work.
- Keep child-with-parent behavior unchanged.
- Add focused runtime tests for root continuation and downstream scheduling from root completion.
- Run runtime and architecture validation.

## Validation

```bash
dotnet test tests/Elsa/Workflows/Runtime/Tests/Elsa.Workflows.Runtime.Tests.csproj --no-restore
dotnet test tests/Elsa/Architecture/Elsa.Architecture.Tests.csproj --no-restore
dotnet build src/Elsa/Workflows/Runtime/Core/Elsa.Workflows.Runtime.Core.csproj --no-restore
dotnet build src/Elsa/Activities/Runtime/Elsa.Activities.Runtime.csproj --no-restore
dotnet test tests/Elsa/Activities/Runtime/Tests/Elsa.Activities.Runtime.Tests.csproj --no-restore
git diff --check
```
