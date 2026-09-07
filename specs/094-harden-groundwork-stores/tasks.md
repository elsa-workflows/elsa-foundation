# Tasks: Harden Groundwork Store Families

**Input**: Design documents from `/specs/094-harden-groundwork-stores/`

**Prerequisites**: `plan.md`, `spec.md`, `research.md`, `data-model.md`, `contracts/`, `coverage-ledger.json`

**Tests**: The specification requires test-first behavioral, provider, concurrency, restart, bounded-query, architecture, CLI, and performance-correctness evidence. Test tasks below must fail for the intended reason before their implementation task begins.

**Organization**: Tasks are grouped by user story for independent verification. Execution uses an intentional dependency DAG: provider-fixture work may be developed in parallel, but merge/ledger-transition order follows the ten ratcheted delivery boundaries in `plan.md`. User Story 3 has an early fencing/checkpoint checkpoint (boundary 5) and a later operational-store checkpoint (boundary 8); the latter must not execute or merge until IAM/secrets and bounded-query boundaries 6–7 are complete.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel because it changes different files and has no dependency on an incomplete task.
- **[Story]**: Maps the task to a user story in `spec.md`.
- Every task names the exact file or files it owns.

## Coverage-Row Ownership

These stable groups make every agent handoff self-contained. A task that advances a group may update only the named rows.

- **ALL32**: `runtime-activity-execution-inspection`, `runtime-activity-execution-state`, `runtime-bookmark-state`, `runtime-durable-timer`, `runtime-durable-value-state`, `runtime-execution-liveness`, `runtime-incident-state`, `runtime-recurring-trigger-schedule`, `runtime-checkpoint-commit`, `runtime-diagnostics-settings`, `runtime-post-commit-outbox`, `runtime-scheduler-state`, `runtime-executable-source-reference`, `runtime-workflow-executable`, `runtime-workflow-execution-state`, `runtime-workflow-hold-state`, `runtime-scheduler-poison`, `runtime-scheduler-work-queue`, `runtime-trigger-binding`, `runtime-publication-projection-state`, `iam-user`, `iam-role`, `iam-application`, `iam-credential`, `iam-external-identity`, `iam-claim-mapping`, `iam-provider-configuration-tenant`, `iam-provider-configuration-global`, `iam-tenant-membership`, `secrets-repository`, `distributed-execution-placement`, `distributed-command-transport`.
- **DIAGNOSTICS2** (program-owner-ratified 2026-07-25 additive denominator): `diagnostics-open-telemetry-store`, `diagnostics-structured-log-store`. Historical ALL32 task evidence remains valid; current ledger, composition, performance, and completion gates cover ALL32 + DIAGNOSTICS2.
- **B5-FENCE-CHECKPOINT**: `runtime-activity-execution-inspection`, `runtime-activity-execution-state`, `runtime-bookmark-state`, `runtime-durable-value-state`, `runtime-execution-liveness`, `runtime-checkpoint-commit`, `runtime-workflow-executable`, `runtime-workflow-execution-state`.
- **B6-IAM-SECRETS**: `iam-user`, `iam-role`, `iam-application`, `iam-credential`, `iam-external-identity`, `iam-claim-mapping`, `iam-provider-configuration-tenant`, `iam-provider-configuration-global`, `iam-tenant-membership`, `secrets-repository`.
- **B7-BOUNDED-QUERIES**: `runtime-activity-execution-inspection`, `runtime-activity-execution-state`, `runtime-bookmark-state`, `runtime-durable-value-state`, `runtime-executable-source-reference`, `runtime-workflow-executable`, `runtime-workflow-execution-state`, `runtime-trigger-binding`.
- **B8-OPERATIONAL-RUNTIME**: `runtime-durable-timer`, `runtime-incident-state`, `runtime-recurring-trigger-schedule`, `runtime-post-commit-outbox`, `runtime-scheduler-state`, `runtime-workflow-hold-state`, `runtime-scheduler-poison`, `runtime-scheduler-work-queue`, `runtime-trigger-binding`, `runtime-publication-projection-state`.
- **B9-DISTRIBUTED**: `distributed-execution-placement`, `distributed-command-transport`.

Unless a row is externally owned by #644 or #660, every provider-evidence task means the complete SQLite, SQL Server, PostgreSQL, and MongoDB matrix. External rows consume linked owner evidence and may not be marked locally complete. Partial provider evidence can be recorded but cannot advance a row to evidence-complete or ready.

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Pin one Groundwork tool/package generation and establish the project/CI shells required by the implementation.

- [X] T001 After Groundwork #70 and #71 land, pin one released Core/Documents/provider/Tool generation containing #32 and #43–#48/#70/#71 in `Directory.Packages.props` and `.config/dotnet-tools.json`
- [X] T002 Add the acyclic composition-contract, conformance, and missing base/unified provider source/test project shells to `Elsa.Server.slnx`, `src/Elsa/Persistence/Groundwork/Composition/Elsa.Persistence.Groundwork.Composition.csproj`, `tests/Elsa/Persistence/Groundwork/Composition/Tests/Elsa.Persistence.Groundwork.Composition.Tests.csproj`, `tests/Elsa/Persistence/Groundwork/Conformance/Tests/Elsa.Persistence.Groundwork.Conformance.Tests.csproj`, `src/Elsa/Persistence/Groundwork/SqlServer/Elsa.Persistence.Groundwork.SqlServer.csproj`, `src/Elsa/Persistence/Groundwork/SqlServer/Unified/Elsa.Persistence.Groundwork.SqlServer.Unified.csproj`, `tests/Elsa/Persistence/Groundwork/SqlServer/Tests/Elsa.Persistence.Groundwork.SqlServer.Tests.csproj`, `src/Elsa/Persistence/Groundwork/MongoDb/Elsa.Persistence.Groundwork.MongoDb.csproj`, `src/Elsa/Persistence/Groundwork/MongoDb/Unified/Elsa.Persistence.Groundwork.MongoDb.Unified.csproj`, and `tests/Elsa/Persistence/Groundwork/MongoDb/Tests/Elsa.Persistence.Groundwork.MongoDb.Tests.csproj`; update `src/Elsa/Persistence/Groundwork/Elsa.Persistence.Groundwork.csproj` to remove Composition/SqlServer/MongoDb Compile/EmbeddedResource/None globs, and make each new SQL Server/MongoDB base provider project exclude its nested Unified tree
- [X] T003 Add fast, provider, failure/restart, plan, temporary-oracle, and readiness job shells to `.github/workflows/ci.yml` and `.github/workflows/integration.yml`

---

## Phase 2: Foundational Provider Fixture (Blocking Prerequisite)

**Purpose**: Supply one real-provider test vocabulary used by every story without allowing provider-specific expected domain outcomes.

**Critical gate**: Complete this phase before provider evidence is accepted for any story.

- [X] T004 Define the provider driver lifecycle and independent-client contract in `tests/Elsa/Persistence/Groundwork/Testing/GroundworkProviderDriver.cs`
- [X] T005 [P] Define scenario observations, result digests, composition fingerprints, and native-plan evidence in `tests/Elsa/Persistence/Groundwork/Testing/GroundworkScenarioResult.cs`
- [X] T006 [P] Define deterministic cancellation/failure-window controls in `tests/Elsa/Persistence/Groundwork/Testing/GroundworkFailureController.cs`
- [X] T007 Write failing shared contract tests for deterministic reset, independent clients, dispose/reopen, process restart, topology rejection, and sanitized evidence in `tests/Elsa/Persistence/Groundwork/Conformance/Tests/ProviderDriverContractTests.cs`
- [X] T008 [P] Implement the file-backed SQLite driver against T007 in `tests/Elsa/Persistence/Groundwork/Testing/SqliteGroundworkProviderDriver.cs`
- [X] T009 [P] Implement the SQL Server Testcontainers driver against T007 in `tests/Elsa/Persistence/Groundwork/Testing/SqlServerGroundworkProviderDriver.cs`
- [X] T010 [P] Refactor the existing PostgreSQL fixture into the shared driver against T007 in `tests/Elsa/Persistence/Groundwork/Testing/PostgreSqlGroundworkProviderDriver.cs` and `tests/Elsa/Persistence/Groundwork/PostgreSql/Tests/PostgresContainerFixture.cs`
- [X] T011 [P] Implement a transaction-capable MongoDB replica-set driver against T007 in `tests/Elsa/Persistence/Groundwork/Testing/MongoDbGroundworkProviderDriver.cs`

**Checkpoint**: All four drivers can run one provider-independent persistent round trip and cannot report memory-backed durability evidence.

---

## Phase 3: User Story 1 — Account For Every Durable Contract (Priority: P1) — MVP

**Goal**: Make the 32-row denominator, ownership boundaries, test continuity, core dependency boundary, and non-growing EF surface executable CI gates.

**Independent Test**: Mutate a fixture copy to omit a row, registration, manifest unit, baseline test identity, authority link, provider evidence, or required verdict and verify the validator rejects the exact omission.

### Tests for User Story 1

- [X] T012 [P] [US1] Write failing schema/state-transition/32-row tests in `tests/Elsa/Architecture/GroundworkCoverageLedgerTests.cs`
- [X] T013 [P] [US1] Write failing exact baseline test-case identity continuity tests in `tests/Elsa/Architecture/GroundworkBehavioralBaselineTests.cs`
- [X] T014 [P] [US1] Write failing contract-registration-manifest reconciliation and #644/#660 ownership tests in `tests/Elsa/Architecture/GroundworkPersistenceCoverageTests.cs`

### Implementation for User Story 1

- [X] T015 [US1] Implement typed ledger loading, JSON Schema validation, state-transition rules, and evidence completeness checks in `tests/Elsa/Architecture/GroundworkCoverageLedgerValidator.cs`
- [X] T016 [US1] Implement immutable-baseline test discovery and removal-approval reconciliation in `tests/Elsa/Architecture/GroundworkBehavioralBaselineScanner.cs` and `specs/094-harden-groundwork-stores/research.md`
- [X] T017 [US1] Extend core-dependency and EF-surface ratchets for every in-scope family in `tests/Elsa/Architecture/ArchitectureGuardTests.cs`, `tests/Elsa/Architecture/EfCoreSurfaceRatchetTests.cs`, and `tests/Elsa/Architecture/Baselines/ef-core-surface.json`
- [X] T018 [US1] Wire ledger and ratchet gates into CI and record their evidence contract in `.github/workflows/ci.yml` and `specs/094-harden-groundwork-stores/contracts/coverage-ledger.md`

**Checkpoint**: Deleting or silently de-scoping any baseline obligation fails CI. This is the minimum independently useful increment.

---

## Phase 4: User Story 2 — Compose Selected Stores In A Real Host (Priority: P1)

**Goal**: Replace the hard-coded partial union with one host-selected composition used by runtime and deployment tooling.

**Independent Test**: Compose each required feature combination, execute one public round trip per family, reopen the same database, and prove missing/duplicate/incompatible declarations fail before public stores resolve.

### Tests for User Story 2

- [X] T019 [P] [US2] Write failing direct branch-coverage tests for manifest sources, snapshot/context/handler/validator, deterministic ordering, duplicate/missing owners, active-path capability derivation, naming-policy propagation, and the compatibility façade in `tests/Elsa/Persistence/Groundwork/Composition/Tests/GroundworkStorageCompositionTests.cs`, `tests/Elsa/Persistence/Groundwork/Composition/Tests/Elsa.Persistence.Groundwork.Composition.Tests.csproj`, and `tests/Elsa/Persistence/Groundwork/Conformance/Tests/ProviderCapabilityContractTests.cs`
- [X] T020 [P] [US2] Write failing runtime-versus-CLI target-fingerprint, host naming-policy transformation, collision, and validate/plan/status/apply lifecycle tests in `tests/Elsa/Persistence/Groundwork/Conformance/Tests/GroundworkSchemaCliContractTests.cs`
- [X] T021 [P] [US2] Write failing all-family selection/dispose/reopen tests and direct SQL Server/MongoDB shell-feature registration tests in `tests/Elsa/Persistence/Groundwork/UnifiedHost/Tests/UnifiedGroundworkHostTests.cs`, `tests/Elsa/Persistence/Groundwork/SqlServer/Tests/SqlServerGroundworkPersistenceRegistrationTests.cs`, and `tests/Elsa/Persistence/Groundwork/MongoDb/Tests/MongoDbGroundworkPersistenceRegistrationTests.cs`

### Implementation for User Story 2

- [X] T022 [US2] Define manifest-source declarations, the host-selected naming-policy identity/transformer, and immutable composition/resolved-name snapshots in `src/Elsa/Persistence/Groundwork/Composition/IGroundworkStorageManifestSource.cs`, `src/Elsa/Persistence/Groundwork/Composition/GroundworkStorageNamingPolicyOptions.cs`, and `src/Elsa/Persistence/Groundwork/Composition/GroundworkStorageCompositionSnapshot.cs`
- [X] T023 [US2] Implement the named contribution event and mutable context in the acyclic contract project, plus the single aggregating handler in Unified, in `src/Elsa/Persistence/Groundwork/Composition/GroundworkStorageComposing.cs`, `src/Elsa/Persistence/Groundwork/Composition/GroundworkStorageCompositionContext.cs`, and `src/Elsa/Persistence/Groundwork/Unified/Composition/GroundworkStorageCompositionHandler.cs`
- [X] T024 [US2] Implement host transformation then provider renderer/normalizer ordering, resolved-name/collision evidence, active-path capability derivation, required-store, route, topology, and deterministic fingerprint validation in `src/Elsa/Persistence/Groundwork/Unified/Composition/GroundworkPhysicalNameResolver.cs`, `src/Elsa/Persistence/Groundwork/Unified/Composition/GroundworkProviderCapabilitySnapshot.cs`, and `src/Elsa/Persistence/Groundwork/Unified/Composition/GroundworkStorageCompositionValidator.cs`
- [X] T025 [US2] Implement and register Runtime, IAM, Secrets, Distributed Runtime, Workflows Design, Activities Design, and Publishing manifest sources, adding only acyclic Composition project references, in `src/Elsa/Persistence/Groundwork/RuntimeGroundworkStorageManifestSource.cs`, `src/Elsa/Persistence/Groundwork/DependencyInjection/GroundworkRuntimeStoreRegistration.cs`, `src/Elsa/Persistence/Groundwork/Elsa.Persistence.Groundwork.csproj`, `src/Elsa/Foundation/Identity/Persistence/Groundwork/IdentityGroundworkStorageManifestSource.cs`, `src/Elsa/Foundation/Identity/Persistence/Groundwork/DependencyInjection/GroundworkIdentityStoresRegistration.cs`, `src/Elsa/Foundation/Identity/Persistence/Groundwork/Elsa.Foundation.Identity.Persistence.Groundwork.csproj`, `src/Elsa/Secrets/Persistence/Groundwork/SecretsGroundworkStorageManifestSource.cs`, `src/Elsa/Secrets/Persistence/Groundwork/DependencyInjection/GroundworkSecretsStoreRegistration.cs`, `src/Elsa/Secrets/Persistence/Groundwork/Elsa.Secrets.Persistence.Groundwork.csproj`, `src/Elsa/Workflows/Runtime/Distributed/Persistence/Groundwork/DistributedGroundworkStorageManifestSource.cs`, `src/Elsa/Workflows/Runtime/Distributed/Persistence/Groundwork/DependencyInjection/GroundworkDistributedStoresRegistration.cs`, `src/Elsa/Workflows/Runtime/Distributed/Persistence/Groundwork/Elsa.Workflows.Runtime.Distributed.Persistence.Groundwork.csproj`, `src/Elsa/Workflows/Design/Persistence/Groundwork/WorkflowsDesignGroundworkStorageManifestSource.cs`, `src/Elsa/Workflows/Design/Persistence/Groundwork/DependencyInjection/GroundworkWorkflowsDesignStoreRegistration.cs`, `src/Elsa/Workflows/Design/Persistence/Groundwork/Elsa.Workflows.Design.Persistence.Groundwork.csproj`, `src/Elsa/Activities/Design/Persistence/Groundwork/ActivitiesDesignGroundworkStorageManifestSource.cs`, `src/Elsa/Activities/Design/Persistence/Groundwork/DependencyInjection/GroundworkActivitiesDesignStoreRegistration.cs`, `src/Elsa/Activities/Design/Persistence/Groundwork/Elsa.Activities.Design.Persistence.Groundwork.csproj`, `src/Elsa/Workflows/Publishing/Persistence/Groundwork/PublishingGroundworkStorageManifestSource.cs`, `src/Elsa/Workflows/Publishing/Persistence/Groundwork/DependencyInjection/GroundworkPublishingStoreRegistration.cs`, and `src/Elsa/Workflows/Publishing/Persistence/Groundwork/Elsa.Workflows.Publishing.Persistence.Groundwork.csproj`
- [X] T026 [US2] Convert the static union into a compatibility façade over the immutable snapshot, remove its hard-coded Runtime/Design/Activities/Publishing project references, and reference only the acyclic Composition contract in `src/Elsa/Persistence/Groundwork/Unified/GroundworkUnifiedManifest.cs` and `src/Elsa/Persistence/Groundwork/Unified/Elsa.Persistence.Groundwork.Unified.csproj`
- [X] T027 [US2] Change SQLite and PostgreSQL base/unified initialization to consume the selected schema source in `src/Elsa/Persistence/Groundwork/Sqlite/DependencyInjection/SqliteGroundworkDocumentStoreRegistration.cs`, `src/Elsa/Persistence/Groundwork/Sqlite/Unified/DependencyInjection/GroundworkSqliteUnifiedRegistration.cs`, `src/Elsa/Persistence/Groundwork/PostgreSql/DependencyInjection/PostgreSqlGroundworkDocumentStoreRegistration.cs`, and `src/Elsa/Persistence/Groundwork/PostgreSql/Unified/DependencyInjection/GroundworkPostgreSqlUnifiedRegistration.cs`
- [X] T028 [P] [US2] Implement the SQL Server base/unified materialization leaf in `src/Elsa/Persistence/Groundwork/SqlServer/DependencyInjection/SqlServerGroundworkDocumentStoreRegistration.cs`, `src/Elsa/Persistence/Groundwork/SqlServer/SqlServerGroundworkRuntimePersistenceShellFeature.cs`, `src/Elsa/Persistence/Groundwork/SqlServer/Unified/DependencyInjection/GroundworkSqlServerUnifiedRegistration.cs`, and `src/Elsa/Persistence/Groundwork/SqlServer/Unified/SqlServerGroundworkUnifiedPersistenceShellFeature.cs`
- [X] T029 [P] [US2] Implement the MongoDB base/unified materialization leaf in `src/Elsa/Persistence/Groundwork/MongoDb/DependencyInjection/MongoDbGroundworkDocumentStoreRegistration.cs`, `src/Elsa/Persistence/Groundwork/MongoDb/MongoDbGroundworkRuntimePersistenceShellFeature.cs`, `src/Elsa/Persistence/Groundwork/MongoDb/Unified/DependencyInjection/GroundworkMongoDbUnifiedRegistration.cs`, and `src/Elsa/Persistence/Groundwork/MongoDb/Unified/MongoDbGroundworkUnifiedPersistenceShellFeature.cs`
- [X] T030 [US2] Expose the same concrete schema source type, deterministic naming-policy definition, resolved-name evidence, and target fingerprint to runtime and `Groundwork.Tool`, then document validate/plan/status/apply exit behavior in `src/Elsa/Persistence/Groundwork/Unified/Composition/GroundworkPhysicalSchemaManifestSource.cs` and `specs/094-harden-groundwork-stores/quickstart.md`
- [X] T031 [US2] Record host-selection/composition evidence for row group ALL32 without overriding #644/#660 authority in `specs/094-harden-groundwork-stores/coverage-ledger.json` and `specs/094-harden-groundwork-stores/contracts/storage-composition.md`

**Checkpoint**: One coherent target backs every selected feature and schema tooling; unsupported compositions cannot become ready.

---

## Phase 5: User Story 4 — Enforce Storage Scope (Priority: P1)

**Goal**: Make tenant/global storage scope and ordinary/privileged operation access independent, immutable persistence-boundary concerns.

**Independent Test**: Use equal identifiers across tenants and global storage, then exercise load/query/write/delete/UoW/cancel/dispose/reuse paths and verify zero cross-scope disclosure or state leakage on all providers.

### Tests for User Story 4

- [X] T032 [P] [US4] Write failing provider-neutral scope/access/default-scope tests in `tests/Elsa/Workflows/Runtime/Tests/PersistenceAccessContextTests.cs`
- [X] T033 [P] [US4] Write failing Groundwork wrong-scope/mixed-UoW/cancellation/reuse contract tests plus direct session-factory/mapper/privileged-recorder branch tests in `tests/Elsa/Persistence/Groundwork/Conformance/Tests/StorageScopeContractTests.cs`, `tests/Elsa/Persistence/Groundwork/Tests/GroundworkStoreSessionFactoryTests.cs`, and `tests/Elsa/Persistence/Groundwork/Tests/GroundworkPrivilegedAccessRecorderTests.cs`
- [X] T034 [P] [US4] Write failing logic-bearing service lifetime, registration, and cross-request leakage tests in `tests/Elsa/Architecture/GroundworkPersistenceLifetimeTests.cs` and `tests/Elsa/Persistence/Groundwork/Tests/GroundworkRuntimePersistenceRegistrationTests.cs`

### Implementation for User Story 4

- [X] T035 [US4] Add provider-neutral scope, access-policy, named-purpose, and current-context contracts in `src/Elsa/Workflows/Runtime/Core/Models/PersistenceScope.cs`, `src/Elsa/Workflows/Runtime/Core/Models/PersistenceAccessPolicy.cs`, `src/Elsa/Workflows/Runtime/Core/Models/PersistenceAccessContext.cs`, and `src/Elsa/Workflows/Runtime/Core/Contracts/IPersistenceAccessContextAccessor.cs`
- [X] T036 [US4] Register scoped access-context selection with the nonblank single-tenant default `default` in `src/Elsa/Workflows/Runtime/Core/Extensions/PersistenceCoreServiceCollectionExtensions.cs`
- [X] T037 [US4] Map Elsa scope/access to immutable Groundwork sessions in `src/Elsa/Persistence/Groundwork/Scoping/IGroundworkStoreSessionFactory.cs`, `src/Elsa/Persistence/Groundwork/Scoping/GroundworkStoreSessionFactory.cs`, and `src/Elsa/Persistence/Groundwork/Scoping/GroundworkPersistenceAccessMapper.cs`
- [X] T038 [US4] Retire the application-wide global store seam and make provider initializers own static factories in `src/Elsa/Persistence/Groundwork/GroundworkDocumentStoreHolder.cs`, `src/Elsa/Persistence/Groundwork/Sqlite/SqliteGroundworkDocumentStoreInitializer.cs`, and `src/Elsa/Persistence/Groundwork/PostgreSql/PostgreSqlGroundworkDocumentStoreInitializer.cs`
- [X] T039 [US4] Make runtime adapters acquire per-operation/per-UoW sessions through `src/Elsa/Persistence/Groundwork/Stores/GroundworkScopedDocumentStore.cs` and `src/Elsa/Persistence/Groundwork/DependencyInjection/GroundworkRuntimeStoreRegistration.cs`
- [X] T040 [P] [US4] Make IAM and secrets adapters scoped session consumers in `src/Elsa/Foundation/Identity/Persistence/Groundwork/DependencyInjection/GroundworkIdentityStoresRegistration.cs` and `src/Elsa/Secrets/Persistence/Groundwork/DependencyInjection/GroundworkSecretsStoreRegistration.cs`
- [X] T041 [P] [US4] Make distributed adapters scoped session consumers in `src/Elsa/Workflows/Runtime/Distributed/Persistence/Groundwork/DependencyInjection/GroundworkDistributedStoresRegistration.cs`
- [X] T042 [US4] Reconcile tenant/global scope and ordinary/privileged access for row group ALL32 and the selected Workflows Design, Activities Design, and Publishing manifest units in `src/Elsa/Persistence/Groundwork/ElsaRuntimeStorageManifest.cs`, `src/Elsa/Foundation/Identity/Persistence/Groundwork/IdentityStorageManifest.cs`, `src/Elsa/Secrets/Persistence/Groundwork/SecretsStorageManifest.cs`, `src/Elsa/Workflows/Runtime/Distributed/Persistence/Groundwork/DistributedGroundworkStorageManifest.cs`, `src/Elsa/Workflows/Design/Persistence/Groundwork/WorkflowsDesignStorageManifest.cs`, `src/Elsa/Activities/Design/Persistence/Groundwork/ActivitiesDesignStorageManifest.cs`, `src/Elsa/Workflows/Publishing/Persistence/Groundwork/PublishingGroundworkStorageManifest.cs`, and `specs/094-harden-groundwork-stores/coverage-ledger.json`, deferring external classification only to #644/#660 and preserving ALL32 as the runtime/IAM/secrets/distributed denominator; verify the three selected non-ALL32 families through `Host_registers_one_document_store_shared_by_every_lane` and `Selected_seven_family_composition_survives_dispose_and_reopen` in `tests/Elsa/Persistence/Groundwork/UnifiedHost/Tests/UnifiedGroundworkHostTests.cs`
- [X] T043 [US4] Emit bounded privilege acquisition/outcome records without tenant metric labels in `src/Elsa/Persistence/Groundwork/Scoping/GroundworkPrivilegedAccessRecorder.cs`

**Checkpoint**: Scope is enforced before provider I/O and all logic-bearing persistence registrations are scoped or have a tested documented exception.

---

## Phase 6: User Story 3 — Preserve Atomic Runtime Behavior (Priority: P1)

**Goal**: Replace process-local/read-check-write correctness with provider-atomic fencing, checkpoint, claim, retry, completion, and schedule transitions.

**Independent Test**: Race independent clients through ownership, checkpoint, recovery, queue, outbox, timer, recurring schedule, incident, hold, projection, and poison failure windows, then reopen/restart and verify one allowed converged outcome.

### Tests for User Story 3

- [x] T044 [P] [US3] Write failing ownership allocation, heartbeat/release, stale-fence, and checkpoint atomicity tests in `tests/Elsa/Persistence/Groundwork/Conformance/Tests/RuntimeFenceContractTests.cs`
- [x] T045 [P] [US3] Write failing outbox/queue claim-token, expiry, retry, stale-ack, and poison-restart contracts plus direct poison-store branch tests in `tests/Elsa/Persistence/Groundwork/Conformance/Tests/RuntimeDeliveryContractTests.cs` and `tests/Elsa/Persistence/Groundwork/Tests/GroundworkWorkflowSchedulerPoisonStoreTests.cs`
- [x] T046 [P] [US3] Write failing timer/schedule/incident/hold/publication-projection race and restart tests in `tests/Elsa/Persistence/Groundwork/Conformance/Tests/RuntimeTransitionContractTests.cs`
- [x] T047 [P] [US3] Extend core ownership/recovery baselines and write failing provider-bounded plus direct recovery-scanner branch tests before T056 in `tests/Elsa/Workflows/Runtime/Tests/RuntimeExecutionOwnershipTests.cs`, `tests/Elsa/Workflows/Runtime/Tests/RuntimeRecoveryScannerTests.cs`, `tests/Elsa/Persistence/Groundwork/Conformance/Tests/RuntimeRecoveryBoundedRouteTests.cs`, and `tests/Elsa/Persistence/Groundwork/Tests/GroundworkRuntimeRecoveryScannerTests.cs`

### Implementation for User Story 3

- [x] T048 [US3] Express fence, claim token, visibility deadline, expected revision, stale acknowledgement, and replay outcomes in `src/Elsa/Workflows/Runtime/Core/Contracts/IRuntimeExecutionOwnershipService.cs`, `src/Elsa/Workflows/Runtime/Core/Contracts/IRuntimeCheckpointCommitStore.cs`, `src/Elsa/Workflows/Runtime/Core/Contracts/IRuntimePostCommitOutboxStore.cs`, and `src/Elsa/Workflows/Runtime/Core/Contracts/IWorkflowSchedulerWorkQueue.cs`
- [x] T049 [US3] Implement provider-atomic ownership allocation/heartbeat/release through liveness state in `src/Elsa/Workflows/Runtime/Services/RuntimeExecutionOwnershipService.cs` and `src/Elsa/Persistence/Groundwork/Stores/GroundworkExecutionLivenessStateStore.cs`
- [X] T050 [US3] Make fence validation, checkpoint state, outbox state, and the idempotency marker one unit of work; execute and record the complete SQLite, SQL Server, PostgreSQL, and MongoDB matrix for the named checkpoint/fence scenarios in `src/Elsa/Persistence/Groundwork/Stores/GroundworkRuntimeCheckpointWriter.cs` and `specs/094-harden-groundwork-stores/coverage-ledger.json`, without advancing any row beyond the evidence actually recorded; T093 retains complete B5-FENCE-CHECKPOINT row-group closure
- [x] T051 [US3] Implement bounded atomic outbox claim/retry/complete transitions in `src/Elsa/Persistence/Groundwork/Stores/GroundworkRuntimePostCommitOutboxStore.cs`
- [x] T052 [US3] Implement bounded atomic scheduler-work claim/retry/complete transitions in `src/Elsa/Persistence/Groundwork/Stores/GroundworkWorkflowSchedulerWorkQueue.cs`
- [x] T053 [US3] Add the missing durable poison implementation and manifest unit in `src/Elsa/Persistence/Groundwork/Stores/GroundworkWorkflowSchedulerPoisonStore.cs` and `src/Elsa/Persistence/Groundwork/ElsaRuntimeStorageManifest.cs`
- [x] T054 [P] [US3] Make timer and recurring-schedule create/advance transitions conditional in `src/Elsa/Persistence/Groundwork/Stores/GroundworkDurableTimerStore.cs` and `src/Elsa/Persistence/Groundwork/Stores/GroundworkRecurringTriggerScheduleStore.cs`
- [x] T055 [P] [US3] Make incident, hold, and publication-projection create/advance transitions conditional in `src/Elsa/Persistence/Groundwork/Stores/GroundworkIncidentStateStore.cs`, `src/Elsa/Persistence/Groundwork/Stores/GroundworkWorkflowHoldStateStore.cs`, and `src/Elsa/Persistence/Groundwork/Stores/GroundworkWorkflowTriggerBindingStore.cs`
- [x] T056 [US3] Replace in-memory recovery scanning with a Groundwork bounded liveness route in `src/Elsa/Persistence/Groundwork/Stores/GroundworkRuntimeRecoveryScanner.cs`, register it in `src/Elsa/Persistence/Groundwork/DependencyInjection/GroundworkRuntimeStoreRegistration.cs`, and retire production use of `src/Elsa/Workflows/Runtime/Services/InMemoryRuntimeRecoveryScanner.cs`
- [x] T057 [US3] Run named before/during/after-decision failure windows for row group B8-OPERATIONAL-RUNTIME in `tests/Elsa/Persistence/Groundwork/Conformance/Tests/RuntimeFailureWindowTests.cs`
- [ ] T058 [US3] Advance the complete provider matrix only for row group B8-OPERATIONAL-RUNTIME in `specs/094-harden-groundwork-stores/coverage-ledger.json`

**Checkpoint**: No correctness claim depends on an adapter instance lock, and stale owners/claimants cannot commit or acknowledge successor-owned state.

---

## Phase 7: User Story 5 — Preserve IAM And Secrets Concurrency (Priority: P1)

**Goal**: Use one identity authority and give every IAM/secrets contract durable create-only, revision, scope, bounded-query, and restart behavior.

**Independent Test**: Race duplicate creation and stale update/delete through independent clients, collide identities across tenants, reopen the database, and prove #644 remains the only user/role/external-login authority.

### Tests for User Story 5

- [x] T059 [P] [US5] Write failing IAM authority/uniqueness/revision/reopen contracts and direct branch tests for every new IAM store in `tests/Elsa/Foundation/Identity/Persistence/Groundwork/Tests/IdentityGroundworkConformanceTests.cs`, `tests/Elsa/Foundation/Identity/Persistence/Groundwork/Tests/GroundworkApplicationStoreTests.cs`, `tests/Elsa/Foundation/Identity/Persistence/Groundwork/Tests/GroundworkCredentialStoreTests.cs`, `tests/Elsa/Foundation/Identity/Persistence/Groundwork/Tests/GroundworkClaimMappingStoreTests.cs`, and `tests/Elsa/Foundation/Identity/Persistence/Groundwork/Tests/GroundworkProviderConfigurationStoreTests.cs`
- [x] T060 [P] [US5] Write failing secret create-only, revision, bounded-list, tenant-collision, and reopen tests in `tests/Elsa/Secrets/Tests/GroundworkSecretRepositoryTests.cs` and `tests/Elsa/Secrets/Tests/SecretRepositoryContractTests.cs`

### Implementation for User Story 5

- [x] T061 [US5] Add provider-neutral revision/conflict and bounded-page outcomes to IAM contracts in `src/Elsa/Foundation/Identity/Abstractions/Iam/IamContracts.cs`
- [x] T062 [US5] Adapt user, role, and external identity operations to #644 without parallel documents in `src/Elsa/Foundation/Identity/Persistence/Groundwork/Stores/GroundworkUserStore.cs`, `src/Elsa/Foundation/Identity/Persistence/Groundwork/Stores/GroundworkRoleStore.cs`, and `src/Elsa/Foundation/Identity/Persistence/Groundwork/Stores/GroundworkExternalIdentityStore.cs`
- [X] T063 [P] [US5] Implement application and credential stores in `src/Elsa/Foundation/Identity/Persistence/Groundwork/Stores/GroundworkApplicationStore.cs` and `src/Elsa/Foundation/Identity/Persistence/Groundwork/Stores/GroundworkCredentialStore.cs`
- [x] T064 [P] [US5] Implement claim-mapping and provider-configuration stores with separate tenant/global access in `src/Elsa/Foundation/Identity/Persistence/Groundwork/Stores/GroundworkClaimMappingStore.cs` and `src/Elsa/Foundation/Identity/Persistence/Groundwork/Stores/GroundworkProviderConfigurationStore.cs`
- [x] T065 [US5] Make tenant membership create/update/delete revision-aware in `src/Elsa/Foundation/Identity/Persistence/Groundwork/Stores/GroundworkTenantMembershipStore.cs`
- [x] T066 [US5] Add all IAM document kinds, physical uniqueness, and bounded lookup routes in `src/Elsa/Foundation/Identity/Persistence/Groundwork/IdentityStorageManifest.cs`
- [x] T067 [US5] Add secret revision/page semantics and preserve provider-neutral core boundaries in `src/Elsa/Secrets/Core/Contracts/ISecretRepository.cs` and `src/Elsa/Secrets/Core/Models/SecretModels.cs`
- [x] T068 [US5] Implement create-only, expected-version update/delete, and bounded deterministic list behavior in `src/Elsa/Secrets/Persistence/Groundwork/Stores/GroundworkSecretRepository.cs` and `src/Elsa/Secrets/Persistence/Groundwork/SecretsStorageManifest.cs`
- [ ] T069 [US5] Run the complete provider matrix for row group B6-IAM-SECRETS, consuming #644 evidence for its authority rows, in `tests/Elsa/Persistence/Groundwork/Conformance/Tests/IamSecretsProviderContractTests.cs` and `specs/094-harden-groundwork-stores/coverage-ledger.json`

**Checkpoint**: IAM and secrets have one authoritative winner under races, stable stale-revision outcomes, bounded queries, and persistent scope/uniqueness evidence.

---

## Phase 8: User Story 6 — Preserve Distributed Takeover And Delivery (Priority: P1)

**Goal**: Make placement and ordered command delivery bounded, provider-atomic, restart-safe, and subordinate to the execution fence.

**Independent Test**: Race independent nodes through placement claim/renew/takeover/release and command send/lease/re-lease/acknowledge across failure and restart, then prove stale actors cannot change successor-owned state.

### Tests for User Story 6

- [x] T070 [P] [US6] Write failing placement CAS/takeover/stale-release tests in `tests/Elsa/Workflows/Runtime/Distributed/Persistence/Groundwork/Tests/ExecutionPlacementStoreContractTests.cs`
- [x] T071 [P] [US6] Write failing stream-sequence, bounded visibility lease, expiry, stale-ack, and restart tests in `tests/Elsa/Workflows/Runtime/Distributed/Persistence/Groundwork/Tests/ExecutionCommandTransportContractTests.cs`

### Implementation for User Story 6

- [x] T072 [US6] Add expected-version placement and tokenized command outcomes to `src/Elsa/Workflows/Runtime/Distributed/Contracts/IExecutionPlacementStore.cs`, `src/Elsa/Workflows/Runtime/Distributed/Contracts/IExecutionCommandTransport.cs`, and `src/Elsa/Workflows/Runtime/Distributed/Models/ExecutionCommandTransportItem.cs`
- [x] T073 [US6] Implement provider-atomic placement claim/renew/takeover/release in `src/Elsa/Workflows/Runtime/Distributed/Persistence/Groundwork/Stores/GroundworkExecutionPlacementStore.cs`
- [x] T074 [US6] Implement create-only command IDs, a per-execution CAS stream head, bounded visibility claims, and tokenized acknowledgement in `src/Elsa/Workflows/Runtime/Distributed/Persistence/Groundwork/Stores/GroundworkExecutionCommandTransport.cs`
- [x] T075 [US6] Add stream-head/lease fields, routes, and executable capability prerequisites in `src/Elsa/Workflows/Runtime/Distributed/Persistence/Groundwork/DistributedGroundworkDocuments.cs` and `src/Elsa/Workflows/Runtime/Distributed/Persistence/Groundwork/DistributedGroundworkStorageManifest.cs`
- [ ] T076 [US6] Prove placement cannot override stale execution fencing and advance the complete provider matrix for row group B9-DISTRIBUTED in `tests/Elsa/Workflows/Runtime/Distributed/Tests/TwoNodeAcceptanceTests.cs` and `specs/094-harden-groundwork-stores/coverage-ledger.json`

**Checkpoint**: Exactly one current placement/visibility owner exists, stream order is durable without scan-max allocation, and checkpoint fencing remains final commit authority.

---

## Phase 9: User Story 7 — Keep Scale-Bearing Queries Bounded (Priority: P1)

**Goal**: Compile every scale-bearing Elsa query into a finite provider route and reject unsupported routes at startup.

**Independent Test**: Seed more candidates than the requested window for every declared query, compare exact ordered results across providers, and inspect native evidence proving scope/predicate/order/limit execute before materialization.

### Tests for User Story 7

- [x] T077 [P] [US7] Write failing runtime result-equivalence and bounded-materialization tests in `tests/Elsa/Persistence/Groundwork/Conformance/Tests/RuntimeBoundedQueryContractTests.cs`
- [x] T078 [P] [US7] Write failing IAM/secrets/distributed bounded-route tests in `tests/Elsa/Persistence/Groundwork/Conformance/Tests/FoundationBoundedQueryContractTests.cs`

### Implementation for User Story 7

- [x] T079 [US7] Declare stable route identities, finite maxima, deterministic ordering, continuation, and required physical fields in `src/Elsa/Persistence/Groundwork/Querying/ElsaGroundworkQueryRoutes.cs` and `src/Elsa/Persistence/Groundwork/ElsaRuntimeStorageManifest.cs`
- [x] T080 [US7] Replace bookmark, trigger-binding, source-reference, recovery, timer, schedule, outbox, and queue client evaluation in `src/Elsa/Persistence/Groundwork/Stores/GroundworkBookmarkStateStore.cs`, `src/Elsa/Persistence/Groundwork/Stores/GroundworkWorkflowTriggerBindingStore.cs`, `src/Elsa/Persistence/Groundwork/Stores/GroundworkWorkflowExecutableSourceReferenceStore.cs`, `src/Elsa/Persistence/Groundwork/Stores/GroundworkWorkflowExecutionStateStore.cs`, `src/Elsa/Persistence/Groundwork/Stores/GroundworkExecutionLivenessStateStore.cs`, `src/Elsa/Persistence/Groundwork/Stores/GroundworkDurableTimerStore.cs`, `src/Elsa/Persistence/Groundwork/Stores/GroundworkRecurringTriggerScheduleStore.cs`, `src/Elsa/Persistence/Groundwork/Stores/GroundworkRuntimePostCommitOutboxStore.cs`, and `src/Elsa/Persistence/Groundwork/Stores/GroundworkWorkflowSchedulerWorkQueue.cs`
- [x] T081 [US7] Replace provider-specific execution-history paging with the common compiled route and retire the fallback seam in `src/Elsa/Persistence/Groundwork/ExecutionHistory/RelationalGroundworkWorkflowExecutionStatePageQuery.cs` and `src/Elsa/Persistence/Groundwork/ExecutionHistory/IGroundworkWorkflowExecutionStatePageQuery.cs`
- [x] T082 [P] [US7] Bind IAM and secrets lookups/lists to compiled routes in `src/Elsa/Foundation/Identity/Persistence/Groundwork/IdentityStorageManifest.cs` and `src/Elsa/Secrets/Persistence/Groundwork/SecretsStorageManifest.cs`
- [x] T083 [P] [US7] Bind placement and command retrieval to compiled routes in `src/Elsa/Workflows/Runtime/Distributed/Persistence/Groundwork/DistributedGroundworkStorageManifest.cs`
- [x] T084 [US7] Capture SQLite, SQL Server, PostgreSQL, and MongoDB native bounded-route evidence in `tests/Elsa/Persistence/Groundwork/Conformance/Tests/ProviderNativePlanTests.cs`
- [x] T085 [US7] Reject missing/unsupported routes without fallback and advance the complete provider route evidence for row group B7-BOUNDED-QUERIES in `src/Elsa/Persistence/Groundwork/Unified/Composition/GroundworkStorageCompositionValidator.cs` and `specs/094-harden-groundwork-stores/coverage-ledger.json`

**Checkpoint**: Every scale-bearing operation has a provider-bound finite route and no production-enabled load-all fallback.

---

## Phase 10: User Story 8 — Prove Provider Equivalence And Recovery (Priority: P1)

**Goal**: Run one public-contract suite against real SQLite, SQL Server, PostgreSQL, and MongoDB with equivalent observable outcomes.

**Independent Test**: Execute the complete scenario catalog on all four provider drivers, including independent-client races, cancellation, dispose/reopen, process restart, failure injection, capability derivation, and native bounded-route evidence.

### Tests and Integration for User Story 8

- [x] T086 [US8] Define the complete provider-independent scenario catalog and result-digest rules in `tests/Elsa/Persistence/Groundwork/Conformance/Tests/GroundworkStoreScenarioCatalog.cs`
- [x] T087 [P] [US8] Run runtime ordinary-document/checkpoint/operational scenarios across all drivers in `tests/Elsa/Persistence/Groundwork/Conformance/Tests/RuntimeProviderContractTests.cs`
- [x] T088 [P] [US8] Run IAM and secrets scenarios across all drivers in `tests/Elsa/Persistence/Groundwork/Conformance/Tests/IamSecretsProviderContractTests.cs`
- [x] T089 [P] [US8] Run distributed placement/transport scenarios across all drivers in `tests/Elsa/Persistence/Groundwork/Conformance/Tests/DistributedProviderContractTests.cs`
- [x] T090 [US8] Run cancellation, declared failure-window, disposal/reopen, process-restart, and topology rejection scenarios in `tests/Elsa/Persistence/Groundwork/Conformance/Tests/ProviderRecoveryContractTests.cs`
- [x] T091 [US8] Extract shared observable scenarios for the temporary EF oracle without adding EF surface in `tests/Elsa/Persistence/Groundwork/Conformance/Tests/TemporaryEfOracleContractTests.cs`
- [x] T092 [US8] Run evidence-only integration scenarios proving capability claims come only from selected passing active paths in `tests/Elsa/Persistence/Groundwork/Conformance/Tests/ProviderCapabilityContractTests.cs`
- [ ] T093 [US8] Record provider/topology/package/fingerprint/result evidence for row groups B5-FENCE-CHECKPOINT, B6-IAM-SECRETS, B7-BOUNDED-QUERIES, B8-OPERATIONAL-RUNTIME, and B9-DISTRIBUTED, consume linked #660 evidence, and advance only complete rows in `specs/094-harden-groundwork-stores/coverage-ledger.json`

**Checkpoint**: Provider-specific mechanics differ, but public results, conflict classifications, restart outcomes, and readiness diagnostics do not.

---

## Phase 11: User Story 9 — Supply And Consume Performance Evidence (Priority: P2)

**Goal**: Give #646 versioned correctness-proven workloads and consume reproducible Pass/Redesign/Blocked physical-shape verdicts.

**Independent Test**: Validate every FR-030 workload definition and correctness digest, ingest representative #646 verdict fixtures, and prove missing/blocked/redesign verdicts prevent row readiness.

### Tests for User Story 9

- [x] T094 [P] [US9] Write failing workload-schema, ledger-mapping, correctness-digest, and verdict-readiness tests in `tests/Elsa/Architecture/GroundworkPerformanceHandoffTests.cs`

### Implementation for User Story 9

- [x] T095 [US9] Define the versioned workload/handoff schema in `specs/094-harden-groundwork-stores/contracts/performance-workload.schema.json`
- [x] T096 [P] [US9] Define checkpoint/bookmark/trigger/recovery/queue/outbox/timer/schedule workloads in `specs/094-harden-groundwork-stores/workloads/runtime.json`
- [x] T097 [P] [US9] Define IAM normalized lookup/update and secret create/read/list workloads in `specs/094-harden-groundwork-stores/workloads/iam-secrets.json`
- [x] T098 [P] [US9] Define placement takeover and command send/lease/ack workloads in `specs/094-harden-groundwork-stores/workloads/distributed-runtime.json`
- [x] T099 [US9] Produce deterministic correctness digests and provider prerequisites through public contracts in `tests/Elsa/Persistence/Groundwork/Conformance/Tests/PerformanceWorkloadCorrectnessTests.cs`
- [ ] T100 [US9] Consume #646 evidence/accepted-shape verdicts for every current ledger row (ALL32 + DIAGNOSTICS2) and block missing/Redesign/Blocked lanes in `specs/094-harden-groundwork-stores/coverage-ledger.json` and `specs/094-harden-groundwork-stores/contracts/performance-handoff.md`

**Checkpoint**: Every hot/ordinary lane has a reproducible #646 verdict; redesign loops return to the owning implementation phase.

---

## Phase 12: Polish & Cross-Cutting Readiness

**Purpose**: Close combined-host, documentation, CLI, architecture, and evidence gates without expanding the EF implementation surface.

- [x] T101 Run the complete production-shaped host on all providers, add the SQL Server/MongoDB host projects to `Elsa.Server.slnx`, and implement their fixtures/direct host tests in `tests/Elsa/Persistence/Groundwork/SqlServer/UnifiedHost/Tests/Elsa.Persistence.Groundwork.SqlServer.UnifiedHost.Tests.csproj`, `tests/Elsa/Persistence/Groundwork/SqlServer/UnifiedHost/Tests/SqlServerContainerFixture.cs`, `tests/Elsa/Persistence/Groundwork/SqlServer/UnifiedHost/Tests/SqlServerUnifiedGroundworkHostTests.cs`, `tests/Elsa/Persistence/Groundwork/MongoDb/UnifiedHost/Tests/Elsa.Persistence.Groundwork.MongoDb.UnifiedHost.Tests.csproj`, `tests/Elsa/Persistence/Groundwork/MongoDb/UnifiedHost/Tests/MongoDbReplicaSetFixture.cs`, and `tests/Elsa/Persistence/Groundwork/MongoDb/UnifiedHost/Tests/MongoDbUnifiedGroundworkHostTests.cs`
- [x] T102 [P] Refresh Groundwork extension points and operational prerequisites in `src/Elsa/Persistence/Groundwork/EXTENSION_POINTS.md`, `src/Elsa/Foundation/Identity/Persistence/Groundwork/EXTENSION_POINTS.md`, `src/Elsa/Secrets/EXTENSION_POINTS.md`, `src/Elsa/Workflows/Runtime/Distributed/Persistence/Groundwork/EXTENSION_POINTS.md`, and `specs/094-harden-groundwork-stores/quickstart.md`
- [x] T103 [P] Update the decision map and program goals in `docs/decision-maps/zero-ef-groundwork.md`, `docs/program-goals/groundwork-persistence-readiness.md`, and `docs/program-goals/zero-ef-persistence.md`
- [x] T104 Refresh generated architecture/feature/dependency/test maps with `tools/maps/generate-maps.sh`, `tools/maps/generate-architecture-reference-map.sh`, and `tools/maps/generate-feature-dependency-map.sh`, then review `docs/reports/maps-v2-findings.md`
- [x] T105 Run every command in `specs/094-harden-groundwork-stores/quickstart.md`, audit every exact ALL32 row, close only rows meeting every local or linked-owner gate, and retain incomplete/external rows with explicit blockers in `specs/094-harden-groundwork-stores/coverage-ledger.json`

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)** has no dependency.
- **Foundational provider fixture (Phase 2)** may be developed after Setup in parallel with boundaries 1–2, but it lands after US2 composition and blocks accepted provider evidence.
- **US1 (Phase 3)** depends on Setup; it can finish while the provider fixture is being built.
- **US2 (Phase 4)** depends on Setup and the Groundwork release selected in T001.
- **US4 (Phase 5)** depends on US2's provider-factory/composition seam.
- **US3 fencing/checkpoint tasks T044 and T047–T050 (Phase 6)** depend on US4 and the provider fixture; operational tasks T045–T046 and T051–T058 wait for completed US5 and US7 boundaries.
- **US5 (Phase 7)** depends on US4, the provider fixture, and #644's authoritative adapter seam.
- **US6 (Phase 8)** depends on US3's execution-fence contract, US4, and the provider fixture.
- **US7 (Phase 9)** depends on US2 composition and US4 scoping; domain route groups may proceed after their owning contracts stabilize.
- **US8 (Phase 10)** consumes the completed US3/US5/US6/US7 scenarios and all four provider drivers.
- **US9 (Phase 11)** requires passing correctness/provider gates from US8 and verdicts from #646.
- **Polish/readiness (Phase 12)** depends on every desired story and sibling authority dependency.

### Delivery-Boundary Mapping

1. Coverage/ratchets: T012–T018.
2. Package, composition, and session substrate: T001–T003, T019–T031, T035–T038.
3. Shared provider fixture: T004–T011.
4. Scope adoption: T032–T034, T039–T043.
5. Ownership/checkpoint fencing: T044, T047–T050.
6. IAM/secrets: T059–T069.
7. Bounded queries: T056, T077–T085.
8. Operational runtime stores: T045–T046, T051–T055, T057–T058.
9. Distributed placement/transport: T070–T076.
10. #646 handoff and readiness: T086–T105.

### Parallel Opportunities

- T005–T006 can run in parallel after T004; T007 must then fail for all missing drivers before T008–T011 run in parallel.
- T012–T014 can run in parallel and should all fail before T015–T018.
- T019–T021 can run in parallel; T028 and T029 can run in parallel after T022–T027 stabilize provider materialization.
- T032–T034 can run in parallel; T040 and T041 can run in parallel after T037.
- T044–T047 can run in parallel; T054 and T055 can run in parallel after runtime transition contracts stabilize.
- T059 and T060 can run in parallel; T063 and T064 can run in parallel after T061.
- T070 and T071 can run in parallel before distributed implementation.
- T077 and T078 can run in parallel; T082 and T083 can run in parallel after T079.
- T087–T089 can run in parallel after T086 and their owning story phases pass.
- T096–T098 can run in parallel after T095.

---

## Parallel Execution Examples

### Provider fixture

```text
Task T008: SQLite provider driver
Task T009: SQL Server provider driver
Task T010: PostgreSQL provider driver
Task T011: MongoDB replica-set provider driver
```

### Store-family hardening after composition and scope

```text
Worker A: T044/T047–T050 runtime fencing checkpoint (boundary 5)
Worker B: T059–T069 IAM and secrets, after #644 seam is available
Worker C: T077–T085 bounded queries after composition/scope
Worker D: T045–T046/T051–T058 operational runtime after Workers B/C merge
Worker E: T070–T076 distributed placement and transport after fence and bounded-query contracts merge
```

### Provider equivalence

```text
Task T087: Runtime scenario matrix
Task T088: IAM/secrets scenario matrix
Task T089: Distributed scenario matrix
```

---

## Implementation Strategy

### MVP First

1. Complete T001–T003.
2. Complete T012–T018.
3. Stop and validate that the executable denominator catches omissions, test deletion, authority duplication, core dependency leaks, and EF-surface growth.
4. Merge the ratchet checkpoint before implementation branches advance ledger rows.

### Incremental Delivery

1. Land one Groundwork release and shared provider fixture.
2. Land truthful host composition and immutable scoped session acquisition.
3. Land runtime fencing/checkpoint correctness before distributed takeover depends on it.
4. Land IAM/secrets as boundary 6 while bounded-query work develops independently.
5. Merge bounded queries as boundary 7, then operational runtime as boundary 8 and distributed placement/transport as boundary 9.
6. Run the complete provider matrix, hand correctness-proven workloads to #646, consume verdicts, and close readiness.

### Agent Handoff Rule

Each implementation issue/branch must name:

- the task IDs and exact coverage-ledger row IDs it owns;
- dependencies and immutable baseline commit;
- files it may change and sibling branches likely to conflict;
- tests that must fail first and the narrowest provider matrix required before handoff;
- evidence links and ledger transitions it is authorized to make;
- an independent review checkpoint before its local commit/PR is marked ready.

## Notes

- `[P]` means separate-file parallelism only; shared manifests, contracts, project files, and the ledger remain serialized integration points.
- Tests precede implementation and must fail for the targeted missing behavior, not for infrastructure setup.
- Existing tests are never removed without an entry in the plan's test-removal approval ledger.
- EF is a temporary oracle only; no task authorizes new EF packages, migrations, providers, or behavior.
- Commit each delivery boundary or independently reviewed sub-boundary with a useful message before pushing its Model B branch.
