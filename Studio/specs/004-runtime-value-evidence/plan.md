# Implementation Plan: Runtime Value Evidence

**Branch**: `codex/runtime-activity-inputs` | **Date**: 2026-07-09 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/004-runtime-value-evidence/spec.md`

**Note**: This template is filled in by the `/speckit-plan` command. See `.specify/templates/plan-template.md` for the execution workflow.

## Summary

Runtime diagnostics should capture bounded Diagnostic Snapshots for workflow and activity input/output evidence by default, subject to Host Policy caps and Studio-managed requested settings. The implementation should extend the existing Elsa foundation runtime payload capture policy and activity execution inspection projection, then update Elsa Foundation Studio's Workflows module to configure requested/effective diagnostics settings and render snapshot evidence in the run details Activity inspector.

## Technical Context

**Language/Version**: TypeScript 5.6, React 19.1, C#/.NET projects in the Studio host and cross-repository Elsa foundation runtime.

**Primary Dependencies**: React, TanStack Query, Vite, Vitest, lucide-react, `@elsa-workflows/studio-sdk`, `@elsa-workflows/studio-ui`, Elsa foundation runtime API contracts.

**Storage**: Studio owns no local persistence. Backend work uses existing Elsa foundation runtime inspection projections/stores for activity execution evidence and backend settings persistence for Workflow Runtime Diagnostics Settings.

**Testing**: Vitest for Studio Workflows UI and API helpers; `pnpm lint` and `pnpm --filter @elsa-workflows/studio-workflows test` in this repo. Cross-repository backend tests use xUnit/dotnet test in `elsa-foundation`.

**Target Platform**: Browser-based Elsa Studio SPA served by Elsa Studio host, backed by Elsa Server runtime/workflow-management APIs.

**Project Type**: Modular web application plus cross-repository backend runtime/API feature.

**Performance Goals**: Activity inspection remains lazy-loaded per selected activity; Diagnostic Snapshot payloads are bounded before persistence; Studio renders compact summaries by default and expands trees on demand.

**Constraints**: Module CSS must use `--studio-*` tokens and pass `pnpm lint:css`; Workbench diagnostics UI must remain dense and inspectable; settings affect future capture events only; Full Payload remains Host Policy and permission gated; Runtime Payload Reference resolution is deferred.

**Scale/Scope**: First slice covers tenant/module-wide Workflows runtime diagnostics settings and activity input/output evidence rendering for workflow run details. Per-workflow overrides, provider-backed references, reference reveal/download, audit for reference resolution, retention policy, and Full Payload productization are follow-up issue seeds.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **Modular UI contract**: Pass. The Studio slice stays inside the Workflows module and reuses Studio endpoint context, TanStack Query hooks, Workflows page patterns, inspector sections, and token-governed module CSS.
- **Workbench pattern fit**: Pass. Runtime value evidence is a diagnostics/logs extension of the existing workflow run details master/detail workbench and Activity inspector.
- **Typography and token discipline**: Pass. New CSS must extend `src/Elsa.Studio.Workflows/Client/src/styles.css` using `--studio-*` surface, text, border, focus, and status tokens.
- **Accessible interaction**: Pass. Snapshot trees and settings controls must support keyboard expansion, focus visibility, disabled/loading/error states, labels, and screen-reader readable marker text.
- **Real-screen proof**: Pass. Proof screen is `WorkflowInstanceDetailsPage` Activity tab with a selected completed activity, plus the Workflows runtime diagnostics settings page/section.

## Project Structure

### Documentation (this feature)

```text
specs/004-runtime-value-evidence/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   ├── activity-execution-inspection-api.md
│   ├── diagnostic-snapshot.md
│   └── workflow-runtime-diagnostics-settings.md
└── checklists/
    └── requirements.md
```

### Source Code (repository root)

```text
src/Elsa.Studio.Workflows/
├── Client/src/
│   ├── api/workflows.ts
│   ├── workflowTypes.ts
│   ├── ActivityAvailabilityPage.tsx
│   ├── workflow-editor/WorkflowInstances.tsx
│   ├── module.tsx
│   ├── styles.css
│   └── __tests__/
│       ├── workflowActivityExecutionDetails.test.tsx
│       └── module.test.tsx
└── Elsa.Studio.Workflows.csproj

src/Elsa.Studio.Web/
├── Client/src/app/runtime.ts
└── Program.cs

tests/Elsa.Studio.Tests/
└── Elsa.Studio.Tests.csproj
```

Cross-repository backend implementation target:

```text
/Users/sipke/Projects/Elsa/elsa-foundation/
├── src/Elsa/Workflows/Runtime/Core/
├── src/Elsa/Workflows/Runtime/Services/
├── src/Elsa/Workflows/Runtime/Api/
├── src/Elsa/Activities/Runtime/Services/
├── src/Elsa/Persistence/Groundwork/Stores/
└── tests/Elsa/
```

**Structure Decision**: This feature is split by ownership. Elsa foundation owns capture policy, snapshot generation, persistence, settings APIs, and activity execution inspection response shape. Elsa Foundation Studio owns Workflows runtime diagnostics settings UI, client contracts/hooks, and Activity inspector rendering.

## Phase 0 Research Decisions

See [research.md](research.md).

## Phase 1 Design Artifacts

- [data-model.md](data-model.md)
- [quickstart.md](quickstart.md)
- [contracts/workflow-runtime-diagnostics-settings.md](contracts/workflow-runtime-diagnostics-settings.md)
- [contracts/diagnostic-snapshot.md](contracts/diagnostic-snapshot.md)
- [contracts/activity-execution-inspection-api.md](contracts/activity-execution-inspection-api.md)

## Implementation Approach

1. Backend foundation: add `DiagnosticSnapshot` to runtime payload capture modes, introduce bounded snapshot node models and generation service, and change default input/output policy from `None` to `DiagnosticSnapshot` where Host Policy allows it.
2. Backend foundation: add Workflow Runtime Diagnostics Settings API that returns requested/effective/host policy state, validates level ordering, and applies changes prospectively only.
3. Backend foundation: project Diagnostic Snapshot values through existing `ActivityExecutionInspectionValueSnapshot` and `ActivityExecutionInspectionValueSnapshotView` without exposing raw full payloads in snapshot mode.
4. Studio Workflows: extend `workflowTypes.ts` with explicit evidence levels, settings DTOs, value evidence states, and Diagnostic Snapshot node union.
5. Studio Workflows: add query/mutation helpers in `api/workflows.ts` following the existing activity availability settings pattern.
6. Studio Workflows: add a Workflows runtime diagnostics configuration page or grouped settings section using the existing Workflows workbench/settings styling.
7. Studio Workflows: replace the current Activity-input-only payload preview with input/output evidence sections and a compact, accessible Diagnostic Snapshot tree renderer.
8. Tests: cover settings hooks/UI, requested/effective rendering, snapshot tree node rendering, redaction/truncation/permission-hidden states, malformed/future node handling, and backend policy/snapshot bounds.

## Constitution Check Re-evaluation

- **Modular UI contract**: Still pass. No host-private CSS or isolated component system is introduced.
- **Workbench pattern fit**: Still pass. The Activity inspector remains a diagnostics detail panel; runtime diagnostics settings use a configuration workbench pattern.
- **Typography and token discipline**: Still pass. Styling work is scoped to Workflows module CSS and token-governed Studio primitives.
- **Accessible interaction**: Still pass. The plan includes keyboard and screen-reader behavior for expandable evidence and settings controls.
- **Real-screen proof**: Still pass. Validation requires the actual workflow run details Activity tab and the Workflows runtime diagnostics settings screen.

## Complexity Tracking

No constitution violations require justification.
