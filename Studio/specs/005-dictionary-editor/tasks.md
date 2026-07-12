# Tasks: Dictionary Entry Editor

**Input**: Design documents from `specs/005-dictionary-editor/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/dictionary-editor-ui.md, quickstart.md

**Tests**: Tests are required and precede implementation for each story.

## Phase 1: Setup (Shared Infrastructure)

- [x] T001 Confirm the current Workflows and Studio UI test baselines with focused commands from `specs/005-dictionary-editor/quickstart.md`
- [x] T002 [P] Add typed dictionary metadata contract tests in `src/Elsa.Studio.Web/Client/src/app/ui/__tests__/kit.test.tsx`
- [x] T003 [P] Add dictionary helper test scaffolding in `src/Elsa.Studio.Workflows/Client/src/__tests__/activityProperties.test.ts`

---

## Phase 2: Foundational (Blocking Prerequisites)

- [x] T004 Add dictionary UI metadata types to `src/Elsa.Studio.Web/Client/src/sdk/index.ts`
- [x] T005 Implement explicit CLR dictionary recognition and generic argument parsing in `src/Elsa.Studio.Workflows/Client/src/activityProperties.ts`
- [x] T006 Implement ordered rows, key comparison, typed value validation, and strict duplicate-aware JSON helpers in `src/Elsa.Studio.Workflows/Client/src/dictionaryEditorModel.ts`
- [x] T007 Implement stable workflow/activity/property session draft storage in `src/Elsa.Studio.Workflows/Client/src/dictionaryEditorSession.ts`
- [x] T008 [P] Add shared action-notice failing tests in `src/Elsa.Studio.Web/Client/src/app/ui/__tests__/kit.test.tsx`
- [x] T009 Implement and export the shared action notice in `src/Elsa.Studio.Web/Client/src/app/ui/feedback/ActionNotice.tsx` and `src/Elsa.Studio.Web/Client/src/app/ui/shared.ts`

**Checkpoint**: Dictionary parsing, validation, session drafts, and reusable notice are independently tested.

---

## Phase 3: User Story 1 - Edit dictionary entries inline (Priority: P1) 🎯 MVP

**Goal**: Provide a responsive inline key/value table with typed values, add/remove, five-row cap, keyboard flow, and precise Undo.

**Independent Test**: Create three entries from empty using keyboard only, remove one, undo it, and verify object value and order.

- [x] T010 [P] [US1] Add inline interaction and focus tests in `src/Elsa.Studio.Workflows/Client/src/__tests__/dictionaryValueEditor.test.tsx`
- [x] T011 [US1] Implement inline rows, typed editor delegation, add/remove, cap, keyboard flow, and Undo in `src/Elsa.Studio.Workflows/Client/src/DictionaryValueEditor.tsx`
- [x] T012 [US1] Integrate Literal dictionary admission ahead of collection/text fallbacks in `src/Elsa.Studio.Workflows/Client/src/ActivityPropertiesPanel.tsx`
- [x] T013 [US1] Add token-driven inline and responsive dictionary styles in `src/Elsa.Studio.Workflows/Client/src/styles.css`
- [x] T014 [US1] Verify the independent inline journey with `src/Elsa.Studio.Workflows/Client/src/__tests__/dictionaryValueEditor.test.tsx`

---

## Phase 4: User Story 2 - Resolve invalid entry drafts safely (Priority: P1)

**Goal**: Preserve incomplete/duplicate/type-invalid rows privately across surfaces and selection changes without altering valid workflow state.

**Independent Test**: Create invalid drafts, move between inline/expanded and activities, then fix/discard them while confirming the committed object remains safe.

- [x] T015 [P] [US2] Add draft lifecycle and external replacement tests in `src/Elsa.Studio.Workflows/Client/src/__tests__/dictionaryValueEditor.test.tsx`
- [x] T016 [US2] Connect row validation and session draft lifecycle in `src/Elsa.Studio.Workflows/Client/src/DictionaryValueEditor.tsx` and `src/Elsa.Studio.Workflows/Client/src/dictionaryEditorSession.ts`
- [x] T017 [US2] Add Activity Properties integration coverage for metadata, session persistence, and syntax transitions in `src/Elsa.Studio.Workflows/Client/src/__tests__/activityPropertyGroups.test.tsx`

---

## Phase 5: User Story 3 - Work with large dictionaries in an expanded editor (Priority: P2)

**Goal**: Reuse and harden the expanded modal for a full filterable Table surface.

**Independent Test**: Filter and edit a 100-entry dictionary in the modal without changing stored order; verify focus isolation and restoration.

- [x] T018 [P] [US3] Add expanded Table, filtering, tab memory, and modal focus tests in `src/Elsa.Studio.Workflows/Client/src/__tests__/dictionaryExpressionEditor.test.tsx`
- [x] T019 [US3] Harden focus isolation/restoration and dictionary expanded admission in `src/Elsa.Studio.Workflows/Client/src/ActivityPropertiesPanel.tsx`
- [x] T020 [US3] Implement expanded Table filtering, tab memory, error summary, and Add-filter behavior in `src/Elsa.Studio.Workflows/Client/src/DictionaryValueEditor.tsx`
- [x] T021 [US3] Add token-driven modal table/filter/error styles in `src/Elsa.Studio.Workflows/Client/src/styles.css`

---

## Phase 6: User Story 4 - Edit and inspect raw JSON (Priority: P2)

**Goal**: Add strict raw JSON authoring with safe private drafts, typed diagnostics, format/copy, and guarded tab transitions.

**Independent Test**: Enter valid and invalid JSON, detect duplicate/type errors, format/copy, close/reopen, and prove only valid objects commit.

- [x] T022 [P] [US4] Add strict JSON, duplicate, type, format/copy, and guard tests in `src/Elsa.Studio.Workflows/Client/src/__tests__/dictionaryExpressionEditor.test.tsx`
- [x] T023 [US4] Implement JSON code-editor tab, diagnostics, formatting, copying, and guarded transitions in `src/Elsa.Studio.Workflows/Client/src/DictionaryValueEditor.tsx`
- [x] T024 [US4] Integrate the dictionary surface for structured Object syntax in `src/Elsa.Studio.Workflows/Client/src/objectExpressionEditor.tsx`
- [x] T025 [US4] Prove Literal/Object integration and non-dictionary syntax regressions in `src/Elsa.Studio.Workflows/Client/src/__tests__/activityPropertyGroups.test.tsx`

---

## Phase 7: User Story 5 - Edit typed and complex values accessibly (Priority: P2)

**Goal**: Delegate known values and provide expandable in-row JSON for complex values in editable and read-only states.

**Independent Test**: Exercise scalar, contributed, nullable, and complex values using keyboard-only and read-only workflows.

- [x] T026 [P] [US5] Add typed/complex/read-only tests in `src/Elsa.Studio.Workflows/Client/src/__tests__/dictionaryValueEditor.test.tsx` and `src/Elsa.Studio.Workflows/Client/src/__tests__/dictionaryExpressionEditor.test.tsx`
- [x] T027 [US5] Implement complex value summaries and expandable per-row JSON details in `src/Elsa.Studio.Workflows/Client/src/DictionaryValueEditor.tsx`
- [x] T028 [US5] Complete accessible labels, descriptions, disabled states, error focus, and read-only behavior in `src/Elsa.Studio.Workflows/Client/src/DictionaryValueEditor.tsx`

---

## Phase 8: Polish & Cross-Cutting Concerns

- [x] T029 [P] Add the dictionary browser fixture in `tests/browser/src.tsx`
- [x] T030 [P] Add wide, narrow, modal, keyboard, and notice browser coverage in `tests/browser/dictionary-editor.spec.ts`
- [x] T031 Run focused and full Workflows/Studio UI tests from `specs/005-dictionary-editor/quickstart.md`
- [x] T032 Run `pnpm typecheck`, `pnpm lint`, and `pnpm build` and resolve all failures
- [x] T033 Run the dictionary Playwright scenario and inspect both standard and black-glass themes
- [x] T034 Review implementation against `specs/005-dictionary-editor/spec.md` and mark completed tasks in `specs/005-dictionary-editor/tasks.md`

---

## Dependencies & Execution Order

- Setup precedes Foundation; Foundation blocks every user story.
- US1 depends on Foundation and is the MVP.
- US2 depends on US1 row rendering but is independently testable through invalid-state safety.
- US3 depends on US1/US2 shared rows and drafts.
- US4 depends on US3 expanded tabs and Foundation JSON helpers.
- US5 depends on US1 typed delegation and US3 expanded rows.
- Browser and full verification follow the desired story set.

## Parallel Opportunities

- Shared notice tests/implementation and dictionary model tests/implementation can proceed in parallel during Foundation.
- Browser fixture/spec work can proceed after stable DOM contracts are agreed.
- Pure helper, inline component, expanded component, and integration tests occupy separate files and can be authored in parallel, then run before their implementation task.

## Implementation Strategy

1. Complete T001-T009 to establish tested pure contracts.
2. Deliver T010-T014 as the independently usable inline MVP.
3. Add safe private drafts through T015-T017.
4. Add expanded Table and JSON in T018-T025.
5. Complete typed/complex accessibility in T026-T028.
6. Finish browser and full-repository gates in T029-T034.

## Format Validation

All 34 tasks use the required checkbox, sequential ID, optional parallel marker, user-story label where applicable, and exact repository file path.
