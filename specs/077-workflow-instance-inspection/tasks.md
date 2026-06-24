# Tasks: Workflow Instance Inspection

**Input**: Design documents from `/specs/077-workflow-instance-inspection/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/workflow-instance-inspection.md, quickstart.md

**Tests**: Included because the user requested end-to-end implementation and the constitution requires focused tests for registration/implementation changes.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing.

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Establish feature context and cross-repo working state.

- [X] T001 Verify both repositories are on `codex/workflow-instance-inspection` and clean enough to edit: `/Users/sipke/Projects/Elsa/elsa-foundation` and `/Users/sipke/Projects/Elsa/elsa-foundation-studio`
- [X] T002 Update `AGENTS.md` Speckit marker to `specs/077-workflow-instance-inspection/plan.md`
- [X] T003 [P] Confirm backend version layout contracts in `src/Elsa/Workflows/Design/Persistence/Core/Stores/IWorkflowDefinitionVersionLayoutStore.cs`
- [X] T004 [P] Confirm Studio canvas adapter reuse points in `/Users/sipke/Projects/Elsa/elsa-foundation-studio/src/Elsa.Studio.Workflows/Client/src/workflowAdapter.ts`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Complete the design-version read contract needed by all instance visualization stories.

- [X] T005 [P] Add backend tests for version details including layout in `tests/Elsa/Workflows/Design/Tests/WorkflowDefinitionVersionDetailsTests.cs`
- [X] T006 [P] Add Studio API/type tests or fixtures for workflow definition version details with layout in `/Users/sipke/Projects/Elsa/elsa-foundation-studio/src/Elsa.Studio.Workflows/Client/src/__tests__/module.test.tsx`
- [X] T007 Extend `WorkflowDefinitionVersionDetailsView` with layout records in `src/Elsa/Workflows/Design/Api/Models/WorkflowDefinitionVersionDetailsView.cs`
- [X] T008 Update version projection/handler to read layout via `IWorkflowDefinitionVersionLayoutStore` in `src/Elsa/Workflows/Design/Api/Handlers/GetVersionRequestHandler.cs` and `src/Elsa/Workflows/Design/Api/Projections/WorkflowViewProjections.cs`
- [X] T009 Implement the design version GET endpoint in `src/Elsa/Workflows/Design/Api/Endpoints/Versions/Get.cs`
- [X] T010 Update Studio workflow API types and client method for version details in `/Users/sipke/Projects/Elsa/elsa-foundation-studio/src/Elsa.Studio.Workflows/Client/src/workflowTypes.ts` and `/Users/sipke/Projects/Elsa/elsa-foundation-studio/src/Elsa.Studio.Workflows/Client/src/api/workflows.ts`

**Checkpoint**: Studio can request the exact definition version state and layout for an instance.

---

## Phase 3: User Story 1 - Inspect an instance on its workflow canvas (Priority: P1) 🎯 MVP

**Goal**: Render the selected workflow instance on a read-only designer canvas using the executed definition version layout.

**Independent Test**: Open an instance created from a Flowchart definition and see the authored graph positioned by saved layout with runtime status/incident markers.

### Tests for User Story 1

- [X] T011 [P] [US1] Add workflow adapter tests for runtime overlays/read-only node data in `/Users/sipke/Projects/Elsa/elsa-foundation-studio/src/Elsa.Studio.Workflows/Client/src/__tests__/workflowAdapter.test.ts`
- [X] T012 [P] [US1] Add Studio route test for direct instance detail rendering with canvas evidence in `/Users/sipke/Projects/Elsa/elsa-foundation-studio/src/Elsa.Studio.Workflows/Client/src/__tests__/module.test.tsx`

### Implementation for User Story 1

- [X] T013 [US1] Add instance graph overlay data helpers in `/Users/sipke/Projects/Elsa/elsa-foundation-studio/src/Elsa.Studio.Workflows/Client/src/workflowAdapter.ts`
- [X] T014 [US1] Add read-only instance detail route registration for `/workflows/instances/:workflowExecutionId` in `/Users/sipke/Projects/Elsa/elsa-foundation-studio/src/Elsa.Studio.Workflows/Client/src/module.tsx`
- [X] T015 [US1] Implement instance detail data loading for runtime details, definition version details, and activity catalog in `/Users/sipke/Projects/Elsa/elsa-foundation-studio/src/Elsa.Studio.Workflows/Client/src/module.tsx`
- [X] T016 [US1] Render read-only React Flow instance canvas with runtime node markers in `/Users/sipke/Projects/Elsa/elsa-foundation-studio/src/Elsa.Studio.Workflows/Client/src/module.tsx`
- [X] T017 [US1] Add CSS for the instance workbench canvas, runtime badges, and fault markers in `/Users/sipke/Projects/Elsa/elsa-foundation-studio/src/Elsa.Studio.Workflows/Client/src/styles.css`

**Checkpoint**: User Story 1 is independently functional and testable.

---

## Phase 4: User Story 2 - Navigate from instance list to a wider inspection view (Priority: P2)

**Goal**: Keep the instance list for scanning while opening selected instances in a wider, deep-linkable workspace.

**Independent Test**: Select an instance from `/workflows/instances` and confirm navigation to `/workflows/instances/{workflowExecutionId}` with the selected instance loaded.

### Tests for User Story 2

- [X] T018 [P] [US2] Update Studio list navigation test in `/Users/sipke/Projects/Elsa/elsa-foundation-studio/src/Elsa.Studio.Workflows/Client/src/__tests__/module.test.tsx`

### Implementation for User Story 2

- [X] T019 [US2] Change instance list row selection to navigate to the instance detail route in `/Users/sipke/Projects/Elsa/elsa-foundation-studio/src/Elsa.Studio.Workflows/Client/src/module.tsx`
- [X] T020 [US2] Add detail-page back/list affordances and not-found/fallback states in `/Users/sipke/Projects/Elsa/elsa-foundation-studio/src/Elsa.Studio.Workflows/Client/src/module.tsx`
- [X] T021 [US2] Adjust responsive layout so the list and detail views remain usable at desktop and narrower widths in `/Users/sipke/Projects/Elsa/elsa-foundation-studio/src/Elsa.Studio.Workflows/Client/src/styles.css`

**Checkpoint**: User Story 2 is independently functional and testable.

---

## Phase 5: User Story 3 - Correlate graph nodes, timeline, and incidents (Priority: P3)

**Goal**: Selecting evidence on the graph, timeline, or incident list highlights the related runtime evidence.

**Independent Test**: Select a timeline item or incident and confirm the matching graph node and detail section are highlighted.

### Tests for User Story 3

- [X] T022 [P] [US3] Add Studio correlation tests for node/history/incident selection in `/Users/sipke/Projects/Elsa/elsa-foundation-studio/src/Elsa.Studio.Workflows/Client/src/__tests__/module.test.tsx`

### Implementation for User Story 3

- [X] T023 [US3] Add correlated selection state and matching helpers in `/Users/sipke/Projects/Elsa/elsa-foundation-studio/src/Elsa.Studio.Workflows/Client/src/module.tsx`
- [X] T024 [US3] Update activity history and incident panels to select/highlight related graph nodes in `/Users/sipke/Projects/Elsa/elsa-foundation-studio/src/Elsa.Studio.Workflows/Client/src/module.tsx`
- [X] T025 [US3] Add unmatched runtime evidence sections for activity executions/incidents without graph matches in `/Users/sipke/Projects/Elsa/elsa-foundation-studio/src/Elsa.Studio.Workflows/Client/src/module.tsx`
- [X] T026 [US3] Add CSS for correlated selected/highlight states in `/Users/sipke/Projects/Elsa/elsa-foundation-studio/src/Elsa.Studio.Workflows/Client/src/styles.css`

**Checkpoint**: All user stories are independently functional.

---

## Final Phase: Polish & Cross-Cutting Concerns

**Purpose**: Verification, documentation consistency, and requested self-review.

- [X] T027 [P] Run backend focused tests from `specs/077-workflow-instance-inspection/quickstart.md`
- [X] T028 [P] Run Studio focused tests/build from `specs/077-workflow-instance-inspection/quickstart.md`
- [X] T029 Perform manual browser validation against `/workflows/instances` and `/workflows/instances/{workflowExecutionId}`
- [X] T030 Run self-review loop over current diffs and fix actionable findings until clean
- [X] T031 Mark all completed tasks `[X]`, commit foundation changes, commit Studio changes, and report verification evidence

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies.
- **Foundational (Phase 2)**: Depends on setup; blocks all user stories.
- **User Story 1 (P1)**: Depends on foundational design-version contract.
- **User Story 2 (P2)**: Depends on foundational route/client shape; can be implemented after or alongside US1 UI shell.
- **User Story 3 (P3)**: Depends on US1 graph rendering and runtime evidence panels.
- **Polish**: Depends on all desired user stories.

### Parallel Opportunities

- T003 and T004 can run in parallel.
- T005 and T006 can run in parallel.
- T011 and T012 can run in parallel.
- T027 and T028 can run in parallel after implementation.

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete setup and foundational contract tasks.
2. Implement the read-only instance canvas route with runtime node markers.
3. Verify direct route and graph rendering with focused Studio tests.

### Incremental Delivery

1. Add exact version state/layout read contract.
2. Add direct instance canvas view.
3. Change list navigation to wide detail route.
4. Add graph/history/incident correlation.
5. Run self-review and final verification.
