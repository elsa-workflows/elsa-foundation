# Implementation Plan: Runtime Start Command Scheduling

**Branch**: `codex/runtime-start-command-scheduling` | **Date**: 2026-06-11 | **Spec**: [spec.md](./spec.md)

## Summary

Turn accepted workflow `Start` commands into deterministic scheduler work. The handler consumes the pinned executable payload emitted by the start dispatcher, verifies the runtime-owned artifact snapshot, and enqueues `ScheduleActivity` work items for the artifact start nodes. The slice deliberately stops before invoking activities or traversing the graph beyond start-node scheduling.

## Scope

- Add a start-command scheduler work handler in Runtime.Core.
- Add a small schedule-activity payload model if the existing scheduler work item shape needs a typed payload.
- Validate pinned executable identity before scheduling start nodes.
- Register the handler in Runtime API composition.
- Add focused tests for successful scheduling, bad payloads, identity mismatches, and feature registration.

## Non-Scope

- Full activity execution or activity middleware invocation.
- Graph traversal beyond start nodes.
- Checkpoint persistence, bookmark processing, incidents, durable retry, or outbox processing.
- Distributed actor provider implementation.
- Elsa 3 live instance resume compatibility.

## Constitution Check

| Gate | Status | Notes |
|---|---|---|
| Runtime artifact/state boundary | PASS | The handler loads `WorkflowExecutable` through the runtime store and validates pinned identity. |
| Design-free runtime | PASS | Scheduled work references executable nodes, not authored workflow documents. |
| Deterministic scheduler work | PASS | Start propagation is represented as queued scheduler work, not recursive execution. |
| Scope control | PASS | Activity body execution, graph traversal, and persistence remain out of scope. |

## Validation

```bash
dotnet test tests/Elsa/Workflows/Runtime/Tests/Elsa.Workflows.Runtime.Tests.csproj
dotnet test tests/Elsa/Architecture/Elsa.Architecture.Tests.csproj
dotnet build src/Elsa/Workflows/Runtime/Core/Elsa.Workflows.Runtime.Core.csproj
dotnet build src/Elsa/Workflows/Runtime/Api/Elsa.Workflows.Runtime.Api.csproj
git diff --check
```
