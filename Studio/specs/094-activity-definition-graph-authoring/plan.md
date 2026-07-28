# Implementation Plan: Activity Definition Graph Authoring

**Branch**: `codex/094-activity-definition-graph-authoring` | **Date**: 2026-07-28 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/094-activity-definition-graph-authoring/spec.md`

## Summary

Replace the bespoke Activity Graph implementation editor with a controlled graph-authoring workspace shared with workflow authoring through a resource-neutral document adapter. Keep workflow and activity-definition persistence/lifecycle hosts separate, add a creation experience centered on composition rather than provider schemas, provide creatable authorized-category suggestions, support full authoring-draft JSON, and align Activity Graph boundary mappings with Elsa's multi-outcome semantics. A small coordinated Foundation change removes the current prohibition on several graph outcomes mapping to one public outcome.

## Technical Context

**Language/Version**: TypeScript 5.6, React 19.1; coordinated Foundation change in C# on the repository's current .NET target

**Primary Dependencies**: Vite 7, TanStack Query 5, `@xyflow/react` 12, Studio SDK/UI/code-editor workspace packages, Foundation Activity Graph design/runtime services

**Storage**: Existing exact-revision Activity Definition draft APIs plus optional identity-scoped local recovery; no new Studio persistence store

**Testing**: Vitest/jsdom component and unit tests, Playwright browser tests, Foundation xUnit tests

**Target Platform**: Modern evergreen desktop browsers hosted by Elsa Foundation Studio; Elsa Foundation server

**Project Type**: Modular web application with a coordinated server-side domain change

**Performance Goals**: Preserve interactive canvas movement at 60 fps for existing acceptance fixtures; avoid additional catalog requests when cached data satisfies category and palette needs; no autosave request for invalid/unapplied JSON

**Constraints**: Exact-revision autosave and recovery must remain authoritative; legacy schema 1 is never implicitly migrated; visual authoring must preserve unknown provider/layout fields; all styles use `--studio-*` tokens; workflow lifecycle UI must not leak into activity authoring

**Scale/Scope**: Workflows module, Studio SDK contribution contract, focused browser fixture, one Foundation provider/runtime rule change, five independently testable user journeys

## Constitution Check

*GATE: Passed before Phase 0 and re-checked after Phase 1 design.*

- **Modular UI contract — PASS**: The plan extracts a shared `GraphAuthoringWorkspace` from the real Workflow screen and exposes a controlled document/host contract. Module contributions declare supported resource kinds rather than copying host CSS or designer internals.
- **Workbench pattern fit — PASS**: Activity Definition editing becomes a split authoring workbench with palette, central Designer/Public Interface/Code views, Inspector/Runtime surfaces, and a bottom diagnostics panel.
- **Typography and token discipline — PASS**: Existing Workflow workbench surfaces and public Studio tokens are reused. New activity-specific rules use only `--studio-*` tokens and existing Workflows aliases backed by them.
- **Accessible interaction — PASS**: The design retains canvas/palette keyboard behavior, tab semantics, visible focus, diagnostic focus/return, disabled reasons, conflict handling, JSON validation, and navigation confirmation.
- **Real-screen proof — PASS**: Both the workflow designer and Activity Definition Graph editor are exercised in focused component tests and a real themed browser fixture.

**Post-design re-check**: PASS. The adapter, host boundaries, JSON projection, contribution filtering, mappings, exact-revision transitions, and cross-repository rule are defined in Phase 1 artifacts without a constitution exception.

## Delivery Phases

### Phase 0 — Research consolidation

1. Trace the existing Activity Definition draft host, provider contribution contract, workflow canvas/scope/inspector ownership, category catalog, and Foundation mapping validator.
2. Confirm which Foundation and Studio contracts already support schema 2, multi-outcome contracts, atomic creation, draft replacement, layout, migrations, and test runs.
3. Record extraction, adapter, JSON, mapping, category, history, and compatibility decisions.

Output: [research.md](./research.md)

### Phase 1 — Contracts and state design

1. Define the controlled graph workspace and document adapter.
2. Define Activity Graph payload/layout projection, root ownership, contract expression scope, and contribution filtering.
3. Define authoring-draft JSON apply/reset/blocking transitions.
4. Define outcome/output boundary invariants and the coordinated Foundation change.
5. Define runnable quickstart and browser acceptance paths.

Outputs: [data-model.md](./data-model.md), [contracts](./contracts/), [quickstart.md](./quickstart.md)

### Phase 2 — Test-first implementation

1. Add failing Foundation tests for convergent boundary outcomes, then update design/runtime validation.
2. Add adapter round-trip tests and extract the controlled workspace with the workflow host unchanged.
3. Replace Activity Graph's bespoke editor with the activity host and adapter; preserve exact-revision autosave through `onChange`.
4. Rework creation, category combobox, legacy/latest-format capability selection, and composition cards.
5. Add Public Interface mappings, shared expression scope, multi-outcome behavior, and contribution resource filtering.
6. Add activity authoring-draft JSON, dirty/invalid gating, layout reconciliation, and history reset boundaries.
7. Integrate diagnostics and activity-specific test/runtime surfaces.

### Phase 3 — Verification and landing

1. Run focused Foundation and Studio tests after each slice.
2. Run Studio typecheck, lint/CSS lint, build, focused Playwright proof, and Foundation build/test for touched projects.
3. Raise dependency-aware Foundation and Studio PRs.
4. Run the requested self-review loop after the PRs exist, repair all actionable findings, and rerun affected checks after every batch.
5. Wait for required CI, merge Foundation first and Studio second, then verify both commits on `main`.

## Project Structure

### Documentation

```text
specs/094-activity-definition-graph-authoring/
├── spec.md
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   ├── activity-draft-json.md
│   ├── activity-graph-boundaries.md
│   └── graph-authoring-host.md
├── checklists/
│   └── requirements.md
└── tasks.md
```

### Studio Source

```text
src/Elsa.Studio.Web/Client/src/sdk/
└── index.ts

src/Elsa.Studio.Workflows/Client/src/
├── ActivityDefinitionCreateDialog.tsx
├── ActivityDefinitionDraftEditor.tsx
├── ActivityDefinitionContractEditor.tsx
├── ActivityGraphImplementationEditor.tsx
├── ActivityDefinitionDraftCodeView.tsx
├── activityDefinitionDraftJson.ts
├── activityGraphContribution.tsx
├── activityGraphDocumentAdapter.ts
├── activityGraphOutcomeMappings.ts
├── graph-authoring/
│   ├── GraphAuthoringWorkspace.tsx
│   ├── graphDocumentAdapter.ts
│   ├── GraphAuthoringInspector.tsx
│   └── useGraphAuthoringCanvas.ts
├── workflow-editor/
│   ├── WorkflowEditor.tsx
│   └── useWorkflowCanvas.ts
└── __tests__/
    ├── activityDefinitionAuthoring.test.tsx
    ├── activityDefinitionCreation.test.tsx
    ├── activityDefinitionDraftJson.test.ts
    ├── activityGraphDocumentAdapter.test.ts
    ├── activityGraphImplementationEditor.test.tsx
    ├── activityGraphOutcomeMappings.test.ts
    ├── graphAuthoringWorkspace.test.tsx
    └── workflowEditorUx.test.tsx

tests/browser/
├── src.tsx
└── activity-definition-graph-authoring.spec.ts
```

### Foundation Source

```text
src/Elsa/Activities/Graph/
├── Design/
│   ├── GraphActivityProvider.cs
│   └── Tests/
└── Runtime/
    ├── GraphActivityDescriptor.cs
    └── Tests/
```

**Structure Decision**: The workflows module owns graph authoring because it already owns the authoritative workflow designer and Activity Definition host. `GraphAuthoringWorkspace` is a controlled presentation/interaction component; workflow and Activity Definition adapters translate their resource documents and keep persistence outside. The Studio SDK gains only resource-scope metadata/props needed by host contributions. Foundation changes only its Activity Graph mapping invariant and tests.

## Integration Order

1. Foundation failing tests and convergent-outcome rule.
2. Studio graph document adapter tests.
3. Workflow-host extraction with no visual/behavioral regression.
4. Activity Graph host and canvas replacement.
5. Creation and category flow.
6. Public Interface and boundary mappings.
7. Draft JSON and history/gating transitions.
8. Diagnostics/runtime integration and browser proof.
9. Full verification, PRs, self-review loop, CI, Foundation merge, Studio merge.

## Risk Controls

- Migrate the existing workflow host onto the extracted workspace before enabling the activity host.
- Preserve provider payload and layout unknown fields in every adapter and JSON round trip.
- Keep Activity Definition `onChange` as the only workspace-to-autosave boundary.
- Capability-gate latest-schema creation and explicit legacy migration; never infer support.
- Use stable reference keys for public inputs, outputs, outcomes, root outcomes, and mappings.
- Test source uniqueness and target convergence in Studio and Foundation.
- Keep invalid JSON outside the autosave signature and require explicit Apply or Reset.
- Reset local history on conflict, migration, or accepted external replacement.
- Run existing broad workflow designer tests after each extraction batch.
- Land Foundation's permissive runtime/design invariant before Studio exposes convergent target selection.

## Complexity Tracking

No constitution violations require justification.
