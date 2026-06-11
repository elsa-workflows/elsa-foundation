# Implementation Plan: Runtime Schedule Activity State Creation

**Branch**: `codex/runtime-schedule-activity-state` | **Date**: 2026-06-11 | **Spec**: [spec.md](./spec.md)

## Summary

Move the scheduler seam one step past start-node work creation. `WorkflowStartSchedulerWorkHandler` assigns an `ActivityExecutionId` when it creates `ScheduleActivity` work. A new `WorkflowScheduleActivitySchedulerWorkHandler` consumes that work, validates the pinned executable artifact and node, and records an `ActivityExecutionState` with `Scheduled` status. The slice stops before invoking the activity pipeline or persisting checkpoints.

## Scope

- Extend runtime ID generation with activity execution IDs.
- Require `RuntimeScheduleActivityCommandPayload.ActivityExecutionId`.
- Add an activity execution state store boundary and in-memory default.
- Add a default `ScheduleActivity` scheduler work handler.
- Register defaults in Runtime API composition.
- Add focused runtime tests and update extension-point documentation.

## Non-Scope

- Activity construction/invocation.
- Input binding evaluation.
- Executable edge traversal.
- Checkpoint writing or durable persistence providers.
- Bookmark, incident, retry, outbox, or distributed actor behavior.

## Constitution Check

| Gate | Status | Notes |
|---|---|---|
| Durable activity identity | PASS | `ActivityExecutionId` is assigned before schedule work is handled. |
| Design-free runtime | PASS | Handler loads `WorkflowExecutable` and executable node identity only. |
| Deterministic scheduler work | PASS | Schedule handling is queued work, not recursive bubbling. |
| Scope control | PASS | No activity bodies or graph traversal are introduced. |

## Validation

```bash
dotnet test tests/Elsa/Workflows/Runtime/Tests/Elsa.Workflows.Runtime.Tests.csproj
dotnet test tests/Elsa/Architecture/Elsa.Architecture.Tests.csproj
dotnet build src/Elsa/Workflows/Runtime/Core/Elsa.Workflows.Runtime.Core.csproj
dotnet build src/Elsa/Workflows/Runtime/Api/Elsa.Workflows.Runtime.Api.csproj
git diff --check
```
