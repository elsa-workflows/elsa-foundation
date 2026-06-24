# Implementation Plan: Workflow Definition Test Runs

**Branch**: `076-workflow-test-runs` | **Date**: 2026-06-24 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/076-workflow-test-runs/spec.md`

**Note**: This template is filled in by the `/speckit-plan` command. See `.specify/templates/plan-template.md` for the execution workflow.

## Summary

Add a designer-facing test-run path to `Elsa.Workflows.Publishing.Api`: read a workflow definition version through the Design persistence seam, compile the authored root activity into a runtime-owned `WorkflowExecutable`, mark that executable as transient/test-scoped, save it in an isolated transient store, and dispatch it through the runtime start-dispatch seam. Normal runtime execution by artifact id remains durable/published-artifact-only; test runs use a separate bridge request and cannot be promoted, scheduled, or listed as published executables.

## Technical Context

**Language/Version**: C# / .NET `net10.0`

**Primary Dependencies**: Existing Elsa mediator request handlers, FastEndpoints feature registration, `Elsa.Workflows.Design.*` persistence seams, `Elsa.Activities.Design.*` activity catalog seams, `Elsa.Workflows.Runtime.Core` execution dispatch contracts

**Storage**: In-memory runtime executable store for durable vertical-slice artifacts remains unchanged; add in-memory transient executable/test-run storage for this development slice

**Testing**: xUnit via existing `tests/Elsa/Workflows/Publishing/Api/Tests` and `tests/Elsa/Workflows/Runtime/Tests`

**Target Platform**: Elsa Server combined design/publishing/runtime development host

**Project Type**: Backend API/bridge feature inside existing publishing/runtime packages

**Performance Goals**: Test-run compilation and dispatch should complete within normal interactive designer expectations; invalid workflows should fail before execution dispatch.

**Constraints**: Runtime projects must not reference Workflows.Design; normal execute endpoint must keep resolving only durable workflow executable artifacts; test-run artifacts must be transient/test-scoped and excluded from durable published artifact views.

**Scale/Scope**: Single-host development slice; repeated designer test runs; no durable database migration or long-term test-run audit table in this unit.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **Workflows.Design/Runtime split (§E2.2)**: Runtime packages must not reference Design packages. PASS: test-run orchestration lives in `Elsa.Workflows.Publishing.Api`, the existing bridge that may read Design seams and drive Runtime seams.
- **Artifact-only runtime (§E2.6)**: Runtime execution must consume runnable artifacts rather than design state. PASS: accepted test runs compile a `WorkflowExecutable` before dispatch; Runtime dispatch and scheduler paths consume executable identity/artifact data, not design records.
- **Triplet separation (§E2.9)**: `WorkflowDefinitionState`, read projections, and `WorkflowExecutable` stay separate. PASS: workflow definition state is a source snapshot; the transient executable remains a runtime-owned derived form.
- **Bridge/adapters (§2.7)**: Bridge code must connect seams without making either side own the other. PASS: publishing/test-run handler depends on `.Core` and persistence contracts already used by the publish bridge.
- **Refactor/test discipline (§2.21, §2.23)**: Existing runtime/publishing behavior and tests must continue to pass; new logic-bearing services require focused tests. PASS: extract shared compiler behavior under publishing API and add tests for transient isolation, rejection, and registration.

Initial gate status: **PASS**. The design keeps the bridge outside Runtime and does not make Runtime load Design state.

## Project Structure

### Documentation (this feature)

```text
specs/076-workflow-test-runs/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── workflow-test-runs.md
└── tasks.md
```

### Source Code (repository root)

```text
src/Elsa/Workflows/Publishing/Api/
├── Contracts/
│   ├── IWorkflowExecutableCompiler.cs
│   ├── IWorkflowTestRunStore.cs
│   └── ITransientWorkflowExecutableStore.cs
├── Handlers/
│   ├── PublishWorkflowRequestHandler.cs
│   └── StartWorkflowTestRunRequestHandler.cs
├── Models/
│   ├── WorkflowExecutableCompileRequest.cs
│   ├── WorkflowTestRun.cs
│   └── WorkflowTestRunViews.cs
├── Requests/
│   └── StartWorkflowTestRun.cs
├── Services/
│   ├── InMemoryTransientWorkflowExecutableStore.cs
│   ├── InMemoryWorkflowTestRunStore.cs
│   └── WorkflowExecutableCompiler.cs
├── Endpoints/
│   └── TestRuns/
│       └── Start.cs
└── WorkflowsPublishingApiFeature.cs

src/Elsa/Workflows/Runtime/Core/
├── Contracts/IWorkflowExecutionStartDispatcher.cs
├── Contracts/IWorkflowExecutableStore.cs
├── Models/WorkflowExecutable.cs
├── Models/WorkflowExecutionStartDispatch.cs
└── Services/WorkflowExecutionStartDispatcher.cs

tests/Elsa/Workflows/Publishing/Api/Tests/
├── PublishWorkflowRequestHandlerTests.cs
├── WorkflowExecutableCompilerTests.cs
├── WorkflowTestRunRequestHandlerTests.cs
└── WorkflowsPublishingApiFeatureTests.cs

tests/Elsa/Workflows/Runtime/Tests/
└── RuntimeWorkflowExecutionStartDispatchTests.cs
```

**Structure Decision**: Keep the design-to-runtime orchestration in `Elsa.Workflows.Publishing.Api`, which is already the compile/publish bridge. Runtime Core receives only artifact-scope contract additions needed to distinguish durable and transient executable lookup without accepting a Design dependency.

## Complexity Tracking

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| None | N/A | N/A |
