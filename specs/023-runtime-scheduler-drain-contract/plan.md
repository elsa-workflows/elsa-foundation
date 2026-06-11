# Implementation Plan: Runtime Scheduler Drain Contract

**Branch**: `codex/runtime-scheduler-drain-contract` | **Date**: 2026-06-11 | **Spec**: [spec.md](./spec.md)

## Summary

Add the first scheduler drain boundary. This slice turns queued scheduler work into deterministic handler dispatch results while deliberately avoiding activity execution, checkpoint persistence, bookmark behavior, and retry semantics.

## Scope

- Add scheduler drain request/result models.
- Add scheduler drain and work handler contracts.
- Add default drain service.
- Add default no-op scheduler work handler.
- Register the drain service and default handler in the runtime API feature.
- Add focused runtime tests.

## Non-Scope

- Activity invocation.
- Scheduler state mutation beyond queue dequeue.
- Checkpoint commit integration.
- Domain retry and operational recovery.
- Bookmark/volatile wait/generated event behavior.
- Durable scheduler persistence.

## Constitution Check

| Gate | Status | Notes |
|---|---|---|
| Runtime must not depend on Design | PASS | Runtime.Core contracts/services only. |
| Deterministic scheduler work | PASS | FIFO queue drain and stop-on-fault results are explicit. |
| Scope control | PASS | Handler dispatch only; no activity execution or checkpoint commit. |
| Focused tests | PASS | Drain ordering, limits, fault behavior, and registration. |

## Validation

- `dotnet test tests/Elsa/Workflows/Runtime/Tests/Elsa.Workflows.Runtime.Tests.csproj`
- `dotnet test tests/Elsa/Architecture/Elsa.Architecture.Tests.csproj`
- `dotnet build src/Elsa/Workflows/Runtime/Core/Elsa.Workflows.Runtime.Core.csproj`
- `git diff --check`
