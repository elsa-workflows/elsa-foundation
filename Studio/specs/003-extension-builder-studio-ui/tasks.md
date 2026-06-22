# Tasks: Extension Builder — Studio UI (trusted-team v1)

**Input**: Design documents from `/specs/003-extension-builder-studio-ui/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/

**Tests**: Focused Vitest/jsdom tests are included because the feature spec requires meaningful tests and real-screen proof.

**Organization**: Tasks are grouped by user story so each story is independently implementable and testable.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1-US5)
- Include exact file paths in descriptions

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Establish the route/client/test seams used by every story.

- [X] T001 Add built-in Extension Builder navigation/route wiring in `src/Elsa.Studio.Web/Client/src/app/App.tsx`
- [X] T002 [P] Create typed Extension Builder API contract/client in `src/Elsa.Studio.Web/Client/src/app/modules/extensionBuilderApi.ts`
- [X] T003 [P] Create Extension Builder page shell in `src/Elsa.Studio.Web/Client/src/app/modules/ExtensionBuilderPage.tsx`
- [X] T004 [P] Create focused test harness and fixtures in `src/Elsa.Studio.Web/Client/src/__tests__/extension-builder.test.tsx`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Core UI state, permission gating, and shared workbench layout needed by all user stories.

**CRITICAL**: No user story work can begin until this phase is complete.

- [X] T005 Implement `GetCapabilities` loading and unauthorized/error states in `src/Elsa.Studio.Web/Client/src/app/modules/ExtensionBuilderPage.tsx`
- [X] T006 Implement shared operation feedback, refresh, selected workspace/project/file state, and dirty-buffer guard helpers in `src/Elsa.Studio.Web/Client/src/app/modules/ExtensionBuilderPage.tsx`
- [X] T007 [P] Add Extension Builder workbench styling using existing Studio tokens in `src/Elsa.Studio.Web/Client/src/app/styles.css`
- [X] T008 [P] Add navigation grouping assertions for `/extension-builder` in `src/Elsa.Studio.Web/Client/src/__tests__/overview.test.tsx`

**Checkpoint**: Foundation ready - user story implementation can now begin.

---

## Phase 3: User Story 1 - Author, Build, Promote, and See an Elsa Activity Extension Loaded (Priority: P1) MVP

**Goal**: A trusted user can create an Elsa project, edit source, build, promote, and see loaded runtime status without leaving Studio.

**Independent Test**: With mocked contract responses, create/open an Elsa template project, edit/save a file, submit a build, promote its artifact, and observe `Loaded` runtime state.

### Tests for User Story 1

- [X] T009 [P] [US1] Test trusted capabilities, workspace/project rendering, create form defaults, and action gating in `src/Elsa.Studio.Web/Client/src/__tests__/extension-builder.test.tsx`
- [X] T010 [P] [US1] Test build/promote/runtime happy path calls canonical endpoints in `src/Elsa.Studio.Web/Client/src/__tests__/extension-builder.test.tsx`

### Implementation for User Story 1

- [X] T011 [US1] Implement workspace/project browser with create workspace and create project from templates in `src/Elsa.Studio.Web/Client/src/app/modules/ExtensionBuilderPage.tsx`
- [X] T012 [US1] Implement project file tree, file read, editor buffer, save flow, and build guard in `src/Elsa.Studio.Web/Client/src/app/modules/ExtensionBuilderPage.tsx`
- [X] T013 [US1] Implement build submission, status/log polling, artifact display, and build history state in `src/Elsa.Studio.Web/Client/src/app/modules/ExtensionBuilderPage.tsx`
- [X] T014 [US1] Implement promotion action and runtime status refresh in `src/Elsa.Studio.Web/Client/src/app/modules/ExtensionBuilderPage.tsx`

**Checkpoint**: User Story 1 is fully functional and independently testable.

---

## Phase 4: User Story 2 - Edit, Iterate, and Diagnose Builds (Priority: P2)

**Goal**: Users can manage project files and diagnose failed builds with actionable source navigation.

**Independent Test**: A failed build with diagnostics shows severity, message, file/line, opens the referenced file, and lets the user fix/save/rebuild.

### Tests for User Story 2

- [X] T015 [P] [US2] Test dirty editor state, save affordance, diagnostic rendering, and diagnostic-to-file navigation in `src/Elsa.Studio.Web/Client/src/__tests__/extension-builder.test.tsx`

### Implementation for User Story 2

- [X] T016 [US2] Add create/delete file controls scoped by `can-edit-files` in `src/Elsa.Studio.Web/Client/src/app/modules/ExtensionBuilderPage.tsx`
- [X] T017 [US2] Add structured diagnostics list, severity chips, source-location navigation, and editor line hinting in `src/Elsa.Studio.Web/Client/src/app/modules/ExtensionBuilderPage.tsx`
- [X] T018 [US2] Add build history selection and log/diagnostic replay for previous builds in `src/Elsa.Studio.Web/Client/src/app/modules/ExtensionBuilderPage.tsx`

**Checkpoint**: User Stories 1 and 2 both work independently.

---

## Phase 5: User Story 3 - Promotion Validation and Rejection UX (Priority: P3)

**Goal**: Promotion rejection categories are distinct, actionable, and never imply overwrite.

**Independent Test**: Each rejection category renders category-specific guidance after `PromoteBuild` rejects an artifact.

### Tests for User Story 3

- [X] T019 [P] [US3] Test `duplicate`, `invalid-manifest`, `dependency-policy`, and `malformed-package` guidance in `src/Elsa.Studio.Web/Client/src/__tests__/extension-builder.test.tsx`

### Implementation for User Story 3

- [X] T020 [US3] Implement category-specific promotion result messages in `src/Elsa.Studio.Web/Client/src/app/modules/ExtensionBuilderPage.tsx`
- [X] T021 [US3] Ensure promote is disabled unless a successful build artifact exists in `src/Elsa.Studio.Web/Client/src/app/modules/ExtensionBuilderPage.tsx`

**Checkpoint**: Promotion failure UX is independently testable.

---

## Phase 6: User Story 4 - Runtime Status, Rollback, and Retry Reconciliation (Priority: P4)

**Goal**: Users can understand runtime states and recover through retry reconciliation or rollback.

**Independent Test**: `FailedReconciliation` exposes retry, `PendingRestart` explains restart, and eligible prior versions can roll back with refreshed runtime status.

### Tests for User Story 4

- [X] T022 [P] [US4] Test runtime state messaging, retry reconciliation, rollback gating, and rollback endpoint calls in `src/Elsa.Studio.Web/Client/src/__tests__/extension-builder.test.tsx`

### Implementation for User Story 4

- [X] T023 [US4] Implement runtime inspector states and contributed capability list in `src/Elsa.Studio.Web/Client/src/app/modules/ExtensionBuilderPage.tsx`
- [X] T024 [US4] Implement retry reconciliation and rollback actions gated by `can-rollback` in `src/Elsa.Studio.Web/Client/src/app/modules/ExtensionBuilderPage.tsx`

**Checkpoint**: Runtime recovery is independently testable.

---

## Phase 7: User Story 5 - Generic (Non-Elsa) .NET Project (Priority: P5)

**Goal**: Generic .NET templates use the same project/build/promote/runtime path as Elsa templates.

**Independent Test**: Generic template is selectable, creates a project, builds/promotes, and accurately reports runtime contributions, including none.

### Tests for User Story 5

- [X] T025 [P] [US5] Test generic .NET template selection and no-contributions runtime messaging in `src/Elsa.Studio.Web/Client/src/__tests__/extension-builder.test.tsx`

### Implementation for User Story 5

- [X] T026 [US5] Ensure generic templates are selectable and rendered alongside primary Elsa templates in `src/Elsa.Studio.Web/Client/src/app/modules/ExtensionBuilderPage.tsx`
- [X] T027 [US5] Render honest no-contributions runtime messaging in `src/Elsa.Studio.Web/Client/src/app/modules/ExtensionBuilderPage.tsx`

**Checkpoint**: Generic .NET projects follow the same path as Elsa projects.

---

## Final Phase: Polish & Cross-Cutting Concerns

**Purpose**: Validate behavior, docs, and integration.

- [X] T028 [P] Update `specs/003-extension-builder-studio-ui/quickstart.md` if implementation validation commands change
- [X] T029 Run `pnpm --filter @elsa-workflows/studio-web test -- extension-builder` from repository root
- [X] T030 Run `pnpm --filter @elsa-workflows/studio-web build` from repository root
- [X] T031 Commit all changes with the required Co-authored-by trailer

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies.
- **Foundational (Phase 2)**: Depends on Setup and blocks user stories.
- **US1 (Phase 3)**: Depends on Foundational and is the MVP.
- **US2 (Phase 4)**: Depends on Foundational; integrates with US1 editor/build state but remains independently testable with mocks.
- **US3 (Phase 5)**: Depends on US1 promotion path.
- **US4 (Phase 6)**: Depends on US1 runtime status path.
- **US5 (Phase 7)**: Depends on US1 creation/build/promote path.
- **Polish**: Depends on all desired stories.

### User Story Dependencies

- **US1 (P1)**: No story dependency after Foundational.
- **US2 (P2)**: Can start after Foundational, but uses US1 file/build primitives.
- **US3 (P3)**: Depends on US1 promote affordance.
- **US4 (P4)**: Depends on US1 runtime inspector.
- **US5 (P5)**: Depends on US1 template/project flow.

### Parallel Opportunities

- T002, T003, and T004 can be split after T001 route intent is known.
- T007 and T008 can run independently of page logic.
- Test tasks T009, T010, T015, T019, T022, and T025 can be written in parallel once fixtures exist.
- US3 and US4 can proceed in parallel after US1 promotion/runtime foundations exist.

---

## Parallel Example: User Story 1

```bash
Task: "Test trusted capabilities, workspace/project rendering, create form defaults, and action gating in src/Elsa.Studio.Web/Client/src/__tests__/extension-builder.test.tsx"
Task: "Test build/promote/runtime happy path calls canonical endpoints in src/Elsa.Studio.Web/Client/src/__tests__/extension-builder.test.tsx"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Setup and Foundational tasks.
2. Implement US1 browser/editor/build/promote/runtime path.
3. Validate with the US1-focused Extension Builder tests.
4. Add US2-US5 increments without breaking the MVP.

### Incremental Delivery

1. Foundation: typed contract, route, capabilities gate.
2. US1: happy-path vertical slice from template to loaded runtime.
3. US2: diagnostics and file-management loop.
4. US3: promotion rejection depth.
5. US4: runtime recovery.
6. US5: generic .NET template parity.

### Format Validation

All tasks follow the required checklist format with checkbox, sequential task ID, optional `[P]`, required story label for user-story tasks, and explicit file paths.
