# Tasks: Activity Execution Inspection

**Input**: Design documents from `/specs/079-activity-execution-inspection/`

**Prerequisites**: plan.md, spec.md, data-model.md, contracts/activity-execution-inspection.md

**Tests**: Required by the plan and Elsa constitution gates for feature registration and logic-bearing implementations.

**Organization**: Tasks are grouped by user story to enable independently testable increments.

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Verify repository setup and implementation targets.

- [X] T001 Verify .gitignore contains C#/.NET and universal generated-file patterns in .gitignore
- [X] T002 Inspect runtime checkpoint, scheduler handler, store, API, and nearby test conventions in src/Elsa and tests/Elsa
- [X] T003 [P] Review extension-point catalog entries for runtime stores and API surfaces in EXTENSION_POINTS.md

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Shared models, contracts, registrations, checkpoint lane, and persistence that all stories depend on.

**CRITICAL**: No user story work can begin until this phase is complete.

### Tests for Foundational Infrastructure

- [X] T004 [P] Add runtime feature registration tests for activity execution inspection services in tests/Elsa/Workflows/Runtime/Tests
- [X] T005 [P] Add in-memory inspection store tests for save/find/list ordering in tests/Elsa/Workflows/Runtime/Tests
- [X] T006 [P] Add checkpoint committer tests for activity execution inspection lane persistence in tests/Elsa/Workflows/Runtime/Tests
- [X] T007 [P] Add Groundwork inspection store persistence tests in tests/Elsa/Persistence/Groundwork/Tests

### Implementation for Foundational Infrastructure

- [X] T008 [P] Add ActivitySchedulingProvenance model in src/Elsa/Workflows/Runtime/Core/Models/ActivitySchedulingProvenance.cs
- [X] T009 [P] Add ActivityExecutionInspectionProjection model in src/Elsa/Workflows/Runtime/Core/Models/ActivityExecutionInspectionProjection.cs
- [X] T010 [P] Add ActivityExecutionInspectionValueSnapshot model and subject enum in src/Elsa/Workflows/Runtime/Core/Models/ActivityExecutionInspectionValueSnapshot.cs
- [X] T011 [P] Add activity execution bookmark and incident summary models in src/Elsa/Workflows/Runtime/Core/Models
- [X] T012 Update ActivityExecutionState with execution sequence and scheduling provenance in src/Elsa/Workflows/Runtime/Core/Models/ActivityExecutionState.cs
- [X] T013 Add IActivityExecutionInspectionStore contract in src/Elsa/Workflows/Runtime/Core/Contracts/IActivityExecutionInspectionStore.cs
- [X] T014 Add IRuntimeActivityExecutionInspectionAccumulator contract in src/Elsa/Workflows/Runtime/Core/Contracts/IRuntimeActivityExecutionInspectionAccumulator.cs
- [X] T015 Implement in-memory activity execution inspection store in src/Elsa/Workflows/Runtime/Core/Services/InMemoryActivityExecutionInspectionStore.cs
- [X] T016 Implement runtime activity execution inspection accumulator in src/Elsa/Workflows/Runtime/Core/Services/RuntimeActivityExecutionInspectionAccumulator.cs
- [X] T017 Extend RuntimeCheckpointStateChangeSet and RuntimeCheckpointCommit with activity execution inspection changes in src/Elsa/Workflows/Runtime/Core/Models/RuntimeCheckpointCommit.cs
- [X] T018 Persist inspection changes from RuntimeCheckpointCommitter in src/Elsa/Workflows/Runtime/Core/Services/RuntimeCheckpointCommitter.cs
- [X] T019 Register inspection store and accumulator in runtime feature/service registration files under src/Elsa/Workflows/Runtime
- [X] T020 Add Groundwork activity execution inspection store in src/Elsa/Persistence/Groundwork/Stores/GroundworkActivityExecutionInspectionStore.cs
- [X] T021 Update Groundwork runtime storage manifest/indexing for activity execution inspection projections in src/Elsa/Persistence/Groundwork/ElsaRuntimeStorageManifest.cs

**Checkpoint**: Runtime can commit and query inspection projections independently of scheduler behavior.

---

## Phase 3: User Story 1 - Inspect each concrete activity execution (Priority: P1) MVP

**Goal**: Operators can see distinct committed executions for the same authored activity with stable identities, statuses, timestamps, and deterministic ordering.

**Independent Test**: Run or simulate multiple executions for one authored activity and confirm all committed projections are distinct and deterministically ordered.

### Tests for User Story 1

- [X] T022 [P] [US1] Add scheduler schedule/start inspection projection tests in tests/Elsa/Workflows/Runtime/Tests
- [X] T023 [P] [US1] Add API detail handler tests for found and not-found activity execution projections in tests/Elsa/Workflows/Runtime/Tests

### Implementation for User Story 1

- [X] T024 [US1] Extend RuntimeScheduleActivityCommandPayload and RuntimeChildActivityScheduleRequest with scheduling provenance in src/Elsa/Workflows/Runtime/Core/Models
- [X] T025 [US1] Refactor WorkflowScheduleActivitySchedulerWorkHandler to checkpoint scheduled state and initial inspection projection before post-commit start work in src/Elsa/Workflows/Runtime/Core/Services/WorkflowScheduleActivitySchedulerWorkHandler.cs
- [X] T026 [US1] Refactor WorkflowStartActivitySchedulerWorkHandler to checkpoint running state and inspection projection before post-commit invoke work in src/Elsa/Workflows/Runtime/Core/Services/WorkflowStartActivitySchedulerWorkHandler.cs
- [X] T027 [US1] Add activity execution detail request, handler, endpoint, and API response model in src/Elsa/Workflows/Runtime/Api
- [X] T028 [US1] Update workflow instance runtime view models to expose lightweight activity execution summaries in src/Elsa/Workflows/Runtime/Api/Models/WorkflowExecutionViews.cs

**Checkpoint**: User Story 1 is fully functional and testable independently.

---

## Phase 4: User Story 2 - Trust committed execution evidence after recovery (Priority: P2)

**Goal**: Inspection evidence is committed with lifecycle state and dependent scheduler work advances only after durable scheduler-boundary checkpoints.

**Independent Test**: Confirm schedule, start, completion, suspension, and fault boundaries persist state/projection before downstream work is enqueued.

### Tests for User Story 2

- [X] T029 [P] [US2] Add completion checkpoint/post-commit scheduler intent tests in tests/Elsa/Activities/Runtime/Tests
- [X] T030 [P] [US2] Add bookmark inspection checkpoint tests in tests/Elsa/Workflows/Runtime/Tests
- [X] T031 [P] [US2] Add fault inspection checkpoint tests in tests/Elsa/Activities/Runtime/Tests

### Implementation for User Story 2

- [X] T032 [US2] Refactor WorkflowInvokeActivitySchedulerWorkHandler normal completion to checkpoint completed state, outputs, inspection projection, and post-commit scheduler work in src/Elsa/Activities/Runtime/Services/WorkflowInvokeActivitySchedulerWorkHandler.cs
- [X] T033 [US2] Update WorkflowCreateBookmarkSchedulerWorkHandler to include bookmark inspection summaries in checkpoint commits in src/Elsa/Workflows/Runtime/Core/Services/WorkflowCreateBookmarkSchedulerWorkHandler.cs
- [X] T034 [US2] Update ActivityFaultIncidentRecorder and fault checkpoint paths to include incident inspection summaries in src/Elsa/Activities/Runtime/Services
- [X] T035 [US2] Ensure scheduler-boundary checkpoints use mandatory persistence semantics before post-commit advancement in src/Elsa/Workflows/Runtime/Core/Services

**Checkpoint**: User Stories 1 and 2 both work independently.

---

## Phase 5: User Story 3 - Correlate composite scheduling and waits (Priority: P3)

**Goal**: Composite scheduling and waits expose parent/scheduler/path/scope/branch/iteration provenance when available.

**Independent Test**: Flowchart loopback scheduling produces distinguishable child execution provenance for repeated authored activities.

### Tests for User Story 3

- [X] T036 [P] [US3] Add Flowchart provenance propagation tests in tests/Elsa/Activities/Flowchart/Tests
- [X] T037 [P] [US3] Add bookmark summary projection tests for wait/resume target evidence in tests/Elsa/Workflows/Runtime/Tests

### Implementation for User Story 3

- [X] T038 [US3] Populate ActivitySchedulingProvenance from FlowchartExecutionEngine scheduling metadata in src/Elsa/Activities/Flowchart/Internal/FlowchartExecutionEngine.cs
- [X] T039 [US3] Thread scheduling provenance through child scheduling services and scheduler payload creation in src/Elsa/Workflows/Runtime/Core
- [X] T040 [US3] Include branch, iteration, execution path, execution scope, and scheduling cause in inspection projections when committed in src/Elsa/Workflows/Runtime/Core/Services

**Checkpoint**: User Story 3 is independently testable without loading value payloads.

---

## Phase 6: User Story 4 - Inspect values and faults safely (Priority: P4)

**Goal**: Activity input/output snapshots and incident summaries are captured according to runtime payload capture policy.

**Independent Test**: Metadata-only and payload-enabled capture policies produce the expected value snapshot payload visibility for inputs, outputs, and input materialization faults.

### Tests for User Story 4

- [X] T041 [P] [US4] Add payload capture policy tests for activity input/output inspection snapshots in tests/Elsa/Activities/Runtime/Tests
- [X] T042 [P] [US4] Add input materialization fault inspection tests in tests/Elsa/Activities/Runtime/Tests

### Implementation for User Story 4

- [X] T043 [US4] Capture policy-governed input value snapshots during activity input materialization in src/Elsa/Activities/Runtime/Services/WorkflowInvokeActivitySchedulerWorkHandler.cs
- [X] T044 [US4] Capture policy-governed output value snapshots during activity output recording in src/Elsa/Activities/Runtime/Services/WorkflowInvokeActivitySchedulerWorkHandler.cs
- [X] T045 [US4] Include fault value and incident evidence for input materialization failures in activity execution inspection projections in src/Elsa/Activities/Runtime/Services

**Checkpoint**: User Story 4 is independently testable with denied, metadata-only, and payload capture decisions.

---

## Phase 7: Polish & Cross-Cutting Concerns

**Purpose**: Documentation, catalogs, generated maps, and validation.

- [X] T046 Update EXTENSION_POINTS.md with activity execution inspection store/API/checkpoint extension surfaces
- [X] T047 Refresh generated extension-point maps with tools/maps/generate-extension-point-map.sh
- [X] T048 Update spec 077 consumer notes if implementation API shape differs from the planned contract in specs/077-workflow-instance-inspection
- [X] T049 Run targeted runtime, activities runtime, flowchart, and Groundwork test suites
- [X] T050 Run repository build or the narrowest available solution build covering changed projects
- [X] T051 Review git diff for generated artifacts, unrelated changes, and task completion markers

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies.
- **Foundational (Phase 2)**: Depends on setup and blocks all user stories.
- **User Story 1 (Phase 3)**: Depends on foundational infrastructure.
- **User Story 2 (Phase 4)**: Depends on US1 scheduler projection surface.
- **User Story 3 (Phase 5)**: Depends on US1 provenance model and US2 checkpoint accumulation.
- **User Story 4 (Phase 6)**: Depends on US2 checkpoint accumulation and US1 detail projection.
- **Polish (Phase 7)**: Depends on all implemented stories.

### User Story Dependencies

- **US1**: MVP and base projection/API story.
- **US2**: Extends US1 with checkpoint trust and post-commit scheduler advancement.
- **US3**: Extends US1/US2 with composite provenance and wait summaries.
- **US4**: Extends US1/US2 with value and fault evidence.

### Parallel Opportunities

- Foundational model tasks T008-T011 can run in parallel.
- Store, accumulator, and Groundwork tests T005-T007 can be written in parallel after model contracts are known.
- User-story tests marked [P] are independent by test file and can run in parallel.
- Catalog/map polish can run after implementation surfaces are stable.

---

## Parallel Example: User Story 1

```bash
# Parallelizable tests:
Task: "Add scheduler schedule/start inspection projection tests in tests/Elsa/Workflows/Runtime/Tests"
Task: "Add API detail handler tests for found and not-found activity execution projections in tests/Elsa/Workflows/Runtime/Tests"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete setup and foundational infrastructure.
2. Implement scheduler scheduled/running checkpoint projections.
3. Add the per-execution detail endpoint.
4. Validate repeated authored activity executions can be distinguished.

### Incremental Delivery

1. US1: Distinct committed executions and detail endpoint.
2. US2: Durable checkpoint trust and post-commit scheduler advancement.
3. US3: Composite provenance and bookmark summaries.
4. US4: Policy-governed values and fault evidence.

### Validation

- All tasks completed and marked `[X]`.
- Tests cover registration, replacement/store behavior, checkpoint lane persistence, scheduler branch behavior, API lookup, Groundwork persistence, Flowchart provenance, and payload capture policy.
- Runtime remains independent of Workflows.Design.
