# Implementation Plan: Runtime Generator Contract

**Branch**: `codex/runtime-generator-contract` | **Date**: 2026-06-10 | **Spec**: [spec.md](./spec.md)

## Summary

Add the next Runtime Execution Seam addendum slice after completion propagation. This slice codifies generator activities as runtime-owned scheduler state: active generator registrations and generated-event work, without implementing the scheduler loop or trigger infrastructure.

## Technical Context

- `SchedulerState` already separates ordinary scheduled activity work, completion-drain work, volatile wait registrations, and continuation work.
- `ActivityExecution` remains the durable identity for one concrete execution of one executable node.
- Generators are in-workflow activities, not external triggers, so contracts must reference generator `ActivityExecution` IDs.
- Runtime architecture tests already guard against Design-owned authored workflow model dependencies.

## Constitution Check

| Gate | Status | Notes |
|---|---|---|
| Runtime must not depend on Design | PASS | This slice changes Runtime.Core models and runtime tests only. |
| Artifact/runtime-owned execution state | PASS | Generator state references workflow execution and activity execution IDs. |
| Focused tests for logic-bearing contracts | PASS | Add dedicated generator contract tests. |
| Scope control | PASS | Scheduler behavior, triggers, durable stores, and control-plane pause are out of scope. |

## Scope

- Add active generator registration contract.
- Add generated event contract with identity, sequence, durability, and metadata.
- Add scheduler generated-event work item contract.
- Extend `SchedulerState` with dedicated generator collections.
- Preserve scheduler collection normalization/snapshot behavior.
- Add focused tests for generator contracts.

## Out of Scope

- Full scheduler emission processing.
- External trigger infrastructure.
- Durable generated-event persistence.
- Control-plane pause/unpause implementation.
- Backpressure execution behavior.

## Validation

- `dotnet test tests/Elsa/Workflows/Runtime/Tests/Elsa.Workflows.Runtime.Tests.csproj`
- `dotnet test tests/Elsa/Architecture/Elsa.Architecture.Tests.csproj`
- `dotnet build src/Elsa/Workflows/Runtime/Core/Elsa.Workflows.Runtime.Core.csproj`
- `git diff --check`
