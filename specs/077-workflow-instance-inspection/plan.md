# Implementation Plan: Workflow Instance Inspection

**Branch**: `codex/workflow-instance-inspection` | **Date**: 2026-06-24 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/077-workflow-instance-inspection/spec.md`

## Summary

Add a full workflow-instance inspection surface that keeps the existing instance list for triage, adds deep-linkable instance detail routes in Studio, and renders the executed workflow on a read-only designer canvas using the saved layout for the exact workflow definition version that produced the instance. The backend work completes the version-detail read contract by exposing version layout alongside authored state; runtime instance details remain runtime-owned and are joined with design-version data at the Studio/application layer.

## Technical Context

**Language/Version**: C# / .NET `net10.0`; TypeScript / React 19 in the Studio workflow module

**Primary Dependencies**: FastEndpoints via existing Elsa API base classes, Elsa mediator handlers, Workflows Design persistence stores, Workflows Runtime API views, Studio SDK HTTP client, React Flow (`@xyflow/react`)

**Storage**: Existing workflow definition version and version-layout stores; existing runtime workflow/activity/incident stores. No new persistence store or migration is required.

**Testing**: xUnit for backend contract/handler/endpoint behavior; Vitest + jsdom for Studio module rendering and navigation behavior

**Target Platform**: Elsa Server combined design/runtime development host plus `elsa-foundation-studio` modular React shell

**Project Type**: Backend API/read-contract extension plus frontend workflow-module UI enhancement

**Performance Goals**: Instance detail route should load the instance summary, activity history, incidents, activity catalog, and definition version snapshot within interactive Studio expectations for typical local-development instances. Missing layout must not block the runtime evidence view.

**Constraints**: Runtime packages must not reference Workflows.Design. Runtime instance details must remain runtime-state views; authored state/layout are read from Workflows.Design APIs and joined in Studio. Instance canvas is read-only and must not mutate definition drafts or versions.

**Scale/Scope**: Single selected workflow instance at a time; first implementation covers Flowchart and Sequence roots supported by the existing designer adapter. Advanced replay/path animation and edge-level execution semantics are deferred.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **Workflows.Design/Runtime split (§E2.2)**: PASS. Runtime API remains design-free; definition state/layout is served by Workflows.Design and consumed by Studio for visualization.
- **Artifact-only runtime (§E2.6.2)**: PASS. Runtime execution continues to depend only on runnable artifacts and runtime features; visualization traverses the executed instance → artifact/version identity → source design entities at the application layer, which the constitution explicitly permits.
- **Triplet separation (§E2.9)**: PASS. `WorkflowDefinitionState`, version read projections, runtime instance views, and `WorkflowExecutable` remain separate contracts.
- **Feature/API registration tests (§2.23.1)**: PASS with work required. Extending Workflows Design API registration/endpoint surface requires focused tests proving layout is available through the handler/endpoint path.
- **Implementation tests (§2.23.2)**: PASS with work required. Studio adapter/read-only canvas behavior and backend version-layout projection require focused tests.

Initial gate status: **PASS**. No violations requiring Complexity Tracking.

## Project Structure

### Documentation (this feature)

```text
specs/077-workflow-instance-inspection/
├── spec.md
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── workflow-instance-inspection.md
├── checklists/
│   └── requirements.md
└── tasks.md
```

### Source Code (repository roots)

```text
elsa-foundation/
├── src/Elsa/Workflows/Design/Api/
│   ├── Endpoints/Versions/Get.cs
│   ├── Handlers/GetVersionRequestHandler.cs
│   ├── Models/WorkflowDefinitionVersionDetailsView.cs
│   └── Projections/WorkflowViewProjections.cs
└── tests/Elsa/Workflows/Design/Tests/
    └── WorkflowDefinitionVersionDetailsTests.cs

elsa-foundation-studio/
└── src/Elsa.Studio.Workflows/Client/src/
    ├── api/workflows.ts
    ├── workflowTypes.ts
    ├── workflowAdapter.ts
    ├── module.tsx
    ├── __tests__/module.test.tsx
    └── __tests__/workflowAdapter.test.ts
```

**Structure Decision**: Keep backend state/layout reads in `Elsa.Workflows.Design.Api`, keep runtime instance details in `Elsa.Workflows.Runtime.Api`, and join them in the Studio workflow module. This preserves the Design/Runtime boundary and avoids adding a runtime dependency on design persistence.

## Complexity Tracking

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| None | N/A | N/A |
