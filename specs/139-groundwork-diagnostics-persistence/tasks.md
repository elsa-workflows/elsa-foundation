# Tasks: Durable Diagnostics Persistence

**Input**: Design documents from `specs/139-groundwork-diagnostics-persistence/`

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
  - Evidence: The shared lifecycle project is present, listed by `dotnet sln Elsa.Server.slnx list`, and builds cleanly on the replay head.
- [X] T002 [P] Create the OpenTelemetry Groundwork adapter project in `src/Elsa/Diagnostics/OpenTelemetry/Persistence/Groundwork/Elsa.Diagnostics.OpenTelemetry.Persistence.Groundwork.csproj`
  - Evidence: Adapter project is present and the OpenTelemetry Groundwork suite built and passed 68/68 on the replay base.
- [X] T003 [P] Create the shared lifecycle test project in `tests/Elsa/Diagnostics/Persistence/Tests/Elsa.Diagnostics.Persistence.Tests.csproj`
  - Evidence: Shared lifecycle test project is present; the focused provider-neutral batches passed 49/49, 19/19, and 34/34.
- [X] T004 [P] Create the OpenTelemetry Groundwork test project in `tests/Elsa/Diagnostics/OpenTelemetry/Persistence/Groundwork/Tests/Elsa.Diagnostics.OpenTelemetry.Persistence.Groundwork.Tests.csproj`
  - Evidence: Adapter test project is present and passed 68/68 on the replay base.
- [X] T005 Add all new projects to `Elsa.Server.slnx` and verify project references remain acyclic
  - Evidence: All seven diagnostics Groundwork/shared projects are listed in `Elsa.Server.slnx`; focused builds of the shared library, adapters, and test projects completed without dependency-cycle errors.

**Checkpoint**: New project boundaries build, but existing EF and Groundwork behavior remains unchanged.

---

## Phase 2: Foundational Provider and Architecture Gates

**Purpose**: Establish reusable conformance infrastructure and boundaries that block all story implementation.

- [X] T006 Write failing architecture tests proving diagnostics core assemblies contain no Groundwork references and persistence registration has one unambiguous replacement path in `tests/Elsa/Diagnostics/Persistence/Tests/DiagnosticsPersistenceArchitectureTests.cs`
  - Evidence: Architecture tests are present; the non-removal architecture slice passed 19/19.
- [X] T007 Implement only the composition abstractions needed by both adapters in `src/Elsa/Diagnostics/Persistence/`, catalog their semantics in `src/Elsa/Diagnostics/Persistence/EXTENSION_POINTS.md`, and make T006 pass without exposing Groundwork types from diagnostics core
  - Evidence: The Groundwork-free helper library and owning extension catalog are present; architecture and lifecycle slices passed.
- [X] T008 Build a reusable four-provider fixture with real SQLite, SQL Server, PostgreSQL, and MongoDB lifecycle support in `tests/Elsa/Diagnostics/Persistence/Tests/Fixtures/DiagnosticsProviderFixture.cs` and instantiate every provider lease in `tests/Elsa/Diagnostics/Persistence/Tests/DiagnosticsProviderLifecycleSmokeTests.cs`
  - Evidence: the Release lifecycle smoke passed 1/1 and opened isolated ready leases for SQLite, SQL Server, PostgreSQL, and replica-set MongoDB with the required transaction, bounded-query, and plan capabilities.
- [X] T009 [P] Add reusable acknowledgement-loss, cancellation, restart, and operational-failure doubles in `tests/Elsa/Diagnostics/Persistence/Tests/Fixtures/DiagnosticsFailureFixtures.cs`
  - Evidence: Failure fixtures are present and exercised by the 49-test drain/observability slice.
- [X] T010 [P] Record the temporary EF behavior-oracle inventory and exact parity mapping in `specs/139-groundwork-diagnostics-persistence/oracle-inventory.md`
  - Evidence: The renumbered EF oracle inventory is present under spec 139 and the focused documentation/fixture/evidence slice passed 10/10.
- [X] T011 Add one shared diagnostics provider capability/readiness assertion helper in `tests/Elsa/Diagnostics/Persistence/Tests/DiagnosticsProviderAssertions.cs`
  - Evidence: Provider assertions passed their four capability cases in the focused 10-test slice; real-provider lease certification remains open in T008.

**Checkpoint**: All adapters can be tested through the same real-provider and failure harness; architecture boundaries are executable.

---

## Phase 3: User Story 1 - Resume durable diagnostics after interruption (Priority: P1) 🎯 MVP

**Goal**: Make Structured Logs and OpenTelemetry history durable, replayable, idempotent, and restart-safe with failures classified correctly.

**Independent Test**: Commit all signal kinds from concurrent writers, restart the store, replay/query them in stable order, retry acknowledgement-lost operations, and reject malformed/trimmed/foreign cursors without hiding operational failures.

### Tests for User Story 1 — write and observe failures first

- [X] T012 [P] [US1] Expand Structured Logs conformance tests for tied ordering, filtered cursor advancement, restart, invalid binding, trimmed anchors, and operational failure visibility in `tests/Elsa/Diagnostics/StructuredLogs/Persistence/Groundwork/Tests/GroundworkStructuredLogReplayTests.cs`
  - Evidence: Structured Logs adapter suite passed 18/18 on the preview.81 integration head, including replay and query conformance.
- [X] T013 [P] [US1] Add OpenTelemetry restart tests for resources, trace summaries, spans, instruments, metric points, and log records in `tests/Elsa/Diagnostics/OpenTelemetry/Persistence/Groundwork/Tests/GroundworkOpenTelemetryRestartTests.cs`
  - Evidence: OpenTelemetry adapter suite discovered 74 tests on the preview.81 integration head; 73 passed and one explicit #130 test was skipped, including restart coverage for every signal kind.
- [X] T014 [P] [US1] Add append idempotency, operation-identity conflict, acknowledgement-loss, cancellation-boundary, concurrent-writer, malformed-payload, and oversized-batch rejection-without-mutation tests in `tests/Elsa/Diagnostics/Persistence/Tests/DiagnosticsDurableOperationConformanceTests.cs`
  - Evidence: Durable-operation conformance ran in the 34-test feature/readiness/schema batch with no failures.
- [ ] T015 [US1] Run T012-T014 against the current adapters and record the expected failing assertions in `specs/139-groundwork-diagnostics-persistence/evidence/red-test-baseline.md` before implementation
  - Evidence note: remains open as a non-retroactive process deviation; the recovered stale-branch red run is provenance only and cannot certify a test-first baseline after implementation.

### Implementation for User Story 1

- [X] T016 [US1] Harden durable append, stable commit ordering, cursor advancement, and exact cursor-failure translation in `src/Elsa/Diagnostics/StructuredLogs/Persistence/Groundwork/GroundworkStructuredLogStore.cs`
  - Evidence: Structured Logs append/replay implementation compiled and its adapter suite passed 18/18 on the preview.81 integration head.
- [X] T017 [US1] Bind replay cursors to version, storage scope, source, stream, provider position, and record anchor in `src/Elsa/Diagnostics/StructuredLogs/Persistence/Groundwork/GroundworkReplayCursorCodec.cs`
  - Evidence: The strict canonical cursor codec compiled and replay tests passed in the 17-test adapter suite.
- [X] T018 [P] [US1] Define OpenTelemetry record-stream mappings and canonical serializers in `src/Elsa/Diagnostics/OpenTelemetry/Persistence/Groundwork/Records/`
  - Evidence: Record mappings and canonical serializers compiled; the OpenTelemetry adapter suite passed 73 tests with one explicit #130 skip on the preview.81 integration head.
  - Preview.86 follow-up: the trace stream now declares the native `trace-summary-v1` grouped-reduction profile and stores the first root/name plus total span count needed to materialize it. Its schema-v2 pre-release reset boundary, retained-record union limit, focused serializer/definition tests, and complete adapter suite passed 75/75.
- [X] T019 [P] [US1] Define resource and instrument catalog document mappings in `src/Elsa/Diagnostics/OpenTelemetry/Persistence/Groundwork/Catalogs/`
  - Evidence: Catalog mappings/serializers compiled; the OpenTelemetry adapter suite passed 73 tests with one explicit #130 skip on the preview.81 integration head.
- [X] T020 [US1] Implement idempotent normalized batch writes and durable restart reads in `src/Elsa/Diagnostics/OpenTelemetry/Persistence/Groundwork/GroundworkOpenTelemetryStore.cs`
  - Evidence: The durable OpenTelemetry store compiled; the OpenTelemetry adapter suite passed 73 tests with one explicit #130 skip on the preview.81 integration head.
- [X] T021 [US1] Declare Structured Logs streams/indexes/ledger requirements in `src/Elsa/Diagnostics/StructuredLogs/Persistence/Groundwork/StructuredLogsGroundworkStorageSchema.cs` and OpenTelemetry streams/catalogs/indexes/ledger requirements in `src/Elsa/Diagnostics/OpenTelemetry/Persistence/Groundwork/OpenTelemetryGroundworkStorageSchema.cs`
  - Evidence: Both schema declarations compiled; adapter schema tests and the shared schema batch passed.
- [X] T022 [US1] Run the US1 conformance set against all four providers and store a summarized evidence manifest in `specs/139-groundwork-diagnostics-persistence/evidence/us1-provider-results.json`
  - Evidence: Exact integrated-head certification at `47d3892446022a2b41a678f0913eba51b3ef4662` passed 24/24 shared provider cases (6 each on SQLite, SQL Server, PostgreSQL, and MongoDB); the complete adapters passed 73 OpenTelemetry tests with one explicit #130 skip and 18/18 Structured Logs tests, and provider-independent operation depth passed 5/5.

**Checkpoint**: Durable append, restart, replay, idempotency, and failure semantics work independently for every diagnostic signal on all four providers.

---

## Phase 4: User Story 2 - Query exact diagnostic history at scale (Priority: P1)

**Goal**: Execute every declared filter, range, ordering, latest-per-key, count, scope, and retention operation exactly and within provider-side bounds.

**Independent Test**: Load a deterministic multi-scope dataset, compare every query and retention outcome across four providers, and verify declared indexes/plans with no broad client evaluation.

### Tests for User Story 2 — write and observe failures first

- [X] T023 [P] [US2] Add the complete Structured Logs filter, limit, tie-break, count, retention-zero, and scope-isolation matrix in `tests/Elsa/Diagnostics/StructuredLogs/Persistence/Groundwork/Tests/GroundworkStructuredLogQueryConformanceTests.cs`
  - Evidence: Structured Logs query conformance passed inside the 17-test adapter suite.
- [X] T024 [P] [US2] Add OpenTelemetry resource, trace, trace-detail, metric, and log query matrices with inclusive boundaries, stable ties, invalid-range rejection, and unsupported-query rejection without broad reads in `tests/Elsa/Diagnostics/OpenTelemetry/Persistence/Groundwork/Tests/GroundworkOpenTelemetryQueryConformanceTests.cs`
  - Evidence: OpenTelemetry query conformance passed inside the 73-pass adapter suite; the only skip is the explicit #130 repeated-trace reduction case.
  - Preview.86 follow-up: the former repeated-trace #130 skip is executable and the complete adapter suite passed 75/75. Real-provider cases passed 12/12 across SQLite, SQL Server, PostgreSQL, and MongoDB, proving grouped merge-before-filter, ordering/take, group continuations, restart, concurrent durable writes, the 257-value union boundary, and the explicit fail-closed preview.81-to-fresh-preview.86 reset rule. This is focused follow-up evidence, not T057 promotion evidence.
- [X] T025 [P] [US2] Add deterministic resource/instrument catalog capacity and least-recently-seen retention tests in `tests/Elsa/Diagnostics/OpenTelemetry/Persistence/Groundwork/Tests/GroundworkOpenTelemetryCatalogTests.cs`
  - Evidence: OpenTelemetry catalog conformance passed inside the 73-pass adapter suite; the only skip is the explicit #130 repeated-trace reduction case.
  - Preview.86 follow-up: a real-provider catalog case passed 4/4 across SQLite, SQL Server, PostgreSQL, and MongoDB, covering supplementary-plane Unicode case collisions, exact latest-write identity, query/load, restart, retention deletion, and scope isolation without an adapter lowercase workaround. This is focused follow-up evidence, not T057 promotion evidence.
- [X] T026 [P] [US2] Add cross-scope non-disclosure and exact retention tests for all signals in `tests/Elsa/Diagnostics/Persistence/Tests/DiagnosticsScopeAndRetentionConformanceTests.cs`
  - Evidence: the real-provider suite passed the structured-log and all-signal telemetry cross-scope retention cases on SQLite, SQL Server, PostgreSQL, and MongoDB.
- [X] T027 [US2] Add real-provider execution-plan/index evidence assertions for every plan-exposed scale-bearing query and trim selection in `tests/Elsa/Diagnostics/Persistence/Tests/DiagnosticsBoundedExecutionTests.cs`
  - Evidence: the 5-test real-provider plan slice passed (four provider theory cases plus SQLite missing-schema rejection), covering eleven diagnostic-record query routes, five trim selections, and eight catalog selection/count/capacity routes. Catalog writes and ledger mutations have correctness coverage, but Groundwork's public document-store API exposes no mutation-plan inspector and this task does not claim one.
  - Preview.86 follow-up: `InspectGroupedQueryAsync` reported a native grouped-query plan for the trace-summary route on all four providers (4/4). It proves provider-side bounded grouped reduction only; it does not supply #646 performance evidence or complete T057.

### Implementation for User Story 2

- [X] T028 [P] [US2] Implement bounded Structured Logs query and retention compilation in `src/Elsa/Diagnostics/StructuredLogs/Persistence/Groundwork/GroundworkStructuredLogStore.cs`
  - Evidence: Bounded Structured Logs query/retention paths passed inside the 17-test adapter suite.
- [X] T029 [P] [US2] Implement bounded resource and instrument catalog queries/upserts/capacity enforcement in `src/Elsa/Diagnostics/OpenTelemetry/Persistence/Groundwork/Catalogs/`
  - Evidence: Bounded catalog query/upsert/capacity paths passed inside the 73-pass adapter suite.
- [X] T030 [US2] Implement bounded trace, span, metric-point, and telemetry-log record queries in `src/Elsa/Diagnostics/OpenTelemetry/Persistence/Groundwork/GroundworkOpenTelemetryStore.cs`
  - Evidence: Bounded telemetry record queries passed inside the 73-pass adapter suite.
- [X] T031 [US2] Add the missing authorized OpenTelemetry logs query endpoint in `src/Elsa/Diagnostics/OpenTelemetry/Endpoints/OpenTelemetry/Logs/Endpoint.cs`
  - Evidence: the POST `/diagnostics/opentelemetry/logs/search` endpoint delegates to `IOpenTelemetryProvider.GetLogsAsync` and is guarded by `Diagnostics:OpenTelemetry`.
- [X] T032 [US2] Add endpoint binding and result tests in `tests/Elsa/Diagnostics/OpenTelemetry/Tests/OpenTelemetryLogsEndpointTests.cs`
  - Evidence: the focused endpoint contract passes 2/2, covering route, verb, authorization, filter/token forwarding, and exact provider result return.
- [X] T033 [US2] Run the US2 query, scope, retention, and plan suite against all four providers and store a summarized evidence manifest in `specs/139-groundwork-diagnostics-persistence/evidence/us2-provider-results.json`
  - Evidence: Exact integrated-head certification at `47d3892446022a2b41a678f0913eba51b3ef4662` passed 17/17 query-result, scope/retention, native bounded-plan, and missing-schema cases across SQLite, SQL Server, PostgreSQL, and MongoDB; the complete OpenTelemetry adapter regression passed 73 tests with one explicit #130 skip.

**Checkpoint**: All persisted diagnostics queries and mutations are exact, scope-safe, and demonstrably bounded across all providers.

---

## Phase 5: User Story 3 - Preserve capture under load and shutdown (Priority: P2)

**Goal**: Give both Groundwork adapters one Elsa-owned bounded, nonblocking, retrying, observable, and drainable capture lifecycle.

**Independent Test**: Saturate queues, inject transient and permanent faults, cancel at every boundary, lose acknowledgements, and stop gracefully or by timeout while proving bounded memory and complete caller outcomes.

### Tests for User Story 3 — write and observe failures first

- [X] T034 [P] [US3] Port and expand the EF drain oracle into provider-independent queue, retry, acknowledgement, closure, and disposal tests in `tests/Elsa/Diagnostics/Persistence/Tests/DiagnosticsDrainLifecycleTests.cs`
  - Evidence: Shared drain lifecycle coverage passed in the focused 49-test provider-neutral slice.
- [X] T035 [P] [US3] Add concurrent producer, overflow shedding, retry exhaustion, and later-batch recovery tests in `tests/Elsa/Diagnostics/Persistence/Tests/DiagnosticsDrainLoadTests.cs`
  - Evidence: Load, overflow, retry-exhaustion, and recovery coverage passed in the focused 49-test slice.
- [X] T036 [P] [US3] Add graceful drain, timeout fallback, final retention, and no-incomplete-acknowledgement tests in `tests/Elsa/Diagnostics/Persistence/Tests/DiagnosticsDrainShutdownTests.cs`
  - Evidence: Shutdown and acknowledgement-settlement coverage passed in the focused 49-test slice.
- [X] T037 [P] [US3] Add non-recursive instrumentation, low-cardinality/no-payload telemetry, and production subscriber-delivery bridge classification tests in `tests/Elsa/Diagnostics/Persistence/Tests/DiagnosticsPersistenceObservabilityTests.cs`
  - Evidence: Payload-free observability and delivery-loss bridge coverage passed in the focused 49-test slice.

### Implementation for User Story 3

- [X] T038 [US3] Extract the bounded drain state machine from `src/Elsa/Persistence/EFCore/Storage/ChannelDrainingStoreBase.cs` into composed services under `src/Elsa/Diagnostics/Persistence/Draining/`
  - Evidence: The provider-neutral drain implementation compiled and its focused lifecycle/load/shutdown tests passed.
- [X] T039 [US3] Implement queue, retry, shutdown, retention, closure, and subscriber-delivery loss classification in `src/Elsa/Diagnostics/Persistence/Observability/DiagnosticsPersistenceObservability.cs` and `src/Elsa/Diagnostics/Persistence/Observability/DiagnosticsSubscriberDeliveryLossBridge.cs`, consuming the existing domain live-feed signals without moving fan-out into persistence
  - Evidence: Observability and subscriber-loss bridge implementation compiled and focused tests passed.
- [X] T040 [P] [US3] Integrate the shared drain into `src/Elsa/Diagnostics/StructuredLogs/Persistence/Groundwork/GroundworkStructuredLogStore.cs`
  - Evidence: Structured Logs uses the shared drain; its adapter suite passed 18/18 on the preview.81 integration head.
- [X] T041 [P] [US3] Integrate the shared drain into `src/Elsa/Diagnostics/OpenTelemetry/Persistence/Groundwork/GroundworkOpenTelemetryStore.cs`
  - Evidence: OpenTelemetry uses the shared drain; its adapter suite passed 73 tests with one explicit #130 skip on the preview.81 integration head.
- [X] T042 [US3] Register one explicit start/stop/drain-before-provider-disposal lifecycle in `src/Elsa/Diagnostics/Persistence/Extensions/DiagnosticsPersistenceRegistration.cs`
  - Evidence: The shared registration/coordinator path is implemented in DiagnosticsPersistenceRegistration.cs; lifecycle tests passed in the 34-test batch.
- [X] T043 [US3] Run the complete load and shutdown suite and record queue bounds, loss totals, and completion outcomes in `specs/139-groundwork-diagnostics-persistence/evidence/us3-lifecycle-results.json`
  - Evidence: The six manifest-bound lifecycle suites passed 49/49 on clean preview.81 integrated head `47d3892446022a2b41a678f0913eba51b3ef4662`, after merging `origin/main` `165cf20723e088ca1bcb1530abf2149cabacb2bc`. The manifest records the exact command and resolved package version. This is T043-only evidence, not four-provider, performance, EF-removal, or final-main certification.

**Checkpoint**: Persistence latency cannot block producers or leave accepted acknowledgements unresolved; every loss and shutdown outcome is observable.

---

## Phase 6: User Story 4 - Operate one provider model without EF migrations (Priority: P2)

**Goal**: Compose, deploy, validate, and operate only Groundwork diagnostics persistence across all providers, then remove diagnostics EF completely.

**Independent Test**: Validate/apply schema before startup, run the same behavior suite on every provider, fail readiness on drift, pass performance gates, and prove zero EF dependencies remain in diagnostics while core remains Groundwork-free.

### Tests for User Story 4 — write and observe failures first

- [X] T044 [P] [US4] Add schema validate/apply, missing-schema, drift, capability-mismatch, and concurrent-start tests in `tests/Elsa/Diagnostics/Persistence/Tests/DiagnosticsSchemaDeploymentTests.cs`
  - Evidence: Schema validate/apply/drift/capability/concurrency tests passed in the 34-test batch.
- [X] T045 [P] [US4] Add enabled/disabled/provider-selection and one-store registration tests in `tests/Elsa/Diagnostics/Persistence/Tests/DiagnosticsPersistenceFeatureTests.cs`
  - Evidence: [`evidence/us4-composition-results.json`](evidence/us4-composition-results.json) binds exact implementation head `e70995de5` to 148/148 diagnostics persistence cases, 6/6 combined deployment-schema cases, and the full 53/53 unified-host suite. Its SQLite fixture resolves both Groundwork stores and proves no EF diagnostics store is registered or selected.
- [X] T046 [P] [US4] Add readiness tests proving provider/schema failures never fall back to empty or in-memory durable results in `tests/Elsa/Diagnostics/Persistence/Tests/DiagnosticsPersistenceReadinessTests.cs`
  - Evidence: Readiness and no-fallback tests passed in the 34-test batch.
- [ ] T047 [US4] Add final dependency/public-surface tests for zero diagnostics EF and zero core Groundwork references in `tests/Elsa/Diagnostics/Persistence/Tests/DiagnosticsPersistenceArchitectureTests.cs`
  - Evidence note: a premature zero-EF assertion was removed in `5538d8414`; introduce the final guard test-first only after T053-T055 delete the retained EF oracle.

### Implementation for User Story 4

- [X] T048 [US4] Contribute each concrete schema declaration to the shared Groundwork validate/apply CLI path from `src/Elsa/Diagnostics/StructuredLogs/Persistence/Groundwork/GroundworkStructuredLogsPersistenceFeature.cs` and `src/Elsa/Diagnostics/OpenTelemetry/Persistence/Groundwork/GroundworkOpenTelemetryPersistenceFeature.cs`
  - Evidence: Both concrete features contribute schema to the combined deployment source; schema and feature tests passed.
- [X] T049 [US4] Implement Groundwork persistence feature composition and readiness in `src/Elsa/Diagnostics/StructuredLogs/Persistence/Groundwork/` and `src/Elsa/Diagnostics/OpenTelemetry/Persistence/Groundwork/`
  - Evidence: [`evidence/us4-composition-results.json`](evidence/us4-composition-results.json) records 53/53 unified-host cases, including the four direct generic provider routes and real SQLite activation, plus a clean `Elsa.Server` Release build with 0 errors. The 29 clean-build warnings are existing preview.81 obsolescence debt in unchanged source and are disclosed in the manifest. Final four-provider promotion remains T057.
- [ ] T050 [P] [US4] Consume the #646-owned diagnostics workload and retained-artifact contract; do not create a lane-local benchmark harness
  - Evidence note: premature lane-local performance tests were removed in `5538d8414`; the program owner ratified `diagnostics-durable-history` as the 13th spec-094 workload on 2026-07-25, but this task remains owned by the #646 handoff and has no retained passing verdict yet.
- [ ] T051 [US4] Import the ratified #646 performance verdict for the diagnostics workload into `specs/139-groundwork-diagnostics-persistence/evidence/performance-decision.json`
- [ ] T052 [US4] For every material correctness or #646 performance regression, record the failing gate, changed source/test paths, remediation commit, and rerun outcome, then repeat the relevant adapter gates until the #646 verdict passes
- [ ] T053 [US4] Delete Structured Logs EF implementation projects and their tests under `src/Elsa/Diagnostics/StructuredLogs/Persistence/EFCore/` and `tests/Elsa/Diagnostics/StructuredLogs/Persistence/Tests/` while preserving provider-neutral conformance coverage
- [ ] T054 [US4] Delete OpenTelemetry EF implementation projects and their tests under `src/Elsa/Diagnostics/OpenTelemetry/Persistence/EFCore/` and `tests/Elsa/Diagnostics/OpenTelemetry/Persistence/Tests/` while preserving provider-neutral conformance coverage
- [ ] T055 [US4] Remove diagnostics EF project/registration/migration/package usages from `Elsa.Server.slnx` and affected diagnostics projects; remove EF entries from `Directory.Packages.props` only when the repository-wide dependency audit proves no remaining feature consumes them
- [ ] T056 [P] [US4] Update `src/Elsa/Diagnostics/StructuredLogs/README.md`, `src/Elsa/Diagnostics/StructuredLogs/EXTENSION_POINTS.md`, `src/Elsa/Diagnostics/OpenTelemetry/README.md`, and `src/Elsa/Diagnostics/OpenTelemetry/EXTENSION_POINTS.md`
- [ ] T057 [US4] Run the complete four-provider suite, solution build, architecture audit, and final `rg` zero-EF/zero-core-Groundwork checks documented in `specs/139-groundwork-diagnostics-persistence/quickstart.md`

**Checkpoint**: Diagnostics has one first-party Groundwork persistence model, four-provider proof, pre-start deployment support, and zero EF implementation surface.

---

## Phase 7: Cross-Cutting Completion and Ratification

**Purpose**: Make the work durable for maintainers and agents, and land it through the approved operating model.

- [ ] T058 [P] Refresh the narrowest affected dependency and extension-point maps using `tools/maps/generate-feature-dependency-map.sh` and `tools/maps/generate-extension-point-map.sh`
- [ ] T059 Reconcile all evidence, task completion, exceptions, and follow-up findings in `specs/139-groundwork-diagnostics-persistence/quickstart.md` and `docs/reports/unfinished-work.md`
- [ ] T060 Run an independent review of the exact branch HEAD against FR-001 through FR-017 and SC-001 through SC-009, then remediate every blocker
- [ ] T061 Push the organization-owned feature branch, open the reviewed PR linked to the diagnostics migration issue, obtain required checks, and merge it to `main`
- [ ] T062 Verify `main` and the remote issue/PR state, then record the final commit and evidence links in `specs/139-groundwork-diagnostics-persistence/quickstart.md`

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
