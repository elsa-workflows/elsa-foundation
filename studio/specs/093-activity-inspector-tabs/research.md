# Research: Activity Inspector Tabs

## Decision 1 — Keep inner-tab state at workflow-editor session scope

**Decision**: The workflow editor owns the active activity-Inspector tab and passes it into the Inspector.

**Rationale**: The outer Inspector body is not rendered while Runtime or Artifacts is active. State owned only by the Inspector would therefore reset whenever the author returns, contradicting the session-persistence requirement.

**Alternatives considered**:

- Inspector-local state: rejected because outer-panel navigation unmounts the Inspector body.
- Persisted preference or URL state: rejected because the spec limits the state to the current editor session.
- Adding the activity tabs as peers of Inspector, Runtime, and Artifacts: rejected because it mixes activity-scoped authoring concerns with workflow-level panel Contributions.

## Decision 2 — Derive conditional tabs from selected-activity capabilities

**Decision**: Inputs, Outputs, Details, and Version are always available. Variables is derived from scoped-variable support; Slots is derived from exposed child slots.

**Rationale**: This produces a stable core navigation model while avoiding empty specialized tabs. The same derived availability set provides the deterministic fallback to Inputs when the current conditional tab disappears.

**Alternatives considered**:

- Hide empty Inputs/Outputs: rejected because it makes the two primary authoring locations unpredictable.
- Always show Variables/Slots: rejected because most activities would gain empty, irrelevant tabs.
- Store available tabs independently: rejected because it would duplicate state already represented by the selected activity.

## Decision 3 — Keep inactive tab panels mounted

**Decision**: All available tab panels remain mounted for the selected activity and inactive panels are hidden from presentation and accessibility.

**Rationale**: Existing property, output, variable, and slot editors contain local disclosure/menu state. Retaining panels preserves this transient state and each panel's independent scroll position during tab switches without inventing parallel state stores.

**Alternatives considered**:

- Conditionally mount only the active panel: rejected because switching tabs would reset editor disclosures, menus, and scroll.
- Hoist every editor's local state: rejected as invasive and unrelated to the information-architecture change.

## Decision 4 — Reset the Inspector subtree on activity identity changes

**Decision**: Changing the inspected activity remounts the activity-specific Inspector subtree while retaining the editor-owned active tab when it remains available.

**Rationale**: A single identity boundary resets all transient controls and scroll containers together, preventing hidden menus or expanded editors from leaking between nodes. The active tab is intentionally outside that boundary.

**Alternatives considered**:

- Add reset effects to every child editor: rejected as brittle and easy to miss when new editors are added.
- Preserve per-activity state caches: rejected because the spec requires reset on activity changes.

## Decision 5 — Reuse shared tab helpers without changing outer tabs

**Decision**: Compose the shared `StudioTabs` and `StudioTabPanel` primitives in `InspectorPanel`. Extend their public presentation contract with optional caller-owned base IDs, CSS classes, and mounted-hidden panels. Leave the outer `PanelTabList` contract unchanged.

**Rationale**: The shared components already own keyboard behavior, roving focus, stable tab/panel IDs, and focusable panel semantics. The additive options let a module link and style separately composed mounted panels without duplicating that interaction contract. Outer panel callers deliberately omit tabpanel linkage and need no change.

**Alternatives considered**:

- Extend `PanelTabList` with optional linkage: rejected because the inner surface needs distinct text-only styling and all corresponding panels at once, while outer callers intentionally have a different body contract.
- Reimplement keyboard behavior: rejected as duplicated interaction logic.
- Add unconditional linkage to outer tabs: rejected because existing outer panel bodies do not currently expose corresponding tabpanel IDs.

## Decision 6 — Keep context fixed and give each panel its own scroll container

**Decision**: The selected activity name, scope-owner hint, availability warning, and inner tab row remain fixed; each tab panel owns vertical scrolling.

**Rationale**: Long activity inputs otherwise scroll the selected identity and navigation out of view. Independent scroll containers also make per-tab scroll restoration deterministic.

**Alternatives considered**:

- One shared Inspector scroll container: rejected because its offset would carry across unrelated tabs and hide context.
- Sticky elements inside the existing long scroller: rejected because conditional content and independent scroll restoration become harder to reason about.

## Decision 7 — Adapt existing editors instead of duplicating their content

**Decision**: The tabbed Inspector continues to render the existing input, intrinsic, output, variable, slot, and reusable-version editors. Narrow presentation options suppress duplicate headings and provide Inspector-specific empty text.

**Rationale**: Existing editors already own value binding, conversion, variable-scope, source navigation, autosave, and undo behavior. Reuse preserves those contracts and limits this feature to composition.

**Alternatives considered**:

- Reimplement Inputs and Outputs inside Inspector: rejected because it would duplicate domain logic and increase regression risk.
- Rename the shared input editor from Properties to Inputs globally: rejected because other consumers still use it as a standalone property panel.

## Decision 8 — Prove behavior at component and browser levels

**Decision**: Add focused component tests for semantics/state/content mapping and a Playwright fixture for minimum-width overflow, fixed context, independent scrolling, and themed visual review.

**Rationale**: DOM tests can prove tab semantics and state transitions but cannot establish real overflow or sticky/fixed layout. The repository constitution requires proof on a real Studio screen.

**Alternatives considered**:

- Component tests only: rejected because layout regressions would remain unverified.
- Manual browser review only: rejected because interaction/state rules need repeatable regression coverage.
