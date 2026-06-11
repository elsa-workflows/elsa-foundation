# Implementation Plan: Runtime Scheduler Command Drain Dispatch

**Branch**: `codex/runtime-scheduler-handler-dispatch` | **Date**: 2026-06-11 | **Spec**: [spec.md](./spec.md)

## Summary

Connect the actor-style command acceptance path to the scheduler drain seam. The command processor will continue to record accepted commands as scheduler work, then use a policy to decide whether to drain queued work for that workflow execution. Drain results are sent to observer contributors so later diagnostics/checkpoint slices can project outcomes without becoming continuation state.

## Scope

- Add scheduler drain policy and observer contracts.
- Add default immediate drain policy and no-op observer.
- Update `WorkflowSchedulerCommandProcessor` to require the drain-capable path in runtime composition; focused tests can defer draining through a replacement policy.
- Register the default policy and observer in `WorkflowsRuntimeApiFeature`.
- Add focused tests covering enqueue-before-drain, policy deferral, observer notification, and in-process agent integration.

## Non-Scope

- Full scheduler behavior.
- Activity execution from scheduler handlers.
- Bookmark, checkpoint, incident, retry, or durable outbox behavior.
- Distributed actor provider implementation.

## Constitution Check

| Gate | Status | Notes |
|---|---|---|
| Runtime artifact/state boundary | PASS | The slice touches command/scheduler contracts only and does not load authored workflow documents. |
| Actor-style single writer | PASS | Draining happens inside the accepted command processor path used by one workflow execution agent mailbox. |
| Continuation state split | PASS | Observers receive drain results; they are not continuation state. |
| Scope control | PASS | No activity execution, bookmark processing, checkpoint commit, or retry implementation. |

## Validation

```bash
dotnet test tests/Elsa/Workflows/Runtime/Tests/Elsa.Workflows.Runtime.Tests.csproj
dotnet test tests/Elsa/Architecture/Elsa.Architecture.Tests.csproj
dotnet build src/Elsa/Workflows/Runtime/Core/Elsa.Workflows.Runtime.Core.csproj
git diff --check
```
