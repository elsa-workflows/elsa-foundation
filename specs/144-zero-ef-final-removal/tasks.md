# Tasks: Zero-EF Final Removal

**Input**: Design documents from `specs/144-zero-ef-final-removal/`

**Prerequisites**: [plan.md](plan.md), [spec.md](spec.md), [research.md](research.md), [data-model.md](data-model.md), [contracts/](contracts/), [quickstart.md](quickstart.md)

**Tests**: Required. This is a refactor/removal lane governed by framework §2.21.1 and §2.23. Tests and test-objective preservation precede deletion.

**Evidence rule**: Check a task only after appending a one-line evidence note to [quickstart.md](quickstart.md) with an exact command/result, artifact identity, review disposition, merge SHA, or issue/project update. A passing claim without opened covering tests or immutable evidence does not complete a task.

**Resource markers**:

- `[CONTAINER]` starts or uses a database-server container and remains deferred while the resource hold applies.
- `[PERF]` executes or evaluates performance measurements and runs only on an otherwise-idle machine.
- Tasks without these markers are container-free.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel because it changes disjoint files and does not depend on an incomplete task.
- **[Story]**: Maps to the user story in [spec.md](spec.md).
- Shared files (`Directory.Packages.props`, `Elsa.Server.slnx`, `shells*.json`, `specs/094-harden-groundwork-stores/coverage-ledger.json`) are never edited in parallel.

## Phase 1: Setup and Intake Freeze

**Purpose**: Establish an authoritative, reviewable starting state before any deletion.

- [x] T001 Record current Elsa and Groundwork remote-main SHAs, consumed package version, issue states, PR states, and Project 33 states in `specs/144-zero-ef-final-removal/quickstart.md`
- [x] T002 Generate the categorized intake from `tests/Elsa/Architecture/Baselines/ef-core-surface.json` and record counts, exact owning families, tools, temporary benchmark oracles, and source head in `specs/144-zero-ef-final-removal/ef-removal-inventory.md`
- [x] T003 Inventory every test method in files directly referencing EF packages/types/registrations in `specs/144-zero-ef-final-removal/test-retention-ledger.md`
- [x] T004 Trace shared fixtures, host builders, and transitive test-project references and add token-free EF-reachable methods under a disclosed addendum in `specs/144-zero-ef-final-removal/test-retention-ledger.md`
- [x] T005 [P] Map FR-001 through FR-028 and SC-001 through SC-010 to tasks/evidence owners in `specs/144-zero-ef-final-removal/quickstart.md`
- [x] T006 [P] Record the current host feature matrix from `src/Apps/Elsa.Server/shells.json`, `src/Apps/Elsa.Server/shells.Production.json`, `src/Apps/Elsa.Server/shells.baseline.json`, and `docker/compose/elsa-server.shells.json` in `specs/144-zero-ef-final-removal/contracts/reference-host-matrix.md`
- [x] T007 Record the serialization owner/order for `Directory.Packages.props`, `Elsa.Server.slnx`, `shells*.json`, and `specs/094-harden-groundwork-stores/coverage-ledger.json` in `specs/144-zero-ef-final-removal/quickstart.md`
- [x] T008 Run the current shrink-only and frozen-Identity-oracle architecture checks and record exact results in `specs/144-zero-ef-final-removal/quickstart.md`

---

## Phase 2: Foundational Prerequisite Admission

**Purpose**: Prove that deletion cannot destroy a still-required oracle.

**⚠️ CRITICAL**: No EF-family deletion task may begin until its linked prerequisite tasks pass on remote `main`.

- [ ] T009 Verify #642 diagnostics four-provider/grouped-reducer/test-ledger evidence is merged and record its merge/artifact identities in `specs/144-zero-ef-final-removal/quickstart.md`
- [ ] T010 Verify #643 completes the 145-member OpenIddict ledger, production registration, four-provider black-box suite, exact-range reviews, and merge; record evidence in `specs/144-zero-ef-final-removal/quickstart.md`
- [ ] T011 Verify #646 supplies an accepted verdict for every required coverage-ledger row, including diagnostics, Identity/OpenIddict, physical forms, and ratified amendments; record evidence in `specs/144-zero-ef-final-removal/quickstart.md`
- [ ] T012 Verify #932 supplies SQL Server and MongoDB dashboard run-health/portfolio support or a separately ratified non-support amendment, including #932's required final issue and Project 33 disposition; record evidence in `specs/144-zero-ef-final-removal/quickstart.md`
- [ ] T013 Record three independent upstream gates in `specs/144-zero-ef-final-removal/quickstart.md`: exact consumed-package provenance for merged #141/#143 APIs; accepted #50 performance evidence and final issue disposition; and Groundwork parent #25 completion (or a named, separately ratified #647/#629 amendment). Never infer #50/#25 completion from package contents or a partial checkpoint.
- [x] T014 [P] Open every test cited as replacement evidence in `specs/144-zero-ef-final-removal/test-retention-ledger.md` and correct or reject unsupported “covered by” claims
- [x] T015 [P] Verify `git status` in the #647 worktree and main checkout and record any unrelated/user-owned changes in `specs/144-zero-ef-final-removal/quickstart.md`
- [x] T016 Finalize the prerequisite/deletion DAG and per-family gate links in `specs/144-zero-ef-final-removal/ef-removal-inventory.md`
- [ ] T017 Review the completed intake, test ledger, prerequisite table, and shared-file serialization plan before admitting any implementation story in `specs/144-zero-ef-final-removal/quickstart.md`

**Checkpoint**: Every removal target and test objective is known; each EF family has explicit non-destructive admission gates.

---

## Phase 3: User Story 1 - Coherent Groundwork-Only Host (Priority: P1) 🎯 MVP

**Goal**: One provider selection backs every enabled durable lane, including dashboard and OpenIddict, with fail-closed readiness.

**Cross-issue boundary**: #932 owns the SQL Server/MongoDB dashboard dialect and aggregation delivery. #647 consumes its merged evidence as a mandatory gate; it does not declare that source work complete, substitute a local workaround, or close while #932 remains incomplete. The host-registration and all-lanes composition checks below remain #647 integration work.

**Independent Test**: Compose each supported provider shape, resolve every enabled durable contract to Groundwork, resolve no EF implementation, and prove dashboard run-health/portfolio remains available.

### Tests for User Story 1

- [x] T018 [US1] Record #932's SQL Server/MongoDB dashboard acceptance tests and exact expected outcomes from `tests/Elsa/Workflows/Dashboard/Tests/GroundworkWorkflowRunHealthDataSourceTests.cs` and `src/Elsa/Workflows/Dashboard/Persistence/Groundwork/GroundworkWorkflowPortfolioDataSource.cs` in `specs/144-zero-ef-final-removal/quickstart.md`
- [x] T019 [US1] Verify #932 has merged SQL Server/MongoDB dialect and deterministic aggregation evidence for `src/Elsa/Workflows/Dashboard/Persistence/Groundwork/GroundworkWorkflowRunHealthDataSource.cs` and `src/Elsa/Workflows/Dashboard/Persistence/Groundwork/GroundworkWorkflowPortfolioDataSource.cs`; otherwise retain it as a blocking gate in `specs/144-zero-ef-final-removal/quickstart.md`
- [ ] T020 [P] [US1] Add SQL Server and MongoDB unified-registration resolution tests, including an exact negative missing-capability/schema-readiness diagnostic, in `tests/Elsa/Persistence/Groundwork/UnifiedHost/Tests/`
- [ ] T021 [P] [US1] Add four-provider all-lanes/no-EF composition assertions that reject silent feature omission or in-memory/EF fallback in `tests/Elsa/Persistence/Groundwork/UnifiedHost/Tests/`

### Implementation for User Story 1

- [ ] T022 [US1] Consume the merged #932 SQL Server dashboard dialect evidence for `src/Elsa/Workflows/Dashboard/Persistence/Groundwork/GroundworkWorkflowRunHealthDataSource.cs` before enabling the SQL Server reference-host row in `specs/144-zero-ef-final-removal/contracts/reference-host-matrix.md`
- [ ] T023 [US1] Consume the merged #932 MongoDB run-health aggregation evidence for `src/Elsa/Workflows/Dashboard/Persistence/Groundwork/GroundworkWorkflowRunHealthDataSource.cs` before enabling the MongoDB reference-host row in `specs/144-zero-ef-final-removal/contracts/reference-host-matrix.md`
- [ ] T024 [US1] Consume the merged #932 MongoDB workflow-portfolio aggregation evidence for `src/Elsa/Workflows/Dashboard/Persistence/Groundwork/GroundworkWorkflowPortfolioDataSource.cs` before enabling the MongoDB reference-host row in `specs/144-zero-ef-final-removal/contracts/reference-host-matrix.md`
- [ ] T025 [US1] Wire dashboard run-health/portfolio into `src/Elsa/Persistence/Groundwork/SqlServer/Unified/SqlServerGroundworkUnifiedPersistenceShellFeature.cs`
- [ ] T026 [US1] Wire dashboard run-health/portfolio into `src/Elsa/Persistence/Groundwork/MongoDb/Unified/MongoDbGroundworkUnifiedPersistenceShellFeature.cs`
- [ ] T027 [US1] Update dashboard Groundwork feature registration/extension-point documentation in `src/Elsa/Workflows/Dashboard/Persistence/Groundwork/README.md` and `src/Elsa/Workflows/Dashboard/Persistence/Groundwork/EXTENSION_POINTS.md`
- [ ] T028 [US1] Update unified provider documentation to promise the complete dashboard-enabled matrix in `src/Elsa/Persistence/Groundwork/Unified/README.md` and `src/Elsa/Persistence/Groundwork/EXTENSION_POINTS.md`
- [ ] T029 [US1] After T010, T012, T017, and T019-T028 pass, admit the sole earlier serialized #647 host slice and replace remaining Identity/OpenIddict EF host feature selection with Groundwork in `src/Apps/Elsa.Server/shells.json`
- [ ] T030 [US1] In the same serialized host slice after T029, replace remaining production Identity/OpenIddict EF host feature selection with Groundwork while preserving seeded-admin configuration in `src/Apps/Elsa.Server/shells.Production.json`
- [ ] T031 [US1] In the same serialized host slice after T030, remove host-only EF project/package references made obsolete by the Groundwork composition from `src/Apps/Elsa.Server/Elsa.Server.csproj`
- [ ] T032 [US1] Run the container-free SQLite registration/composition/startup slice and record exact results in `specs/144-zero-ef-final-removal/quickstart.md`
- [ ] T033 [US1] [CONTAINER] Run the SQL Server, PostgreSQL, and MongoDB dashboard-enabled host matrix and retain provider/version/topology/result evidence under `docs/reports/evidence/144-zero-ef-final-removal/`
- [ ] T034 [US1] Reconcile the implemented host matrix against `specs/144-zero-ef-final-removal/contracts/reference-host-matrix.md` and record its verdict in `specs/144-zero-ef-final-removal/quickstart.md`

**Checkpoint**: Each supported provider independently composes every required lane through Groundwork and fails closed on missing readiness.

---

## Phase 4: User Story 2 - Remove EF Without Losing Behavior (Priority: P1)

**Goal**: Preserve all valid behavior while deleting every EF family in dependency order.

**Independent Test**: Every affected test objective has passing EF-independent evidence or architect-approved removal, and every intake EF entry is gone after its prerequisite gate.

### Test Preservation for User Story 2

- [ ] T035 [P] [US2] Convert/rehost provider-neutral ASP.NET Core Identity test objectives from `tests/Elsa/Foundation/Identity/Tests/AspNetCoreIdentity/` into Groundwork-backed fixtures under `tests/Elsa/Foundation/Identity/Tests/AspNetCoreIdentity/Groundwork/`
- [ ] T036 [P] [US2] Convert/rehost OpenIddict authentication, endpoint, scheme, and lifecycle test objectives from `tests/Elsa/Foundation/Identity/Tests/OpenIddict/` and `tests/Elsa/Foundation/Identity/Tests/Api/` onto the production Groundwork stores
- [ ] T037 [P] [US2] Convert/rehost provider-neutral diagnostics test objectives from `tests/Elsa/Diagnostics/*/Persistence/Tests/` onto the Groundwork diagnostics suites
- [ ] T038 [P] [US2] Classify or convert provider-neutral objectives from `tests/Elsa/Persistence/EFCore/Tests/` into the appropriate persistence contract/architecture suites
- [ ] T039 [P] [US2] Remove token-free EF reachability from `tests/Elsa/Modularity/Tests/` and other shared-host consumers while preserving their composition objectives
- [ ] T040 [US2] Record architect decisions for every genuinely EF-mechanism-specific removal row in `specs/144-zero-ef-final-removal/test-retention-ledger.md`

### Dependency-Ordered Deletion for User Story 2

- [ ] T041 [US2] After T009 and T011 pass, delete `src/Elsa/Diagnostics/OpenTelemetry/Persistence/EFCore/` and remove its project/test references
- [ ] T042 [US2] After T009 and T011 pass, delete `src/Elsa/Diagnostics/StructuredLogs/Persistence/EFCore/` and remove its project/test references
- [ ] T043 [US2] Remove obsolete diagnostics EF fixtures/packages from `tests/Elsa/Diagnostics/OpenTelemetry/Persistence/Tests/` and `tests/Elsa/Diagnostics/StructuredLogs/Persistence/Tests/` only after T037/T040 dispositions
- [ ] T044 [US2] After T010/T011 pass, remove `UseEntityFrameworkCore`, the OpenIddict `DbContext`, migrations, EF packages, and EF registration paths from `src/Elsa/Foundation/Identity/OpenIddict/`
- [ ] T045 [US2] Remove OpenIddict/Identity `UseInMemoryDatabase` and EF fixture setup from `tests/Elsa/Foundation/Identity/Tests/` only after T035/T036 replacements pass
- [ ] T046 [US2] After T011 passes, delete `src/Elsa/Foundation/Identity/AspNetCoreIdentity/EntityFrameworkCore/` together with its frozen oracle baseline/test
- [ ] T047 [US2] Remove the frozen Identity EF benchmark target and temporary EF comparison code from `benchmarks/Elsa.Groundwork.StorePerformance.Benchmarks/` only after its verdict import is durably complete
- [ ] T048 [US2] Delete `src/Elsa/Persistence/EFCore/` after diagnostics, OpenIddict, Identity, and every other dependent edge is gone
- [ ] T049 [US2] Remove or rehome all approved/converted tests and delete `tests/Elsa/Persistence/EFCore/Tests/`
- [ ] T050 [US2] Remove residual EF project references from every app/library/test/tool project identified in `specs/144-zero-ef-final-removal/ef-removal-inventory.md`
- [ ] T051 [US2] Remove deleted EF projects from `Elsa.Server.slnx` in the serialized shared-file slice
- [ ] T052 [US2] Remove all EF central package versions and remaining direct EF package references from `Directory.Packages.props` and affected project files in the serialized shared-file slice
- [ ] T053 [US2] Remove the EF logging/provider configuration residue from `src/Apps/Elsa.Server/appsettings.json`
- [ ] T054 [US2] Audit `shells.baseline.json`, `docker/compose/elsa-server.shells.json`, samples, scripts, tools, docs, EF aliases, and EF initializers for silent feature omission or EF runtime configuration and correct the affected files
- [ ] T055 [US2] Run a repository-wide source/project/package/configuration search and reconcile every result against `specs/144-zero-ef-final-removal/ef-removal-inventory.md`
- [ ] T056 [US2] During each vertical deletion, regenerate and review the shrink-only `tests/Elsa/Architecture/Baselines/ef-core-surface.json` in the same commit, proving the diff only removes entries
- [ ] T057 [US2] Run the complete container-free affected build/unit/architecture suites and record results in `specs/144-zero-ef-final-removal/quickstart.md`
- [ ] T058 [US2] [CONTAINER] Run four-provider diagnostics, Identity, OpenIddict, tenancy, concurrency, and restart/recovery suites on the post-deletion candidate and retain evidence under `docs/reports/evidence/144-zero-ef-final-removal/`

**Checkpoint**: No EF source, test fixture, project, package, registration, migration, or configuration remains; every valid objective passes in an EF-independent home.

---

## Phase 5: User Story 3 - Prevent EF From Returning (Priority: P2)

**Goal**: Replace migration-era ratchets with a permanent fail-closed absolute-zero guard over every repository project.

**Independent Test**: Synthetic fixtures detect every bypass class, and the real complete restored graph has every guarded category empty.

### Tests for User Story 3

- [x] T059 [US3] Add omitted-project and Windows-style project-reference bypass tests in `tests/Elsa/Architecture/EfCoreSurfaceRatchetTests.cs`
- [x] T060 [US3] Add central/shared/imported/conditional/direct/static-transitive dependency bypass tests in `tests/Elsa/Architecture/EfCoreSurfaceRatchetTests.cs`
- [x] T061 [US3] Add restored-transitive, missing-assets, stale-but-present assets/receipt, changed dependency-input, and project-set-mismatch fail-closed tests in `tests/Elsa/Architecture/EfCoreSurfaceRatchetTests.cs`
  - Evidence: the receipt-focused run passed 6/6 and the complete `EfCoreSurfaceRatchetTests` class passed 43/43 at the recorded T061/T063/T068 checkpoint.
- [ ] T062 [US3] Add migration/context/registration/JSON/YAML host-configuration detection and comment-false-positive tests in `tests/Elsa/Architecture/EfCoreSurfaceRatchetTests.cs`

### Implementation for User Story 3

- [x] T063 [US3] Ensure `tests/Elsa/Architecture/EfCoreSurfaceScanner.cs` discovers every repository project independently of `Elsa.Server.slnx`, validates the all-project restore receipt's exact project/input/assets bindings, and evaluates all contract categories
  - Evidence: fresh discovery found 246 projects; both real receipts bound that set, 251 inputs, and all assets; the C# scanner returned `isValid=True`, and `Categories()` now exposes all 14 contract categories.
- [ ] T064 [US3] Rewrite `tests/Elsa/Architecture/EfCoreSurfaceRatchetTests.cs` so the production assertion requires a valid current all-project restore receipt plus every EF category and `ProjectsMissingAssets` to be empty
- [ ] T065 [US3] Remove baseline load/save/compare behavior and the `ELSA_UPDATE_EF_CORE_BASELINE` switch from `tests/Elsa/Architecture/EfCoreSurfaceScanner.cs` and `tests/Elsa/Architecture/EfCoreSurfaceRatchetTests.cs`
- [ ] T066 [US3] Delete `tests/Elsa/Architecture/Baselines/ef-core-surface.json` after T046 has already retired the frozen Identity oracle baseline/test in the same reviewed oracle-removal change
- [ ] T067 [US3] Rewrite `tests/Elsa/Architecture/Baselines/README.md` as permanent absolute-zero guard documentation without an update path
- [x] T068 [US3] Add and run repository-owned Bash and PowerShell restore-driver entry points under `tools/architecture/` to independently discover and force-evaluate every repository project, write the exact project/input/assets restore receipt consumed by the guard, and prove its project set matches fresh scanner discovery
  - Evidence: Bash and PowerShell each completed 246/246 forced restores on clean exact heads; their project-set, input, and 246 assets identities match exactly, with receipt hashes and failure dispositions retained in `quickstart.md`.
- [ ] T069 [US3] Run the complete EF architecture guard and all bypass fixtures against the frozen candidate; record results in `specs/144-zero-ef-final-removal/quickstart.md`
- [ ] T070 [US3] Run `dotnet nuget why` for every unexpected dependency found during certification and record the removed chain in `specs/144-zero-ef-final-removal/ef-removal-inventory.md`
- [ ] T071 [US3] Record the exact-head absolute-zero certification and category counts in `specs/144-zero-ef-final-removal/quickstart.md`

**Checkpoint**: The permanent guard has no baseline/allow-list/update path and cannot be bypassed by solution omission or missing dependency evidence.

---

## Phase 6: User Story 4 - Truthful Program Record (Priority: P3)

**Goal**: Make governance, operations, maps, issues, and Project 33 describe the verified Groundwork-only state.

**Independent Test**: Every completion claim links to immutable evidence on remote `main`, and no source of truth treats OpenIddict as outside the gate or EF as shipped.

- [ ] T072 [US4] Prepare the targeted `.specify/memory/constitution.md` amendment, preserve provider-neutral persistence invariant gates, obtain the required architect consensus, and record the ratified version/evidence in `specs/144-zero-ef-final-removal/quickstart.md`
- [ ] T073 [P] [US4] Mark the final state and retained evidence in `docs/program-goals/zero-ef-persistence.md`
- [ ] T074 [P] [US4] Resolve the `ef-removal` decision and retain the delivery-lane/completion-gate distinction for OpenIddict in `docs/decision-maps/zero-ef-groundwork.md`
- [ ] T075 [P] [US4] Update the final consequences/status and links in `docs/adr/0042-elsa-foundation-ships-only-groundwork-persistence-implementations.md`
- [ ] T076 [P] [US4] Document Groundwork schema validation and authorized apply for reference CI/CD/deployments in the owning operational documentation under `docs/`, then run and record the owning documentation validation command
- [ ] T077 [P] [US4] Update affected provider/host READMEs and `EXTENSION_POINTS.md` catalogs in the same work unit as their registration changes
- [ ] T078 [US4] Prepare evidence-bound #647/#629 closure summaries and an immutable Project 33 closure ledger enumerating #629, #642, #643, #646, #647, #932, and every other parent-required item with required/actual final state, merge or amendment evidence, and verification timestamp; do not apply transitions before remote-main verification
- [ ] T079 [US4] Refresh `docs/maps/` through the five authorized map scripts after implementation inputs settle
- [ ] T080 [US4] Review and disposition `docs/reports/maps-v2-findings.md` and `docs/reports/maps-v1-findings.md`

**Checkpoint**: Governance, operational docs, maps, issue text, and project state are ready to reflect the verified remote-main result.

---

## Phase 7: Final Verification, Independent Review, and Model B Landing

**Purpose**: Prove the complete work unit at exact head and close only after merge presence.

- [ ] T081 Re-audit every FR/SC and evidence contract row against the frozen candidate in `specs/144-zero-ef-final-removal/quickstart.md`
- [ ] T082 Run the complete `Elsa.Server.slnx` restore and build on the frozen candidate and record exact results in `specs/144-zero-ef-final-removal/quickstart.md`
- [ ] T083 Run the complete container-free test graph, including architecture, registration, test-retention replacements, and package-boundary suites, and record exact results in `specs/144-zero-ef-final-removal/quickstart.md`
- [ ] T084 Run package/pack audits for every shipped project and prove no package dependency reintroduces EF; record results in `specs/144-zero-ef-final-removal/quickstart.md`
- [ ] T085 Run maintained local SQLite reference-host startup/schema-readiness smoke tests and record results in `specs/144-zero-ef-final-removal/quickstart.md`
- [ ] T086 [CONTAINER] Run the complete SQL Server/PostgreSQL/MongoDB provider and reference-host startup matrix on the frozen candidate and retain evidence under `docs/reports/evidence/144-zero-ef-final-removal/`
- [ ] T087 [PERF] Verify every retained #646 artifact/verdict binds to the final package/manifest/input fingerprints in `specs/094-harden-groundwork-stores/coverage-ledger.json` and record invalidation/rerun evidence in `specs/144-zero-ef-final-removal/quickstart.md`
- [ ] T088 Freeze base/head SHAs and candidate metadata for independent review in `specs/144-zero-ef-final-removal/quickstart.md`
- [ ] T089 [P] Run an adversarial read-only correctness/mechanism reviewer on the exact T088 commit range and record findings in `specs/144-zero-ef-final-removal/quickstart.md`
- [ ] T090 [P] Run an adversarial read-only evidence-integrity reviewer on the exact T088 commit range and record findings in `specs/144-zero-ef-final-removal/quickstart.md`
- [ ] T091 [P] Run an adversarial read-only scope/test-preservation reviewer on the exact T088 commit range and record findings in `specs/144-zero-ef-final-removal/quickstart.md`
- [ ] T092 Remediate every confirmed T089-T091 finding in the cited source/test/doc paths, rerun affected checks, and record the updated candidate head in `specs/144-zero-ef-final-removal/quickstart.md`
- [ ] T093 Have each originating reviewer re-verify its finding dispositions on the remediated exact range and record final verdicts in `specs/144-zero-ef-final-removal/quickstart.md`
- [ ] T094 Verify `git status` in the #647 worktree and main checkout, stage only explicit paths, commit coherent work-unit checkpoints, push the organization branch, and record the pushed head in `specs/144-zero-ef-final-removal/quickstart.md`
- [ ] T095 Mark the draft PR ready only after hosted checks and all local/evidence/review gates pass, merge with a merge commit, and record the PR/merge result in `specs/144-zero-ef-final-removal/quickstart.md`
- [ ] T096 Verify remote `main` contains the merge SHA, audit every row of the T078 closure ledger, apply/verify all required child issue and Project 33 final dispositions, then close #647 and #629 and retain the durable summary in `specs/144-zero-ef-final-removal/quickstart.md`

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 1 (T001-T008)**: Starts immediately; T005/T006 can run in parallel. T003 precedes T004 because both append to the same test-retention ledger.
- **Phase 2 (T009-T017)**: Depends on Phase 1; each prerequisite may be verified independently, but T017 blocks deletion.
- **US1 (T018-T034)**: #647-owned host integration tests may begin after Phase 1; T029-T031 are one serialized host slice and cannot start until T010, T012, T017, and T019-T028 pass.
- **US2 (T035-T058)**: Test conversions may begin after Phase 1; each deletion task depends on its explicit Phase-2 gate and replacement-test disposition.
- **US3 (T059-T071)**: Fixture tests/scanner hardening may begin after Phase 1; baseline deletion waits until US2 reaches real zero.
- **US4 (T072-T080)**: Drafting may begin after Phase 1; final wording/maps wait for the implemented result.
- **Final (T081-T096)**: Depends on all stories and all deferred container/performance evidence.

### Deletion DAG

```text
#642 + diagnostics #646 verdict ──> diagnostics EF deletion ──┐
#643 + OpenIddict #646 verdict ─> OpenIddict EF deletion ─────┤
Identity #646 verdict ──────────> Identity EF deletion ───────┤
                                                              ├─> shared EF substrate deletion
#932 ─> four-provider dashboard host parity ──────────────────┤
#50 / all #646 verdicts ──────────────────────────────────────┘

shared EF substrate deletion
  ─> package/solution/host cleanup
  ─> absolute-zero guard
  ─> docs/maps/reviews
  ─> Model B merge
  ─> #647 close
  ─> #629 close
```

### Parallel Opportunities

- T005/T006 are parallel intake artifacts; T003/T004 serialize edits to the same ledger.
- T014/T015 are parallel reviews.
- T020/T021 can proceed alongside the serialized dashboard-source tests T018/T019.
- T035-T039 are parallel test-preservation lanes.
- T059-T062 serialize edits to the shared guard test file.
- T073-T077 are parallel documentation updates in different files.
- T089-T091 MUST run in parallel on the same frozen exact range.

### Shared-File Serialization

The following shared-file slices execute in this strict order and are never delegated concurrently:

1. Any prerequisite #646 `coverage-ledger.json` import before T011
2. T029 `shells.json`
3. T030 `shells.Production.json`
4. T031 host project references
5. T051 `Elsa.Server.slnx`
6. T052 `Directory.Packages.props` and central/project package references

## Parallel Examples

### Intake

```text
Worker A: Read-only direct-token test-method inventory report for T003
Worker B: Read-only shared-fixture/host reachability report for T004
Worker C: T006 reference-host matrix intake
Root: serialize T003/T004 into the ledger, open cited tests, and complete T014/T017
```

### Test preservation

```text
Worker A: T035 ASP.NET Core Identity objectives
Worker B: T036 OpenIddict/API objectives
Worker C: T037 diagnostics objectives
Root: T038/T039 integration, architect dispositions, and all replacement verification
```

### Final review

```text
Reviewer A: T089 correctness/mechanism
Reviewer B: T090 evidence integrity
Reviewer C: T091 scope/test preservation
Root: T092 remediation, originating-reviewer T093 re-verification, and Model B landing
```

## Implementation Strategy

### Container-Free First

1. Complete Phase 1 and all container-free Phase 2 verification.
2. Verify merged Groundwork PRs #147/#148 are present in the exact published package family consumed by #643; do not treat either partial checkpoint as closure of Groundwork #50 or #141.
3. Advance test ledgers, guard fixture tests, #932 evidence intake/final host integration, and documentation without starting server containers.
4. Stop only when T033/T058/T086/T087 are the actual remaining gates.

### Incremental Delivery

1. Freeze intake and prerequisites.
2. Land #932/host parity without deleting EF.
3. Convert/rehost tests.
4. Delete each EF leaf after its exact gates.
5. Delete the shared substrate and package/configuration surface.
6. Replace the ratchet with absolute zero.
7. Refresh governance/maps, verify, review, merge, and close.

### Review Discipline

- Every implementation checkpoint remains on the issue #647 organization branch.
- Workers do not commit.
- Root verifies worker claims and cited tests.
- Candidate reviews bind to an exact range.
- Confirmed findings are remediated and re-verified by the originating reviewer.
- Issue and Project 33 status changes happen only after remote-main verification.

## Notes

- Tasks marked `[CONTAINER]` or `[PERF]` are intentionally visible but deferred during the resource hold.
- The `speckit-agent-context-update` script could not run because neither available Python runtime included PyYAML; its exact managed-block update was applied directly to `AGENTS.md` and must be verified in review.
- The branch number (`779`) follows repository-wide branch sequencing; the independently allocated spec directory is `144-zero-ef-final-removal`.
