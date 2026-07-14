# Tasks: Durable Diagnostics Persistence

**Input**: Design documents from `specs/091-groundwork-diagnostics-persistence/`

**Prerequisites**: `plan.md`, `spec.md`, `research.md`, `data-model.md`, `contracts/diagnostics-persistence.md`

**Tests**: Tests are mandatory. For every behavior-changing implementation task, complete the listed test task first and observe the relevant assertion fail.

**Organization**: Tasks are grouped by user story. Shared setup and provider fixtures are blocking foundations; each story then has an independently executable acceptance boundary.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: May run in parallel after its declared phase dependencies because it owns different files.
- **[Story]**: Maps the task to a user story in `spec.md`.
- Every task names the exact file or directory it owns.

## Phase 1: Setup

**Purpose**: Create project boundaries and registration points without changing active persistence behavior.

- [X] T001 Create the shared diagnostics lifecycle library in `src/Elsa/Diagnostics/Persistence/Elsa.Diagnostics.Persistence.csproj` and add it to `Elsa.Server.slnx`
- [X] T002 [P] Create the OpenTelemetry Groundwork adapter project in `src/Elsa/Diagnostics/OpenTelemetry/Persistence/Groundwork/Elsa.Diagnostics.OpenTelemetry.Persistence.Groundwork.csproj`
- [X] T003 [P] Create the shared lifecycle test project in `tests/Elsa/Diagnostics/Persistence/Tests/Elsa.Diagnostics.Persistence.Tests.csproj`
- [X] T004 [P] Create the OpenTelemetry Groundwork test project in `tests/Elsa/Diagnostics/OpenTelemetry/Persistence/Groundwork/Tests/Elsa.Diagnostics.OpenTelemetry.Persistence.Groundwork.Tests.csproj`
- [X] T005 Add all new projects to `Elsa.Server.slnx` and verify project references remain acyclic

**Checkpoint**: New project boundaries build, but existing EF and Groundwork behavior remains unchanged.

---

## Phase 2: Foundational Provider and Architecture Gates

**Purpose**: Establish reusable conformance infrastructure and boundaries that block all story implementation.

- [X] T006 Write failing architecture tests proving diagnostics core assemblies contain no Groundwork references and persistence registration has one unambiguous replacement path in `tests/Elsa/Diagnostics/Persistence/Tests/DiagnosticsPersistenceArchitectureTests.cs`
- [X] T007 Implement only the composition abstractions needed by both adapters in `src/Elsa/Diagnostics/Persistence/`, catalog their semantics in `src/Elsa/Diagnostics/Persistence/EXTENSION_POINTS.md`, and make T006 pass without exposing Groundwork types from diagnostics core
- [X] T008 Build a reusable four-provider fixture with real SQLite, SQL Server, PostgreSQL, and MongoDB lifecycle support in `tests/Elsa/Diagnostics/Persistence/Tests/Fixtures/DiagnosticsProviderFixture.cs` and instantiate every provider lease in `tests/Elsa/Diagnostics/Persistence/Tests/DiagnosticsProviderLifecycleSmokeTests.cs`
- [X] T009 [P] Add reusable acknowledgement-loss, cancellation, restart, and operational-failure doubles in `tests/Elsa/Diagnostics/Persistence/Tests/Fixtures/DiagnosticsFailureFixtures.cs`
- [X] T010 [P] Record the temporary EF behavior-oracle inventory and exact parity mapping in `specs/091-groundwork-diagnostics-persistence/oracle-inventory.md`
- [X] T011 Add one shared diagnostics provider capability/readiness assertion helper in `tests/Elsa/Diagnostics/Persistence/Tests/DiagnosticsProviderAssertions.cs`

**Checkpoint**: All adapters can be tested through the same real-provider and failure harness; architecture boundaries are executable.

---

## Phase 3: User Story 1 - Resume durable diagnostics after interruption (Priority: P1) 🎯 MVP

**Goal**: Make Structured Logs and OpenTelemetry history durable, replayable, idempotent, and restart-safe with failures classified correctly.

**Independent Test**: Commit all signal kinds from concurrent writers, restart the store, replay/query them in stable order, retry acknowledgement-lost operations, and reject malformed/trimmed/foreign cursors without hiding operational failures.

### Tests for User Story 1 — write and observe failures first

- [X] T012 [P] [US1] Expand Structured Logs conformance tests for tied ordering, filtered cursor advancement, restart, invalid binding, trimmed anchors, and operational failure visibility in `tests/Elsa/Diagnostics/StructuredLogs/Persistence/Groundwork/Tests/GroundworkStructuredLogReplayTests.cs`
- [X] T013 [P] [US1] Add OpenTelemetry restart tests for resources, trace summaries, spans, instruments, metric points, and log records in `tests/Elsa/Diagnostics/OpenTelemetry/Persistence/Groundwork/Tests/GroundworkOpenTelemetryRestartTests.cs`
- [X] T014 [P] [US1] Add append idempotency, operation-identity conflict, acknowledgement-loss, cancellation-boundary, concurrent-writer, malformed-payload, and oversized-batch rejection-without-mutation tests in `tests/Elsa/Diagnostics/Persistence/Tests/DiagnosticsDurableOperationConformanceTests.cs`
- [X] T015 [US1] Run T012-T014 against the current adapters and record the expected failing assertions in `specs/091-groundwork-diagnostics-persistence/evidence/red-test-baseline.md` before implementation

### Implementation for User Story 1

- [X] T016 [US1] Harden durable append, stable commit ordering, cursor advancement, and exact cursor-failure translation in `src/Elsa/Diagnostics/StructuredLogs/Persistence/Groundwork/GroundworkStructuredLogStore.cs`
- [X] T017 [US1] Bind replay cursors to version, storage scope, source, stream, provider position, and record anchor in `src/Elsa/Diagnostics/StructuredLogs/Persistence/Groundwork/GroundworkReplayCursorCodec.cs`
- [X] T018 [P] [US1] Define OpenTelemetry record-stream mappings and canonical serializers in `src/Elsa/Diagnostics/OpenTelemetry/Persistence/Groundwork/Records/`
- [X] T019 [P] [US1] Define resource and instrument catalog document mappings in `src/Elsa/Diagnostics/OpenTelemetry/Persistence/Groundwork/Catalogs/`
- [ ] T020 [US1] Implement idempotent normalized batch writes and durable restart reads in `src/Elsa/Diagnostics/OpenTelemetry/Persistence/Groundwork/GroundworkOpenTelemetryStore.cs`
- [ ] T021 [US1] Declare Structured Logs streams/indexes/ledger requirements in `src/Elsa/Diagnostics/StructuredLogs/Persistence/Groundwork/StructuredLogsGroundworkStorageSchema.cs` and OpenTelemetry streams/catalogs/indexes/ledger requirements in `src/Elsa/Diagnostics/OpenTelemetry/Persistence/Groundwork/OpenTelemetryGroundworkStorageSchema.cs`
- [ ] T022 [US1] Run the US1 conformance set against all four providers and store a summarized evidence manifest in `specs/091-groundwork-diagnostics-persistence/evidence/us1-provider-results.json`

**Checkpoint**: Durable append, restart, replay, idempotency, and failure semantics work independently for every diagnostic signal on all four providers.

---

## Phase 4: User Story 2 - Query exact diagnostic history at scale (Priority: P1)

**Goal**: Execute every declared filter, range, ordering, latest-per-key, count, scope, and retention operation exactly and within provider-side bounds.

**Independent Test**: Load a deterministic multi-scope dataset, compare every query and retention outcome across four providers, and verify declared indexes/plans with no broad client evaluation.

### Tests for User Story 2 — write and observe failures first

- [ ] T023 [P] [US2] Add the complete Structured Logs filter, limit, tie-break, count, retention-zero, and scope-isolation matrix in `tests/Elsa/Diagnostics/StructuredLogs/Persistence/Groundwork/Tests/GroundworkStructuredLogQueryConformanceTests.cs`
- [ ] T024 [P] [US2] Add OpenTelemetry resource, trace, trace-detail, metric, and log query matrices with inclusive boundaries, stable ties, invalid-range rejection, and unsupported-query rejection without broad reads in `tests/Elsa/Diagnostics/OpenTelemetry/Persistence/Groundwork/Tests/GroundworkOpenTelemetryQueryConformanceTests.cs`
- [ ] T025 [P] [US2] Add deterministic resource/instrument catalog capacity and least-recently-seen retention tests in `tests/Elsa/Diagnostics/OpenTelemetry/Persistence/Groundwork/Tests/GroundworkOpenTelemetryCatalogTests.cs`
- [ ] T026 [P] [US2] Add cross-scope non-disclosure and exact retention tests for all signals in `tests/Elsa/Diagnostics/Persistence/Tests/DiagnosticsScopeAndRetentionConformanceTests.cs`
- [ ] T027 [US2] Add real-provider execution-plan/index evidence assertions for every scale-bearing query and mutation in `tests/Elsa/Diagnostics/Persistence/Tests/DiagnosticsBoundedExecutionTests.cs`

### Implementation for User Story 2

- [ ] T028 [P] [US2] Implement bounded Structured Logs query and retention compilation in `src/Elsa/Diagnostics/StructuredLogs/Persistence/Groundwork/GroundworkStructuredLogStore.cs`
- [ ] T029 [P] [US2] Implement bounded resource and instrument catalog queries/upserts/capacity enforcement in `src/Elsa/Diagnostics/OpenTelemetry/Persistence/Groundwork/Catalogs/`
- [ ] T030 [US2] Implement bounded trace, span, metric-point, and telemetry-log record queries in `src/Elsa/Diagnostics/OpenTelemetry/Persistence/Groundwork/GroundworkOpenTelemetryStore.cs`
- [ ] T031 [US2] Add the missing authorized OpenTelemetry logs query endpoint in `src/Elsa/Diagnostics/OpenTelemetry/Endpoints/OpenTelemetry/Logs/Endpoint.cs`
- [ ] T032 [US2] Add endpoint binding and result tests in `tests/Elsa/Diagnostics/OpenTelemetry/Tests/OpenTelemetryLogsEndpointTests.cs`
- [ ] T033 [US2] Run the US2 query, scope, retention, and plan suite against all four providers and store a summarized evidence manifest in `specs/091-groundwork-diagnostics-persistence/evidence/us2-provider-results.json`

**Checkpoint**: All persisted diagnostics queries and mutations are exact, scope-safe, and demonstrably bounded across all providers.

---

## Phase 5: User Story 3 - Preserve capture under load and shutdown (Priority: P2)

**Goal**: Give both Groundwork adapters one Elsa-owned bounded, nonblocking, retrying, observable, and drainable capture lifecycle.

**Independent Test**: Saturate queues, inject transient and permanent faults, cancel at every boundary, lose acknowledgements, and stop gracefully or by timeout while proving bounded memory and complete caller outcomes.

### Tests for User Story 3 — write and observe failures first

- [X] T034 [P] [US3] Port and expand the EF drain oracle into provider-independent queue, retry, acknowledgement, closure, and disposal tests in `tests/Elsa/Diagnostics/Persistence/Tests/DiagnosticsDrainLifecycleTests.cs`
- [X] T035 [P] [US3] Add concurrent producer, overflow shedding, retry exhaustion, and later-batch recovery tests in `tests/Elsa/Diagnostics/Persistence/Tests/DiagnosticsDrainLoadTests.cs`
- [X] T036 [P] [US3] Add graceful drain, timeout fallback, final retention, and no-incomplete-acknowledgement tests in `tests/Elsa/Diagnostics/Persistence/Tests/DiagnosticsDrainShutdownTests.cs`
- [X] T037 [P] [US3] Add non-recursive instrumentation, low-cardinality/no-payload telemetry, and production subscriber-delivery bridge classification tests in `tests/Elsa/Diagnostics/Persistence/Tests/DiagnosticsPersistenceObservabilityTests.cs`

### Implementation for User Story 3

- [X] T038 [US3] Extract the bounded drain state machine from `src/Elsa/Persistence/EFCore/Storage/ChannelDrainingStoreBase.cs` into composed services under `src/Elsa/Diagnostics/Persistence/Draining/`
- [X] T039 [US3] Implement queue, retry, shutdown, retention, closure, and subscriber-delivery loss classification in `src/Elsa/Diagnostics/Persistence/Observability/DiagnosticsPersistenceObservability.cs` and `src/Elsa/Diagnostics/Persistence/Observability/DiagnosticsSubscriberDeliveryLossBridge.cs`, consuming the existing domain live-feed signals without moving fan-out into persistence
- [ ] T040 [P] [US3] Integrate the shared drain into `src/Elsa/Diagnostics/StructuredLogs/Persistence/Groundwork/GroundworkStructuredLogStore.cs`
- [ ] T041 [P] [US3] Integrate the shared drain into `src/Elsa/Diagnostics/OpenTelemetry/Persistence/Groundwork/GroundworkOpenTelemetryStore.cs`
- [ ] T042 [US3] Register one explicit start/stop/drain-before-provider-disposal lifecycle in `src/Elsa/Diagnostics/Persistence/Extensions/ServiceCollectionExtensions.cs`
- [ ] T043 [US3] Run the complete load and shutdown suite and record queue bounds, loss totals, and completion outcomes in `specs/091-groundwork-diagnostics-persistence/evidence/us3-lifecycle-results.json`

**Checkpoint**: Persistence latency cannot block producers or leave accepted acknowledgements unresolved; every loss and shutdown outcome is observable.

---

## Phase 6: User Story 4 - Operate one provider model without EF migrations (Priority: P2)

**Goal**: Compose, deploy, validate, and operate only Groundwork diagnostics persistence across all providers, then remove diagnostics EF completely.

**Independent Test**: Validate/apply schema before startup, run the same behavior suite on every provider, fail readiness on drift, pass performance gates, and prove zero EF dependencies remain in diagnostics while core remains Groundwork-free.

### Tests for User Story 4 — write and observe failures first

- [ ] T044 [P] [US4] Add schema validate/apply, missing-schema, drift, capability-mismatch, and concurrent-start tests in `tests/Elsa/Diagnostics/Persistence/Tests/DiagnosticsSchemaDeploymentTests.cs`
- [ ] T045 [P] [US4] Add enabled/disabled/provider-selection and one-store registration tests in `tests/Elsa/Diagnostics/Persistence/Tests/DiagnosticsPersistenceFeatureTests.cs`
- [ ] T046 [P] [US4] Add readiness tests proving provider/schema failures never fall back to empty or in-memory durable results in `tests/Elsa/Diagnostics/Persistence/Tests/DiagnosticsPersistenceReadinessTests.cs`
- [ ] T047 [US4] Add final dependency/public-surface tests for zero diagnostics EF and zero core Groundwork references in `tests/Elsa/Diagnostics/Persistence/Tests/DiagnosticsPersistenceArchitectureTests.cs`

### Implementation for User Story 4

- [ ] T048 [US4] Contribute each concrete schema declaration to the shared Groundwork validate/apply CLI path from `src/Elsa/Diagnostics/StructuredLogs/Persistence/Groundwork/GroundworkStructuredLogsPersistenceFeature.cs` and `src/Elsa/Diagnostics/OpenTelemetry/Persistence/Groundwork/GroundworkOpenTelemetryPersistenceFeature.cs`
- [ ] T049 [US4] Implement Groundwork persistence feature composition and readiness in `src/Elsa/Diagnostics/StructuredLogs/Persistence/Groundwork/` and `src/Elsa/Diagnostics/OpenTelemetry/Persistence/Groundwork/`
- [ ] T050 [P] [US4] Add the ratified EF-versus-Groundwork diagnostics workload, datasets, environment manifest, and raw-artifact schema under `tools/performance/diagnostics/`
- [ ] T051 [US4] Run the non-promotable smoke profile, then the full ratified four-provider performance matrix, and store the decision artifact in `specs/091-groundwork-diagnostics-persistence/evidence/performance-decision.json`
- [ ] T052 [US4] For every material correctness or performance regression, record the failing gate, changed source/test paths, remediation commit, and rerun outcome in `specs/091-groundwork-diagnostics-persistence/evidence/performance-decision.json`, then repeat T044-T051 until every removal gate passes
- [ ] T053 [US4] Delete Structured Logs EF implementation projects and their tests under `src/Elsa/Diagnostics/StructuredLogs/Persistence/EFCore/` and `tests/Elsa/Diagnostics/StructuredLogs/Persistence/Tests/` while preserving provider-neutral conformance coverage
- [ ] T054 [US4] Delete OpenTelemetry EF implementation projects and their tests under `src/Elsa/Diagnostics/OpenTelemetry/Persistence/EFCore/` and `tests/Elsa/Diagnostics/OpenTelemetry/Persistence/Tests/` while preserving provider-neutral conformance coverage
- [ ] T055 [US4] Remove diagnostics EF project/registration/migration/package usages from `Elsa.Server.slnx` and affected diagnostics projects; remove EF entries from `Directory.Packages.props` only when the repository-wide dependency audit proves no remaining feature consumes them
- [ ] T056 [P] [US4] Update `src/Elsa/Diagnostics/StructuredLogs/README.md`, `src/Elsa/Diagnostics/StructuredLogs/EXTENSION_POINTS.md`, `src/Elsa/Diagnostics/OpenTelemetry/README.md`, and `src/Elsa/Diagnostics/OpenTelemetry/EXTENSION_POINTS.md`
- [ ] T057 [US4] Run the complete four-provider suite, solution build, architecture audit, and final `rg` zero-EF/zero-core-Groundwork checks documented in `specs/091-groundwork-diagnostics-persistence/quickstart.md`

**Checkpoint**: Diagnostics has one first-party Groundwork persistence model, four-provider proof, pre-start deployment support, and zero EF implementation surface.

---

## Phase 7: Cross-Cutting Completion and Ratification

**Purpose**: Make the work durable for maintainers and agents, and land it through the approved operating model.

- [ ] T058 [P] Refresh the narrowest affected dependency and extension-point maps using `tools/maps/generate-feature-dependency-map.sh` and `tools/maps/generate-extension-point-map.sh`
- [ ] T059 Reconcile all evidence, task completion, exceptions, and follow-up findings in `specs/091-groundwork-diagnostics-persistence/quickstart.md` and `docs/reports/unfinished-work.md`
- [ ] T060 Run an independent review of the exact branch HEAD against FR-001 through FR-017 and SC-001 through SC-009, then remediate every blocker
- [ ] T061 Push the organization-owned feature branch, open the reviewed PR linked to the diagnostics migration issue, obtain required checks, and merge it to `main`
- [ ] T062 Verify `main` and the remote issue/PR state, then record the final commit and evidence links in `specs/091-groundwork-diagnostics-persistence/quickstart.md`

---

## Dependencies & Execution Order

### Phase dependencies

- Phase 1 has no dependencies.
- Phase 2 depends on Phase 1 and blocks every user story.
- US1 and US2 may start after Phase 2; US2 query implementations depend on the relevant US1 record/catalog mappings.
- US3 may start after Phase 2; execute T034-T039 immediately as the first independent implementation slice, then integrate T040-T041 after the relevant adapters exist.
- US4 schema/readiness test work may begin after Phase 2, but EF removal tasks T053-T055 depend on all US1-US3 gates plus T044-T052.
- Phase 7 depends on the desired user stories being complete; merge and final verification are strictly last.

### Story dependencies

- **US1**: No story dependency; supplies durable record/catalog foundations.
- **US2**: Uses US1 mappings but remains independently testable through deterministic query fixtures.
- **US3**: Owns capture lifecycle and can be developed with failure doubles before concrete adapters integrate it.
- **US4**: Integrates and removes the old implementation only after US1-US3 correctness and performance proof.

### Parallel opportunities

- T002-T004, T008-T010, and all test tasks explicitly marked `[P]` own different files.
- After Phase 2, one worker can harden Structured Logs, one can implement OpenTelemetry storage, and one can implement the shared drain, provided only one worker owns any shared project/solution file at a time.
- Provider certification executions may run in parallel only when their container/database names and artifact directories are isolated.
- Documentation/map refresh can run in parallel after the corresponding implementation paths stabilize.

## Implementation Strategy

1. Land Phase 1-2 as a small architecture/test-fixture checkpoint.
2. Complete the T034-T039 shared-drain tests and extraction as the first independently certified implementation slice.
3. Deliver US1 durable restart/replay and integrate the tested drain into each adapter as it becomes available.
4. Add US2 exact query/retention behavior and provider-plan evidence.
5. Complete US4 deployment/performance gates; only then delete EF.
6. Obtain exact-HEAD independent review, merge through Model B, and verify remote `main`.

## Notes

- Do not weaken provider-plan gates to make small fixtures pass; correct dataset selectivity, schema, or query shape.
- Do not replace bounded provider operations with identity fan-out, broad materialization, or client evaluation.
- Do not catch operational failures as cursor unavailability or empty results.
- Keep all scope, operation identity, and route/schema binding checks ahead of provider I/O when possible.
- Commit after each coherent approved work unit; do not commit personal `.agent-prefs/` files.
