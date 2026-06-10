# Tasks: Runtime Executable Artifact And Execution State Contracts

## Phase 1: Documentation And Setup

- [X] T001 Create Speckit specification artifacts under `specs/007-runtime-executable-state-contracts/`
- [X] T002 Point `.specify/feature.json` and `AGENTS.md` Speckit context at `specs/007-runtime-executable-state-contracts/plan.md`

## Phase 2: Runtime Contracts

- [X] T003 [P] Add workflow executable artifact contracts in `src/Elsa/Workflows/Runtime/Core/Models/WorkflowExecutable.cs`
- [X] T004 [P] Add workflow execution state contracts in `src/Elsa/Workflows/Runtime/Core/Models/WorkflowExecutionState.cs`
- [X] T005 [P] Add activity execution state contracts in `src/Elsa/Workflows/Runtime/Core/Models/ActivityExecutionState.cs`
- [X] T006 [P] Add scheduler state contracts in `src/Elsa/Workflows/Runtime/Core/Models/SchedulerState.cs`
- [X] T007 [P] Add durable value state contracts in `src/Elsa/Workflows/Runtime/Core/Models/DurableValueState.cs`
- [X] T008 Add checkpoint names, checkpoint models, policy hooks, and default policy in `src/Elsa/Workflows/Runtime/Core/`
- [X] T009 Add workflow execution agent/provider abstractions in `src/Elsa/Workflows/Runtime/Core/Contracts/`

## Phase 3: Tests And Guards

- [X] T010 Add workflow runtime test project in `tests/Elsa/Workflows/Runtime/Tests/`
- [X] T011 Add focused runtime contract tests in `tests/Elsa/Workflows/Runtime/Tests/RuntimeContractTests.cs`
- [X] T012 Add runtime dependency boundary tests in `tests/Elsa/Workflows/Runtime/Tests/RuntimeDependencyBoundaryTests.cs`
- [X] T013 Update architecture guard tests in `tests/Elsa/Architecture/ArchitectureGuardTests.cs`
- [X] T014 Add the new workflow runtime test project to `Elsa.Server.slnx`

## Phase 4: Validation

- [X] T015 Run workflow runtime tests
- [X] T016 Run architecture guard tests
- [X] T017 Refresh relevant generated maps if required by changed project inputs
- [X] T018 Commit completed work locally
