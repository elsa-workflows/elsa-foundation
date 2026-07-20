---

description: "Dependency-ordered implementation tasks for Groundwork design persistence"
---

# Tasks: Groundwork Design Persistence

**Input**: Design documents from `/specs/093-groundwork-design-persistence/`

**Prerequisites**: `plan.md`, `spec.md`, `research.md`, `data-model.md`, `contracts/design-persistence-contract.md`, `quickstart.md`

**Tests**: Required. Write the red contract/behavior tests before the corresponding implementation and preserve the objective of every applicable existing test.

**Organization**: Tasks are grouped by user story so each story reaches an independently testable checkpoint. The final EF deletion remains gated on all four stories and the recorded performance evidence.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel after its stated phase dependencies because it owns different files.
- **[Story]**: Maps to the user stories in `spec.md`.
- Every task names its authoritative file or directory.

## Phase 1: Setup and Baseline

**Purpose**: Establish the exact current surface, oracle prerequisites, upstream version, and test scaffolding before changing behavior.

- [X] T001 Record branch base, Groundwork package/tool version, provider images, and current focused/full test counts in `specs/093-groundwork-design-persistence/quickstart.md`
- [X] T002 Reconcile every current workflow/activity design store and command with the coverage list in `specs/093-groundwork-design-persistence/contracts/design-persistence-contract.md`
- [X] T003 Classify every EF-referencing design test by preserved domain objective versus removable EF mechanism and populate the exact per-test architect approval ledger in `specs/093-groundwork-design-persistence/test-removal-ledger.md` (linked from `research.md`); T072/T073 may not delete a test without its approved ledger row
- [ ] T004 Capture the temporary EF SQLite behavioral-oracle result hashes in `specs/093-groundwork-design-persistence/quickstart.md` after T021–T024 define the canonical scenarios; T025 performs and records this run
- [X] T005 Create `tests/Elsa/Persistence/Groundwork/DesignConformance/Tests/Elsa.Persistence.Groundwork.DesignConformance.Tests.csproj` and add it to `Elsa.Server.slnx`
- [X] T006 [P] Add the released `Groundwork.SqlServer`, `Groundwork.MongoDb`, and matching provider test/tool package versions to `Directory.Packages.props`
- [X] T007 [P] Add a pinned `Groundwork.Tool` entry matching the library version in `.config/dotnet-tools.json`
- [X] T008 Add deterministic design catalog fixtures, clocks, identifiers, scopes, and payload serializers in `tests/Elsa/Persistence/Groundwork/DesignConformance/Tests/DesignPersistenceFixtureData.cs`

**Checkpoint**: Current behavior and test objectives are recorded; the shared test project restores without changing production composition. The workload-level EF behavioral oracle is deliberately deferred to T025 and is not a Phase-2 entry gate.

---

## Phase 2: Foundational Physical Storage and Query Binding

**Purpose**: Install the immutable schema/query/session foundation that blocks all user-story implementation.

**⚠️ CRITICAL**: No production adapter migration starts until this phase is green.

- [x] T009 Upgrade all Groundwork libraries and the tool as one released binary-compatible set that supplies the required physical-storage/query APIs in `Directory.Packages.props`, and resolve compile breaks without adding compatibility fallbacks
- [x] T010 [P] Replace legacy workflow design storage declarations with versioned `PhysicalTableDefinition` and bounded query identities in `src/Elsa/Workflows/Design/Persistence/Groundwork/WorkflowsDesignStorageManifest.cs`
- [x] T011 [P] Replace legacy activity design storage declarations with versioned `PhysicalTableDefinition` and bounded query identities in `src/Elsa/Activities/Design/Persistence/Groundwork/ActivitiesDesignStorageManifest.cs`
- [x] T012 Preserve the stable composite identity/version and union selected physical definitions through `src/Elsa/Persistence/Groundwork/Unified/Composition/GroundworkStorageCompositionFactory.cs` and `src/Elsa/Persistence/Groundwork/Unified/Composition/GroundworkStorageCompositionValidator.cs`; keep `src/Elsa/Persistence/Groundwork/Unified/GroundworkUnifiedManifest.cs` as a compatibility facade
- [x] T013 Verify the provider-neutral `IPhysicalSchemaManifestSource` and host naming-policy bridge in `src/Elsa/Persistence/Groundwork/Unified/Composition/GroundworkDeploymentSchemaManifestSource.cs`
- [x] T014 Write red translation tests for every Elsa `QueryOp`, AND-of-OR group, sort, page, count, any, first, null/missing case, and unsupported shape in `tests/Elsa/Persistence/Groundwork/Querying/Tests/GroundworkQueryTranslatorTests.cs`
- [x] T015 Implement one serialized-path-aware `Query<TEntity>` to Groundwork `DocumentQuery` translator in `src/Elsa/Persistence/Groundwork/Querying/GroundworkQueryTranslator.cs`
- [x] T016 Reuse the immutable access-bound `GroundworkStoreSession` bundle for scoped point reads, bounded queries, and UoW acquisition in `src/Elsa/Persistence/Groundwork/Scoping/IGroundworkStoreSessionFactory.cs` and `src/Elsa/Persistence/Groundwork/Stores/GroundworkScopedDocumentStore.cs`
- [x] T017 Reuse explicit audited privileged-across-scope acquisition for tenant-agnostic design operations through `src/Elsa/Persistence/Groundwork/Scoping/GroundworkStoreSessionFactory.cs`
- [x] T018 Add domain-scoped translation, readiness, corrupt-payload, and provider-failure exceptions in `src/Elsa/Persistence/Groundwork/Querying/Exceptions/`
- [x] T019 Update the in-memory Groundwork test substrate to execute declared bounded queries truthfully or reject them before I/O in `tests/Elsa/Persistence/Groundwork/Testing/InMemoryDocumentStore.cs`
- [x] T020 Create the live §2.23 coverage ledger in `specs/093-groundwork-design-persistence/research.md`, inventory every existing and Phase-2 feature/logic-bearing implementation, and add §2.23.1 registration plus direct §2.23.2 branch coverage for manifests/translators, session and factory classes under `tests/Elsa/Persistence/Groundwork/Querying/Tests/`; include route compilation, naming collision, scope injection, algorithm-version fingerprint, and missing-handler paths, and assign every later command/UoW, readiness, schema-source, provider registration, and provider materialization row to T035, T048, T061, or T062 before its implementation task closes

**Checkpoint**: Physical definitions compile, every declared query binds to one certified handler, core projects remain Groundwork-free, and unsupported shapes fail before provider work.

---

## Phase 3: User Story 1 — Persist Complete Design Lifecycles (Priority: P1) 🎯 MVP

**Goal**: All workflow/activity design reads and lifecycle mutations preserve their public behavior, atomicity, scope, concurrency, retry, and restart semantics over Groundwork.

**Independent Test**: Run the public design contract fixture on real SQLite, close/reopen the database between phases, inject failures/lost acknowledgement into every multi-document transition, and obtain identical entities, events, conflicts, and rollback outcomes.

### Tests for User Story 1

- [ ] T021 [P] [US1] Extract workflow definition/draft/version/layout black-box scenarios from existing tests into `tests/Elsa/Persistence/Groundwork/DesignConformance/Tests/WorkflowDesignContractSuite.cs`
- [ ] T022 [P] [US1] Extract activity definition/version/reconciliation black-box scenarios from existing tests into `tests/Elsa/Persistence/Groundwork/DesignConformance/Tests/ActivityDesignContractSuite.cs`
- [ ] T023 [P] [US1] Add multi-document rollback, partial staging, cancellation, lost acknowledgement, replay fingerprint, and duplicate-event scenarios in `tests/Elsa/Persistence/Groundwork/DesignConformance/Tests/DesignAtomicityContractSuite.cs`
- [ ] T024 [P] [US1] Add point-read/write scope isolation, stale OCC, duplicate identity, wrong-scope non-disclosure, and restart scenarios in `tests/Elsa/Persistence/Groundwork/DesignConformance/Tests/DesignIsolationAndRestartContractSuite.cs`
- [ ] T025 [US1] Run T021–T024 against the temporary EF SQLite oracle and record canonical result hashes/behavior baseline in `specs/093-groundwork-design-persistence/quickstart.md` (completes T004), then run the same scenarios against existing Groundwork adapters and record the intentional red baseline

### Implementation for User Story 1

- [ ] T026 [P] [US1] Move workflow definition and version stores onto `GroundworkDesignStoreSession` plus named bounded queries in `src/Elsa/Workflows/Design/Persistence/Groundwork/Services/GroundworkWorkflowDefinitionStore.cs` and `GroundworkWorkflowDefinitionVersionStore.cs`
- [ ] T027 [P] [US1] Move activity definition and version stores onto `GroundworkDesignStoreSession` plus named bounded queries in `src/Elsa/Activities/Design/Persistence/Groundwork/Services/GroundworkActivityDefinitionStore.cs` and `GroundworkActivityDefinitionVersionStore.cs`
- [ ] T028 [P] [US1] Preserve the logical workflow draft-plus-layout aggregate and remove by-collection filtering in `src/Elsa/Workflows/Design/Persistence/Groundwork/Services/GroundworkWorkflowDefinitionDraftDocument.cs`
- [ ] T029 [P] [US1] Move version-layout and workflow-list projection reads onto declared bounded routes in `src/Elsa/Workflows/Design/Persistence/Groundwork/Services/GroundworkWorkflowDefinitionVersionLayoutStore.cs` and `GroundworkWorkflowDefinitionListProjectionStore.cs`
- [ ] T030 [US1] Centralize deterministic operation identity, canonical request fingerprint, result inspection, rollback, and acknowledgement-replay behavior for design UoWs in `src/Elsa/Persistence/Groundwork/Querying/GroundworkDesignAtomicWrite.cs`
- [ ] T031 [US1] Apply the hardened atomic write path to every workflow definition/version/draft/promote/submit/delete command in `src/Elsa/Workflows/Design/Persistence/Groundwork/Services/`
- [ ] T032 [US1] Apply the hardened atomic write path to activity definition/version commands in `src/Elsa/Activities/Design/Persistence/Groundwork/Services/`
- [ ] T033 [US1] Map provider write/read/serialization failures to domain-scoped exceptions without swallowing cancellation in `src/Elsa/Workflows/Design/Persistence/Groundwork/Services/` and `src/Elsa/Activities/Design/Persistence/Groundwork/Services/`
- [ ] T034 [US1] Register scoped store sessions, translators, commands, and stores exactly once in `src/Elsa/Workflows/Design/Persistence/Groundwork/DependencyInjection/GroundworkWorkflowsDesignStoreRegistration.cs` and `src/Elsa/Activities/Design/Persistence/Groundwork/DependencyInjection/GroundworkActivitiesDesignStoreRegistration.cs`
- [ ] T035 [US1] Complete every T020 coverage-ledger row introduced by T026–T034 with per-feature registration and per-implementation direct branch tests (including atomic command/UoW, failure, retry, cancellation, and default paths) in the owning workflow/activity/Groundwork test directories; pass all focused suites plus SQLite T021–T024 and record exact counts in `specs/093-groundwork-design-persistence/quickstart.md`
- [ ] T036 [US1] Commit and independently review the US1 exact HEAD for lifecycle, atomicity, scope, retry, and test-objective preservation; record the commit/review in `specs/093-groundwork-design-persistence/quickstart.md`

**Checkpoint**: User Story 1 is durable and independently demonstrable on SQLite with no partial aggregate visibility or scope leak.

---

## Phase 4: User Story 2 — Query Design Data Predictably at Scale (Priority: P1)

**Goal**: Every scale-bearing design query executes through its declared provider route with stable result semantics and no load-all/client-evaluated fallback.

**Independent Test**: Seed a large mixed catalog, execute every bounded-query catalog row, compare exact result hashes with the oracle, and inspect native SQLite plans plus memory/command evidence proving the result set—not the entire kind—was fetched.

### Tests for User Story 2

- [ ] T037 [P] [US2] Add complete workflow query-shape parity cases, null/missing values, semantic-version ordering, and deterministic ties in `tests/Elsa/Persistence/Groundwork/DesignConformance/Tests/WorkflowDesignQueryContractSuite.cs`
- [ ] T038 [P] [US2] Add complete activity query-shape parity cases, OR/IN/contains behavior, and deterministic result ordering in `tests/Elsa/Persistence/Groundwork/DesignConformance/Tests/ActivityDesignQueryContractSuite.cs`
- [ ] T039 [P] [US2] Add bounded cardinality, deterministic batching, count/any/first, and zero-load-all command evidence in `tests/Elsa/Persistence/Groundwork/DesignConformance/Tests/DesignQueryScaleContractSuite.cs`
- [ ] T040 [P] [US2] Add provider-plan assertions for all selected physical entity tables and indexes in `tests/Elsa/Persistence/Groundwork/DesignConformance/Tests/DesignQueryPlanContractSuite.cs`

### Implementation for User Story 2

- [ ] T041 [US2] Replace `GroundworkReadStore<TEntity>` candidate enumeration and `InMemoryQueryEvaluator` with bound `IBoundedDocumentStore` execution in `src/Elsa/Persistence/Groundwork/Querying/GroundworkReadStore.cs`
- [ ] T042 [US2] Make result operation, stable tie-break ordering, skip/take, and total count explicit in `src/Elsa/Persistence/Groundwork/Querying/GroundworkQueryTranslator.cs`
- [ ] T043 [P] [US2] Declare native workflow definition search/equality/membership fields and indexes in `src/Elsa/Workflows/Design/Persistence/Groundwork/WorkflowsDesignStorageManifest.cs`
- [ ] T044 [P] [US2] Declare workflow version compound existence/latest routes, draft current-order routes, and layout lookup routes in `src/Elsa/Workflows/Design/Persistence/Groundwork/WorkflowsDesignStorageManifest.cs`
- [ ] T045 [P] [US2] Declare native activity definition search/equality/membership fields and version compound routes in `src/Elsa/Activities/Design/Persistence/Groundwork/ActivitiesDesignStorageManifest.cs`
- [ ] T046 [US2] Enforce declared maximum `IN` cardinality and deterministic bounded batches in `src/Elsa/Persistence/Groundwork/Querying/GroundworkQueryTranslator.cs` and `GroundworkWorkflowDefinitionListProjectionStore.cs`
- [ ] T047 [US2] Remove every design by-collection/list-all fallback and obsolete fallback documentation in `src/Elsa/Persistence/Groundwork/Querying/`, `src/Elsa/Workflows/Design/Persistence/Groundwork/`, and `src/Elsa/Activities/Design/Persistence/Groundwork/`
- [ ] T048 [US2] Complete every T020 coverage-ledger row introduced by T041–T047 with direct translator/read-store/manifest branch tests in the owning Groundwork test directories, then run T037–T040 at representative SQLite scale with allocation/command counters and attach result hashes plus plan index in `specs/093-groundwork-design-persistence/quickstart.md`
- [ ] T049 [US2] Add a negative architecture test that fails on `InMemoryQueryEvaluator`, `DocumentStoreQuery` list-all, or uncertified scale-bearing design paths in `tests/Elsa/Architecture/DesignPersistenceBoundedQueryTests.cs`
- [ ] T050 [US2] Commit and independently review the US2 exact HEAD for query semantic parity and bounded execution; record the commit/review in `specs/093-groundwork-design-persistence/quickstart.md`

**Checkpoint**: User Story 2 passes on SQLite with complete server-side query evidence and no production fallback path.

---

## Phase 5: User Story 3 — Choose One Host Storage Provider (Priority: P2)

**Goal**: One host-level choice configures and validates every design/runtime lane on SQLite, SQL Server, PostgreSQL, or MongoDB using the same manifest and Elsa adapters.

**Independent Test**: Materialize a fresh database with each provider, run the shared lifecycle/query suite, restart, validate schema and routes, and prove unsupported MongoDB topology fails before any design write.

### Tests for User Story 3

- [ ] T051 [P] [US3] Add a real SQLite fixture with close/reopen and schema-drift hooks in `tests/Elsa/Persistence/Groundwork/DesignConformance/Tests/Providers/SqliteDesignProviderFixture.cs`
- [ ] T052 [P] [US3] Add a SQL Server Testcontainers fixture and native plan capture in `tests/Elsa/Persistence/Groundwork/DesignConformance/Tests/Providers/SqlServerDesignProviderFixture.cs`
- [ ] T053 [P] [US3] Add a PostgreSQL Testcontainers fixture and native plan capture in `tests/Elsa/Persistence/Groundwork/DesignConformance/Tests/Providers/PostgreSqlDesignProviderFixture.cs`
- [ ] T054 [P] [US3] Add MongoDB replica-set and standalone negative fixtures with winning-plan capture in `tests/Elsa/Persistence/Groundwork/DesignConformance/Tests/Providers/MongoDbDesignProviderFixture.cs`

### Implementation for User Story 3

- [ ] T055 [P] [US3] Reconcile SQLite runtime/design physical route materialization and scoped store creation in `src/Elsa/Persistence/Groundwork/Sqlite/` and `src/Elsa/Persistence/Groundwork/Sqlite/Unified/`
- [ ] T056 [P] [US3] Reconcile PostgreSQL runtime/design physical route materialization and scoped store creation in `src/Elsa/Persistence/Groundwork/PostgreSql/` and `src/Elsa/Persistence/Groundwork/PostgreSql/Unified/`
- [ ] T057 [P] [US3] Add SQL Server runtime/unified provider projects, registration, initializer, and shell feature in `src/Elsa/Persistence/Groundwork/SqlServer/` and `src/Elsa/Persistence/Groundwork/SqlServer/Unified/`
- [ ] T058 [P] [US3] Add MongoDB runtime/unified provider projects, registration, initializer, topology validation, and shell feature in `src/Elsa/Persistence/Groundwork/MongoDb/` and `src/Elsa/Persistence/Groundwork/MongoDb/Unified/`
- [ ] T059 [US3] Bind all four provider compositions to `GroundworkUnifiedManifest` plus `ElsaGroundworkSchema` with one naming-policy/override pipeline in `src/Elsa/Persistence/Groundwork/Unified/`
- [ ] T060 [US3] Add startup readiness validation that never auto-applies or falls back in `src/Elsa/Persistence/Groundwork/Unified/GroundworkSchemaReadinessTask.cs`
- [ ] T061 [P] [US3] Add offline/live plan/validate/status/apply CLI contract tests plus direct schema-source/readiness branch tests required by the T020 coverage ledger in `tests/Elsa/Persistence/Groundwork/DesignConformance/Tests/UnifiedSchemaToolContractTests.cs` and `UnifiedSchemaReadinessTests.cs`
- [ ] T062 [P] [US3] Complete every provider feature/registration/materialization row in the T020 coverage ledger with direct branch tests, then add one-provider-per-host conflict tests that compose the actual `src/Apps/Elsa.Server/` reference host in design-only, runtime-only, and combined deployment shapes for each provider in `tests/Elsa/Persistence/Groundwork/DesignConformance/Tests/UnifiedDesignProviderRegistrationTests.cs` and the owning provider test directories
- [ ] T063 [US3] Run the complete T021–T040 suite through the actual `Elsa.Server` reference host on all four real providers in design-only and combined shapes, prove runtime-only excludes design while retaining runtime composition, and remediate every provider-specific semantic, plan, or composition difference under `tests/Elsa/Persistence/Groundwork/DesignConformance/Tests/`
- [ ] T064 [US3] Prove additive schema/backfill restart, naming collision, drift, cancellation, and safe-apply recovery across all providers in `tests/Elsa/Persistence/Groundwork/DesignConformance/Tests/UnifiedSchemaEvolutionContractTests.cs`
- [ ] T065 [US3] Document provider selection, connection-secret inputs, MongoDB topology, and CI/CD CLI commands in `src/Elsa/Persistence/Groundwork/Unified/README.md`
- [ ] T066 [US3] Commit and independently review the US3 exact HEAD for four-provider parity, topology truthfulness, schema operations, and coherent host composition; record the commit/review in `specs/093-groundwork-design-persistence/quickstart.md`

**Checkpoint**: User Story 3 passes one shared suite and schema protocol on all four mandatory providers.

---

## Phase 6: User Story 4 — Maintain One First-Party Implementation (Priority: P3)

**Goal**: Design persistence ships only Groundwork; core remains provider-neutral; all useful test objectives survive; the reference host no longer composes design EF.

**Independent Test**: Run parity/performance gates, remove the design EF lane, build/test/pack the complete repository, audit direct/transitive design dependencies, and demonstrate that an intentional EF reintroduction fails with its path.

### Tests and evidence for User Story 4

- [ ] T067 [P] [US4] Add fixed 1K correctness, 100K acceptance, and 1M scale-bearing design workload datasets; deterministic seeds/payload and result hashes; same-provider EF/Groundwork adapter targets; one untimed warm-up plus three independent process runs of at least 100 operations and 30 seconds steady state; raw-sample capture and 95% bootstrap comparison to the temporary harness under `benchmarks/Elsa.DesignPersistence.Benchmarks/`
- [ ] T068 [P] [US4] Add shared/dedicated/entity physical-form selection and provider-plan capture to `benchmarks/Elsa.DesignPersistence.Benchmarks/`
- [ ] T069 [US4] Run the required correctness-first 1K/100K/1M matrix, record any architect-approved pre-timing workload exclusion, raw samples, environment, fixed inputs, per-operation medians/confidence intervals, native plans, and threshold decisions in `docs/reports/groundwork-design-persistence-performance.md`
- [ ] T070 [US4] Convert every still-valid EF-specific workflow/activity test objective identified in T003 into Groundwork/shared contract tests under `tests/Elsa/Workflows/Design/`, `tests/Elsa/Activities/Design/`, or `tests/Elsa/Persistence/Groundwork/DesignConformance/Tests/`

### Implementation for User Story 4

- [ ] T071 [US4] Switch `src/Apps/Elsa.Server/Elsa.Server.csproj`, `shells.json`, reset paths, and design feature composition to Groundwork-only design persistence
- [ ] T072 [P] [US4] Remove workflow design EF source, migrations, project references, and EF-only setup from `src/Elsa/Workflows/Design/Persistence/EFCore/`; delete an affected workflow test only when T003 records its exact architect-approved ledger row and replacement evidence
- [ ] T073 [P] [US4] Remove activity design EF source, migrations, project references, and EF-only setup from `src/Elsa/Activities/Design/Persistence/EFCore/`; delete an affected activity test only when T003 records its exact architect-approved ledger row and replacement evidence
- [ ] T074 [US4] Remove only now-unused design EF package/project entries from `Directory.Packages.props` and `Elsa.Server.slnx` while preserving EF dependencies still owned by unfinished zero-EF lanes
- [ ] T075 [US4] Tighten the EF surface ratchet to absolute zero for workflow/activity design source and dependency graphs in `tests/Elsa/Architecture/EfCoreSurfaceRatchetTests.cs` and `EfCoreSurfaceScanner.cs`
- [ ] T076 [US4] Add core-to-Groundwork negative dependency and complete provider-registration architecture tests in `tests/Elsa/Architecture/DesignPersistenceBoundaryTests.cs`
- [ ] T077 [P] [US4] Update workflow/activity Groundwork READMEs and extension-point catalogs in `src/Elsa/Workflows/Design/Persistence/Groundwork/` and `src/Elsa/Activities/Design/Persistence/Groundwork/`
- [ ] T078 [US4] Run focused behavior, architecture, full solution test/build/pack, and design dependency audits; reconcile evidence in `specs/093-groundwork-design-persistence/quickstart.md`
- [ ] T079 [US4] Commit and independently review the US4 exact HEAD for performance-gate satisfaction, test-objective preservation, design EF deletion, and core independence; record the commit/review in `specs/093-groundwork-design-persistence/quickstart.md`

**Checkpoint**: All four user stories pass; workflow/activity design has one Groundwork implementation and zero EF artifacts/dependencies.

---

## Phase 7: Polish, Documentation, and Landing

**Purpose**: Complete cross-cutting quality gates, refresh generated facts, and prove the exact merged mainline state.

- [ ] T080 [P] Update `docs/reports/groundwork-design-provider-implementation-plan.md` from transitional status to bounded four-provider completion and remove stale fallback guidance
- [ ] T081 [P] Update issue #641 links and current findings in `docs/program-goals/zero-ef-persistence.md` and `docs/decision-maps/zero-ef-groundwork.md`
- [ ] T082 Refresh `docs/maps/manifest.json`, the feature-dependency map, and extension-point map with `tools/maps/generate-feature-dependency-map.sh` and `tools/maps/generate-extension-point-map.sh`; review generated findings before continuing
- [ ] T083 Run `git diff --check`, format analyzers, Release build, complete solution tests, pack validation, provider matrix, schema CLI matrix, and benchmark acceptance from `specs/093-groundwork-design-persistence/quickstart.md`
- [ ] T084 Run an independent requirement-by-requirement audit of FR-001–FR-022 and SC-001–SC-008 against exact branch HEAD and record findings in `specs/093-groundwork-design-persistence/quickstart.md`
- [ ] T085 Remediate every blocking independent-review, CI, provider, schema, architecture, or performance finding in the owning source/test files and repeat T083–T084
- [ ] T086 Push `093-groundwork-design-persistence`, open a reviewed PR linked to #641, and obtain all required checks with evidence in the PR body
- [ ] T087 Finalize `specs/093-groundwork-design-persistence/quickstart.md` before merge with the exact reviewed candidate HEAD, PR/check/provider/schema/benchmark links, and remaining zero-EF dependencies
- [ ] T088 Merge the approved PR using Model B, verify remote `main` contains the exact reviewed result, and record the merge commit plus final issue #641/parent #629 state in the PR or issue timeline as the post-merge durable record

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 1 (Setup)**: starts immediately.
- **Phase 2 (Foundational)**: depends on T001–T003 and T005–T008; T004 is completed by T025 after its canonical black-box scenarios exist and is not a Phase-2 gate. Phase 2 blocks every production adapter change until it is green.
- **US1 / Phase 3**: depends on T009–T020; establishes durable lifecycle behavior.
- **US2 / Phase 4**: depends on the translator/session foundation and may execute alongside late US1 command hardening once shared files T015–T017 are stable.
- **US3 / Phase 5**: provider fixtures T051–T054 may begin after Phase 2; final provider matrix T063 depends on US1 and US2.
- **US4 / Phase 6**: benchmark scaffolding T067–T068 may begin after US2; EF deletion T071–T076 depends on completed T004/T025 behavioral-oracle evidence, US1–US3, and passing T069. T067–T069 remain the separate fixed-scale performance/form-selection gate.
- **Phase 7**: depends on all requested user stories and every deletion gate.

### User Story Dependencies

- **US1 (P1)**: independently testable on SQLite after Phase 2.
- **US2 (P1)**: independently testable on SQLite after Phase 2; uses the same session/translator foundation but not US1 mutation completion.
- **US3 (P2)**: provider composition can develop in parallel, but its final conformance run consumes US1 and US2 suites.
- **US4 (P3)**: depends on all prior stories because deletion is irreversible at the repository boundary and must follow parity/performance evidence.

### Critical Path

`(T001–T003 + T005–T008) -> T009–T020 -> ((T021–T035 -> T036) || (T037–T049 -> T050)) -> T051–T065 -> T066 -> T067–T078 -> T079 -> T080–T082 -> T083–T088`, with T025 completing T004 before any T071–T076 deletion work.

## Parallel Opportunities

### Setup and foundation

- T006 and T007 are independent package/tool edits.
- T010 and T011 own separate feature manifests.
- T014 tests can be written while T010/T011 declarations are authored.

### User Story 1

```text
Worker A: T021, T026, then workflow portions of T031/T033
Worker B: T022, T027, then activity portions of T032/T033
Worker C: T023–T024, then T030 atomic-write hardening
Integrator: T025, T028–T029, T034–T036
```

### User Story 2

```text
Worker A: T037, T043–T044
Worker B: T038, T045
Worker C: T039–T040, T049
Integrator: T041–T042, T046–T050
```

### User Story 3

```text
Worker A: T051, T055
Worker B: T052, T057
Worker C: T053, T056
Worker D: T054, T058
Integrator: T059–T066
```

### User Story 4

```text
Worker A: T067–T069
Worker B: T070, T072
Worker C: T070, T073
Integrator: T071, T074–T079
```

## Implementation Strategy

### MVP First

1. Complete Setup and Foundational phases.
2. Complete US1 against real SQLite.
3. Stop and validate atomic lifecycle/restart behavior independently.
4. Do not delete EF at the MVP checkpoint.

### Incremental Delivery

1. **US1** proves durable behavior.
2. **US2** removes the production performance hazard.
3. **US3** proves the provider-neutral product promise.
4. **US4** captures evidence and removes the duplicated implementation.
5. **Phase 7** lands and verifies the mainline result.

### Commit and Review Discipline

- Commit each completed logical task group locally with useful messages.
- Freeze an exact commit before every independent review.
- Never use a narrow provider test as evidence for a four-provider or repository-wide claim.
- Keep EF only while it supplies an explicit oracle; remove its code immediately after the recorded gates pass.
- Update task checkboxes and quickstart evidence in the same commit that proves completion.
