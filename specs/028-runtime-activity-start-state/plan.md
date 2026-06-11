# Implementation Plan: Runtime Activity Start State Transition

**Branch**: `codex/runtime-activity-start-state` | **Date**: 2026-06-11 | **Spec**: [spec.md](./spec.md)

## Summary

Move the scheduler seam one step past scheduled activity state creation. `WorkflowScheduleActivitySchedulerWorkHandler` will enqueue deterministic `StartActivity` scheduler work after it records a new `Scheduled` state. A new `WorkflowStartActivitySchedulerWorkHandler` consumes that work, validates the pinned executable artifact and node against the existing activity state, and transitions the activity state to `Running`. The slice stops before invoking activity bodies, evaluating inputs, traversing executable edges, or writing checkpoints.

## Scope

- Add `WorkflowExecutionCommandKind.StartActivity` at the end of the enum to preserve existing ordinals.
- Add `RuntimeStartActivityCommandPayload`.
- Extend schedule handling to enqueue start work after a new scheduled state is recorded.
- Add a default `StartActivity` scheduler work handler.
- Register defaults in Runtime API composition.
- Add focused runtime tests and update extension-point documentation.

## Non-Scope

- Activity construction or invocation.
- Input binding evaluation.
- Executable edge traversal.
- Checkpoint writing or durable persistence providers.
- Bookmark, incident, retry, outbox, or distributed actor behavior.

## Constitution Check

| Gate | Status | Notes |
|---|---|---|
| Durable activity identity | PASS | Start work targets `ActivityExecutionId`, not authored activity identity. |
| Design-free runtime | PASS | Handler reads `WorkflowExecutable` and split activity execution state only. |
| Deterministic scheduler work | PASS | Start propagation is queued scheduler work, not recursive bubbling. |
| Scope control | PASS | No activity bodies, graph traversal, checkpoints, or bookmarks are introduced. |

## Validation

```bash
dotnet test tests/Elsa/Workflows/Runtime/Tests/Elsa.Workflows.Runtime.Tests.csproj
dotnet test tests/Elsa/Architecture/Elsa.Architecture.Tests.csproj
dotnet build src/Elsa/Workflows/Runtime/Core/Elsa.Workflows.Runtime.Core.csproj
dotnet build src/Elsa/Workflows/Runtime/Api/Elsa.Workflows.Runtime.Api.csproj
git diff --check
```
