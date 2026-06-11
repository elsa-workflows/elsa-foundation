# Implementation Plan: Runtime Execution Agent Provider Contract

**Branch**: `codex/runtime-agent-provider-contract` | **Date**: 2026-06-11 | **Spec**: [spec.md](./spec.md)

## Summary

Add the actor-style execution agent/provider contract slice. This encodes single-writer mailbox semantics, command delivery metadata, activation requests, provider capabilities, and passivation boundaries without implementing a provider or depending on any actor framework.

## Technical Context

- `IWorkflowExecutionAgent` and `IWorkflowExecutionAgentProvider` already exist as minimal placeholders.
- `WorkflowExecutionCommand` carries command kind and payload but not delivery metadata.
- `SchedulerState` remains the single-writer continuation state.
- Elsa checkpoint state remains the durable source of truth.

## Constitution Check

| Gate | Status | Notes |
|---|---|---|
| Runtime must not depend on Design | PASS | This slice changes Runtime.Core models/contracts and runtime tests only. |
| Framework-neutral runtime boundary | PASS | Contracts do not reference actor frameworks. |
| Focused tests for logic-bearing contracts | PASS | Add dedicated agent provider contract tests. |
| Scope control | PASS | Provider implementation, placement, and execution behavior are out of scope. |

## Scope

- Add command delivery envelope/result contracts.
- Add activation request, descriptor, capabilities, and passivation request contracts.
- Update execution agent/provider interfaces to use the new contracts.
- Add focused runtime agent-provider contract tests.

## Out of Scope

- In-process mailbox implementation.
- Distributed actor provider implementation.
- Command execution.
- Durable persistence implementation.

## Validation

- `dotnet test tests/Elsa/Workflows/Runtime/Tests/Elsa.Workflows.Runtime.Tests.csproj`
- `dotnet test tests/Elsa/Architecture/Elsa.Architecture.Tests.csproj`
- `dotnet build src/Elsa/Workflows/Runtime/Core/Elsa.Workflows.Runtime.Core.csproj`
- `git diff --check`
