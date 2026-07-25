# Implementation Plan: Activity Inspector Tabs

**Branch**: `codex/activity-inspector-tabs` | **Date**: 2026-07-25 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `specs/093-activity-inspector-tabs/spec.md`

## Summary

Replace the workflow-authoring activity Inspector's single long scroll surface with a fixed activity context and a nested, accessible tab set ordered as Inputs, Outputs, conditional Variables, conditional Slots, Details, and Version. Keep the active tab at workflow-editor session scope so it survives outer-panel navigation, retain inactive tab panels so tab-local state and scroll positions survive tab switches, and reset the Inspector subtree when activity identity changes so transient state cannot leak between nodes.

## Technical Context

**Language/Version**: TypeScript 5.6, React 19

**Primary Dependencies**: `@elsa-workflows/studio-ui` tab keyboard and element-ID primitives, `@elsa-workflows/studio-sdk`, Lucide, existing Workflows module Inspector editors

**Storage**: No persisted storage; active inner-tab state is local to the current workflow editor session

**Testing**: Vitest 2 with React DOM test harnesses; Playwright 1.61 for real-browser layout, keyboard, and visual verification

**Target Platform**: Modern desktop browsers supported by Elsa Foundation Studio

**Project Type**: Modular browser application

**Performance Goals**: Tab selection and activity selection remain synchronous at normal Inspector scale; no additional network requests or persisted state; inactive panels do not duplicate editor data loading

**Constraints**: Preserve existing authoring/autosave/undo behavior; keep all styles on the `--studio-*`/`--wf-*` token contracts; no new nested Slot or Contribution API; no page-level horizontal overflow at the minimum expanded Inspector width

**Scale/Scope**: One workflow-authoring Inspector, up to six inner tabs, existing activity input/output/variable/slot/version editors, focused unit/component coverage, and one browser fixture

## Constitution Check

*GATE: Passed before Phase 0 and re-checked after Phase 1 design.*

- **Modular UI contract — PASS**: The design composes the shared `StudioTabs` and `StudioTabPanel` primitives. Their public contract gains narrowly scoped base-ID, class, and mounted-hidden options needed by module-owned panels. Existing outer panel tabs remain unchanged; no module copies host CSS or gains a new contribution contract.
- **Workbench pattern fit — PASS**: The workflow designer remains a split configuration workbench. The change improves the existing right-hand Inspector without changing the workbench archetype.
- **Typography and token discipline — PASS**: New Inspector layout rules reuse existing Workflows aliases backed by the public Studio token contract. No raw colors, fonts, radii, shadows, or status treatments are introduced.
- **Accessible interaction — PASS**: The plan covers labelled tablist/tab/tabpanel relationships, roving keyboard focus, fixed context, pointer-independent access, empty/loading/unavailable states, conditional-tab fallback, and state reset between activities.
- **Real-screen proof — PASS**: The actual activity Inspector is exercised through a browser fixture at constrained width, including horizontal tab reachability, fixed context, independent body scrolling, and screenshot review.

**Post-design re-check**: PASS. The UI-state model, interaction contract, conditional availability, accessibility linkage, transient-state lifecycle, and browser proof are defined in the Phase 1 artifacts with no constitution exception.

## Delivery Phases

### Phase 0 — Research consolidation

1. Trace current Inspector content ownership, conditional capabilities, and outer-panel lifecycle.
2. Confirm shared tab keyboard and element-ID behavior for a fully linked inner tablist and tabpanels.
3. Identify existing tests that encode the long-page layout and must be migrated.
4. Record state ownership, mounting, reset, empty-state, and styling decisions.

Output: [research.md](./research.md)

### Phase 1 — Interaction design and contracts

1. Define the session-local active-tab state and activity-change transitions.
2. Define the tab availability/order and content ownership contract.
3. Define accessible tab semantics, fixed/scrolling regions, and state persistence boundaries.
4. Define runnable component and browser validation scenarios.

Outputs: [data-model.md](./data-model.md), [activity Inspector contract](./contracts/activity-inspector-tabs.md), [quickstart.md](./quickstart.md)

### Phase 2 — Test-first implementation

1. Add failing tests for core/conditional tab order, content ownership, empty states, ARIA linkage, keyboard behavior, and state transitions.
2. Add controlled active-tab state at workflow-editor scope and compose the shared Studio tab/tab-panel primitives.
3. Recompose Inspector content into fixed context plus mounted tab panels with independent scrolling.
4. Add presentation options to existing input/output panels only where required to suppress duplicate headings and expose explicit empty states.
5. Update the browser fixture and add constrained-width/fixed-context proof.

### Phase 3 — Verification and landing

1. Run focused tests, Workflows typecheck/build, repository lint/CSS lint, and the focused browser test.
2. Perform browser screenshot review in the real themed Workflows surface.
3. Run independent code review and repair all material findings.
4. Push the organization `codex/*` branch, open a draft PR, wait for required checks, mark ready, merge to `main`, and verify the landed commit.

## Project Structure

### Documentation

```text
specs/093-activity-inspector-tabs/
├── spec.md
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── activity-inspector-tabs.md
├── checklists/
│   └── requirements.md
└── tasks.md
```

### Source Code

```text
src/Elsa.Studio.Workflows/Client/src/
├── ActivityPropertiesPanel.tsx
├── ActivityOutputsPanel.tsx
├── styles.css
├── workflow-editor/
│   ├── InspectorPanel.tsx
│   └── WorkflowEditor.tsx
└── __tests__/
    ├── activityInspectorTabs.test.tsx
    ├── inspectorSlotActions.test.tsx
    └── outputCaptureEditor.test.tsx

tests/browser/
├── src.tsx
└── activity-inspector-tabs.spec.ts
```

**Structure Decision**: Keep activity-specific composition and inner-tab rendering in `InspectorPanel`, session-level navigation state in `WorkflowEditor`, and reuse the shared Studio keyboard/element-ID helpers directly. Existing outer `PanelTabList` callers remain untouched. Existing input/output editors retain their domain behavior and receive only narrowly scoped presentation controls needed by the tabbed surface. Browser proof uses the repository's existing Workflows fixture rather than adding a separate application.

## Integration Order

1. Failing component tests and test harness updates.
2. Workflow-editor active-tab state and activity-identity reset boundary.
3. Inspector tab composition, accessible linkage, and input/output presentation adjustments.
4. Token-based layout styles.
5. Browser fixture and Playwright proof.
6. Full validation, review, Model B PR, and merge.

## Risk Controls

- Preserve every existing Inspector control in exactly one tab or the fixed context; tests assert the mapping.
- Keep Inputs and Outputs stable even when empty; only Variables and Slots are conditional.
- Preserve active-tab state outside the conditionally rendered outer Inspector panel.
- Keep inactive tab content mounted but hidden so component-local state and scroll positions survive tab changes.
- Key the Inspector body by selected activity identity so hidden controls, menus, and scroll state reset together on selection changes.
- Build complete ARIA linkage from the shared Studio tab helpers without changing existing `PanelTabList` callers.
- Use existing tokens and run CSS lint to prevent design-contract drift.
- Exercise minimum-width overflow and fixed-context scrolling in Chromium rather than relying on DOM-only tests.

## Complexity Tracking

No constitution violations require justification.
