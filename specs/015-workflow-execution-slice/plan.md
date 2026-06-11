# Implementation Plan: Workflow Execution Vertical Slice

> Supersession note (2026-06-11): workflow-level executable graph planning in this slice is
> superseded by
> [070-workflow-root-activity-contract](../070-workflow-root-activity-contract/spec.md).

**Branch**: `015-workflow-execution-slice` | **Date**: 2026-06-10 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `specs/015-workflow-execution-slice/spec.md`

## Summary

Add a bounded, demonstrable runtime vertical slice: existing Design REST endpoints create workflow definitions and versions from JSON; a new Publishing endpoint compiles a workflow version into a runtime-owned `WorkflowExecutable`; a new Runtime endpoint executes the artifact and reports ordered activity execution results. The slice supports literal-only sequential workflows first, explicitly rejecting unsupported graph and binding shapes.

## Technical Context

**Language/Version**: C# / .NET 10

**Primary Dependencies**: FastEndpoints via `Elsa.Api.FastEndpoints`, CShells feature registration, existing Elsa Mediator, existing activity construction seam (`IActivityFactory`)

**Storage**: In-memory `IWorkflowExecutableStore` for this slice; durable artifact persistence is out of scope

**Testing**: xUnit focused unit tests plus selected endpoint/feature registration tests; validation with `dotnet test` on affected projects and architecture tests

**Target Platform**: `Elsa.Server` ASP.NET Core host

**Project Type**: Modular backend/runtime feature

**Performance Goals**: Demo-oriented synchronous execution; a two-node sequential workflow should execute within one HTTP request under normal local development conditions

**Constraints**:
- `Elsa.Workflows.Runtime.*` must not reference `Elsa.Workflows.Design.*`.
- Runtime execution must consume `WorkflowExecutable` only.
- Publishing may read Design and Activity catalog data because it is the bridge.
- Literal-only, single-start, sequential graph support is intentional for this work unit.

**Scale/Scope**: One connected sequential workflow graph with primitive CLR activities, validated through a two-step `WriteLine` demo.

## Constitution Check

| Gate | Status | Notes |
|---|---|---|
| §E2.2 Runtime must not depend on Design | PASS | Runtime contracts/services stay in `Elsa.Workflows.Runtime.Core` and Runtime API; Publishing is the bridge that reads Design. |
| §E2.6 Artifact-only runtime | PASS | Runtime endpoint receives an artifact id and loads `WorkflowExecutable` from runtime-owned store only. |
| §E2.9 Triplet separation | PASS | `WorkflowDefinitionState` remains authored source; `WorkflowExecutable` is derived runtime artifact; endpoint views remain projections. |
| Framework §2.6 bridge/adapter | PASS | `Elsa.Workflows.Publishing.Api` connects Design/Activity catalog seams to Runtime artifact seam. |
| Framework §2.23 tests | PASS | Add logic-bearing unit tests, feature registration tests, and architecture dependency tests. |
| Extension-point catalog obligation | PASS | Update Runtime.Core catalog if new replacement contracts are added. |

## Project Structure

### Documentation (this feature)

```text
specs/015-workflow-execution-slice/
├── spec.md
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── rest-api.md
├── checklists/
│   └── requirements.md
└── tasks.md
```

### Source Code

```text
src/Elsa/Workflows/Runtime/Core/
├── Contracts/
│   ├── IWorkflowExecutableStore.cs
│   └── IWorkflowExecutor.cs
├── Models/
│   ├── WorkflowExecutable.cs
│   └── WorkflowExecutionResult.cs
├── Services/
│   ├── InMemoryWorkflowExecutableStore.cs
│   ├── SequentialWorkflowExecutor.cs
│   └── SimpleActivityExecutionContext.cs
└── EXTENSION_POINTS.md

src/Elsa/Workflows/Runtime/Api/
├── Endpoints/Execute.cs
├── Models/WorkflowExecutionViews.cs
├── Requests/ExecuteWorkflow.cs
├── Constants/RouteConstants.cs
├── WorkflowsRuntimeApiFeature.cs
└── Elsa.Workflows.Runtime.Api.csproj

src/Elsa/Workflows/Publishing/Api/
├── Endpoints/PublishWorkflow.cs
├── Handlers/PublishWorkflowRequestHandler.cs
├── Models/PublishedWorkflowView.cs
├── Requests/PublishWorkflow.cs
└── _requests/workflow-execution-slice.http

src/Apps/Elsa.Server/Program.cs

tests/Elsa/Workflows/Runtime/Tests/
tests/Elsa/Workflows/Publishing/Api/Tests/
tests/Elsa/Architecture/
```

**Structure Decision**: Runtime-owned contracts and simple default implementations go in `Elsa.Workflows.Runtime.Core` because this repo currently has Runtime.Core as the runtime composition surface for state and service contracts. REST exposure is isolated in a new `Elsa.Workflows.Runtime.Api` feature. Publishing remains the bridge and may reference Design persistence contracts plus Runtime.Core.

## Complexity Tracking

No constitution gate violation expected.

## Implementation Notes

- Add `ExecutableEdge` to `WorkflowExecutable` so Runtime does not need Design `ActivityConnection`.
- Add runtime input value type metadata sufficient to materialize typed `InputArgument<T>` for literal inputs.
- Keep graph compiler strict: exactly one start node, no cycles, no branching/fan-out, all nodes reachable, one next node maximum.
- Use an in-memory artifact store keyed by artifact id, with deterministic artifact hash based on executable content.
- Execute activities synchronously and in order; capture activity status and exception messages in the execution result.
