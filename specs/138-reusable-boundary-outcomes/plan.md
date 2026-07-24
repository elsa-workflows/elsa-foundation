# Implementation Plan: Reusable Activity Boundary Outcomes

**Branch**: `codex/1012-reusable-boundary-outcomes` | **Date**: 2026-07-23 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/138-reusable-boundary-outcomes/spec.md`

## Summary

Add schema 2 to the `elsa.activity-graph` provider with explicit mappings from the resolved direct-entry activity's emitted outcome references to the reusable boundary's emitted public outcome references. Compilation validates and resolves those references, then pins runtime outcome names in the graph descriptor. Runtime propagates the selected mapped name through the existing structural completion checkpoint. Publication projects emitted public outcomes into the existing `elsa.outcomes` catalog facet. Elsa Foundation Studio adds an exact schema-2 graph editor contribution and mapping editor while retaining its schema-1 contribution unchanged.

## Technical Context

**Language/Version**: C# / .NET 10; TypeScript / React 19

**Primary Dependencies**: Elsa activity design/runtime/publishing abstractions; React; Vite; Vitest

**Storage**: Existing activity definition, manifest, compiled template, and catalog persistence; no new store

**Testing**: xUnit with `dotnet test`; Vitest/Testing Library with pnpm workspace scripts

**Target Platform**: Cross-platform .NET server/runtime and browser Studio

**Project Type**: Multi-project modular libraries plus web frontend

**Performance Goals**: Mapping validation and runtime selection are linear in the number of declared outcomes; no runtime definition lookup or additional persistence round trip

**Constraints**: Schema-1 manifests/artifacts remain compatible; compilation stays deterministic and atomic; runtime uses only pinned artifacts; no Studio-specific server contract

**Scale/Scope**: One Foundation provider, one runtime activity, one publishing projection, and the corresponding Studio schema-2 authoring surface

## Constitution Check

*GATE: Passed before Phase 0 research and re-checked after Phase 1 design.*

- **Status warning**: Both constitutions remain draft/provisional. This plan applies their current gates without treating them as ratified doctrine.
- **Design/runtime separation (§2.6.4; Elsa E2.2)**: PASS. Stable outcome references remain design-time; only resolved runtime names enter the compiled descriptor.
- **Artifact-only execution (Elsa E2.6)**: PASS. Runtime selection reads descriptor mappings and the child completion only.
- **Catalog authority (Elsa E2.8)**: PASS. Published contract outcomes project into the existing catalog facet consumed by Studio.
- **Provider-owned structure (Elsa E2.9)**: PASS. Mapping syntax, validation, and canonicalization remain inside `elsa.activity-graph`.
- **Compatibility/refactor cost (§2.16, §2.21.1)**: PASS. Schema 1 and runtime descriptor schema 1 remain readable; new fields are optional for old artifacts.
- **Testing (§2.23)**: PASS. Existing tests are preserved, new logic branches receive focused tests, and cross-boundary behavior receives an end-to-end harness test.
- **Sanctioned patterns (§2.24)**: PASS. This extends the existing design/runtime contract split and provider boundary; it does not add an architectural pattern.
- **Versioning (§4.2; activity-contract policy)**: PASS. New emitted outcomes remain subject to the existing major-version classification.
- **Post-design re-check**: PASS. No new service, event, contributor interface, persistence abstraction, package, or constitutional exception is introduced.

## Project Structure

### Documentation (this feature)

```text
specs/138-reusable-boundary-outcomes/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── activity-graph-schema-2.json
└── tasks.md
```

### Source Code (repository roots)

```text
src/Elsa/Activities/Graph/
├── Design/Models/ActivityGraphManifest.cs
├── Design/Services/GraphActivityProvider.cs
└── Runtime/
    ├── Activities/GraphActivity.cs
    └── Models/GraphActivityDescriptor.cs

src/Elsa/Activities/Design/Api/
├── Handlers/ListActivityAuthoringCatalogRequestHandler.cs
└── Models/ActivityAuthoringCatalogView.cs

src/Elsa/Workflows/Publishing/Api/Services/
├── ActivityDefinitionPublisher.cs
└── SourceOwnedActivityVersionPublisher.cs

tests/Elsa/Activities/Graph/Tests/
├── GraphActivityProviderTests.cs
└── GraphActivityExecutionTests.cs

tests/Elsa/Activities/Flowchart/Tests/ReusableBoundaryOutcomeFlowchartTests.cs
tests/Elsa/Workflows/Publishing/Api/Tests/ActivityDefinitionPublicationTests.cs

../elsa-foundation-studio/src/Elsa.Studio.Web/Client/src/sdk/index.ts

../elsa-foundation-studio/src/Elsa.Studio.Workflows/Client/src/
├── ActivityDefinitionDraftEditor.tsx
├── ActivityGraphImplementationEditor.tsx
├── activityGraphContribution.tsx
├── module.tsx
└── workflowAdapter.ts
```

**Structure Decision**: Extend the existing provider, runtime, publishing, and Studio modules in place. No new project, package, service, or persistence boundary is introduced. Foundation and Studio use separate worktrees and local commits because they are separate repositories.

## Complexity Tracking

No constitution violations require tracking.
