# Implementation Plan: Runtime API Agent Dispatch

**Branch**: `codex/runtime-api-agent-dispatch` | **Date**: 2026-06-11 | **Spec**: [spec.md](./spec.md)

## Summary

Replace the temporary synchronous execute handler path with a start-dispatch seam that emits actor-style workflow execution commands. The dispatcher loads the executable artifact only to pin the runtime-owned artifact identity, generates command/envelope identity, activates the workflow execution agent for the workflow execution ID, and enqueues a `Start` command.

## Scope

- Add start-dispatch request/result models and dispatcher contract in Runtime.Core.
- Add a default start dispatcher service that builds `WorkflowExecutionCommandEnvelope` instances for `Start`.
- Add a small runtime ID generator abstraction for workflow execution, command, and envelope IDs.
- Update Runtime API execute request/handler/endpoint views to return dispatch status instead of inline execution results.
- Register the dispatcher and ID generator in `WorkflowsRuntimeApiFeature`.
- Add focused tests covering agent dispatch, pinned artifact payload, unknown artifact rejection, and dependency boundaries.

## Non-Scope

- Full scheduler behavior.
- Activity execution from `Start` commands.
- Durable workflow execution state persistence.
- Bookmark, checkpoint, incident, retry, or outbox processing.
- Distributed actor provider implementation.

## Constitution Check

| Gate | Status | Notes |
|---|---|---|
| Runtime artifact/state boundary | PASS | Dispatch loads `WorkflowExecutable` and pins its identity; it does not load authored workflow documents. |
| Actor-style single writer | PASS | Runtime API routes starts through `IWorkflowExecutionAgentProvider`. |
| Continuation state split | PASS | The slice emits a command; it does not add history/diagnostics as continuation state. |
| Scope control | PASS | No activity execution, checkpoint commit, bookmark processing, or retry implementation is added. |

## Validation

```bash
dotnet test tests/Elsa/Workflows/Runtime/Tests/Elsa.Workflows.Runtime.Tests.csproj
dotnet test tests/Elsa/Architecture/Elsa.Architecture.Tests.csproj
dotnet build src/Elsa/Workflows/Runtime/Core/Elsa.Workflows.Runtime.Core.csproj
dotnet build src/Elsa/Workflows/Runtime/Api/Elsa.Workflows.Runtime.Api.csproj
git diff --check
```
