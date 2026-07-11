# Tasks: Observable Shell Readiness and Cold Activation

**Input**: Design documents from `specs/091-lazy-shell-activation/`

**Prerequisites**: `plan.md`, `spec.md`, `research.md`, `data-model.md`, `contracts/shell-readiness.md`, `quickstart.md`

**Tests**: Required by FR-012/SC-006 and the repository constitution. Write each story's tests first and confirm the focused lane fails for the intended missing behavior before implementation.

**Organization**: Tasks are grouped by user story so readiness, measurement, telemetry, and optimization remain reviewable and independently verifiable.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel because it touches different files and does not depend on an incomplete task.
- **[Story]**: Maps to the prioritized stories in `spec.md`.

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Freeze the baseline and prepare exact test/report locations.

- [x] T001 Record the current-main 43.500964 s activation and 0.610067 s first-success evidence, environment, and stale-snapshot failure in `docs/reports/shell-activation-performance-2026-07.md`
- [x] T002 Remove the duplicate `Elsa.Workflows.Runtime.Http` import warning in `src/Apps/Elsa.Server/Program.cs` so measured Release builds remain warning-free

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Establish shared bounded diagnostic vocabulary and test utilities used across stories.

- [ ] T003 [P] Add shared host-test startup/activation gates and response readers in `tests/Elsa/Modularity/Tests/ServerReadinessFixture.cs`
- [ ] T004 [P] Add bounded activity/metric names and outcome constants in `src/Elsa/Tasks/Diagnostics/StartupTaskTelemetry.cs`
- [ ] T005 [P] Add the provider-initialization activity/metric names and outcomes in `src/Elsa/Persistence/Groundwork/Sqlite/SqliteGroundworkTelemetry.cs`

**Checkpoint**: Shared vocabulary and test scaffolding compile; story tests can now be written independently.

---

## Phase 3: User Story 1 - Trustworthy Workflow Readiness (Priority: P1) 🎯 MVP

**Goal**: Expose excluded liveness/readiness probes and prepare the default shell once after listening without making probes trigger or await activation.

**Independent Test**: Hold the real default shell at route initialization; liveness returns 200, readiness immediately returns 503, concurrent probes do not add activation attempts, and readiness becomes 200 only after the Active generation is published.

### Tests for User Story 1

- [ ] T006 [P] [US1] Add branch-complete state/options/warmup unit tests in `tests/Elsa/Modularity/Tests/ShellReadinessTests.cs` and confirm they fail before implementation
- [ ] T007 [P] [US1] Add real Kestrel/CShells readiness integration tests for pending, success, failure, concurrency, and cross-shell isolation in `tests/Elsa/Modularity/Tests/ServerReadinessTests.cs` and confirm they fail before implementation
- [ ] T008 [P] [US1] Add architecture guards for distinct health paths and CShells exclusions in `tests/Elsa/Architecture/ArchitectureGuardTests.cs` and confirm they fail before composition changes

### Implementation for User Story 1

- [ ] T009 [P] [US1] Implement immutable snapshot/options and atomic transitions in `src/Apps/Elsa.Server/Readiness/ShellReadinessSnapshot.cs`, `ShellReadinessOptions.cs`, and `ShellReadinessState.cs`
- [ ] T010 [US1] Implement cancellation-aware post-`ApplicationStarted` activation with bounded diagnostics in `src/Apps/Elsa.Server/Readiness/DefaultShellWarmup.cs`
- [ ] T011 [US1] Register readiness services, bind `Elsa:Readiness`, exclude both health paths from shell routing, and map immediate live/ready responses in `src/Apps/Elsa.Server/Program.cs`
- [ ] T012 [US1] Add default host readiness configuration and operator descriptions in `src/Apps/Elsa.Server/appsettings.json`
- [ ] T013 [US1] Run the focused Modularity and Architecture tests and mark T006-T012 complete in `specs/091-lazy-shell-activation/tasks.md`

**Checkpoint**: Health probes are operationally honest and independently testable; first-request activation is moved to background preparation.

---

## Phase 4: User Story 2 - Reproducible Cold-Start Evidence (Priority: P2)

**Goal**: Produce raw and aggregate listening/readiness/first-success evidence across isolated frozen-data boots.

**Independent Test**: Run at least five boots against a prebuilt server and frozen baseline; verify exact workflow validation, all five milestones, p50/p95, provenance, budget handling, and retained diagnostics on a forced failure.

### Tests for User Story 2

- [ ] T014 [P] [US2] Add shell-level contract cases for help, unknown arguments, invalid boot counts, missing tools/files, occupied ports, and budget failure in `tools/performance/tests/measure-server-cold-start-tests.sh`

### Implementation for User Story 2

- [ ] T015 [US2] Implement isolated boot orchestration, SQLite snapshot copying, process cleanup, response validation, provenance, percentiles, and JSON/Markdown output in `tools/performance/measure-server-cold-start.sh`
- [ ] T016 [US2] Validate `bash -n`, command help/error paths, a forced-failure retained log, and a five-boot smoke lane using `tools/performance/measure-server-cold-start.sh`
- [ ] T017 [US2] Update reproducible commands and expected report fields in `specs/091-lazy-shell-activation/quickstart.md`

**Checkpoint**: Cold-start evidence is reproducible without modifying ordinary CI or source databases.

---

## Phase 5: User Story 3 - Actionable Activation Telemetry (Priority: P3)

**Goal**: Attribute activation time to discovery/composition, provider initialization/migrations, reconciliation/startup tasks, and route initialization with bounded dimensions.

**Independent Test**: Attach in-memory activity/meter listeners, run representative successful/skipped/failed startup tasks and provider/route initialization, and assert names, durations, outcomes, counts, and absence of sensitive/high-cardinality tags.

### Tests for User Story 3

- [ ] T018 [P] [US3] Add activity/metric tests for successful, skipped, cancelled, and failed startup tasks in `tests/Elsa/Tasks/Tests/StartupTaskTelemetryTests.cs` and confirm they fail before instrumentation
- [ ] T019 [P] [US3] Add history-hit/materialized/failure provider telemetry tests in `tests/Elsa/Persistence/Groundwork/Sqlite/Tests/SqliteGroundworkInitializationTests.cs` and confirm they fail before instrumentation
- [ ] T020 [P] [US3] Extend route synchronizer tests with duration/outcome/route-count observations in `tests/Elsa/Workflows/Runtime/Http/Tests/HttpEndpointRouteTableSynchronizerTests.cs`

### Implementation for User Story 3

- [ ] T021 [US3] Instrument ordered startup-task execution and structured completion/failure logs in `src/Elsa/Tasks/Services/TaskManager.cs`
- [ ] T022 [US3] Instrument Groundwork initialization outcome/duration in `src/Elsa/Persistence/Groundwork/Sqlite/SqliteGroundworkDocumentStoreInitializer.cs`
- [ ] T023 [US3] Instrument route resolution/refresh duration and route count in `src/Elsa/Workflows/Runtime/Http/Services/HttpEndpointRouteTableSynchronizer.cs`
- [ ] T024 [US3] Emit overall warmup, feature-discovery, and shell-activation activities/metrics from `src/Apps/Elsa.Server/Readiness/DefaultShellWarmup.cs`
- [ ] T025 [US3] Run focused Tasks, Groundwork SQLite, Runtime HTTP, and Modularity tests and document the stable telemetry vocabulary in `specs/091-lazy-shell-activation/contracts/shell-readiness.md`

**Checkpoint**: A cold activation is attributable through owned phases and failures without telemetry becoming a correctness dependency.

---

## Phase 6: User Story 4 - Faster First Workflow Availability (Priority: P4)

**Goal**: Remove repeated unchanged-schema Groundwork materialization and prove the cold/first/warm budgets without changing durable behavior.

**Independent Test**: Against identical pre-materialized data, the first initialization materializes, the second exact-history initialization performs no index backfill, changed/absent history and the repair setting rematerialize, and the 20-boot after lane improves shell-ready p95 by at least 30% while all durability/HTTP suites stay green.

### Baseline for User Story 4

- [ ] T026 [US4] Run and preserve the 20-boot pre-change lane with `tools/performance/measure-server-cold-start.sh`, including phase telemetry and matching frozen-data provenance, before implementing the fast path

### Tests for User Story 4

- [ ] T027 [P] [US4] Add exact-history hit, absent-table, changed-manifest/provider, force-rematerialize, concurrency, and usable-store tests in `tests/Elsa/Persistence/Groundwork/Sqlite/Tests/SqliteGroundworkInitializationTests.cs` and confirm they fail before the fast path
- [ ] T028 [P] [US4] Add shell-feature inheritability plus setting/registration tests for `RematerializeOnStartup` in `tests/Elsa/Persistence/Groundwork/Sqlite/Tests/SqliteGroundworkRuntimePersistenceShellFeatureTests.cs`

### Implementation for User Story 4

- [ ] T029 [US4] Implement exact schema-history inspection and direct store opening with full-factory fallback in `src/Elsa/Persistence/Groundwork/Sqlite/SqliteGroundworkDocumentStoreInitializer.cs`
- [ ] T030 [US4] Thread the force-rematerialize option through `src/Elsa/Persistence/Groundwork/Sqlite/DependencyInjection/SqliteGroundworkDocumentStoreRegistration.cs`
- [ ] T031 [US4] Make the touched feature inheritable and expose/document `RematerializeOnStartup` in `src/Elsa/Persistence/Groundwork/Sqlite/SqliteGroundworkRuntimePersistenceShellFeature.cs` and reference server shell configuration
- [ ] T032 [US4] Run the Groundwork SQLite, Groundwork recovery, Activities HTTP integration, shell lifecycle/isolation, and Architecture regression commands recorded in `specs/091-lazy-shell-activation/quickstart.md`
- [ ] T033 [US4] Build a warning-free Release server and run the 20-boot optimized lane plus the existing 200-request warm lane using `tools/performance/measure-server-cold-start.sh` and `tools/performance/measure-http-workflow.sh`
- [ ] T034 [US4] Record raw report provenance, before/after p50/p95, phase attribution, budgets, operator knobs, rollback, and residual costs in `docs/reports/shell-activation-performance-2026-07.md`

**Checkpoint**: The measured dominant activation phase is reduced and the client-visible first/warm behavior satisfies the declared budgets.

---

## Phase 7: Polish & Cross-Cutting Concerns

**Purpose**: Complete documentation, broad verification, self-review, and delivery tracking.

- [ ] T035 [P] Update startup-task extension documentation and operator diagnostics in `src/Elsa/Tasks/EXTENSION_POINTS.md` and `specs/091-lazy-shell-activation/contracts/shell-readiness.md`
- [ ] T036 Run the full `Elsa.Server.slnx` build and all affected solution test lanes with zero unexpected warnings or failures
- [ ] T037 Run up to five self-review/fix iterations across the implementation files listed in `specs/091-lazy-shell-activation/plan.md`, covering correctness, cancellation, lifecycle races, metric cardinality, data safety, shell isolation, script cleanup, and acceptance completeness
- [ ] T038 Update every completed checkbox in `specs/091-lazy-shell-activation/tasks.md`, re-run `speckit-analyze`, and resolve all critical/high findings
- [ ] T039 Push `codex/624-shell-readiness`, open a PR with `Closes #624`, link validation/evidence, converge required automated reviews and CI, then merge without bypassing protections

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: Starts immediately.
- **Foundational (Phase 2)**: Depends on Setup and blocks story implementation.
- **US1 (Phase 3)**: Depends on Foundational and supplies the readiness contract used by US2.
- **US2 (Phase 4)**: Depends on US1 endpoints; otherwise independent of telemetry/optimization.
- **US3 (Phase 5)**: Depends on Foundational; can proceed alongside US1/US2 after shared vocabulary exists.
- **US4 (Phase 6)**: Depends on US2 measurement and US3 attribution so the optimization is evidence-led.
- **Polish (Phase 7)**: Depends on all four stories.

### User Story Dependencies

- **US1**: No story dependency; MVP.
- **US2**: Requires the US1 readiness endpoint to measure shell-ready honestly.
- **US3**: No story dependency after Foundational; feeds US4 diagnosis.
- **US4**: Requires US2 and US3 evidence.

### Parallel Opportunities

- T003-T005 can run independently.
- T006-T008 can be authored in parallel before T009-T012.
- T018-T020 can be authored in parallel before T021-T024.
- T027 and T028 can be authored in parallel after the T026 baseline is preserved.
- T035 documentation can proceed while T036 broad verification runs.

---

## Parallel Example: User Story 3

```text
Task: "Add startup-task activity/metric tests in tests/Elsa/Tasks/Tests/StartupTaskTelemetryTests.cs"
Task: "Add provider activity/metric tests in tests/Elsa/Persistence/Groundwork/Sqlite/Tests/SqliteGroundworkInitializationTests.cs"
Task: "Add route observation tests in tests/Elsa/Workflows/Runtime/Http/Tests/HttpEndpointRouteTableSynchronizerTests.cs"
```

---

## Implementation Strategy

### MVP First

1. Freeze the baseline and shared diagnostic vocabulary.
2. Deliver US1's excluded, non-blocking readiness and one background warmup.
3. Validate lifecycle/isolation before adding measurement or persistence optimization.

### Incremental Delivery

1. US1 makes operational traffic gating correct.
2. US2 makes clean-boot performance reproducible.
3. US3 identifies the dominant owned cost.
4. US4 changes only the measured bottleneck and proves before/after plus safety.

### Commit Discipline

- Commit specification/plan/tasks separately from implementation.
- Commit each story after its focused tests pass.
- Keep generated reports and documentation in the same story commit that establishes their evidence.
- Never combine unrelated cleanup; the duplicate import warning is explicitly scoped because warning-free measured builds are required.

## Notes

- `[P]` tasks touch different files and have no incomplete dependency.
- Tests must fail for the intended missing behavior before production implementation.
- No source database is deleted or mutated by the benchmark; use SQLite backups and per-boot copies.
- Wall-clock budgets are opt-in evidence, while semantic readiness/materialization tests remain deterministic CI gates.
