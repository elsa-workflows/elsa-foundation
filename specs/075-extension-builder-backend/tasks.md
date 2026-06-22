# Tasks: Extension Builder — Backend Pipeline (Trusted-Team v1)

**Input**: Design documents from `/specs/075-extension-builder-backend/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/openapi.yaml, quickstart.md
**Tests**: Required by feature spec independent tests and repo constitution.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: User story label (`US1` through `US5`)
- Include exact file paths in every task

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Prepare host-owned Extension Builder structure and baseline wiring.

- [X] T001 Create `src/Apps/Elsa.Server/ExtensionBuilder/` folder and add `ExtensionBuilderOptions.cs`, `ExtensionBuilderModels.cs`, `ExtensionBuilderTemplates.cs`, `ExtensionBuilderStorage.cs`, `ExtensionBuilderBuildRunner.cs`, `ExtensionBuilderPromotionService.cs`, and `ExtensionBuilderService.cs`.
- [X] T002 Add `src/Apps/Elsa.Server/ElsaExtensionBuilderApi.cs` with `MapElsaExtensionBuilderApi` route group rooted at `/_elsa/extension-builder`.
- [X] T003 Register Extension Builder services/options and map `app.MapElsaExtensionBuilderApi()` in `src/Apps/Elsa.Server/Program.cs`.
- [X] T004 Add test project references needed to exercise `Elsa.Server` internals from `tests/Elsa/Modularity/Tests/Elsa.Modularity.Tests.csproj`.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Core infrastructure that MUST be complete before any user story can be implemented.

**CRITICAL**: No user story work can begin until this phase is complete.

- [X] T005 Define all canonical entity/request/response records and enums in `src/Apps/Elsa.Server/ExtensionBuilder/ExtensionBuilderModels.cs`.
- [X] T006 Implement file-system root resolution, metadata persistence, normalized path safety, and owner-scoped lookup primitives in `src/Apps/Elsa.Server/ExtensionBuilder/ExtensionBuilderStorage.cs`.
- [X] T007 Implement Extension Builder template catalog for `elsa-activity-module` and `generic-dotnet` starter projects in `src/Apps/Elsa.Server/ExtensionBuilder/ExtensionBuilderTemplates.cs`.
- [X] T008 Implement trusted caller resolution, management API key validation reuse, trusted/admin role enforcement, and advisory capability calculation in `src/Apps/Elsa.Server/ElsaExtensionBuilderApi.cs`.
- [X] T009 [P] Add authorization/capability tests for missing API key, invalid API key, untrusted authenticated caller, trusted caller, and `GetCapabilities` advisory flags in `tests/Elsa/Modularity/Tests/ExtensionBuilderAuthorizationTests.cs`.
- [X] T010 [P] Add storage/path-safety tests for owner scoping, restart-survivable metadata, safe relative paths, and path traversal rejection in `tests/Elsa/Modularity/Tests/ExtensionBuilderServiceTests.cs`.

**Checkpoint**: Foundation ready; story implementation can now proceed in priority order.

---

## Phase 3: User Story 1 - Author, build, promote, and load an Elsa activity end-to-end (Priority: P1) MVP

**Goal**: Trusted caller creates a workspace/project from the Elsa template, builds a `.nupkg`, promotes it through Nuplane, and observes runtime status.

**Independent Test**: Create workspace and Elsa activity project, submit a build, promote successful artifact, and assert runtime status reports a package state plus contributed catalog data when available.

### Tests for User Story 1

- [X] T011 [P] [US1] Add create-workspace/list-workspaces/get-workspace/create-project/get-project/list-templates endpoint tests in `tests/Elsa/Modularity/Tests/ExtensionBuilderServiceTests.cs`.
- [X] T012 [P] [US1] Add build happy-path test for unmodified Elsa activity template producing `Succeeded`, zero error diagnostics, log, and `.nupkg` artifact in `tests/Elsa/Modularity/Tests/ExtensionBuilderServiceTests.cs`.
- [X] T013 [P] [US1] Add promote/runtime-status happy-path test using stubbed Nuplane admin/catalog services in `tests/Elsa/Modularity/Tests/ExtensionBuilderServiceTests.cs`.

### Implementation for User Story 1

- [X] T014 [US1] Implement `CreateWorkspace`, `ListWorkspaces`, `GetWorkspace`, `ListTemplates`, `CreateProject`, and `GetProject` service methods in `src/Apps/Elsa.Server/ExtensionBuilder/ExtensionBuilderService.cs`.
- [X] T015 [US1] Implement immutable source snapshot creation at build submission in `src/Apps/Elsa.Server/ExtensionBuilder/ExtensionBuilderStorage.cs`.
- [X] T016 [US1] Implement per-project serialized `dotnet restore`/`dotnet pack` execution, build log capture, diagnostic parsing, and artifact discovery in `src/Apps/Elsa.Server/ExtensionBuilder/ExtensionBuilderBuildRunner.cs`.
- [X] T017 [US1] Implement `SubmitBuild`, `GetBuild`, `GetBuildLog`, and `GetBuildArtifact` service methods in `src/Apps/Elsa.Server/ExtensionBuilder/ExtensionBuilderService.cs`.
- [X] T018 [US1] Implement package validation, feed duplicate detection, Nuplane drop-folder publish, and reconciliation trigger in `src/Apps/Elsa.Server/ExtensionBuilder/ExtensionBuilderPromotionService.cs`.
- [X] T019 [US1] Implement `PromoteBuild` and `GetRuntimeStatus` service methods in `src/Apps/Elsa.Server/ExtensionBuilder/ExtensionBuilderService.cs`.
- [X] T020 [US1] Map workspace, template, project, build, promote, build log, artifact, and runtime-status endpoints in `src/Apps/Elsa.Server/ElsaExtensionBuilderApi.cs`.

**Checkpoint**: User Story 1 is fully functional and independently testable.

---

## Phase 4: User Story 2 - Edit project files and iterate with build diagnostics and logs (Priority: P2)

**Goal**: Trusted caller lists/reads/writes/deletes project files, receives failing build diagnostics/logs for invalid C#, fixes the file, and rebuilds successfully.

**Independent Test**: Introduce invalid C#, submit build, assert `Failed` with diagnostic and log, then fix file and assert a fresh successful build without mutating prior build records.

### Tests for User Story 2

- [X] T021 [P] [US2] Add list/read/write/delete project file endpoint tests with persistence and last-write-wins behavior in `tests/Elsa/Modularity/Tests/ExtensionBuilderServiceTests.cs`.
- [X] T022 [P] [US2] Add invalid C# build test asserting `Failed`, error diagnostic source location when available, retrievable log, and no promotable artifact in `tests/Elsa/Modularity/Tests/ExtensionBuilderServiceTests.cs`.
- [X] T023 [P] [US2] Add rebuild-after-fix test asserting a new `Succeeded` build leaves the previous failed build record intact in `tests/Elsa/Modularity/Tests/ExtensionBuilderServiceTests.cs`.

### Implementation for User Story 2

- [X] T024 [US2] Implement project file listing, reading, writing, and deletion in `src/Apps/Elsa.Server/ExtensionBuilder/ExtensionBuilderStorage.cs`.
- [X] T025 [US2] Implement `ListProjectFiles`, `ReadProjectFile`, `WriteProjectFile`, and `DeleteProjectFile` service methods in `src/Apps/Elsa.Server/ExtensionBuilder/ExtensionBuilderService.cs`.
- [X] T026 [US2] Map project file endpoints including catch-all `{*path}` handling in `src/Apps/Elsa.Server/ElsaExtensionBuilderApi.cs`.
- [X] T027 [US2] Harden build diagnostic parsing for restore/compile failures in `src/Apps/Elsa.Server/ExtensionBuilder/ExtensionBuilderBuildRunner.cs`.

**Checkpoint**: User Stories 1 and 2 both work independently.

---

## Phase 5: User Story 3 - Promotion validation rejects invalid or conflicting packages (Priority: P3)

**Goal**: Promotion rejects duplicate, invalid-manifest, dependency-policy, and malformed-package cases without publishing or disturbing runtime state.

**Independent Test**: Attempt each invalid promotion type and assert distinct rejection reason with no feed mutation.

### Tests for User Story 3

- [X] T028 [P] [US3] Add duplicate package id+version promotion rejection test in `tests/Elsa/Modularity/Tests/ExtensionBuilderServiceTests.cs`.
- [X] T029 [P] [US3] Add invalid or missing `elsa-package.json` manifest rejection test in `tests/Elsa/Modularity/Tests/ExtensionBuilderServiceTests.cs`.
- [X] T030 [P] [US3] Add malformed `.nupkg` promotion rejection test in `tests/Elsa/Modularity/Tests/ExtensionBuilderServiceTests.cs`.
- [X] T031 [P] [US3] Add dependency-policy rejection test using a configured denied dependency pattern in `tests/Elsa/Modularity/Tests/ExtensionBuilderServiceTests.cs`.

### Implementation for User Story 3

- [X] T032 [US3] Implement manifest and `.nuspec` package identity validation in `src/Apps/Elsa.Server/ExtensionBuilder/ExtensionBuilderPromotionService.cs`.
- [X] T033 [US3] Implement dependency policy configuration and validation in `src/Apps/Elsa.Server/ExtensionBuilder/ExtensionBuilderOptions.cs` and `src/Apps/Elsa.Server/ExtensionBuilder/ExtensionBuilderPromotionService.cs`.
- [X] T034 [US3] Ensure promotion failure paths return exactly one machine-classifiable rejection reason and skip feed writes in `src/Apps/Elsa.Server/ExtensionBuilder/ExtensionBuilderPromotionService.cs`.

**Checkpoint**: Invalid promotions are rejected deterministically and safely.

---

## Phase 6: User Story 4 - Observe runtime status, roll back, and retry reconciliation (Priority: P4)

**Goal**: Trusted caller can inspect promoted package runtime state, roll back to an available version, and retry reconciliation for failed packages.

**Independent Test**: Promote versions N and N+1, roll back to N, assert active version/status, force failed reconciliation state, retry, and assert updated outcome.

### Tests for User Story 4

- [X] T035 [P] [US4] Add runtime status mapping tests for `Loaded`, `PendingRestart`, and `FailedReconciliation` in `tests/Elsa/Modularity/Tests/ExtensionBuilderServiceTests.cs`.
- [X] T036 [P] [US4] Add rollback-to-available-version and rollback-missing-version rejection tests in `tests/Elsa/Modularity/Tests/ExtensionBuilderServiceTests.cs`.
- [X] T037 [P] [US4] Add retry reconciliation test with stubbed Nuplane outcome updates in `tests/Elsa/Modularity/Tests/ExtensionBuilderServiceTests.cs`.

### Implementation for User Story 4

- [X] T038 [US4] Persist promotion history, active version, reconcile outcome, and rollback version metadata in `src/Apps/Elsa.Server/ExtensionBuilder/ExtensionBuilderStorage.cs`.
- [X] T039 [US4] Implement runtime status aggregation from stored promotion history, Nuplane packages, and feature catalog data in `src/Apps/Elsa.Server/ExtensionBuilder/ExtensionBuilderService.cs`.
- [X] T040 [US4] Implement rollback package copy/activation and reconciliation trigger in `src/Apps/Elsa.Server/ExtensionBuilder/ExtensionBuilderPromotionService.cs`.
- [X] T041 [US4] Implement `RollbackPackage` and `RetryReconciliation` service methods in `src/Apps/Elsa.Server/ExtensionBuilder/ExtensionBuilderService.cs`.
- [X] T042 [US4] Map rollback and retry endpoints in `src/Apps/Elsa.Server/ElsaExtensionBuilderApi.cs`.

**Checkpoint**: Runtime lifecycle operations are available without deleting authoring or promoted package state.

---

## Phase 7: User Story 5 - Create and build a generic .NET project (Priority: P5)

**Goal**: Trusted caller creates a generic .NET class-library project, builds it, and promotes it through the same pipeline without Elsa-specific feature/activity content.

**Independent Test**: Create generic project, build successfully, promote successfully, and assert runtime status reports no contributed Elsa features/activities when none are present.

### Tests for User Story 5

- [X] T043 [P] [US5] Add generic .NET template create/build/promote test in `tests/Elsa/Modularity/Tests/ExtensionBuilderServiceTests.cs`.
- [X] T044 [P] [US5] Add generic package runtime-status test asserting empty contributed features/activities when no Elsa manifest features exist in `tests/Elsa/Modularity/Tests/ExtensionBuilderServiceTests.cs`.

### Implementation for User Story 5

- [X] T045 [US5] Complete generic .NET template starter files and package metadata in `src/Apps/Elsa.Server/ExtensionBuilder/ExtensionBuilderTemplates.cs`.
- [X] T046 [US5] Ensure build and promotion code paths do not require Elsa activity-specific files for generic projects in `src/Apps/Elsa.Server/ExtensionBuilder/ExtensionBuilderBuildRunner.cs` and `src/Apps/Elsa.Server/ExtensionBuilder/ExtensionBuilderPromotionService.cs`.
- [X] T047 [US5] Ensure runtime status returns an empty contributions collection for generic packages with no Elsa features in `src/Apps/Elsa.Server/ExtensionBuilder/ExtensionBuilderService.cs`.

**Checkpoint**: Generic .NET packages share the same backend pipeline.

---

## Phase 8: Polish & Cross-Cutting Concerns

**Purpose**: Validation, docs, contract consistency, and final hardening across all stories.

- [X] T048 [P] Update `specs/075-extension-builder-backend/contracts/openapi.yaml` if implementation response shapes differ from the planned contract.
- [X] T049 [P] Update `specs/075-extension-builder-backend/quickstart.md` with the final local server URL, request examples, and validation commands.
- [X] T050 Run `dotnet test tests/Elsa/Modularity/Tests/Elsa.Modularity.Tests.csproj` and fix any failures from Extension Builder changes.
- [X] T051 Run `dotnet build src/Apps/Elsa.Server/Elsa.Server.csproj` and fix any build failures.
- [X] T052 Review `git --no-pager diff` for accidental `.agent-prefs/*`, generated artifacts, secrets, absolute local paths in committed docs, and unrelated changes.
- [X] T053 Create a local git commit with message `Implement extension builder backend pipeline` and the required `Co-authored-by` trailer.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies.
- **Foundational (Phase 2)**: Depends on Phase 1 and blocks all user stories.
- **US1 MVP (Phase 3)**: Depends on Phase 2.
- **US2 (Phase 4)**: Depends on Phase 2; builds on US1 project/build primitives but remains independently testable by creating its own project.
- **US3 (Phase 5)**: Depends on US1 promotion primitives.
- **US4 (Phase 6)**: Depends on US1 promotion/runtime primitives and US3 safe promotion validation.
- **US5 (Phase 7)**: Depends on Phase 2 and reuses US1 build/promote primitives.
- **Polish (Phase 8)**: Depends on selected stories being complete.

### User Story Dependencies

- **US1 (P1)**: MVP; no story dependency after foundation.
- **US2 (P2)**: Can start after foundation but integrates with build runner from US1.
- **US3 (P3)**: Requires promotion path from US1.
- **US4 (P4)**: Requires promotion state from US1/US3.
- **US5 (P5)**: Can start after US1 build/promote primitives are available.

### Parallel Opportunities

- T009 and T010 can run in parallel after T005-T008 interfaces are sketched.
- US1 tests T011-T013 can be written in parallel before T014-T020.
- US2 tests T021-T023 can be written in parallel; T024-T027 touch separate implementation areas.
- US3 tests T028-T031 can be written in parallel; T032-T034 are sequential in promotion service.
- US4 tests T035-T037 can be written in parallel; T038-T042 are sequential by storage/service/API layering.
- US5 tests T043-T044 can be written in parallel; T045-T047 are sequential by template/build/status layering.
- Polish docs T048-T049 can run in parallel with final validation once implementation shapes are stable.

---

## Parallel Example: User Story 1

```text
Task: "Add create-workspace/list-workspaces/get-workspace/create-project/get-project/list-templates endpoint tests in tests/Elsa/Modularity/Tests/ExtensionBuilderServiceTests.cs"
Task: "Add build happy-path test for unmodified Elsa activity template producing Succeeded, zero error diagnostics, log, and .nupkg artifact in tests/Elsa/Modularity/Tests/ExtensionBuilderServiceTests.cs"
Task: "Add promote/runtime-status happy-path test using stubbed Nuplane admin/catalog services in tests/Elsa/Modularity/Tests/ExtensionBuilderServiceTests.cs"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1 and Phase 2.
2. Complete Phase 3 to deliver create -> build -> promote -> runtime-status.
3. Validate with the US1 tests and the quickstart happy path.

### Incremental Delivery

1. Add US2 to make file editing and build diagnostics usable.
2. Add US3 to make promotion safe and machine-classifiable.
3. Add US4 to make runtime lifecycle operations available.
4. Add US5 to prove the pipeline is not hard-wired to Elsa activity packages.

### Final Validation

Run the focused `Elsa.Modularity.Tests` project and `Elsa.Server` build before committing.
