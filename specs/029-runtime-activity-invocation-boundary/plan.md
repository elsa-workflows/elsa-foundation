# Implementation Plan: Runtime Activity Invocation Boundary

**Branch**: `codex/runtime-activity-invocation-boundary` | **Date**: 2026-06-11 | **Spec**: [spec.md](./spec.md)

## Summary

Move the scheduler seam one step past `Running` activity state. `WorkflowStartActivitySchedulerWorkHandler` will enqueue deterministic `InvokeActivity` scheduler work after it records a running state. Workflows Runtime contributes a fallback handler that faults clearly when no activity invocation provider is composed. Activities Runtime contributes the real invocation handler: it validates the pinned executable and running activity state, constructs the activity from the runtime-owned executable node descriptor through `IActivityFactory`, invokes `CanExecuteAsync`/`ExecuteAsync`, and records the single activity execution as completed or faulted. The slice stops before edge traversal, completion propagation, checkpoints, bookmarks, or retry behavior.

## Scope

- Add `WorkflowExecutionCommandKind.InvokeActivity` at the end of the enum to preserve existing ordinals.
- Add `RuntimeInvokeActivityCommandPayload`.
- Extend start handling to enqueue invoke work after a running state exists.
- Add a Workflows Runtime fallback handler for missing activity invocation support.
- Add an Activities Runtime invocation scheduler work handler and feature registration.
- Extract literal input materialization so direct sequential execution and scheduler invocation share one implementation.
- Add focused runtime and activities-runtime tests plus extension-point documentation.

## Non-Scope

- Executable edge traversal or downstream scheduling.
- Completion propagation drain behavior.
- Checkpoint writing or durable persistence providers.
- Bookmark, incident, retry, outbox, or distributed actor behavior.
- Full expression/durable-value input materialization beyond the existing literal-only runtime helper.

## Constitution Check

| Gate | Status | Notes |
|---|---|---|
| Durable activity identity | PASS | Invoke work targets `ActivityExecutionId`, not authored activity identity. |
| Design-free runtime | PASS | Invocation reads `WorkflowExecutable`, `ActivityExecutionState`, and activity runtime construction contracts only. |
| Deterministic scheduler work | PASS | Invocation is queued scheduler work after `Running`; no recursive completion bubbling. |
| Scope control | PASS | Activity body invocation is isolated from graph traversal, checkpoints, bookmarks, and retry/outbox behavior. |

## Validation

```bash
dotnet test tests/Elsa/Workflows/Runtime/Tests/Elsa.Workflows.Runtime.Tests.csproj
dotnet test tests/Elsa/Activities/Runtime/Tests/Elsa.Activities.Runtime.Tests.csproj
dotnet test tests/Elsa/Architecture/Elsa.Architecture.Tests.csproj
dotnet build src/Elsa/Workflows/Runtime/Core/Elsa.Workflows.Runtime.Core.csproj
dotnet build src/Elsa/Workflows/Runtime/Api/Elsa.Workflows.Runtime.Api.csproj
dotnet build src/Elsa/Activities/Runtime/Elsa.Activities.Runtime.csproj
git diff --check
```
