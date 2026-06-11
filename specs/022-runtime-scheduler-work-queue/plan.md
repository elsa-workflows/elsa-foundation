# Implementation Plan: Runtime Scheduler Work Queue

**Branch**: `codex/runtime-scheduler-work-queue` | **Date**: 2026-06-11 | **Spec**: [spec.md](./spec.md)

## Summary

Add the first non-noop command-processing slice for workflow execution agents. The in-process agent still owns single-writer delivery; this slice records accepted command envelopes as scheduler work in a provider-neutral queue so later slices can implement draining, activity invocation, checkpoints, and durable persistence.

## Scope

- Add scheduler work item/query models.
- Add scheduler work queue contract.
- Add in-memory scheduler work queue default.
- Add workflow execution command processor that converts envelopes into scheduler work.
- Register the scheduler queue and command processor in the runtime API feature.
- Add focused runtime tests.

## Non-Scope

- Full scheduler drain behavior.
- Activity invocation from scheduler work.
- Durable queue persistence.
- Distributed queue placement or leasing.
- Checkpoint commit integration.
- Bookmark or volatile-wait dispatch behavior.

## Constitution Check

| Gate | Status | Notes |
|---|---|---|
| Runtime must not depend on Design | PASS | Runtime.Core models/contracts/services only. |
| Provider-neutral contracts | PASS | Queue contract does not require a concrete queue framework. |
| Scope control | PASS | Records scheduler work only; no scheduler drain or activity execution. |
| Focused tests | PASS | Queue, processor, registration, and dependency-boundary tests. |

## Validation

- `dotnet test tests/Elsa/Workflows/Runtime/Tests/Elsa.Workflows.Runtime.Tests.csproj`
- `dotnet test tests/Elsa/Architecture/Elsa.Architecture.Tests.csproj`
- `dotnet build src/Elsa/Workflows/Runtime/Core/Elsa.Workflows.Runtime.Core.csproj`
- `git diff --check`
