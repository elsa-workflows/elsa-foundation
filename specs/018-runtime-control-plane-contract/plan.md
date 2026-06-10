# Implementation Plan: Runtime Control Plane Contract

**Branch**: `codex/runtime-control-plane-contract` | **Date**: 2026-06-11 | **Spec**: [spec.md](./spec.md)

## Summary

Add the next Runtime Execution Seam addendum slice after generator contracts. This slice codifies administrative pause/unpause as control-plane contracts with explicit scopes, safe boundary names, scheduler gating decisions, and ingress default policy. It does not implement scheduler behavior or adapters.

## Technical Context

- `OperationalState` already models runtime leases, heartbeat, drain, interruption, and pending post-commit intent coordination.
- Pause/unpause is broader administrative control-plane policy and should not be folded into ordinary workflow execution state.
- `WorkflowExecutionCommand` already names `ResumeBookmark` and `ContinueVolatileWait`; this slice adds pause/unpause terminology without changing command processing.
- `SchedulerState` remains single-writer continuation state; this slice only adds decision contracts future scheduler code can consume.

## Constitution Check

| Gate | Status | Notes |
|---|---|---|
| Runtime must not depend on Design | PASS | This slice changes Runtime.Core models and runtime tests only. |
| Runtime-owned state boundaries | PASS | Control-plane holds are runtime administrative state and reference runtime IDs. |
| Focused tests for logic-bearing contracts | PASS | Add dedicated control-plane contract tests. |
| Scope control | PASS | Scheduler behavior, adapters, stores, and distributed actor providers are out of scope. |

## Scope

- Add `ControlPlaneState` and scoped hold models.
- Add safe pause boundary, continuation policy, ingress source type, and ingress default policy models.
- Add scheduler pause decision contract.
- Extend `WorkflowExecutionCommandKind` with pause/unpause operation names.
- Add focused runtime control-plane contract tests.

## Out of Scope

- Full scheduler pause implementation.
- Ingress adapter implementation.
- Durable control-plane persistence.
- Host drain execution behavior.
- Durable suspension/bookmark resume behavior.

## Validation

- `dotnet test tests/Elsa/Workflows/Runtime/Tests/Elsa.Workflows.Runtime.Tests.csproj`
- `dotnet test tests/Elsa/Architecture/Elsa.Architecture.Tests.csproj`
- `dotnet build src/Elsa/Workflows/Runtime/Core/Elsa.Workflows.Runtime.Core.csproj`
- `git diff --check`
