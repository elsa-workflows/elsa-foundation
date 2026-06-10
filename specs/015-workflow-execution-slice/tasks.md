# Tasks: Workflow Execution Vertical Slice

**Input**: Design documents from `specs/015-workflow-execution-slice/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/rest-api.md, quickstart.md

**Tests**: Required by FR-013 and framework §2.23.

**Organization**: Tasks are grouped by user story to enable independently demonstrable increments.

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Establish the feature surface and compile-time project references.

- [x] T001 Create `src/Elsa/Workflows/Runtime/Api/Elsa.Workflows.Runtime.Api.csproj` with references to `Elsa.Workflows.Runtime.Core`, `Elsa.Api.FastEndpoints`, and `Elsa.Mediator.Core`
- [x] T002 Add `Elsa.Workflows.Runtime.Api` to `Elsa.Server.slnx`
- [x] T003 Register the Runtime API assembly in `src/Apps/Elsa.Server/Program.cs`
- [x] T004 Add `Elsa.Workflows.Runtime.Core` reference to `src/Elsa/Workflows/Publishing/Api/Elsa.Workflows.Publishing.Api.csproj`
- [x] T005 Add `Elsa.Workflows.Design.Persistence.Core` reference to `src/Elsa/Workflows/Publishing/Api/Elsa.Workflows.Publishing.Api.csproj`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Add runtime artifact/execution support that publishing and execution both use.

- [x] T006 Extend `src/Elsa/Workflows/Runtime/Core/Models/WorkflowExecutable.cs` with `ExecutableEdge` and start-node support
- [x] T007 Add `WorkflowExecutionResult` and `ActivityExecutionResult` models in `src/Elsa/Workflows/Runtime/Core/Models/WorkflowExecutionResult.cs`
- [x] T008 Add `IWorkflowExecutableStore` in `src/Elsa/Workflows/Runtime/Core/Contracts/IWorkflowExecutableStore.cs`
- [x] T009 Add `IWorkflowExecutor` in `src/Elsa/Workflows/Runtime/Core/Contracts/IWorkflowExecutor.cs`
- [x] T010 Implement `InMemoryWorkflowExecutableStore` in `src/Elsa/Workflows/Runtime/Core/Services/InMemoryWorkflowExecutableStore.cs`
- [x] T011 Implement `SimpleActivityExecutionContext` in `src/Elsa/Workflows/Runtime/Core/Services/SimpleActivityExecutionContext.cs`
- [x] T012 Implement `SequentialWorkflowExecutor` in `src/Elsa/Workflows/Runtime/Core/Services/SequentialWorkflowExecutor.cs`
- [x] T013 Update `src/Elsa/Workflows/Runtime/Core/EXTENSION_POINTS.md` for new replacement contracts

---

## Phase 3: User Story 1 - Publish A JSON-Authored Workflow (Priority: P1)

**Goal**: Publish a design workflow version into a runtime artifact.

**Independent Test**: A workflow version with two connected `WriteLine` nodes publishes into an executable with two nodes, one edge, and one start node.

### Tests for User Story 1

- [x] T014 [US1] Add publish compiler tests in `tests/Elsa/Workflows/Publishing/Api/Tests/PublishWorkflowRequestHandlerTests.cs`
- [x] T015 [US1] Add publishing feature registration tests in `tests/Elsa/Workflows/Publishing/Api/Tests/WorkflowsPublishingApiFeatureTests.cs`

### Implementation for User Story 1

- [x] T016 [US1] Add `PublishWorkflow` request in `src/Elsa/Workflows/Publishing/Api/Requests/PublishWorkflow.cs`
- [x] T017 [US1] Add `PublishedWorkflowView` in `src/Elsa/Workflows/Publishing/Api/Models/PublishedWorkflowView.cs`
- [x] T018 [US1] Implement `PublishWorkflowRequestHandler` in `src/Elsa/Workflows/Publishing/Api/Handlers/PublishWorkflowRequestHandler.cs`
- [x] T019 [US1] Add `PublishWorkflow` endpoint in `src/Elsa/Workflows/Publishing/Api/Endpoints/PublishWorkflow.cs`
- [x] T020 [US1] Register publishing handler dependencies in `src/Elsa/Workflows/Publishing/Api/WorkflowsPublishingApiFeature.cs`

---

## Phase 4: User Story 2 - Execute A Published Workflow Artifact (Priority: P1)

**Goal**: Execute a published artifact through Runtime REST.

**Independent Test**: Saving a two-node executable and executing it returns completed workflow and activity execution results in order.

### Tests for User Story 2

- [x] T021 [US2] Add executor tests in `tests/Elsa/Workflows/Runtime/Tests/SequentialWorkflowExecutorTests.cs`
- [x] T022 [US2] Add runtime API registration tests in `tests/Elsa/Workflows/Runtime/Tests/WorkflowsRuntimeApiFeatureTests.cs`
- [x] T023 [US2] Add architecture dependency tests in `tests/Elsa/Architecture/RuntimeExecutionSliceDependencyTests.cs`

### Implementation for User Story 2

- [x] T024 [US2] Add runtime route constants in `src/Elsa/Workflows/Runtime/Api/Constants/RouteConstants.cs`
- [x] T025 [US2] Add `ExecuteWorkflow` request in `src/Elsa/Workflows/Runtime/Api/Requests/ExecuteWorkflow.cs`
- [x] T026 [US2] Add workflow execution API views in `src/Elsa/Workflows/Runtime/Api/Models/WorkflowExecutionViews.cs`
- [x] T027 [US2] Add execute endpoint in `src/Elsa/Workflows/Runtime/Api/Endpoints/Execute.cs`
- [x] T028 [US2] Add `WorkflowsRuntimeApiFeature` registrations in `src/Elsa/Workflows/Runtime/Api/WorkflowsRuntimeApiFeature.cs`

---

## Phase 5: User Story 3 - Demonstrate The End-To-End REST Journey (Priority: P2)

**Goal**: Provide a runnable demo script and verify server composition.

**Independent Test**: Start `Elsa.Server`, follow the checked-in HTTP script, and receive a completed execute response.

- [x] T029 [US3] Add demo requests in `src/Elsa/Workflows/Publishing/Api/_requests/workflow-execution-slice.http`
- [x] T030 [US3] Update `specs/015-workflow-execution-slice/quickstart.md` if implementation route or JSON shape changes
- [x] T031 [US3] Build `src/Apps/Elsa.Server/Elsa.Server.csproj`

---

## Phase 6: User Story 4 - Reject Unsupported Workflow Shapes Clearly (Priority: P2)

**Goal**: Make unsupported shapes fail with deterministic diagnostics.

**Independent Test**: Unsupported publish/execute cases produce targeted exceptions/messages.

- [x] T032 [US4] Add publish rejection tests for missing start, multiple starts, fan-out, unknown activity row, and non-literal input in `tests/Elsa/Workflows/Publishing/Api/Tests/PublishWorkflowRequestHandlerTests.cs`
- [x] T033 [US4] Add execute rejection tests for unknown artifact and fan-out executable shape in `tests/Elsa/Workflows/Runtime/Tests/SequentialWorkflowExecutorTests.cs`
- [x] T034 [US4] Implement missing diagnostics in publishing/runtime services

---

## Phase 7: Polish & Cross-Cutting Concerns

- [x] T035 Run `dotnet test tests/Elsa/Workflows/Runtime/Tests/Elsa.Workflows.Runtime.Tests.csproj`
- [x] T036 Run `dotnet test tests/Elsa/Workflows/Publishing/Api/Tests/Elsa.Workflows.Publishing.Api.Tests.csproj`
- [x] T037 Run `dotnet test tests/Elsa/Architecture/Elsa.Architecture.Tests.csproj`
- [x] T038 Run targeted build/test for `src/Apps/Elsa.Server/Elsa.Server.csproj`
- [x] T039 Self-review changed code for DRY, dependency direction, unsupported shortcut leakage, and test gaps
- [x] T040 Commit the completed work unit locally

---

## Dependencies & Execution Order

### Phase Dependencies

- Phase 1 blocks all source/test changes that reference the new Runtime API project.
- Phase 2 blocks publishing and execution implementation.
- User Stories 1 and 2 can proceed after Phase 2 but should be completed before the demo script.
- User Story 4 diagnostics should be completed before final validation.
- Polish depends on all selected user stories.

### MVP First

1. Complete Phases 1 and 2.
2. Complete User Story 1 publishing.
3. Complete User Story 2 execution.
4. Validate with tests and a manual server build.
5. Add demo quickstart and rejection diagnostics.

### Parallel Opportunities

- T001-T005 can be split across project files after branch setup.
- T014/T015 can be written before T016-T020 implementation.
- T021-T023 can be written before T024-T028 implementation.
- T035-T037 can run independently after implementation.
