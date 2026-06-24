# Tasks: Workflow Definition Test Runs

**Input**: Design documents from `/specs/076-workflow-test-runs/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/

**Tests**: Tests are included because the feature changes bridge/runtime behavior and the constitution requires focused tests for logic-bearing implementation.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3)
- Include exact file paths in descriptions

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Confirm current package/test structure and create shared folders for the bridge slice.

- [x] T001 Create publishing API folders `src/Elsa/Workflows/Publishing/Api/Contracts`, `src/Elsa/Workflows/Publishing/Api/Services`, and `src/Elsa/Workflows/Publishing/Api/Endpoints/TestRuns`
- [x] T002 [P] Inspect package references in `src/Elsa/Workflows/Publishing/Api/Elsa.Workflows.Publishing.Api.csproj` and `tests/Elsa/Workflows/Publishing/Api/Tests/Elsa.Workflows.Publishing.Api.Tests.csproj` before adding code

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Extract reusable compile behavior and runtime dispatch support that all stories depend on.

**CRITICAL**: No user story work can begin until this phase is complete.

- [x] T003 [P] Add compile request/result contracts in `src/Elsa/Workflows/Publishing/Api/Models/WorkflowExecutableCompileModels.cs`
- [x] T004 [P] Add `IWorkflowExecutableCompiler` in `src/Elsa/Workflows/Publishing/Api/Contracts/IWorkflowExecutableCompiler.cs`
- [x] T005 Move compile logic from `PublishWorkflowRequestHandler` into `WorkflowExecutableCompiler` in `src/Elsa/Workflows/Publishing/Api/Services/WorkflowExecutableCompiler.cs`
- [x] T006 Update `PublishWorkflowRequestHandler` in `src/Elsa/Workflows/Publishing/Api/Handlers/PublishWorkflowRequestHandler.cs` to use `IWorkflowExecutableCompiler` and keep published artifact behavior unchanged
- [x] T007 Register `IWorkflowExecutableCompiler` in `src/Elsa/Workflows/Publishing/Api/WorkflowsPublishingApiFeature.cs`
- [x] T008 [P] Add compiler-focused tests for published artifact parity in `tests/Elsa/Workflows/Publishing/Api/Tests/WorkflowExecutableCompilerTests.cs`
- [x] T009 Update existing publish handler tests in `tests/Elsa/Workflows/Publishing/Api/Tests/PublishWorkflowRequestHandlerTests.cs` for the extracted compiler dependency

**Checkpoint**: Existing publish path still compiles and saves durable executable artifacts exactly as before.

---

## Phase 3: User Story 1 - Run the current workflow under development (Priority: P1) MVP

**Goal**: A designer can start a test run from a workflow definition version without publishing first.

**Independent Test**: Create a valid workflow version, start a test run, verify dispatch is accepted, verify a test-run id and execution id are returned, and verify durable published artifact storage remains unchanged.

### Tests for User Story 1

- [x] T010 [P] [US1] Add accepted test-run handler test in `tests/Elsa/Workflows/Publishing/Api/Tests/WorkflowTestRunRequestHandlerTests.cs`
- [x] T011 [P] [US1] Add normal durable store isolation assertion in `tests/Elsa/Workflows/Publishing/Api/Tests/WorkflowTestRunRequestHandlerTests.cs`

### Implementation for User Story 1

- [x] T012 [P] [US1] Add `WorkflowTestRun` model and status enum in `src/Elsa/Workflows/Publishing/Api/Models/WorkflowTestRun.cs`
- [x] T013 [P] [US1] Add `WorkflowTestRunView` response model in `src/Elsa/Workflows/Publishing/Api/Models/WorkflowTestRunViews.cs`
- [x] T014 [P] [US1] Add `StartWorkflowTestRun` mediator request in `src/Elsa/Workflows/Publishing/Api/Requests/StartWorkflowTestRun.cs`
- [x] T015 [P] [US1] Add `IWorkflowTestRunStore` in `src/Elsa/Workflows/Publishing/Api/Contracts/IWorkflowTestRunStore.cs`
- [x] T016 [P] [US1] Add `ITransientWorkflowExecutableStore` in `src/Elsa/Workflows/Publishing/Api/Contracts/ITransientWorkflowExecutableStore.cs`
- [x] T017 [US1] Implement `InMemoryWorkflowTestRunStore` in `src/Elsa/Workflows/Publishing/Api/Services/InMemoryWorkflowTestRunStore.cs`
- [x] T018 [US1] Implement `InMemoryTransientWorkflowExecutableStore` in `src/Elsa/Workflows/Publishing/Api/Services/InMemoryTransientWorkflowExecutableStore.cs`
- [x] T019 [US1] Add transient executable dispatch support to `IWorkflowExecutionStartDispatcher` in `src/Elsa/Workflows/Runtime/Core/Contracts/IWorkflowExecutionStartDispatcher.cs`
- [x] T020 [US1] Implement transient executable dispatch overload in `src/Elsa/Workflows/Runtime/Core/Services/WorkflowExecutionStartDispatcher.cs`
- [x] T021 [US1] Implement `StartWorkflowTestRunRequestHandler` in `src/Elsa/Workflows/Publishing/Api/Handlers/StartWorkflowTestRunRequestHandler.cs`
- [x] T022 [US1] Register test-run services and request handler dependencies in `src/Elsa/Workflows/Publishing/Api/WorkflowsPublishingApiFeature.cs`

**Checkpoint**: User Story 1 is functional and testable independently.

---

## Phase 4: User Story 2 - Receive clear feedback when the editable workflow cannot run (Priority: P2)

**Goal**: Invalid workflow definitions are rejected before runtime execution dispatch with actionable reasons.

**Independent Test**: Request test runs for missing-root, duplicate-node, unknown-activity, and unsupported-input cases; verify rejection, reason, no execution id, and no runtime agent command.

### Tests for User Story 2

- [x] T023 [P] [US2] Add missing-root rejection test in `tests/Elsa/Workflows/Publishing/Api/Tests/WorkflowTestRunRequestHandlerTests.cs`
- [x] T024 [P] [US2] Add unknown-activity rejection test in `tests/Elsa/Workflows/Publishing/Api/Tests/WorkflowTestRunRequestHandlerTests.cs`
- [x] T025 [P] [US2] Add non-literal-input rejection test in `tests/Elsa/Workflows/Publishing/Api/Tests/WorkflowTestRunRequestHandlerTests.cs`

### Implementation for User Story 2

- [x] T026 [US2] Add rejection handling and rejected test-run persistence in `src/Elsa/Workflows/Publishing/Api/Handlers/StartWorkflowTestRunRequestHandler.cs`
- [x] T027 [US2] Ensure compile validation exceptions preserve actionable messages in `src/Elsa/Workflows/Publishing/Api/Services/WorkflowExecutableCompiler.cs`

**Checkpoint**: Invalid test runs are visible as rejected test-run results and never dispatch runtime execution.

---

## Phase 5: User Story 3 - Keep test runs isolated from production workflow artifacts (Priority: P3)

**Goal**: Test-run artifacts remain transient, are blocked from normal production starts, and can expire without manual designer cleanup.

**Independent Test**: Start a test run, attempt normal runtime execute by its artifact id, verify not found, then expire cleanup and verify transient lookup no longer returns the artifact while test-run metadata remains.

### Tests for User Story 3

- [x] T028 [P] [US3] Add runtime test proving normal dispatch does not find transient artifacts in `tests/Elsa/Workflows/Runtime/Tests/RuntimeWorkflowExecutionStartDispatchTests.cs`
- [x] T029 [P] [US3] Add transient store expiration cleanup test in `tests/Elsa/Workflows/Publishing/Api/Tests/WorkflowTestRunRequestHandlerTests.cs`

### Implementation for User Story 3

- [x] T030 [US3] Add expiration-aware lookup and cleanup behavior in `src/Elsa/Workflows/Publishing/Api/Services/InMemoryTransientWorkflowExecutableStore.cs`
- [x] T031 [US3] Add test-run metadata for source/test correlation and expiration in `src/Elsa/Workflows/Publishing/Api/Handlers/StartWorkflowTestRunRequestHandler.cs`
- [x] T032 [US3] Add FastEndpoints start endpoint in `src/Elsa/Workflows/Publishing/Api/Endpoints/TestRuns/Start.cs`
- [x] T033 [US3] Register endpoint route constants or route helper updates in `src/Elsa/Workflows/Publishing/Api/Constants/RouteConstants.cs`

**Checkpoint**: Test-run artifacts cannot be used through production artifact execution and can be cleaned up without losing test-run correlation.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Verification, catalog/context updates, and cleanup across all stories.

- [x] T034 [P] Update feature registration test in `tests/Elsa/Workflows/Publishing/Api/Tests/WorkflowsPublishingApiFeatureTests.cs`
- [x] T035 [P] Update bridge dependency direction test in `tests/Elsa/Workflows/Publishing/Api/Tests/BridgeDependencyDirectionTests.cs` if new types affect dependency assertions
- [x] T036 [P] Update `src/Elsa/Workflows/Publishing/Api/EXTENSION_POINTS.md` if new public test-run contracts are extension points
- [x] T037 Run `dotnet test tests/Elsa/Workflows/Publishing/Api/Tests/Elsa.Workflows.Publishing.Api.Tests.csproj`
- [x] T038 Run `dotnet test tests/Elsa/Workflows/Runtime/Tests/Elsa.Workflows.Runtime.Tests.csproj`
- [x] T039 Run quickstart validation steps from `specs/076-workflow-test-runs/quickstart.md`

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies.
- **Foundational (Phase 2)**: Depends on Setup and blocks all user stories.
- **User Story 1 (Phase 3)**: Depends on Foundational and is the MVP.
- **User Story 2 (Phase 4)**: Depends on Foundational; can be built after or alongside US1 handler shape, but its user value is rejection feedback.
- **User Story 3 (Phase 5)**: Depends on US1 transient storage/dispatch shape.
- **Polish (Phase 6)**: Depends on selected story phases.

### User Story Dependencies

- **US1 (P1)**: No dependencies after Foundational.
- **US2 (P2)**: Depends on the test-run handler introduced by US1, but rejection behavior remains independently testable.
- **US3 (P3)**: Depends on transient artifact creation from US1.

### Parallel Opportunities

- T002 can run independently of folder creation.
- T003, T004, and T008 can be worked in parallel before wiring.
- T010 and T011 can be written in parallel before US1 implementation.
- T012 through T016 touch separate model/contract files and can be parallelized.
- T023 through T025 can be written in parallel for separate rejection scenarios.
- T028 and T029 can be written in parallel because they cover runtime dispatch and publishing transient store behavior separately.
- T034 through T036 can be done in parallel before final test runs.

---

## Parallel Example: User Story 1

```bash
# Tests first:
Task: "Add accepted test-run handler test in tests/Elsa/Workflows/Publishing/Api/Tests/WorkflowTestRunRequestHandlerTests.cs"
Task: "Add normal durable store isolation assertion in tests/Elsa/Workflows/Publishing/Api/Tests/WorkflowTestRunRequestHandlerTests.cs"

# Independent model/contract files:
Task: "Add WorkflowTestRun model and status enum in src/Elsa/Workflows/Publishing/Api/Models/WorkflowTestRun.cs"
Task: "Add WorkflowTestRunView response model in src/Elsa/Workflows/Publishing/Api/Models/WorkflowTestRunViews.cs"
Task: "Add StartWorkflowTestRun mediator request in src/Elsa/Workflows/Publishing/Api/Requests/StartWorkflowTestRun.cs"
Task: "Add IWorkflowTestRunStore in src/Elsa/Workflows/Publishing/Api/Contracts/IWorkflowTestRunStore.cs"
Task: "Add ITransientWorkflowExecutableStore in src/Elsa/Workflows/Publishing/Api/Contracts/ITransientWorkflowExecutableStore.cs"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1 and Phase 2 to preserve publish behavior while extracting shared compile logic.
2. Complete Phase 3 to support accepted designer test runs.
3. Validate that accepted test runs dispatch and durable published artifact storage remains clean.

### Incremental Delivery

1. Foundation: shared compiler and unchanged publish path.
2. MVP: accepted workflow definition test runs.
3. Rejection feedback: invalid workflow state persists rejected test-run results without dispatch.
4. Isolation/cleanup: transient artifacts cannot leak into production start paths and can expire.

### Constitution Gates During Implementation

- Do not add any `Elsa.Workflows.Design.*` reference to `src/Elsa/Workflows/Runtime/*`.
- Do not make normal `ExecuteWorkflow` accept transient artifact ids.
- Keep compile/test-run bridge code in `Elsa.Workflows.Publishing.Api`.
- Add focused tests for every new logic-bearing service and registration change.
