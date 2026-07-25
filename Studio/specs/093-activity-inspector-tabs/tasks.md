# Tasks: Activity Inspector Tabs

**Input**: Design documents from `specs/093-activity-inspector-tabs/`

**Prerequisites**: `plan.md`, `spec.md`, `research.md`, `data-model.md`, `contracts/activity-inspector-tabs.md`, `quickstart.md`

**Tests**: Tests are mandatory. For every behavior-changing implementation task, complete the corresponding failing test task first and observe the relevant assertion fail.

**Organization**: Tasks are grouped by user story. The shared accessible tab shell is foundational; each story then adds an independently testable content or state slice.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: May run in parallel after declared dependencies because it owns different files.
- **[Story]**: Maps the task to a user story in `spec.md`.
- Every task names the exact file or directory it owns.

## Phase 1: Setup

**Purpose**: Establish focused test fixtures without changing production behavior.

- [x] T001 Create a reusable controlled activity-Inspector render/rerender harness and representative ordinary, intrinsic, variable-supporting, slot-owning, and reusable activity fixtures in `src/Elsa.Studio.Workflows/Client/src/__tests__/activityInspectorTabs.test.tsx`
- [x] T002 [P] Add an activity-Inspector browser fixture mode with long input content, output captures, scoped variables, slots, details, version data, availability context, and outer-panel controls in `tests/browser/src.tsx`

**Checkpoint**: Test fixtures compile and can express every spec scenario; production UI remains unchanged.

---

## Phase 2: Foundational Accessible Tab Shell

**Purpose**: Establish the shared inner navigation and panel semantics required by all stories.

- [x] T003 Write failing tests for text-only tab labels, exact core order, tab/tabpanel ID linkage, selected state, roving tabindex, and Arrow/Home/End navigation in `src/Elsa.Studio.Workflows/Client/src/__tests__/activityInspectorTabs.test.tsx`
- [x] T004 Implement the activity Inspector tab ID type, ordered availability derivation, text-only tablist, shared keyboard behavior, and linked mounted tabpanels in `src/Elsa.Studio.Workflows/Client/src/workflow-editor/InspectorPanel.tsx`
- [x] T005 Add token-driven single-row overflow and tab/panel shell styles without fixed-context or final body-scrolling behavior in `src/Elsa.Studio.Workflows/Client/src/styles.css`

**Checkpoint**: A selected activity exposes an accessible inner tab shell with mounted, linked panels and no content migration yet.

---

## Phase 3: User Story 1 — Edit activity inputs without unrelated content (Priority: P1) 🎯 MVP

**Goal**: Inputs is the initial focused authoring surface and has an explicit empty state without a duplicate heading.

**Independent Test**: Select ordinary, empty-input, Set Variable, and Set Output activities; verify Inputs is initially active, contains only the existing input/destination controls, and shows the agreed empty state.

### Tests

- [x] T006 [US1] Write failing tests for Inputs as the initial tab, no duplicate Properties heading, the no-configurable-inputs state, and intrinsic destination controls remaining in Inputs in `src/Elsa.Studio.Workflows/Client/src/__tests__/activityInspectorTabs.test.tsx`
- [x] T007 [P] [US1] Update the production Input-reference Inspector test to activate Inputs through the controlled tab contract while preserving current authoring behavior in `src/Elsa.Studio.Workflows/Client/src/__tests__/inputReferenceUi.test.tsx`

### Implementation

- [x] T008 [US1] Add narrowly scoped heading and empty-label presentation options while preserving standalone defaults in `src/Elsa.Studio.Workflows/Client/src/ActivityPropertiesPanel.tsx`
- [x] T009 [US1] Route ordinary and intrinsic property editors into the Inputs tab with the agreed presentation options in `src/Elsa.Studio.Workflows/Client/src/workflow-editor/InspectorPanel.tsx`

**Checkpoint**: User Story 1 passes independently on top of the foundational tab shell.

---

## Phase 4: User Story 2 — Configure output capture in a focused view (Priority: P1)

**Goal**: Outputs is always second, contains existing capture controls, and has an explicit empty state without a duplicate heading.

**Independent Test**: Select activities with browsable, hidden, and absent outputs; activate Outputs and verify capture behavior, exact placement, filtering, and empty messaging.

### Tests

- [x] T010 [P] [US2] Update output-capture tests to select the Outputs tab and add failing assertions for the permanent second tab, absent duplicate heading, and explicit no-outputs state in `src/Elsa.Studio.Workflows/Client/src/__tests__/outputCaptureEditor.test.tsx`

### Implementation

- [x] T011 [P] [US2] Add narrowly scoped heading and empty-label presentation options while preserving output-capture behavior in `src/Elsa.Studio.Workflows/Client/src/ActivityOutputsPanel.tsx`
- [x] T012 [US2] Route the existing output-capture editor into the permanent Outputs tab in `src/Elsa.Studio.Workflows/Client/src/workflow-editor/InspectorPanel.tsx`

**Checkpoint**: Inputs and Outputs form the complete stable P1 binding workflow.

---

## Phase 5: User Story 3 — Reach specialized activity information only when needed (Priority: P2)

**Goal**: Variables and Slots appear conditionally while Details and Version remain stable and every existing specialized control has one location.

**Independent Test**: Inspect ordinary, reusable, variable-supporting, and slot-owning activities; verify exact tab order/content, reusable status/actions, slot navigation/replacement, and the absence of empty conditional tabs.

### Tests

- [x] T013 [US3] Replace obsolete long-layout/disclosure assertions with failing tests for conditional Variables/Slots order, Details identity content, permanent Version content, reusable version/source actions, and slot controls in `src/Elsa.Studio.Workflows/Client/src/__tests__/inspectorSlotActions.test.tsx`
- [x] T014 [P] [US3] Add failing tests proving container declarations appear only in Variables and intrinsic destinations remain excluded from that tab in `src/Elsa.Studio.Workflows/Client/src/__tests__/activityInspectorTabs.test.tsx`

### Implementation

- [x] T015 [US3] Move scoped-variable declarations, slot navigation/replacement, Node ID/Activity Type, and activity/reusable version content into their contracted tabpanels in `src/Elsa.Studio.Workflows/Client/src/workflow-editor/InspectorPanel.tsx`
- [x] T016 [US3] Remove obsolete long-page margins/disclosure rules and normalize Variables, Slots, Details, and Version panel spacing with existing tokens in `src/Elsa.Studio.Workflows/Client/src/styles.css`

**Checkpoint**: Every pre-existing Inspector capability is reachable in the fixed context or exactly one inner tab.

---

## Phase 6: User Story 4 — Maintain orientation while navigating (Priority: P2)

**Goal**: Fixed context remains visible; tab/scroll/transient state follows the agreed inner-tab, activity-change, and outer-panel lifecycles.

**Independent Test**: Scroll and interact in multiple tabs, change activities with and without the active conditional tab, and round-trip through Runtime/Artifacts; verify persistence, fallback, reset, and no state leakage.

### Tests

- [x] T017 [US4] Write failing controlled-state tests for valid-tab preservation, conditional-tab fallback to Inputs, outer-panel round-trip restoration, per-tab scroll retention, activity-change scroll reset, and transient control reset in `src/Elsa.Studio.Workflows/Client/src/__tests__/activityInspectorTabs.test.tsx`
- [x] T018 [P] [US4] Add failing browser assertions for fixed activity context, independent vertical body scrolling, minimum-width horizontal tab reachability, no page overflow, keyboard navigation, and outer-panel restoration in `tests/browser/activity-inspector-tabs.spec.ts`

### Implementation

- [x] T019 [US4] Own active activity-tab state at editor-session scope, normalize it against selected capabilities, key per-activity Inspector view state, and pass the controlled contract in `src/Elsa.Studio.Workflows/Client/src/workflow-editor/WorkflowEditor.tsx`
- [x] T020 [US4] Place activity name, scope-owner hint, availability warning, and inner tablist in fixed context while retaining mounted panels and resetting slot-picker state per activity in `src/Elsa.Studio.Workflows/Client/src/workflow-editor/InspectorPanel.tsx`
- [x] T021 [US4] Implement fixed-context and independent tabpanel scrolling plus constrained-width overflow behavior using existing token aliases in `src/Elsa.Studio.Workflows/Client/src/styles.css`
- [x] T022 [US4] Complete the browser fixture interactions and selectors needed by the focused Playwright proof in `tests/browser/src.tsx`

**Checkpoint**: All four user stories and state-lifecycle acceptance scenarios pass.

---

## Phase 7: Polish, Verification, and Model B Landing

**Purpose**: Prove compatibility, quality, visual fit, and landed-main state.

- [x] T023 Update any remaining Inspector tests whose assertions encode the removed long-page headings or Version disclosure in `src/Elsa.Studio.Workflows/Client/src/__tests__/`
- [x] T024 Run the focused component suite from `specs/093-activity-inspector-tabs/quickstart.md` and record/fix all failures
- [x] T025 Run Workflows typecheck/build plus repository lint and CSS lint from `specs/093-activity-inspector-tabs/quickstart.md` and record/fix all failures
- [x] T026 Run the focused Chromium browser test and review light and black-glass screenshots at normal/minimum widths in `tests/browser/activity-inspector-tabs.spec.ts`
- [x] T027 Perform an independent correctness/accessibility/architecture review of changes under `src/Elsa.Studio.Workflows/Client/src/` and `tests/browser/` and resolve every material finding
- [x] T028 Mark completed tasks and record final verification evidence in `specs/093-activity-inspector-tabs/tasks.md`
- [ ] T029 Commit the changes listed in `specs/093-activity-inspector-tabs/tasks.md`, push `codex/activity-inspector-tabs`, open a draft organization PR against `main`, wait for required checks, address review/CI failures, mark ready, and merge via Model B
- [ ] T030 Fetch `origin/main` after merge and audit the landed commit against every FR/SC and quickstart command in `specs/093-activity-inspector-tabs/`

---

## Verification Evidence

- 2026-07-25 — Focused activity Inspector suite: 39/39 tests passed.
- 2026-07-25 — Shared Studio tab accessibility suite: 17/17 tests passed.
- 2026-07-25 — Focused Chromium proof: 3/3 scenarios passed; light and black-glass captures reviewed at normal/minimum widths and with Inputs scrolled.
- 2026-07-25 — Repository `pnpm typecheck`: passed.
- 2026-07-25 — Repository `pnpm lint`: passed with the existing 50-warning baseline and zero errors; CSS token lint passed.
- 2026-07-25 — Repository `pnpm build`: passed, including Studio UI type compilation and Workflows bundle budgets.
- 2026-07-25 — Independent Standards and Spec review axes: clean after replacing the local tab renderer with composed `StudioTabs`/`StudioTabPanel` primitives.

---

## Dependencies and Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: Starts immediately.
- **Foundational (Phase 2)**: Depends on T001; blocks every story.
- **User Story 1 (Phase 3)**: Depends on Phase 2; establishes the MVP.
- **User Story 2 (Phase 4)**: Depends on Phase 2 and may implement its editor-specific slice alongside Story 1, but final Inspector composition must integrate serially.
- **User Story 3 (Phase 5)**: Depends on Phase 2 and the stable tabpanel composition established by Stories 1–2.
- **User Story 4 (Phase 6)**: Depends on all content tabs so it can verify the complete lifecycle.
- **Polish/Landing (Phase 7)**: Depends on all user stories.

### User Story Dependencies

```text
Foundational shell
├── US1 Inputs ─────┐
├── US2 Outputs ────┼── US3 Specialized tabs ── US4 Navigation lifecycle
└───────────────────┘
```

### Parallel Opportunities

- T002 can prepare the browser fixture while T001 builds component fixtures.
- T007 can update the Input-reference compatibility test while T006 defines new Inputs behavior.
- T010/T011 can proceed on output-specific files after the foundational shell while US1 work owns input-specific files.
- T014 can define Variables separation while T013 updates slot/version/detail expectations.
- T018 can define browser acceptance assertions while component lifecycle tests are added in T017.

## Parallel Execution Examples

### User Story 1

```text
Agent A: T006 in activityInspectorTabs.test.tsx
Agent B: T007 in inputReferenceUi.test.tsx
Integrate: T008 then T009
```

### User Story 2

```text
Agent A: T010 in outputCaptureEditor.test.tsx
Agent B: T011 in ActivityOutputsPanel.tsx after T010 is observed failing
Integrate: T012 in InspectorPanel.tsx
```

### User Story 3

```text
Agent A: T013 in inspectorSlotActions.test.tsx
Agent B: T014 in activityInspectorTabs.test.tsx
Integrate: T015 then T016
```

### User Story 4

```text
Agent A: T017 in activityInspectorTabs.test.tsx
Agent B: T018 in activity-inspector-tabs.spec.ts
Integrate: T019, T020, T021, T022
```

## Implementation Strategy

### MVP First

1. Complete Setup and Foundational phases.
2. Deliver User Story 1 with Inputs as the initial focused surface.
3. Validate the MVP independently before adding the remaining tabs.

### Incremental Delivery

1. Add Outputs as the permanent second binding surface.
2. Add conditional Variables/Slots plus stable Details/Version.
3. Add complete navigation lifecycle and fixed/scrolling behavior.
4. Run full proof and land through Model B.

### Completion Rule

No task is complete until its specified file change or command has been verified. Model B landing is complete only when the merge is present on `origin/main` and the post-merge requirement audit passes.
