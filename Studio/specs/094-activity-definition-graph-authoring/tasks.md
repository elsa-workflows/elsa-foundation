# Tasks: Activity Definition Graph Authoring

**Input**: Design documents from `/specs/094-activity-definition-graph-authoring/`

**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md), [data-model.md](./data-model.md), [contracts](./contracts/)

**Tests**: Tests are mandatory for every behavior slice because the specification requires component, integration, accessibility, browser, and coordinated Foundation coverage.

**Organization**: Tasks are grouped by independently testable user stories. `elsa-foundation/…` paths are relative to the coordinated Foundation repository; other paths are relative to this Studio repository.

## Phase 1: Setup and baselines

**Purpose**: Establish clean baselines and the coordinated repository boundary.

- [x] T001 Run and record the focused Workflows baseline tests in `src/Elsa.Studio.Workflows/Client/src/__tests__/`
- [x] T002 Run and record the existing Foundation Activity Graph tests in `elsa-foundation/tests/Elsa/Activities/Graph/Tests/`
- [x] T003 [P] Add the accepted domain terms to `CONTEXT.md` and architectural decisions to `docs/adr/0012-*.md` through `docs/adr/0018-*.md`

---

## Phase 2: Foundational graph-authoring seam

**Purpose**: Create the controlled adapter/workspace boundary required by every graph-authoring story.

- [x] T004 [P] Add failing adapter round-trip and unknown-field preservation tests in `src/Elsa.Studio.Workflows/Client/src/__tests__/activityGraphDocumentAdapter.test.ts`
- [x] T005 [P] Add failing resource-scope contribution tests in `src/Elsa.Studio.Workflows/Client/src/__tests__/graphAuthoringContributions.test.ts`
- [x] T006 Define graph resource kinds and controlled document adapter types in `src/Elsa.Studio.Workflows/Client/src/graph-authoring/graphDocumentAdapter.ts`
- [x] T007 Implement the Activity Graph payload/layout adapter in `src/Elsa.Studio.Workflows/Client/src/activityGraphDocumentAdapter.ts`
- [x] T008 Extend graph-authoring contribution metadata in `src/Elsa.Studio.Web/Client/src/sdk/index.ts` and host registration in `src/Elsa.Studio.Workflows/Client/src/module.tsx`
- [x] T009 Add failing workspace interaction and accessibility tests in `src/Elsa.Studio.Workflows/Client/src/__tests__/graphAuthoringWorkspace.test.tsx`
- [x] T010 Extract controlled canvas, palette, breadcrumb, scope, and inspector composition into `src/Elsa.Studio.Workflows/Client/src/graph-authoring/GraphAuthoringWorkspace.tsx`
- [x] T011 Extract adapter-driven canvas mechanics into `src/Elsa.Studio.Workflows/Client/src/graph-authoring/useGraphAuthoringCanvas.ts`
- [x] T012 Extract the resource-neutral inspector shell into `src/Elsa.Studio.Workflows/Client/src/graph-authoring/GraphAuthoringInspector.tsx`
- [x] T013 Adapt `src/Elsa.Studio.Workflows/Client/src/workflow-editor/WorkflowEditor.tsx` to host `GraphAuthoringWorkspace` without lifecycle or UX changes
- [x] T014 Run existing workflow adapter, document, history, serialization, UX, and accessibility tests in `src/Elsa.Studio.Workflows/Client/src/__tests__/`

**Checkpoint**: Workflow authoring uses the controlled workspace with no observable regression, and Activity Graph documents round-trip through the adapter.

---

## Phase 3: User Story 1 — Create a graph activity without provider jargon (Priority: P1) 🎯 MVP

**Goal**: Create a latest-format Flowchart, Sequence, or BPMN Activity Graph with clear implementation naming and a creatable category combobox.

**Independent Test**: Create each composition through the dialog, select or enter a category, and inspect the exact initial request.

### Tests for User Story 1

- [x] T015 [P] [US1] Add failing provider-collapse/latest-format/composition request tests in `src/Elsa.Studio.Workflows/Client/src/__tests__/activityDefinitionCreation.test.tsx`
- [x] T016 [P] [US1] Add failing category normalization, authorization-source, keyboard, and free-form tests in `src/Elsa.Studio.Workflows/Client/src/__tests__/activityCategoryCombobox.test.tsx`

### Implementation for User Story 1

- [x] T017 [US1] Add provider capability collapsing and latest compatible schema selection in `src/Elsa.Studio.Workflows/Client/src/ActivityDefinitionCreateDialog.tsx`
- [x] T018 [US1] Add Flowchart, Sequence, and BPMN composition cards using shared root templates in `src/Elsa.Studio.Workflows/Client/src/ActivityDefinitionCreateDialog.tsx`
- [x] T019 [US1] Add authorized catalog-backed category suggestion derivation in `src/Elsa.Studio.Workflows/Client/src/activityCategories.ts`
- [x] T020 [US1] Replace Category with an accessible creatable combobox in `src/Elsa.Studio.Workflows/Client/src/ActivityDefinitionCreateDialog.tsx`
- [x] T021 [US1] Add token-driven creation/composition/combobox styles in `src/Elsa.Studio.Workflows/Client/src/styles.css`

**Checkpoint**: Creation is independently usable and never exposes schema revisions as a product choice.

---

## Phase 4: User Story 2 — Edit the activity graph in the shared designer (Priority: P1)

**Goal**: Replace the root dropdown/mini editor with the shared graph workspace while retaining Activity Definition revision semantics.

**Independent Test**: Add, configure, nest, connect, lay out, undo/redo, save, reload, conflict, and migrate an Activity Graph draft.

### Tests for User Story 2

- [x] T022 [P] [US2] Add failing root-scope, palette, selection, property, nested-scope, connection, and layout tests in `src/Elsa.Studio.Workflows/Client/src/__tests__/activityGraphImplementationEditor.test.tsx`
- [x] T023 [P] [US2] Extend exact-revision autosave, conflict, recovery, navigation, and external-replacement tests in `src/Elsa.Studio.Workflows/Client/src/__tests__/activityDefinitionAuthoring.test.tsx`
- [x] T024 [P] [US2] Add failing Activity Graph undo/redo and reset-boundary tests in `src/Elsa.Studio.Workflows/Client/src/__tests__/activityGraphHistory.test.ts`

### Implementation for User Story 2

- [x] T025 [US2] Replace the bespoke root editor with the adapter-backed workspace host in `src/Elsa.Studio.Workflows/Client/src/ActivityGraphImplementationEditor.tsx`
- [x] T026 [US2] Add graph-root scope-owner variables/composition behavior in `src/Elsa.Studio.Workflows/Client/src/activityGraphDocumentAdapter.ts` and `src/Elsa.Studio.Workflows/Client/src/graph-authoring/GraphAuthoringInspector.tsx`
- [x] T027 [US2] Adapt public-input and graph-variable expression references in `src/Elsa.Studio.Workflows/Client/src/ActivityGraphImplementationEditor.tsx`
- [x] T028 [US2] Wire shared property/expression editors and filtered graph host contributions through `src/Elsa.Studio.Workflows/Client/src/ActivityDefinitionsPage.tsx` and `src/Elsa.Studio.Workflows/Client/src/ActivityDefinitionDraftEditor.tsx`
- [x] T029 [US2] Implement local document history whose restores flow through provider `onChange` in `src/Elsa.Studio.Workflows/Client/src/activityGraphHistory.ts`
- [x] T030 [US2] Reset Activity Graph history on conflict, migration, recovery, and accepted external replacement in `src/Elsa.Studio.Workflows/Client/src/ActivityDefinitionDraftEditor.tsx`

**Checkpoint**: Activity Graph editing provides shared designer behavior without workflow lifecycle controls.

---

## Phase 5: User Story 3 — Define and map a multi-outcome public interface (Priority: P1)

**Goal**: Support multiple public outcomes for every provider and explicit convergent graph boundary mappings.

**Independent Test**: Author multiple outcomes/outputs, converge graph outcomes on one public target, and validate all source/target/output invariants.

### Tests for User Story 3

- [x] T031 [P] [US3] Add failing Foundation provider validation tests for target convergence and source uniqueness in `elsa-foundation/tests/Elsa/Activities/Graph/Tests/GraphActivityProviderTests.cs`
- [x] T032 [P] [US3] Add failing Foundation descriptor/runtime tests for convergent boundary outcomes in `elsa-foundation/tests/Elsa/Activities/Graph/Tests/GraphActivityExecutionTests.cs`
- [x] T033 [P] [US3] Update Studio mapping tests for target reuse, source uniqueness, and emitted-target coverage in `src/Elsa.Studio.Workflows/Client/src/__tests__/activityGraphOutcomeMappings.test.ts`
- [x] T034 [P] [US3] Add public multi-outcome default/emission tests in `src/Elsa.Studio.Workflows/Client/src/__tests__/activityContractAuthoring.test.ts`
- [x] T035 [P] [US3] Add boundary output expression and required/optional validation tests in `src/Elsa.Studio.Workflows/Client/src/__tests__/activityGraphImplementationEditor.test.tsx`

### Implementation for User Story 3

- [x] T036 [US3] Permit repeated public boundary targets while retaining source uniqueness in `elsa-foundation/src/Elsa/Activities/Graph/Design/Services/GraphActivityProvider.cs`
- [x] T037 [US3] Permit convergent boundary names in `elsa-foundation/src/Elsa/Activities/Graph/Runtime/Models/GraphActivityDescriptor.cs`
- [x] T038 [US3] Remove target-reuse prohibition and retain emitted-target coverage in `src/Elsa.Studio.Workflows/Client/src/activityGraphOutcomeMappings.ts` and `src/Elsa.Studio.Workflows/Client/src/ActivityGraphImplementationEditor.tsx`
- [x] T039 [US3] Ensure new public outcomes default to emitted for every provider in `src/Elsa.Studio.Workflows/Client/src/activityContractAuthoring.ts`
- [x] T040 [US3] Recompose public inputs, outputs, outcomes, contract proposals, and mappings under Public Interface in `src/Elsa.Studio.Workflows/Client/src/ActivityDefinitionDraftEditor.tsx`
- [x] T041 [US3] Add boundary output mappings using shared expression/reference editors in `src/Elsa.Studio.Workflows/Client/src/ActivityGraphImplementationEditor.tsx`

**Checkpoint**: Foundation and Studio agree on multi-outcome and convergent-boundary semantics.

---

## Phase 6: User Story 4 — Edit the complete authoring draft as JSON (Priority: P2)

**Goal**: Offer safe, reversible authoring-draft JSON without exposing database records or autosaving invalid buffers.

**Independent Test**: Round-trip visual/JSON edits, validate excluded fields and provider identity, preserve layouts, and exercise Apply/Reset/navigation gating.

### Tests for User Story 4

- [x] T042 [P] [US4] Add failing serialization, validation, unknown-field, and layout reconciliation tests in `src/Elsa.Studio.Workflows/Client/src/__tests__/activityDefinitionDraftJson.test.ts`
- [x] T043 [P] [US4] Add failing Code tab Apply/Reset/dirty/invalid/navigation/action-gating tests in `src/Elsa.Studio.Workflows/Client/src/__tests__/activityDefinitionDraftCodeView.test.tsx`

### Implementation for User Story 4

- [x] T044 [US4] Implement authoring projection serialization, parsing, semantic validation, and layout reconciliation in `src/Elsa.Studio.Workflows/Client/src/activityDefinitionDraftJson.ts`
- [x] T045 [US4] Implement the controlled JSON buffer and Apply/Reset UI in `src/Elsa.Studio.Workflows/Client/src/ActivityDefinitionDraftCodeView.tsx`
- [x] T046 [US4] Add Designer/Public Interface/Code view state and JSON dirty/invalid gates in `src/Elsa.Studio.Workflows/Client/src/ActivityDefinitionDraftEditor.tsx`
- [x] T047 [US4] Add Code view and dirty-state token styles in `src/Elsa.Studio.Workflows/Client/src/styles.css`

**Checkpoint**: Visual and JSON editing share one applied draft and one exact-revision persistence path.

---

## Phase 7: User Story 5 — Validate and test the activity as an activity (Priority: P2)

**Goal**: Integrate diagnostics and Activity Definition runtime inspection without workflow lifecycle UI.

**Independent Test**: Focus representative diagnostics, repair the draft, run it, and inspect outputs/outcomes/incidents/logs/details.

### Tests for User Story 5

- [x] T048 [P] [US5] Add diagnostic grouping/focus/return and JSON-gate tests in `src/Elsa.Studio.Workflows/Client/src/__tests__/activityDefinitionDiagnostics.test.tsx`
- [x] T049 [P] [US5] Extend test-run gating and runtime result tests in `src/Elsa.Studio.Workflows/Client/src/__tests__/activityDefinitionTestRuns.test.ts`
- [x] T050 [P] [US5] Add legacy schema edit/migration-gating tests in `src/Elsa.Studio.Workflows/Client/src/__tests__/activityGraphImplementationEditor.test.tsx`

### Implementation for User Story 5

- [x] T051 [US5] Recompose graph/contract/provider/JSON diagnostics as a collapsible bottom surface in `src/Elsa.Studio.Workflows/Client/src/ActivityDefinitionDiagnosticsPanel.tsx`
- [x] T052 [US5] Integrate Activity Definition Inspector and Runtime supporting surfaces in `src/Elsa.Studio.Workflows/Client/src/ActivityDefinitionDraftEditor.tsx`
- [x] T053 [US5] Preserve explicit schema 1 editing and reviewed migration guidance in `src/Elsa.Studio.Workflows/Client/src/ActivityGraphImplementationEditor.tsx` and `src/Elsa.Studio.Workflows/Client/src/ActivityDefinitionProviderMigrationDialog.tsx`

**Checkpoint**: Authors can move from diagnostics to a successful activity-specific test run without encountering workflow lifecycle controls.

---

## Phase 8: Polish, verification, and landing

**Purpose**: Prove the complete feature, review it after publication, and land both repositories safely.

- [x] T054 [P] Add the real themed Activity Definition graph browser fixture in `tests/browser/src.tsx`
- [x] T055 Add creation, shared designer, mapping, JSON, diagnostics, test-run, and legacy browser scenarios in `tests/browser/activity-definition-graph-authoring.spec.ts`
- [x] T056 Run the `quickstart.md` focused checks and repair all feature failures
- [x] T057 Run full Studio `pnpm typecheck`, `pnpm lint`, `pnpm build`, and relevant browser checks
- [x] T058 Run required Foundation formatting, affected-project build, and Activity Graph test gates
- [x] T059 Review all changed code for duplication and extract only justified shared helpers/fixtures
- [x] T060 Push coordinated `codex/*` branches and create dependency-linked Foundation and Studio PRs
- [x] T061 Run the requested 10-pass self-review loop after both PRs exist, fixing every actionable finding and rerunning affected checks
- [x] T062 Wait for required CI, repair failures, and keep both PRs current with their target branches
- [x] T063 Merge Foundation first, verify its commit on `main`, then merge Studio and verify its commit on `main`

---

## Dependencies & Execution Order

### Phase dependencies

- Setup establishes baselines.
- Foundational graph seam blocks all Studio user stories.
- US1 may proceed after the adapter types exist.
- US2 requires the complete workspace seam.
- Foundation US3 tasks can proceed in parallel with the Studio workspace extraction.
- US3 Studio UI requires US2 and its Foundation rule must land before Studio.
- US4 requires the Activity Graph host from US2.
- US5 requires US2–US4.
- Browser/full verification requires all user stories.

### Parallel opportunities

- T003–T005 can run independently.
- Foundation tasks T031, T032, T036, and T037 are isolated from Studio extraction.
- Creation tests/implementation can proceed while the workflow host is being proven, after shared root templates are identified.
- JSON pure-model work T042/T044 can proceed after the authoring projection contract is fixed.
- Focused tests across distinct files marked `[P]` can be written concurrently.

## Implementation Strategy

1. Establish clean baselines.
2. Prove the adapter and migrate Workflow Definition to the controlled workspace.
3. Deliver US1 + US2 as the first end-to-end Activity Graph authoring slice.
4. Land the Foundation semantic correction and complete US3.
5. Add US4 JSON authoring and US5 diagnostics/runtime.
6. Run full cross-repository verification.
7. Publish both PRs, perform the requested post-PR self-review loop, then merge Foundation followed by Studio.

## Notes

- Tests are written first and must demonstrate the missing behavior before implementation.
- Mark each task `[x]` only after its implementation and focused verification are complete.
- Preserve unknown payload/layout fields and unrelated user changes.
- Do not fold workflow lifecycle operations into the shared workspace.
