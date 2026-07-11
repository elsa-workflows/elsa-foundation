# Tasks: Runtime HTTP Hot-Path Performance

**Input**: Design documents from `specs/090-runtime-http-performance/`

**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/runtime-http-performance.md](./contracts/runtime-http-performance.md)

**Tests**: Required. Production behavior follows a RED → GREEN → REFACTOR cycle; latency budgets remain opt-in while structural commit and recovery assertions run in CI.

## Phase 1: Setup

**Purpose**: Prepare durable HTTP integration and measurement surfaces.

- [x] T001 Add Groundwork document and SQLite project references to `tests/Elsa/Activities/Http/IntegrationTests/Elsa.Activities.Http.IntegrationTests.csproj`
- [x] T002 [P] Create the executable measurement-script skeleton and argument contract in `tools/performance/measure-http-workflow.sh`

---

## Phase 2: Foundational — Operator Policy Selection

**Purpose**: Provide the shell-scoped policy selection required by every user story.

- [x] T003 Write failing metadata, default-mode, configured-cap, invalid-setting, provider-capture, and duplicate-composition tests in `tests/Elsa/Workflows/Runtime/Tests/WorkflowsRuntimeCheckpointPersistenceFeatureTests.cs`
- [x] T004 Add `CheckpointPersistenceMode` in `src/Elsa/Workflows/Runtime/Api/Coalescing/CheckpointPersistenceMode.cs`
- [x] T005 Implement post-provider policy composition and validation in `src/Elsa/Workflows/Runtime/Api/Coalescing/WorkflowsRuntimeCheckpointPersistenceFeature.cs`
- [x] T006 Guard the coalescing decorator registration against duplicate application in `src/Elsa/Workflows/Runtime/Api/Coalescing/CoalescingRuntimeCheckpointPersistenceExtensions.cs`
- [x] T007 Run `dotnet test tests/Elsa/Workflows/Runtime/Tests/Elsa.Workflows.Runtime.Tests.csproj` and refactor duplicated test setup while preserving green behavior

**Checkpoint**: A fresh shell can select Immediate or Coalesced after its provider has registered stores, and invalid configuration fails before provider build.

---

## Phase 3: User Story 1 — Fast Synchronous HTTP Response (Priority: P1) 🎯 MVP

**Goal**: Prove the real durable synchronous HTTP path folds checkpoint writes without changing the response or terminal result.

**Independent Test**: Run the SQLite-backed immediate-versus-coalesced HTTP integration comparison and inspect response/state equivalence plus persisted checkpoint-marker deltas.

### Tests for User Story 1

- [x] T008 [US1] Write a failing Groundwork SQLite immediate-versus-coalesced HTTP integration test in `tests/Elsa/Activities/Http/IntegrationTests/HttpEndpointRuntimePerformanceTests.cs`

### Implementation for User Story 1

- [x] T009 [US1] Extend `tests/Elsa/Activities/Http/IntegrationTests/HttpEndpointHostFixture.cs` with isolated runtime-provider, policy, cap, and physical-commit query support
- [x] T010 [US1] Complete response, terminal-state, durable-artifact, exact coalesced-commit, and ≥75% reduction assertions in `tests/Elsa/Activities/Http/IntegrationTests/HttpEndpointRuntimePerformanceTests.cs`
- [x] T011 [US1] Run `dotnet test tests/Elsa/Activities/Http/IntegrationTests/Elsa.Activities.Http.IntegrationTests.csproj` and record the verified physical commit counts in `specs/090-runtime-http-performance/quickstart.md`

**Checkpoint**: The optimized policy is proven on the exact durable HTTP workflow and remains independently testable.

---

## Phase 4: User Story 2 — Explicit Durability and Replay Controls (Priority: P2)

**Goal**: Make the validated policy available in the reference server with configuration-only rollback.

**Independent Test**: Load both committed server configuration snapshots and resolve the checkpoint feature settings; changing only `Mode` selects the corresponding runtime policy.

### Tests for User Story 2

- [x] T012 [US2] Write failing architecture/configuration assertions for the default server checkpoint policy in `tests/Elsa/Architecture/ArchitectureGuardTests.cs`

### Implementation for User Story 2

- [x] T013 [P] [US2] Enable coalesced checkpoint persistence with cap 50 in `src/Apps/Elsa.Server/shells.json`
- [x] T014 [P] [US2] Keep the baseline shell snapshot aligned in `src/Apps/Elsa.Server/shells.baseline.json`
- [x] T015 [US2] Run `dotnet test tests/Elsa/Architecture/Elsa.Architecture.Tests.csproj` and verify the server configuration rollback example in `specs/090-runtime-http-performance/quickstart.md`

**Checkpoint**: The reference server uses the measured low-latency policy, while other hosts retain Immediate unless configured.

---

## Phase 5: User Story 3 — Reproducible Performance and Safety Evidence (Priority: P3)

**Goal**: Provide repeatable latency evidence and close cap/recovery regression gaps.

**Independent Test**: Run the cap test and crash suite, then execute one command against a published endpoint to obtain JSON and Markdown cold/warm reports.

### Tests for User Story 3

- [x] T016 [US3] Write a failing long-segment cap-overflow test for caps 1, 5, and 50 in `tests/Elsa/Workflows/Runtime/Tests/RuntimeCheckpointCoalescingTests.cs`
- [x] T017 [P] [US3] Add shell-level response validation, SQLite marker counting, percentile calculation, environment metadata, JSON/Markdown output, and optional p95 enforcement to `tools/performance/measure-http-workflow.sh`

### Implementation for User Story 3

- [x] T018 [US3] Make the cap-overflow test pass with existing coalescing behavior or the smallest required correction in `src/Elsa/Workflows/Runtime/Services/Coalescing/CoalescingRuntimeCheckpointCommitStore.cs`
- [x] T019 [US3] Run the existing two-generation crash, mandatory-boundary, fencing, and cap suites in `tests/Elsa/Persistence/Groundwork/Tests/Elsa.Persistence.Groundwork.Tests.csproj` and `tests/Elsa/Workflows/Runtime/Tests/Elsa.Workflows.Runtime.Tests.csproj`
- [x] T020 [US3] Run the measurement command against the reference server, save the reproducible result in `docs/reports/runtime-http-performance-2026-07.md`, and continue measured optimization if p95 exceeds 50 ms

**Checkpoint**: Maintainers have deterministic safety gates plus reproducible user-visible latency evidence.

---

## Phase 6: Polish and Cross-Cutting Validation

- [x] T021 Update checkpoint policy settings and performance trade-offs in `docs/runtime-durable-resumption.md` and `src/Elsa/Workflows/Runtime/EXTENSION_POINTS.md`
- [x] T022 Run the quickstart commands in `specs/090-runtime-http-performance/quickstart.md` and update every task in `specs/090-runtime-http-performance/tasks.md` to `[x]`
- [x] T023 Run `dotnet build Elsa.Server.slnx`, inspect `git diff --check`, review all changed files for secrets/unrelated edits, and make a local commit with the completed work unit

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup**: Starts immediately.
- **Foundational**: Depends on Setup and blocks all stories.
- **US1**: Depends on Foundational; proves the actual performance fix.
- **US2**: Depends on US1 evidence before the reference server opts in.
- **US3**: May develop in parallel with US1 after Foundational, but its final report depends on US1 and US2.
- **Polish**: Depends on all stories.

### User Story Dependencies

- **US1 (P1)**: Requires only the foundational policy feature and durable fixture references.
- **US2 (P2)**: Requires US1 to prove the chosen reference-server setting.
- **US3 (P3)**: Safety tests are independent after Foundational; the final measurement uses the completed US1/US2 configuration.

### Parallel Opportunities

- T002 can proceed independently of the .NET test-project setup.
- T013 and T014 touch separate configuration snapshots after T012 fails.
- T017 can proceed independently of T016/T018.
- A separate agent can review recovery invariants while the HTTP integration fixture is implemented.

## Implementation Strategy

1. Establish RED feature-composition tests and implement the narrow post-configured shell feature.
2. Establish RED durable HTTP integration evidence, then make the fixture exercise real SQLite stores.
3. Opt the reference server in only after the structural gain is proven.
4. Close the cap evidence gap and run all existing recovery authorities.
5. Measure the live reference server; only add provider or hot-path changes if the measured p95 still misses the specification.
6. Commit the complete, verified work unit locally.
