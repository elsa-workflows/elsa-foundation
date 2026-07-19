# Implementation Plan: Run Detail Input Evidence Workbench

**Branch**: `codex/run-detail-input-evidence` | **Date**: 2026-07-16 | **Spec**: [spec.md](./spec.md)

**Input**: Approved PRD in `specs/006-run-detail-input-evidence/spec.md`

## Summary

Deliver a shell-aware full-height Run detail workbench and a paired Activity > Inputs experience. Elsa Foundation will preserve frozen authored inputs on each per-publish Source Reference while retaining structured compiled bindings on the content-addressed executable, propagate ReferenceKey identity and sensitivity into replay-safe ordered runtime-evaluation evidence, and project source/evidence through independently protected endpoints. Elsa Foundation Studio will join those records by `inputKey`, render compact and expanded evidence with honest compatibility/anomaly states, consume optional read-only renderers from the public expression contribution slot with a generic fallback, and switch a Run-specific inspector between desktop split, medium modal drawer, and narrow focus modes based on workbench width.

## Technical Context

**Language/Version**: C#/.NET 10 in Elsa Foundation; TypeScript 5.6 and React 19 in Studio
**Primary Dependencies**: ASP.NET Core, Elsa runtime/design abstractions, System.Text.Json; React, TanStack Query, Lucide, Vite
**Storage**: Existing content-addressed executable, per-publish Source Reference sidecar, and workflow-execution persistence; backward-compatible optional fields, no migration-dependent read path
**Testing**: xUnit/.NET test projects; Vitest + Testing Library; Playwright/browser verification
**Target Platform**: Elsa server and browser-based Foundation Studio
**Project Type**: Cross-repository web application and runtime/API contract
**Performance Goals**: Input-row derivation linear in declared + source + evidence records; no unbounded payload fetch; responsive resize updates without layout thrash
**Constraints**: Bounded Diagnostic Snapshots; independent server-side authorization; immutable pinned source; arbitrary expression types; token-only module CSS; legacy records must remain readable
**Scale/Scope**: One Run detail route, executable-inspection contract, activity input capture/projection path, expression display contribution point, and targeted tests across two repositories

## Constitution Check

- **Modular UI contract — PASS**: Workflows owns the Run workbench and generic input/source presentation. Expression-specific renderers are contributed through an expression-owned public contract; no host-private CSS namespace is copied.
- **Workbench pattern fit — PASS**: This is the existing diagnostics/logs Run workbench. The plan strengthens its shell allocation and master canvas/detail inspector behavior.
- **Typography and token discipline — PASS**: Changes use the versioned `--studio-*`, `--studio-material-*`, and `--studio-glass-*` contracts. No raw colors, fonts, shadows, radii, or ad hoc status palette are introduced.
- **Accessible interaction — PASS**: Native buttons/disclosure semantics, keyboard resizing/panel switching, focus transfer on responsive activity selection, labelled protected/empty/error states, and reduced-motion compatibility are in scope.
- **Real-screen proof — PASS**: The actual `/workflows/instances/:id` route will be tested and captured at desktop, medium, and narrow workbench widths with console resize.

**Post-design re-check**: PASS. The canonical contracts, legacy states, module boundary, responsive transitions, and proof route are documented in Phase 1 artifacts.

## Delivery Phases

### Phase 0 — Research consolidation

1. Confirm shell/workbench height ownership and responsive defects.
2. Trace authored expression → runtime binding → pinned executable inspection.
3. Trace input materialization and Diagnostic Snapshot capture across invoke/resume/parent-completion paths.
4. Record security, expression-module, terminology, and payload-evidence ADR constraints.

Output: [research.md](./research.md)

### Phase 1 — Contract and design

1. Define canonical executable input-source and evaluation-evidence contracts.
2. Define ReferenceKey identity, source/evidence permission states, sensitivity, replay-idempotent evaluation ID/atomic sequence, and legacy compatibility.
3. Define client union-row derivation and renderer contribution contract.
4. Define shell-aware layout state and responsive transitions.

Outputs: [data-model.md](./data-model.md), [contracts](./contracts/), [quickstart.md](./quickstart.md)

### Phase 2 — Foundation delivery

1. Preserve authored source as opaque JSON in a Source Reference Authored Inputs Sidecar and keep structured compiled bindings on the executable, proving artifact-hash invariance.
2. Carry ReferenceKey, sensitivity, phase, evaluation ID/sequence, and per-input success/failure results through every materialization and snapshot persistence/projection path.
3. Expose source under `workflow-publishing.read` and evidence under `workflow-runtime.read`, removing protected fields before serialization.
4. Add compatibility and contract/runtime tests.

### Phase 3 — Studio delivery

1. Extend client types and executable-graph normalization without parsing summaries.
2. Build input union-row derivation and extend the public expression-editor contribution with an optional read-only renderer while keeping the fallback in Workflows.
3. Redesign compact/expanded Activity Inputs, ordered history, protected/missing/failure states, and activity-section order.
4. Convert only the Run detail variant to full-bleed shell allocation and route-specific container-driven desktop/medium/narrow state.
5. Add unit/component/layout/browser coverage.

### Phase 4 — Integration and proof

1. Run targeted Foundation tests, Studio tests, typecheck, lint, CSS lint, and builds.
2. Perform no-more-than-five self-review/fix iterations across correctness, security, accessibility, compatibility, and maintainability.
3. Verify the real route at desktop, medium, and narrow sizes and with bottom-console resizing.
4. Update tasks, tracking, issues, and PR notes with evidence.

## Project Structure

### Documentation

```text
specs/006-run-detail-input-evidence/
├── spec.md
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── roadmap.md
├── tracking.md
├── checklists/requirements.md
├── contracts/
│   ├── executable-input-source.md
│   ├── input-evaluation-evidence.md
│   └── studio-input-inspection.md
└── tasks.md
```

### Elsa Foundation

```text
../elsa-foundation/src/Elsa/Workflows/
├── Design/Core/Models/ArgumentState.cs
├── Publishing/Api/Services/RuntimeInputBindingCompiler.cs
├── Publishing/Api/Handlers/PublishWorkflowRequestHandler.cs
├── Runtime/Core/Models/
│   ├── RuntimeInputBinding.cs
│   └── ActivityExecutionInspectionValueSnapshot.cs
└── Runtime/Api/
    ├── Models/WorkflowExecutableInspectionViews.cs
    ├── Models/WorkflowExecutionViews.cs
    └── Services/WorkflowExecutableInspector.cs

../elsa-foundation/src/Elsa/Activities/Runtime/Services/ActivityOutputPublisher.cs
../elsa-foundation/test/               # targeted compiler, runtime, inspector/API tests
```

### Elsa Foundation Studio

```text
src/Elsa.Studio.Workflows/Client/src/
├── workflowTypes.ts
├── styles.css
└── workflow-editor/
    ├── WorkflowInstances.tsx
    ├── executableGraph.ts
    ├── pages.tsx
    ├── useRunDetailLayout.ts
    └── *.test.ts(x)

src/Elsa.Studio.Web/Client/src/app/styles.css
tests/                                 # route/browser coverage where appropriate
```

**Structure Decision**: Keep behavioral bindings in the Foundation executable, non-behavioral authored provenance on the Source Reference per ADRs 0038/0039, runtime evidence in execution state, and route/module presentation in Studio. Reuse the shell’s existing grid allocation and bottom-panel sizing. Extend the existing SDK expression-editor slot with an optional read-only source surface instead of creating a Workflows-owned registry. Use a dedicated Run layout hook/storage namespace so other designer surfaces are unchanged.

## Integration Order

1. Foundation models and serialization compatibility.
2. Foundation compiler/materialization/capture/projection behavior and tests.
3. Studio client contracts and normalization.
4. Studio input evidence UI and expression display.
5. Studio layout/responsiveness.
6. Cross-repository verification and PRs. Studio must not merge before its required Foundation contract is available.

## Risk Controls

- New fields and Source Reference sidecar are additive and nullable/defaultable for old persisted records.
- Authored sidecar uses verbatim `JsonElement` per ADR 0035 and never contributes to executable hashing.
- Legacy records produce explicit “legacy identity/provenance unavailable” states.
- Sensitive content is removed server-side, including previews and summaries.
- Stable keys are the activity input `ReferenceKey` within executable-node/activity-execution scope; the UI never derives identity from labels.
- Evaluation IDs deduplicate scheduler replay and sequence allocation is atomic with checkpoint persistence.
- Histories remain bounded by existing evidence persistence/capture policy.
- Container observation and persistence are Run-specific and do not alter the global shell or shared designer hook.
- Foundation remains closed over supported compiled binding variants; Studio generically tolerates future API discriminators.

## Complexity Tracking

No constitution violations require justification.
