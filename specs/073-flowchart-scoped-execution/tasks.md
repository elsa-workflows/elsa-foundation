# Tasks: Flowchart Scoped Execution

**Input**: Design documents from `specs/073-flowchart-scoped-execution/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/

**Tests**: Required by the specification and constitution. Write focused failing tests before implementation tasks in each user-story phase.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing.

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Prepare feature files and preserve current Flowchart baseline before engine replacement.

- [X] T001 Review existing Flowchart runtime and test behavior in `src/elsa/Activities/Flowchart/Activities/Flowchart.cs` and `tests/Elsa/Activities/Flowchart/Tests/FlowchartActivityTests.cs`
- [X] T002 [P] Create public contract namespace folder in `src/elsa/Activities/Flowchart/Contracts/`
- [X] T003 [P] Create built-in policy folder in `src/elsa/Activities/Flowchart/Internal/Policies/`
- [X] T004 [P] Add placeholder focused test files `tests/Elsa/Activities/Flowchart/Tests/FlowchartExecutionEngineTests.cs`, `tests/Elsa/Activities/Flowchart/Tests/FlowchartPolicyContractTests.cs`, `tests/Elsa/Activities/Flowchart/Tests/FlowchartImplicitJoinTests.cs`, `tests/Elsa/Activities/Flowchart/Tests/FlowchartLoopIterationTests.cs`, and `tests/Elsa/Activities/Flowchart/Tests/FlowchartRacePolicyTests.cs`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Define the shared state model, graph metadata, and policy seam required by all stories.

**CRITICAL**: No user story work can begin until this phase is complete.

- [X] T005 [P] Add execution scope and execution path enums/models in `src/elsa/Activities/Flowchart/Models/ExecutionScope.cs` and `src/elsa/Activities/Flowchart/Models/ExecutionPath.cs`
- [X] T006 [P] Add Flowchart state, arrival, active-child, and diagnostic models in `src/elsa/Activities/Flowchart/Models/FlowchartExecutionState.cs`, `src/elsa/Activities/Flowchart/Models/FlowchartArrival.cs`, `src/elsa/Activities/Flowchart/Models/FlowchartActiveChild.cs`, and `src/elsa/Activities/Flowchart/Models/FlowchartDiagnosticEvent.cs`
- [X] T007 [P] Add policy metadata models in `src/elsa/Activities/Flowchart/Models/FlowchartNodeMetadata.cs`, `src/elsa/Activities/Flowchart/Models/FlowchartConnectionMetadata.cs`, and update `src/elsa/Activities/Flowchart/Models/FlowchartStructure.cs`
- [X] T008 [P] Add policy command and decision models in `src/elsa/Activities/Flowchart/Models/FlowchartPolicyCommand.cs` and `src/elsa/Activities/Flowchart/Models/FlowchartPolicyDecision.cs`
- [X] T009 [P] Add public policy contracts in `src/elsa/Activities/Flowchart/Contracts/IFlowchartPolicy.cs`, `src/elsa/Activities/Flowchart/Contracts/IFlowchartPolicyRegistry.cs`, and `src/elsa/Activities/Flowchart/Contracts/IFlowchartPolicyContext.cs`
- [X] T010 Add policy registry implementation in `src/elsa/Activities/Flowchart/Internal/FlowchartPolicyRegistry.cs`
- [X] T011 Add graph topology and reachability helpers in `src/elsa/Activities/Flowchart/Internal/FlowchartGraph.cs` and `src/elsa/Activities/Flowchart/Internal/FlowchartReachabilityAnalyzer.cs`
- [X] T012 Add Flowchart execution engine command application skeleton in `src/elsa/Activities/Flowchart/Internal/FlowchartExecutionEngine.cs`
- [X] T013 Register the execution engine, registry, reachability analyzer, and built-in policy services in `src/elsa/Activities/Flowchart/ActivitiesFlowchartFeature.cs`
- [X] T014 [P] Add feature registration tests for policy services in `tests/Elsa/Activities/Flowchart/Tests/ActivitiesFlowchartFeatureTests.cs`
- [X] T015 [P] Add policy contract validation tests for read-only context and command-returning behavior in `tests/Elsa/Activities/Flowchart/Tests/FlowchartPolicyContractTests.cs`

**Checkpoint**: Foundation ready - user story implementation can now begin.

---

## Phase 3: User Story 1 - Run reconverging flowcharts without explicit joins (Priority: P1) MVP

**Goal**: Ordinary multi-inbound activities synchronize active branches by default without explicit Join activities.

**Independent Test**: Run diamond and decision-reconvergence Flowcharts; the reconverged activity runs exactly once and never waits for untaken branches.

### Tests for User Story 1

- [X] T016 [P] [US1] Add failing direct-continuation metadata test in `tests/Elsa/Activities/Flowchart/Tests/FlowchartActivityTests.cs`
- [X] T017 [P] [US1] Add failing diamond implicit-join engine test in `tests/Elsa/Activities/Flowchart/Tests/FlowchartImplicitJoinTests.cs`
- [X] T018 [P] [US1] Add failing decision dead-path reconvergence test in `tests/Elsa/Activities/Flowchart/Tests/FlowchartImplicitJoinTests.cs`
- [X] T019 [P] [US1] Add failing runtime test for reconverged activity running once in `tests/Elsa/Activities/Flowchart/Tests/FlowchartRuntimeTests.cs`

### Implementation for User Story 1

- [X] T020 [US1] Implement root scope and start execution path creation in `src/elsa/Activities/Flowchart/Internal/FlowchartExecutionEngine.cs`
- [X] T021 [US1] Implement generic `executionPathId` and `executionScopeId` child scheduling metadata in `src/elsa/Activities/Flowchart/Activities/Flowchart.cs`
- [X] T022 [US1] Implement direct continuation policy in `src/elsa/Activities/Flowchart/Internal/Policies/DirectContinuationFlowchartPolicy.cs`
- [X] T023 [US1] Implement implicit activation-aware join policy in `src/elsa/Activities/Flowchart/Internal/Policies/ImplicitActivationJoinFlowchartPolicy.cs`
- [X] T024 [US1] Implement arrival recording and consumption for implicit joins in `src/elsa/Activities/Flowchart/Internal/FlowchartExecutionEngine.cs`
- [X] T025 [US1] Implement dead-path detection for untaken decision branches in `src/elsa/Activities/Flowchart/Internal/FlowchartReachabilityAnalyzer.cs`
- [X] T026 [US1] Update `src/elsa/Activities/Flowchart/Activities/Flowchart.cs` to delegate start/completion handling to the execution engine
- [X] T027 [US1] Add join waiting/firing/dead-path diagnostics in `src/elsa/Activities/Flowchart/Internal/FlowchartExecutionEngine.cs`
- [X] T028 [US1] Update existing direct Flowchart tests to clean-slate scoped execution expectations in `tests/Elsa/Activities/Flowchart/Tests/FlowchartActivityTests.cs`

**Checkpoint**: User Story 1 is independently functional and is the MVP.

---

## Phase 4: User Story 2 - Execute loopbacks without cross-iteration interference (Priority: P2)

**Goal**: Loopbacks create isolated loop iteration scopes so joins are not satisfied by stale arrivals.

**Independent Test**: Run loopback scenarios and verify each iteration has separate execution-scope state.

### Tests for User Story 2

- [X] T029 [P] [US2] Add failing loopback iteration isolation test in `tests/Elsa/Activities/Flowchart/Tests/FlowchartLoopIterationTests.cs`
- [X] T030 [P] [US2] Add failing stale-arrival rejection test in `tests/Elsa/Activities/Flowchart/Tests/FlowchartLoopIterationTests.cs`
- [X] T031 [P] [US2] Add failing ambiguous loopback validation test in `tests/Elsa/Activities/Flowchart/Tests/FlowchartLoopIterationTests.cs`

### Implementation for User Story 2

- [X] T032 [US2] Implement backward-edge classification in `src/elsa/Activities/Flowchart/Internal/FlowchartGraph.cs`
- [X] T033 [US2] Implement loop iteration scope creation in `src/elsa/Activities/Flowchart/Internal/FlowchartExecutionEngine.cs`
- [X] T034 [US2] Enforce loop iteration keys during join evaluation in `src/elsa/Activities/Flowchart/Internal/Policies/ImplicitActivationJoinFlowchartPolicy.cs`
- [X] T035 [US2] Implement conservative ambiguous-loop validation in `src/elsa/Activities/Flowchart/Internal/FlowchartReachabilityAnalyzer.cs`
- [X] T036 [US2] Add loop iteration diagnostics in `src/elsa/Activities/Flowchart/Internal/FlowchartExecutionEngine.cs`

**Checkpoint**: User Story 2 works independently with the MVP foundation.

---

## Phase 5: User Story 3 - Extend gateway behavior through public policies (Priority: P3)

**Goal**: Module authors can add custom routing/join behavior through public policy contracts.

**Independent Test**: Register a custom policy, configure a Flowchart node to use it, and verify the engine applies returned commands.

### Tests for User Story 3

- [X] T037 [P] [US3] Add failing custom policy registration test in `tests/Elsa/Activities/Flowchart/Tests/FlowchartPolicyContractTests.cs`
- [X] T038 [P] [US3] Add failing custom policy command application test in `tests/Elsa/Activities/Flowchart/Tests/FlowchartExecutionEngineTests.cs`
- [X] T039 [P] [US3] Add failing invalid policy command rejection test in `tests/Elsa/Activities/Flowchart/Tests/FlowchartPolicyContractTests.cs`

### Implementation for User Story 3

- [X] T040 [US3] Implement policy lookup from Flowchart node metadata in `src/elsa/Activities/Flowchart/Internal/FlowchartExecutionEngine.cs`
- [X] T041 [US3] Implement read-only policy context in `src/elsa/Activities/Flowchart/Internal/FlowchartPolicyContext.cs`
- [X] T042 [US3] Implement policy command validation in `src/elsa/Activities/Flowchart/Internal/FlowchartExecutionEngine.cs`
- [X] T043 [US3] Implement custom policy registration behavior in `src/elsa/Activities/Flowchart/Internal/FlowchartPolicyRegistry.cs`
- [X] T044 [US3] Document public policy contracts in `src/elsa/Activities/Flowchart/EXTENSION_POINTS.md`

**Checkpoint**: User Story 3 works independently after foundational tasks.

---

## Phase 6: User Story 4 - Explain advanced Flowchart decisions (Priority: P4)

**Goal**: Flowchart execution records useful diagnostics for scheduling, waiting, joining, dead paths, loop iterations, cancellation, and policy failures.

**Independent Test**: Execute advanced scenarios and inspect diagnostic records for graph/user explanations.

### Tests for User Story 4

- [X] T045 [P] [US4] Add failing join diagnostic test in `tests/Elsa/Activities/Flowchart/Tests/FlowchartExecutionEngineTests.cs`
- [X] T046 [P] [US4] Add failing dead-path diagnostic test in `tests/Elsa/Activities/Flowchart/Tests/FlowchartImplicitJoinTests.cs`
- [X] T047 [P] [US4] Add failing policy failure diagnostic test in `tests/Elsa/Activities/Flowchart/Tests/FlowchartPolicyContractTests.cs`

### Implementation for User Story 4

- [X] T048 [US4] Centralize diagnostic event creation in `src/elsa/Activities/Flowchart/Internal/FlowchartExecutionEngine.cs`
- [X] T049 [US4] Add graph/user diagnostic messages for waiting joins and dead paths in `src/elsa/Activities/Flowchart/Internal/Policies/ImplicitActivationJoinFlowchartPolicy.cs`
- [X] T050 [US4] Add policy failure diagnostic handling in `src/elsa/Activities/Flowchart/Internal/FlowchartExecutionEngine.cs`

**Checkpoint**: User Story 4 diagnostics are independently verifiable.

---

## Phase 7: Additional Built-in Gateway Policies

**Purpose**: Complete the v1 built-in gateway set after the core model and public policy seam are proven.

- [X] T051 [P] Add failing Decision policy tests in `tests/Elsa/Activities/Flowchart/Tests/FlowchartExecutionEngineTests.cs`
- [X] T052 [P] Add failing Parallel Fork/Join policy tests in `tests/Elsa/Activities/Flowchart/Tests/FlowchartExecutionEngineTests.cs`
- [X] T053 [P] Add failing Inclusive Fork/Join policy tests in `tests/Elsa/Activities/Flowchart/Tests/FlowchartExecutionEngineTests.cs`
- [X] T054 [P] Add failing First Wins race policy tests in `tests/Elsa/Activities/Flowchart/Tests/FlowchartRacePolicyTests.cs`
- [X] T055 [P] Add failing Merge policy tests in `tests/Elsa/Activities/Flowchart/Tests/FlowchartExecutionEngineTests.cs`
- [X] T056 Implement Decision policy in `src/elsa/Activities/Flowchart/Internal/Policies/DecisionFlowchartPolicy.cs`
- [X] T057 Implement Parallel Fork and Parallel Join policies in `src/elsa/Activities/Flowchart/Internal/Policies/ParallelForkFlowchartPolicy.cs` and `src/elsa/Activities/Flowchart/Internal/Policies/ParallelJoinFlowchartPolicy.cs`
- [X] T058 Implement Inclusive Fork and Inclusive Join policies in `src/elsa/Activities/Flowchart/Internal/Policies/InclusiveForkFlowchartPolicy.cs` and `src/elsa/Activities/Flowchart/Internal/Policies/InclusiveJoinFlowchartPolicy.cs`
- [X] T059 Implement First Wins policy in `src/elsa/Activities/Flowchart/Internal/Policies/FirstWinsFlowchartPolicy.cs`
- [X] T060 Implement Merge policy in `src/elsa/Activities/Flowchart/Internal/Policies/MergeFlowchartPolicy.cs`
- [X] T061 Register all built-in policies in `src/elsa/Activities/Flowchart/ActivitiesFlowchartFeature.cs`

---

## Phase 8: Polish & Cross-Cutting Concerns

**Purpose**: Documentation, extension catalogs, generated maps, and final validation.

- [X] T062 [P] Update Flowchart README with scoped execution overview in `src/elsa/Activities/Flowchart/README.md`
- [X] T063 [P] Update root extension-point catalog for Flowchart policies in `EXTENSION_POINTS.md`
- [X] T064 Refresh generated extension-point maps using `bash tools/maps/generate-extension-point-map.sh`
- [X] T065 Refresh generated test maps using `bash tools/maps/generate-maps.sh` if test-map inputs changed
- [X] T066 Run Flowchart test project with `dotnet test tests/Elsa/Activities/Flowchart/Tests/Elsa.Activities.Flowchart.Tests.csproj`
- [X] T067 Run runtime-adjacent test projects when shared runtime behavior is touched: `dotnet test tests/Elsa/Activities/Runtime/Tests/Elsa.Activities.Runtime.Tests.csproj` and `dotnet test tests/Elsa/Workflows/Runtime/Tests/Elsa.Workflows.Runtime.Tests.csproj`

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies.
- **Foundational (Phase 2)**: Depends on Setup; blocks all user stories.
- **US1 MVP (Phase 3)**: Depends on Foundational.
- **US2 (Phase 4)**: Depends on Foundational and benefits from US1 join infrastructure.
- **US3 (Phase 5)**: Depends on Foundational; can run parallel with US1/US2 after the core policy contracts exist.
- **US4 (Phase 6)**: Depends on Foundational and can be layered onto each story, but final diagnostics depend on US1/US3 behavior.
- **Built-in Gateway Policies (Phase 7)**: Depends on US1 and US3.
- **Polish (Phase 8)**: Depends on desired implementation phases.

### User Story Dependencies

- **US1 (P1)**: MVP; no dependency on other stories after Foundation.
- **US2 (P2)**: Uses US1 join infrastructure but remains independently testable.
- **US3 (P3)**: Can start after Foundation; validates public extension seam.
- **US4 (P4)**: Can start after Foundation; each diagnostic task maps to behavior from US1/US3.

### Parallel Opportunities

- T002-T004 can run in parallel.
- T005-T009 and T014-T015 can run in parallel after Setup.
- T016-T019 can run in parallel before US1 implementation.
- T029-T031 can run in parallel before US2 implementation.
- T037-T039 can run in parallel before US3 implementation.
- T045-T047 can run in parallel before US4 implementation.
- T051-T055 can run in parallel before built-in policy implementation.
- T062-T063 can run in parallel during polish.

---

## Parallel Example: User Story 1

```text
Task: "T016 Add failing direct-continuation metadata test in tests/Elsa/Activities/Flowchart/Tests/FlowchartActivityTests.cs"
Task: "T017 Add failing diamond implicit-join engine test in tests/Elsa/Activities/Flowchart/Tests/FlowchartImplicitJoinTests.cs"
Task: "T018 Add failing decision dead-path reconvergence test in tests/Elsa/Activities/Flowchart/Tests/FlowchartImplicitJoinTests.cs"
Task: "T019 Add failing runtime test for reconverged activity running once in tests/Elsa/Activities/Flowchart/Tests/FlowchartRuntimeTests.cs"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1 and Phase 2.
2. Complete Phase 3 tests and implementation.
3. Run `dotnet test tests/Elsa/Activities/Flowchart/Tests/Elsa.Activities.Flowchart.Tests.csproj`.
4. Stop for review before adding loop/race/custom-policy complexity.

### Incremental Delivery

1. US1: Scoped execution state + direct continuation + implicit activation-aware join.
2. US2: Loop iteration isolation and conservative loop validation.
3. US3: Public policy extension seam and custom policy behavior.
4. US4: Diagnostics hardening.
5. Phase 7: Remaining built-in policies.

### Review Points

- Review after foundational contracts before implementing policies.
- Review after US1 MVP before loop/race work.
- Review public policy contract before documenting it as an extension point.
- Review extension-point docs and generated maps before final validation.

## Notes

- Public gateway policies are extension points and must be documented when implemented.
- `.agent-prefs/*.md` files are local preferences and must not be committed.
- The current Flowchart implementation is treated as clean-slate replaceable; preserve user-visible simple-flow behavior through tests, not through compatibility mode.
