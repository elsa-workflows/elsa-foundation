# Implementation Plan: Runtime Executable Artifact And Execution State Contracts

**Branch**: `codex/runtime-executable-state-contracts` | **Date**: 2026-06-10 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `specs/007-runtime-executable-state-contracts/spec.md`

## Summary

Define the first Runtime Execution Seam contracts in `Elsa.Workflows.Runtime.Core`: a runtime-owned `WorkflowExecutable`, runtime executable node identity, split workflow/activity/scheduler/durable-value state, named checkpoints with policy hooks, and a workflow execution agent/provider abstraction. Add focused tests and structural guards proving these contracts do not depend on `Elsa.Workflows.Design.*` authored workflow models.

## Technical Context

**Language/Version**: C# / .NET 10 (`net10.0`)
**Primary Dependencies**: Existing Runtime.Core references only: `Elsa.Activities.Runtime.Core`, `Elsa.Expressions.Core`, plus BCL `System.Text.Json`
**Storage**: No persistence implementation in this slice; durable state contracts only
**Testing**: xUnit focused unit/contract tests and existing architecture guard tests
**Target Platform**: Elsa server/library runtime packages
**Project Type**: Modular multi-project .NET library
**Performance Goals**: N/A for contract slice
**Constraints**: `Elsa.Workflows.Runtime.*` must not depend on `Elsa.Workflows.Design.*`; no full scheduler/bookmark/outbox behavior
**Scale/Scope**: Runtime.Core contract additions, one workflow runtime test project, architecture guard updates, Speckit artifacts

## Constitution Check

| Gate | Status | Note |
|---|---|---|
| Elsa §E2.2 Runtime must not depend on Design | PASS | Contracts live in `Elsa.Workflows.Runtime.Core`; tests enforce no Design references or authored model names. |
| Elsa §E2.6 artifact-only runtime | PASS | `WorkflowExecutionState` pins `WorkflowExecutableIdentity`; no design document is loaded or stored. |
| Elsa §E2.9 triplet separation | PASS | `WorkflowExecutable` is separate from `WorkflowDefinitionState` and read projections. |
| Framework §2.23 tests for new logic/classes | PASS | Contract tests cover construction and structural invariants; no behavior-heavy implementation is introduced. |

No unjustified violations.

## Project Structure

### Documentation

```text
specs/007-runtime-executable-state-contracts/
├── spec.md
├── plan.md
├── tasks.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── runtime-contracts.md
└── checklists/
    └── requirements.md
```

### Source Code

```text
src/Elsa/Workflows/Runtime/Core/
├── Constants/RuntimeCheckpointNames.cs
├── Contracts/
│   ├── IRuntimeCheckpointPersistencePolicy.cs
│   ├── IRuntimeCheckpointWriter.cs
│   ├── IWorkflowExecutionAgent.cs
│   └── IWorkflowExecutionAgentProvider.cs
├── Models/
│   ├── ActivityExecutionState.cs
│   ├── DurableValueState.cs
│   ├── RuntimeCheckpoint.cs
│   ├── SchedulerState.cs
│   ├── WorkflowExecutable.cs
│   ├── WorkflowExecutionCommand.cs
│   └── WorkflowExecutionState.cs
└── Services/
    └── ImmediateRuntimeCheckpointPersistencePolicy.cs

tests/Elsa/Workflows/Runtime/Tests/
├── Elsa.Workflows.Runtime.Tests.csproj
├── RuntimeContractTests.cs
└── RuntimeDependencyBoundaryTests.cs
```

## Implementation Notes

- Keep all models runtime-owned and immutable-by-default records.
- Store `AuthoredActivityId` only as a trace/link field; execution and scheduling use `ExecutableNodeId` and `ActivityExecutionId`.
- Use JSON-first value payloads or external references for `DurableValueState`; do not model raw activity outputs as durable state.
- Agent abstractions name the actor-style semantic boundary while leaving actor frameworks as provider implementations.

## Validation

Run:

```bash
dotnet test tests/Elsa/Workflows/Runtime/Tests/Elsa.Workflows.Runtime.Tests.csproj
dotnet test tests/Elsa/Architecture/Elsa.Architecture.Tests.csproj
```
