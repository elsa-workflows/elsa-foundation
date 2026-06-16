# Tasks: Elsa Studio UI System

**Input**: Design documents from `/specs/001-ui-system/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md

**Tests**: Include focused tests for shared behavior and migrated real screens, per FR-012.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing.

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Prepare the shared UI workspace and documentation entry points.

- [ ] T001 Create shared UI directory structure in src/Elsa.Studio.Web/Client/src/app/ui/
- [ ] T002 [P] Add UI system README in src/Elsa.Studio.Web/Client/src/app/ui/README.md
- [ ] T003 [P] Add implementation tracking notes in specs/001-ui-system/implementation-notes.md
- [ ] T004 [P] Review package dependency requirements in package.json and src/Elsa.Studio.Web/Client/package.json

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Core tokens and primitive contracts that block real screen migrations.

**CRITICAL**: No user story implementation should bypass these shared foundations.

- [ ] T005 Define Studio token CSS in src/Elsa.Studio.Web/Client/src/app/ui/tokens.css
- [ ] T006 Update global shell style imports in src/Elsa.Studio.Web/Client/src/app/styles.css
- [ ] T007 [P] Implement shared status chip primitive in src/Elsa.Studio.Web/Client/src/app/ui/feedback/StatusChip.tsx
- [ ] T008 [P] Implement shared alert/empty/loading/error primitives in src/Elsa.Studio.Web/Client/src/app/ui/feedback/FeedbackStates.tsx
- [ ] T009 [P] Implement shared field and setting group primitives in src/Elsa.Studio.Web/Client/src/app/ui/forms/Field.tsx
- [ ] T010 [P] Implement shared tabs primitive in src/Elsa.Studio.Web/Client/src/app/ui/layout/Tabs.tsx
- [ ] T011 [P] Implement shared toolbar/search/action primitives in src/Elsa.Studio.Web/Client/src/app/ui/layout/Toolbar.tsx
- [ ] T012 Implement shared inspector/drawer primitives in src/Elsa.Studio.Web/Client/src/app/ui/inspector/Inspector.tsx
- [ ] T013 Implement shared resource list/data grid shell primitives in src/Elsa.Studio.Web/Client/src/app/ui/data-grid/DataGrid.tsx
- [ ] T014 Add shared UI exports in src/Elsa.Studio.Web/Client/src/app/ui/index.ts
- [ ] T015 Add Vitest coverage for shared primitive states in src/Elsa.Studio.Web/Client/src/app/ui/__tests__/primitives.test.tsx
- [ ] T016 Run pnpm --filter @elsa-workflows/studio-web test

**Checkpoint**: Shared UI foundation can render representative resource, settings, feedback, and inspector states.

---

## Phase 3: User Story 1 - Shared Workbench Foundation (Priority: P1) MVP

**Goal**: Module authors can build native-feeling admin pages using shared UI primitives and tokens.

**Independent Test**: A representative sample page uses shared primitives without defining its own typography, status, card, toolbar, or inspector styling.

### Tests for User Story 1

- [ ] T017 [P] [US1] Add sample workbench rendering test in src/Elsa.Studio.Web/Client/src/app/ui/__tests__/workbench.test.tsx
- [ ] T018 [P] [US1] Add token availability test in src/Elsa.Studio.Web/Client/src/__tests__/ui-tokens.test.ts

### Implementation for User Story 1

- [ ] T019 [US1] Refactor ShellFrame in src/Elsa.Studio.Web/Client/src/app/App.tsx to consume shared shell/page primitives where stable
- [ ] T020 [US1] Replace metric-card/admin-card defaults in src/Elsa.Studio.Web/Client/src/app/styles.css with workbench summary/resource styles
- [ ] T021 [US1] Add module author usage examples in src/Elsa.Studio.Web/Client/src/app/ui/README.md
- [ ] T022 [US1] Validate shared primitive export boundaries in src/Elsa.Studio.Web/Client/src/app/ui/index.ts
- [ ] T023 [US1] Run pnpm --filter @elsa-workflows/studio-web build

**Checkpoint**: The host shell and sample UI primitives establish the Workbench language.

---

## Phase 4: User Story 2 - Feature Management Workbench Screen (Priority: P2)

**Goal**: Feature management becomes the first real split-inspector configuration screen.

**Independent Test**: The Features page supports scan, select, edit, validation, dirty state, reset/apply, and backend error states using shared primitives.

### Tests for User Story 2

- [ ] T024 [P] [US2] Update feature management tests in src/Elsa.Studio.FeatureManagement/Client/src/__tests__/module.test.ts
- [ ] T025 [P] [US2] Add setting editor state tests in src/Elsa.Studio.FeatureManagement/Client/src/__tests__/settings.test.tsx

### Implementation for User Story 2

- [ ] T026 [US2] Refactor FeatureManagementPage layout in src/Elsa.Studio.FeatureManagement/Client/src/module.tsx to shared split inspector primitives
- [ ] T027 [US2] Replace feature row/status/toggle styling in src/Elsa.Studio.FeatureManagement/Client/src/styles.css with shared token-compatible styles
- [ ] T028 [US2] Implement dirty state and sticky action footer behavior in src/Elsa.Studio.FeatureManagement/Client/src/module.tsx
- [ ] T029 [US2] Normalize feature warnings, errors, and validation through shared alert/field primitives in src/Elsa.Studio.FeatureManagement/Client/src/module.tsx
- [ ] T030 [US2] Run pnpm --filter @elsa-workflows/studio-feature-management test
- [ ] T031 [US2] Run pnpm --filter @elsa-workflows/studio-feature-management build

**Checkpoint**: Feature management proves settings, validation, and split inspector behavior.

---

## Phase 5: User Story 3 - Module Management And Backend Modules (Priority: P3)

**Goal**: Studio exposes a module management screen covering frontend and backend modules, contributions, compatibility, and diagnostics.

**Independent Test**: A representative mixed module registry can be inspected through a resource grid and module inspector.

### Tests for User Story 3

- [ ] T032 [P] [US3] Add backend module registry model tests in tests/Elsa.Studio.Tests/StudioModuleManifestProviderTests.cs
- [ ] T033 [P] [US3] Add module management API tests in tests/Elsa.Studio.Tests/StudioModuleManagementEndpointTests.cs
- [ ] T034 [P] [US3] Add module management client tests in src/Elsa.Studio.Web/Client/src/__tests__/module-management.test.tsx

### Implementation for User Story 3

- [ ] T035 [US3] Define module management response models in src/Elsa.Studio.Api/Models/StudioModuleRegistryResponse.cs
- [ ] T036 [US3] Extend backend module metadata aggregation in src/Elsa.Studio.Api/Services/StudioModuleManifestProvider.cs or a new service
- [ ] T037 [US3] Add module management endpoint registration in src/Elsa.Studio.Api/Extensions/StudioApiEndpointRouteBuilderExtensions.cs
- [ ] T038 [US3] Add module management SDK types in src/Elsa.Studio.Web/Client/src/sdk/index.ts
- [ ] T039 [US3] Implement Modules route in src/Elsa.Studio.Web/Client/src/app/App.tsx
- [ ] T040 [US3] Implement module registry grid and inspector in src/Elsa.Studio.Web/Client/src/app/modules/ModuleManagementPage.tsx
- [ ] T041 [US3] Add contribution list and diagnostics timeline UI in src/Elsa.Studio.Web/Client/src/app/modules/ModuleDiagnostics.tsx
- [ ] T042 [US3] Run pnpm --filter @elsa-workflows/studio-web test
- [ ] T043 [US3] Run dotnet test

**Checkpoint**: Module management proves backend-backed resource grid, inspector, and diagnostics surfaces.

---

## Phase 6: User Story 4 - Shell Command Center Behavior (Priority: P4)

**Goal**: Global search/status/actions live consistently in the shell while page-specific actions live in toolbars.

**Independent Test**: Overview, Features, Modules, and Diagnostics use one command hierarchy without duplicated shell actions.

### Tests for User Story 4

- [ ] T044 [P] [US4] Add shell command bar rendering tests in src/Elsa.Studio.Web/Client/src/__tests__/shell-command-bar.test.tsx

### Implementation for User Story 4

- [ ] T045 [US4] Implement top command bar primitive in src/Elsa.Studio.Web/Client/src/app/ui/shell/TopCommandBar.tsx
- [ ] T046 [US4] Move global search/backend status/theme/source actions into TopCommandBar in src/Elsa.Studio.Web/Client/src/app/App.tsx
- [ ] T047 [US4] Normalize page-local actions through StudioToolbar in src/Elsa.Studio.Web/Client/src/app/App.tsx and src/Elsa.Studio.FeatureManagement/Client/src/module.tsx
- [ ] T048 [US4] Run pnpm --filter @elsa-workflows/studio-web test

**Checkpoint**: Shell command behavior is consistent across host and module pages.

---

## Final Phase: Polish & Cross-Cutting Concerns

- [ ] T049 [P] Update README.md with UI system and module authoring guidance
- [ ] T050 [P] Update specs/001-ui-system/quickstart.md with final verification commands and routes
- [ ] T051 Run pnpm build
- [ ] T052 Run pnpm test
- [ ] T053 Run dotnet build
- [ ] T054 Run dotnet test
- [ ] T055 Browser-verify desktop and responsive layouts for Overview, Features, Modules, and Diagnostics
- [ ] T056 Audit changed CSS for unmanaged typography, color, radius, shadow, and status styles

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies.
- **Foundational (Phase 2)**: Depends on Setup and blocks user stories.
- **US1 Foundation proof (Phase 3)**: Depends on Foundational.
- **US2 Feature Management (Phase 4)**: Depends on US1 foundation proof.
- **US3 Module Management (Phase 5)**: Depends on US1 foundation proof; can overlap with late US2 work if API and UI files do not conflict.
- **US4 Command Center (Phase 6)**: Depends on US1 and should wait until page toolbar conventions are proven.
- **Polish**: Depends on selected user stories.

### User Story Dependencies

- **US1**: Required MVP for shared primitives and module authoring.
- **US2**: Uses US1 primitives and proves configuration workflows.
- **US3**: Uses US1 primitives and adds backend module data.
- **US4**: Uses US1 primitives and normalizes shell commands after the first real screens.

### Parallel Opportunities

- T002-T004 can run in parallel.
- T007-T011 can run in parallel after tokens exist.
- T017-T018 can run in parallel.
- T024-T025 can run in parallel.
- T032-T034 can run in parallel.
- T049-T050 can run in parallel.

## Implementation Strategy

### MVP First

1. Complete Setup and Foundational phases.
2. Complete US1 Shared Workbench Foundation.
3. Stop and verify that the host shell plus a representative sample page can use shared primitives without custom visual language.

### Incremental Delivery

1. Ship foundation.
2. Migrate Feature Management.
3. Add Module Management with backend modules as required.
4. Normalize shell command behavior.
5. Update docs and verification.

## Notes

- Keep unrelated existing worktree changes intact.
- Do not export shared UI to module authors until props and token usage are proven by real screens.
- Feature Management and Module Management are the first two requested follow-up screens after the initial Spec Kit foundation.
