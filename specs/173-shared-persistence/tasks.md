---

description: "Reviewed task list for shared persistence resources"
---

# Tasks: Shared persistence resources

**Input**: Design documents from `/specs/173-shared-persistence/`

**Prerequisites**: `plan.md`, `spec.md`, `research.md`, `data-model.md`, `contracts/`, and `quickstart.md`.

**Status**: Integrated design review approved on 2026-09-23. #1967 publication completed through PR #1973; implementation is active. One active delivery issue at a time: #1968 owns US1, US2 and US4; #1969 owns US3 after #1968. T001-T007, T009-T011, T014, T016-T017, T020-T022, T024, T027-T028, T033-T036 are complete; T008 and T015 are in progress. Remaining tasks are open.

**Tests**: Tests are required by the specification. Add focused tests before implementation within each user-story phase and retain the existing regression suites.

**Upstream prerequisite**: Pin the already-published CShells `0.0.30-preview.158`; do not duplicate the upstream preparation-hook implementation in Elsa.

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Establish the reviewed package and test-fixture prerequisites without changing default runtime behavior.

- [x] T001 [P] Pin `CShells`, `CShells.Management.Api`, `CShells.Abstractions`, `CShells.AspNetCore`, `CShells.AspNetCore.Abstractions`, `CShells.FastEndpoints`, and `CShells.FastEndpoints.Abstractions` to `0.0.30-preview.158` in `Directory.Packages.props`; do not copy the upstream CShells hook into Elsa.
- [x] T002 [P] Add the bounded shared-persistence host/test fixture in `tests/essentials/Persistence/EntityFrameworkCore/SharedResources/Tests/SharedPersistenceHostFixture.cs`, the PostgreSQL target-provisioning support in `tests/essentials/Persistence/EntityFrameworkCore/SharedResources/Tests/PostgreSqlTargetFixture.cs`, and their test project in `tests/essentials/Persistence/EntityFrameworkCore/SharedResources/Tests/Elsa.Persistence.EntityFrameworkCore.SharedResources.Tests.csproj` registered in `Elsa.Server.slnx`, while preserving the existing SQLite and legacy fixtures.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Implement the provider-neutral input, resolution, enrollment, and preparation seams required by every user story. #1967 publication is complete; execute these tasks after the setup phase.

- [x] T003 [P] Add the presence-aware detached resource, shell-selection, source-provenance, participant, resolution, refusal, and evidence records in `src/essentials/Persistence/EntityFramework/ResourceResolution/PersistenceResourceModels.cs`, keeping connection-string values out of the detached model.
- [x] T004 Implement the side-effect-free precedence and atomic Provider/ConnectionName resolver in `src/essentials/Persistence/EntityFramework/ResourceResolution/PersistenceResourceResolver.cs` using shell binding → shell default → root default → legacy configuration, including blank/null/wrong-type/refusal and reset semantics.
- [x] T005 [P] Add the explicit participant marker and metadata adapter in `src/essentials/Persistence/EntityFramework/EfPersistenceResourceParticipantAttribute.cs` and `src/essentials/Persistence/EntityFramework/Tooling/EfPersistenceParticipantCatalog.cs`, then apply the marker to the 13 reviewed feature classes in `src/essentials/Workflows/Runtime/Persistence/EntityFrameworkCore/RuntimeEntityFrameworkCoreFeature.cs`, `src/essentials/Workflows/Runtime/Persistence/EntityFrameworkCore/RuntimeWorkflowExecutionEntityFrameworkCoreFeature.cs`, `src/essentials/Workflows/Runtime/Persistence/EntityFrameworkCore/RuntimeActivityExecutionEntityFrameworkCoreFeature.cs`, `src/essentials/Workflows/Runtime/Persistence/EntityFrameworkCore/RuntimeOperationalStateEntityFrameworkCoreFeature.cs`, `src/essentials/Workflows/Runtime/Persistence/EntityFrameworkCore/RuntimeWorkflowAlterationEntityFrameworkCoreFeature.cs`, `src/essentials/Workflows/Runtime/Persistence/EntityFrameworkCore/RuntimeWorkflowTestScopeEntityFrameworkCoreFeature.cs`, `src/essentials/Workflows/Runtime/Persistence/EntityFrameworkCore/RuntimeBookmarksEntityFrameworkCoreFeature.cs`, `src/essentials/Workflows/Runtime/Persistence/EntityFrameworkCore/RuntimeArtifactsEntityFrameworkCoreFeature.cs`, `src/essentials/Workflows/Design/Persistence/EntityFrameworkCore/WorkflowsDesignEntityFrameworkCoreFeature.cs`, `src/essentials/Activities/Design/Persistence/EntityFrameworkCore/ActivitiesDesignEntityFrameworkCoreFeature.cs`, `src/essentials/Workflows/Publishing/Persistence/EntityFrameworkCore/PublishingEntityFrameworkCoreFeature.cs`, `src/essentials/Diagnostics/StructuredLogs/Persistence/EntityFrameworkCore/StructuredLogsEntityFrameworkCoreFeature.cs`, and `src/essentials/Diagnostics/OpenTelemetry/Persistence/EntityFrameworkCore/EfOpenTelemetryFeature.cs`, using stable feature identities and existing `UsesEfModuleAttribute`/`EfModuleDescriptor` metadata rather than property resemblance.
- [x] T006 Implement the root/shell configuration and authored legacy-target presence adapter in `src/essentials/Persistence/EntityFramework/ResourceResolution/PersistenceConfigurationAdapter.cs`, and the public static `EfPersistencePreparation.Prepare(...)` facade in `src/essentials/Persistence/EntityFramework/ResourceResolution/EfPersistencePreparation.cs`; preserve object-map/array CShells shapes, explicit null, reset, unknown fields, and source context without constructing feature classes while keeping the detached models internal.
- [x] T007 [P] Add the provider-neutral preparation seam in `src/essentials/Modularity/Core/Contracts/IFeatureActivationContextPreparer.cs` and the pass-through implementation in `src/essentials/Modularity/Nuplane/Services/LegacyFeatureActivationContextPreparer.cs`; use `Task<FeatureActivationContext> PrepareAsync(...)`, the existing `FeatureActivationRefusedException`, and no parallel result hierarchy.
- [ ] T008 Implement the EF management context-preparation adapter and known participant/context validation in `src/essentials/Modularity/EntityFramework/EfPersistenceActivationContextPreparer.cs` and `src/essentials/Persistence/EntityFramework/Tooling/EfPersistenceResourceValidator.cs`, covering provider support, shared-context agreement, transaction-affinity constraints, opaque configurators, schema/pooling ownership, migration policy, and host-owned exclusions; this implementation must not also implement the CShells `IShellSettingsPreparer`.
- [x] T009 Register exactly one CShells `IShellSettingsPreparer` plus the EF preparation implementation through `src/essentials/Modularity/EntityFramework/Extensions/ModularityEntityFrameworkServiceCollectionExtensions.cs`, validating the explicit host composer and keeping Core/Nuplane free of EF references.
- [x] T010 [P] Add resolver contract tests in `tests/essentials/Persistence/EntityFramework/Tests/PersistenceResourceResolverTests.cs` for precedence, atomic provider/connection selection, legacy ambiguity, missing targets, explicit false/zero/null/blank presence, reset, unknowns, redaction, and no side effects.
- [x] T011 [P] Add enrollment and architecture tests in `tests/essentials/Persistence/EntityFrameworkCore/Migrations/Tests/EfPersistenceResourceEnrollmentTests.cs` and `tests/essentials/Architecture/ArchitectureGuardTests.cs` proving the 13 reviewed participants, module/context ownership, host-owned exclusions, stable identities, and no resolver dependency on workflow feature classes.
- [x] T012 [P] Add CShells preparation lifecycle tests in `tests/essentials/Modularity/EntityFramework/Tests/EfPersistenceActivationContextPreparerTests.cs` and `EfPersistenceResourceRegistrationTests.cs` proving globals and dependency-enabled participants are visible before feature construction/configurators, duplicate preparers fail, cancellation refuses activation, and no feature effect occurs on refusal.
- [ ] T013 Add the shared foundational project/reference and package checks in `tests/essentials/Persistence/EntityFramework/Tests/Elsa.Persistence.EntityFramework.Tests.csproj`, `tests/essentials/Modularity/EntityFramework/Tests/Elsa.Modularity.EntityFramework.Tests.csproj`, and `tests/essentials/Architecture/Elsa.Architecture.Tests.csproj` without adding EF to Modularity.Core/Nuplane or the CLI worker.

**Checkpoint**: Contracts reviewed, CShells `0.0.30-preview.158` pinned, and the pure resolver plus preparation seam can be tested without a database or feature construction.

---

## Phase 3: User Story 1 - Configure one shared database (Priority: P1)

**Owner**: #1968.

**Goal**: A supported host authors one resource, materializes it into the enrolled Runtime, Workflows Design, Activities Design, and Publishing consumers, and gives runtime and migration tooling the same effective target.

**Independent Test**: A rebuilt Workbench designs, publishes, executes, and restarts against one PostgreSQL resource; the matching migration-tooling context selects the same target and module set without exposing connection values.

### Tests for User Story 1

- [x] T014 [P] [US1] Add shared-layout resolver/materialization contract tests in `tests/essentials/Persistence/EntityFramework/Tests/SharedPersistenceLayoutTests.cs` covering all enrolled Runtime, Workflows Design, Activities Design, and Publishing consumers and rejecting omitted-provider SQLite drift.
- [ ] T015 [P] [US1] Add host composition tests in `tests/essentials/Workflows/Runtime/Persistence/EntityFrameworkCore/Tests/SharedPersistenceCompositionTests.cs`, `tests/essentials/Workflows/Design/Persistence/EntityFrameworkCore/Tests/SharedPersistenceCompositionTests.cs`, and `tests/essentials/Workflows/Publishing/Persistence/EntityFrameworkCore/Tests/SharedPersistenceCompositionTests.cs` for restart-safe persisted design, publication, and execution state. The shared host fixture/E2E must also explicitly exercise Activities Design creation/publication and inspect its persisted rows/history; a Runtime-only smoke does not cover it.

### Implementation for User Story 1

- [x] T016 [US1] Add the reviewed host defaults contract in `src/essentials/Persistence/EntityFramework/Tooling/EfToolingShellDefaultsAttribute.cs` and `src/essentials/Persistence/EntityFramework/Tooling/IEfToolingShellDefaults.cs`, implement the Workbench composer in `src/apps/Elsa.Workbench/WorkbenchEfToolingShellDefaults.cs`, and wire opt-in resource registration through `src/apps/Elsa.Workbench/Program.cs`, `src/apps/Elsa.Workbench/Elsa.Workbench.csproj`, and `src/apps/Elsa.Workbench/appsettings.json` without changing the legacy default shell unless resource mode is explicitly authored.
- [x] T017 [US1] Materialize only Provider and ConnectionName patches in the CShells final-settings adapter `src/essentials/Modularity/EntityFramework/EfPersistenceShellSettingsPreparer.cs` by calling `EfPersistencePreparation.Prepare(...)`, preserving all unrelated settings and letting existing module/provider connection lookup remain authoritative; this adapter must not also implement `IFeatureActivationContextPreparer`, which remains `EfPersistenceActivationContextPreparer.cs`.
- [ ] T018 [US1] Integrate the shared effective plan with EF provider agreement and module/context registration in `src/essentials/Persistence/EntityFramework/Tooling/EfProviderAgreement.cs`, `src/essentials/Persistence/EntityFramework/EfModuleBinding.cs`, and `src/essentials/Modularity/EntityFramework/EfPendingMigrationActivationGuard.cs`.
- [ ] T019 [US1] Add the shared-resource migration and module-selection tests in `tests/essentials/Persistence/EntityFrameworkCore/Migrations/Tests/EfToolingHostTests.cs`, `tests/essentials/Persistence/EntityFrameworkCore/Migrations/Tests/EfProviderAgreementTests.cs`, and `tests/essentials/Persistence/EntityFrameworkCore/Migrations/Tests/ModuleMigrationTests.cs` for equivalent runtime/tooling contexts and pre-database refusals.
- [x] T020 [US1] Extend the private worker envelope and host-operation/context negotiation in `src/essentials/Cli/Worker/WorkerContract.cs`, `src/essentials/Cli/Worker/ToolingEntryPoint.cs`, and `src/essentials/Persistence/EntityFramework/Tooling/EfToolingContract.cs` using the reviewed private WorkerContract v2, host operation v2, and factory/context v1 contracts; retain legacy v1 behavior.
- [x] T021 [US1] Implement host-owned tooling context creation, resource-aware operation handling, strict expected-connection verification, and per-target module selection in `src/essentials/Persistence/EntityFramework/Tooling/EfToolingHost.cs` and `src/essentials/Persistence/EntityFramework/Tooling/EfToolingConfigurationContext.cs`, refusing before context/connection/database creation on mismatch.
- [x] T022 [US1] Update CLI source-context metadata and resource transport in `src/essentials/Cli/ShellConfiguration.cs`, `src/essentials/Cli/Worker/HostAppSettings.cs`, `src/essentials/Cli/Worker/WorkerRunner.cs`, and `src/essentials/Cli/ElsaCli.cs`; keep source loading and expected-value lookup host-owned, preserve EF-free front-end behavior, and do not accept new selection flags for script-check.
- [ ] T023 [US1] Add closed-contract, capability-negotiation, context-lifetime, command-path, redaction, and target-verification tests in `tests/essentials/Cli/Tests/ToolingEntryPointTests.cs`, `tests/essentials/Cli/Tests/PersistenceCliTests.cs`, `tests/essentials/Cli/Tests/WorkerLaunchTests.cs`, and `tests/essentials/Persistence/EntityFrameworkCore/Migrations/Tests/EfToolingHostTests.cs`.
- [x] T024 [US1] Add the rebuilt Workbench shared PostgreSQL journey in `e2e-tests/composition/Test-SharedPersistence.ps1` and document its provisioning, migration-history, design/publish/execute/restart, tooling-agreement, and redacted-evidence receipt in `e2e-tests/README.md`.

**Checkpoint**: The shared-layout portions of SC-001, SC-002, SC-003, and SC-004 pass, with runtime/tooling agreement and no unintended database activity on negative cases. The diagnostics half of SC-003 remains a US3 gate.

---

## Phase 4: User Story 2 - Keep existing configurations working (Priority: P1)

**Owner**: #1968.

**Goal**: Legacy configurations preserve their current defaults, explicit options, source precedence, unknown settings, and safe management behavior when resource mode is absent.

**Independent Test**: Existing SQLite and provider-specific configurations pass their recorded focused suites unchanged; resource mode refuses ambiguous legacy ownership without saving or reloading.

### Tests for User Story 2

- [ ] T025 [P] [US2] Extend legacy characterization tests in `tests/essentials/Persistence/EntityFramework/Tests/CommittedCompositionConnectionTests.cs`, `tests/essentials/Persistence/EntityFramework/Tests/EfConnectionDefaultsTests.cs`, `tests/essentials/Persistence/EntityFramework/Tests/EfMigrateOptionsTests.cs`, and `tests/essentials/Persistence/EntityFramework/Tests/EfSchemaTests.cs` for legacy precedence, missing named connections, provider defaults, schema, pooling, migration policy, OpenTelemetry, and host-owned OpenIddict.
- [ ] T026 [P] [US2] Add authored-preservation tests in `tests/essentials/Modularity/Tests/JsonShellFeatureConfigurationStoreTests.cs` and `tests/essentials/Modularity/Tests/FeatureManagementServiceTests.cs` for unknown feature/settings, explicit false/zero/empty/null, reset/removal, masked secrets, and unchanged legacy requests.

### Implementation for User Story 2

- [x] T027 [US2] Implement the mandatory management preparation order in `src/essentials/Modularity/Nuplane/Services/FeatureManagementService.cs` and `src/essentials/Modularity/Core/Contracts/IFeatureActivationContextPreparer.cs`: restore secrets and validate, prepare once, pass the returned context to `EnsureActivationAllowedAsync`, then run ordinary guards and only then save/refresh/reload.
- [x] T028 [US2] Register the legacy pass-through and EF preparation implementations in `src/essentials/Modularity/Nuplane/Extensions/ModularityNuplaneServiceCollectionExtensions.cs` and `src/essentials/Modularity/EntityFramework/Extensions/ModularityEntityFrameworkServiceCollectionExtensions.cs`, throwing the existing redacted `FeatureActivationRefusedException` with the exact `[resource-managed-configuration]` Reason prefix in the unchanged HTTP 409 envelope before any ordinary guard or mutation.
- [ ] T029 [US2] Add management refusal and downstream-side-effect tests in `tests/essentials/Modularity/Tests/FeatureManagementServiceTests.cs` and `tests/essentials/Modularity/EntityFramework/Tests/EfPendingMigrationActivationGuardTests.cs` for current-only, candidate-only, invalid applicable intent, definitions-only, and wholly legacy requests.
- [ ] T030 [US2] Add code-default, disabled/reintroduced dependency, opaque configurator, unknown-consumer, and host-owned/private-store preservation tests in `tests/essentials/Modularity/EntityFramework/Tests/FeatureActivationContextPreparerTests.cs` and `tests/essentials/Persistence/EntityFramework/Tests/PersistenceResourceResolverTests.cs`.
- [ ] T031 [US2] Add legacy host regression journeys in `tests/essentials/Secrets/Persistence/EntityFrameworkCore/Tests/SecretsEntityFrameworkCoreShellReloadTests.cs`, `tests/essentials/Persistence/EntityFrameworkCore/Migrations/Tests/ModuleMigrationTests.cs`, and `e2e-tests/durability/Test-RestartRecovery.ps1` proving resource mode is absent and existing behavior remains intact.

**Checkpoint**: SC-005 passes, including legacy parity and diagnostics-binding removal semantics, without flattening generated values into authored configuration.

---

## Phase 5: User Story 4 - Understand changes before activation (Priority: P1)

**Owner**: #1968.

**Goal**: Runtime, management, and tooling expose effective source/evidence and distinguish planning, saving, refreshing, and activation without leaking secrets.

**Independent Test**: A file-only and environment-context candidate shows the same resolved sources where inputs are equivalent, refuses stale/unsupported contexts before mutation, and reports saved versus activated state truthfully.

### Tests for User Story 4

- [ ] T032 [P] [US4] Add redacted plan/evidence and presence tests in `tests/essentials/Persistence/EntityFramework/Tests/PersistenceResolutionEvidenceTests.cs` and `tests/essentials/Persistence/EntityFrameworkCore/Migrations/Tests/EfToolingHostTests.cs` covering checked sources, unverified prerequisites, stable refusal codes, connection canaries, and no raw paths/hashes/values.
- [x] T033 [P] [US4] Add reload recomputation tests in `tests/essentials/Modularity/EntityFramework/Tests/PersistenceResourceReloadTests.cs` and the endpoint-level `tests/essentials/Modularity/Tests/ServerReadinessTests.cs` proving a fresh generation uses changed authored targets and a failed candidate retains the previous active generation. The live PostgreSQL journey supplies the cross-boundary target and generation proof.

### Implementation for User Story 4

- [x] T034 [US4] Implement fresh source-snapshot preparation in `src/essentials/Modularity/EntityFramework/EfPersistenceShellSettingsPreparer.cs` and prove explicit reload through the existing Workbench `POST /_admin/shells/reload/{name}` mapping in `src/apps/Elsa.Workbench/Program.cs` using `e2e-tests/composition/Test-SharedPersistence.ps1`; assert success/new generation/readiness rather than HTTP 200 alone, and assert previous-generation availability on failure. Do not add an endpoint or rewire the existing shell-scoped reloader fallback.
- [x] T035 [US4] Implement host-owned file and inherited-environment context creation in `src/essentials/Persistence/EntityFramework/Tooling/EfToolingConfigurationContext.cs`; keep `src/essentials/Cli/Worker/HostAppSettings.cs`, `src/essentials/Cli/ShellConfiguration.cs`, and `src/essentials/Cli/Worker/WorkerRunner.cs` limited to canonical host/shell/environment metadata and resource-intent transport, with no claim of observing a separate running host.
- [x] T036 [US4] Implement closed manifest v2/redacted context evidence and schema v1 compatibility in `src/essentials/Persistence/EntityFramework/Tooling/EfToolingContract.cs`, `src/essentials/Persistence/EntityFramework/Tooling/EfMigrationPlan.cs`, `src/essentials/Cli/MigrationPlan.cs`, `src/essentials/Cli/Report.cs`, and `src/essentials/Cli/ScriptCheck.cs`; script-check must reconstruct selectors from the committed manifest only.
- [ ] T037 [US4] Add current/candidate resource-mode refusal and source-change detection tests in `tests/essentials/Modularity/Tests/FeatureManagementServiceTests.cs` and `tests/essentials/Modularity/EntityFramework/Tests/PersistenceResourceReloadTests.cs`, proving refusal before save/refresh/reload with zero downstream guard activity while definitions-only and wholly legacy edits retain existing behavior; do not implement deferred #1964 acceptance or recovery here.
- [ ] T038 [US4] Add full public command-path and old/partial/unknown-host compatibility tests in `tests/essentials/Cli/Tests/PersistenceCliTests.cs`, `tests/essentials/Cli/Tests/ScriptCheckCliTests.cs`, `tests/essentials/Cli/Tests/ToolingEntryPointTests.cs`, and `tests/essentials/Cli/Tests/WorkerLaunchTests.cs` for factory/context negotiation, private WorkerContract v2, host operation v2, cancellation/disposal, closed responses, and no downgrade.
- [ ] T039 [US4] Update the executable verification record in `specs/173-shared-persistence/quickstart.md` and `specs/173-shared-persistence/contracts/tooling.md` with the implemented source modes, context evidence, live/unverified distinctions, module-set selection, negative side-effect checks, and secret-canary proof.

**Checkpoint**: SC-006 passes for runtime reload and management writes, and FR-011/FR-014/FR-016 are evidenced across plan, tooling, and management paths.

---

## Phase 6: User Story 3 - Isolate diagnostics explicitly (Priority: P2)

**Owner**: #1969; depends on the completed #1968 shared-resource path and its evidence.

**Goal**: Both supported diagnostics persistence consumers bind to a second target while all other enrolled consumers retain the shared default, and unsupported physical layouts remain unresolved.

**Independent Test**: A rebuilt host runs a representative workflow with Structured Logs and OpenTelemetry on the second PostgreSQL target, verifies both migration histories/data placements after restart, and refuses an invalid split before database activity.

### Tests for User Story 3

- [ ] T040 [P] [US3] Add diagnostics-binding and inheritance tests in `tests/essentials/Persistence/EntityFramework/Tests/DiagnosticsPersistenceBindingTests.cs` and `tests/essentials/Diagnostics/OpenTelemetry/Persistence/EntityFrameworkCore/Tests/EfOpenTelemetryResourceBindingTests.cs` for both diagnostic consumers, binding removal, unrelated-target preservation, private defaults, and redacted refusal.

### Implementation for User Story 3

- [ ] T041 [US3] Implement EF-owned diagnostics/shared-context and unsupported-layout validation in `src/essentials/Persistence/EntityFramework/Tooling/EfPersistenceResourceValidator.cs` and `src/essentials/Modularity/EntityFramework/EfPersistenceActivationContextPreparer.cs`; reuse the existing strict provider/connection checks in `src/essentials/Persistence/EntityFramework/EfSharedTransaction.cs` and change that file only if integrated contract review identifies a concrete gap. Resource names or aliases must not establish physical identity.
- [ ] T042 [US3] Add per-target tooling module selection and diagnostics migration coverage in `src/essentials/Persistence/EntityFramework/Tooling/EfToolingHost.cs`, `tests/essentials/Persistence/EntityFrameworkCore/Migrations/Tests/EfToolingHostTests.cs`, and `tests/essentials/Persistence/EntityFrameworkCore/Migrations/Tests/ModuleMigrationTests.cs`.
- [ ] T043 [US3] Add the separate-diagnostics PostgreSQL journey and invalid-layout refusal in `e2e-tests/diagnostics/Test-SharedDiagnosticsPersistence.ps1`, `e2e-tests/diagnostics/Test-OpenTelemetryApiMigration.ps1`, and `e2e-tests/README.md`, recording module sets, migration histories, data placement, restart, target mismatch, shared-context, and transaction-affinity evidence.

**Checkpoint**: SC-003 and the diagnostics portions of SC-004/SC-005 pass; no unsupported custom split is reported ready from configuration names alone.

---

## Phase 7: Polish & Cross-Cutting Concerns

**Purpose**: Complete source-grounded documentation, traceability, and focused verification without expanding the feature into profiles, groups, builder UX, generic settings, or durable apply/recovery.

- [ ] T044 [P] Update `specs/173-shared-persistence/quickstart.md`, `specs/173-shared-persistence/plan.md`, `specs/173-shared-persistence/contracts/persistence-configuration.md`, `specs/173-shared-persistence/contracts/runtime-management.md`, `specs/173-shared-persistence/contracts/tooling.md`, `src/essentials/Persistence/EntityFramework/README.md`, `src/essentials/Cli/README.md`, `src/essentials/Persistence/EntityFramework/EXTENSION_POINTS.md`, and `src/essentials/Modularity/Api/EXTENSION_POINTS.md` with final supported layouts, legacy migration, host-owned exclusions, refusal/evidence rules, and a checked FR/SC traceability record; keep implementation evidence distinct from reviewed design.
- [ ] T045 Run the focused component/architecture suites, rebuilt-host/database/tooling quickstart, mutation checks, `git diff --check`, root diff review, required current-head CI/review gates, and the generated-map freshness check before every merge; refresh generated maps only when authoritative inputs changed and that refresh is explicitly authorized. Record exact commands, current-head evidence, and unavailable gates in `specs/173-shared-persistence/quickstart.md` and the delivery issue without claiming unrun evidence.

---

## Requirements and Success-Criteria Traceability

| Requirement | Tasks | Requirement | Tasks |
|---|---|---|---|
| FR-001 | T003-T004, T014 | FR-012 | T008, T041 |
| FR-002 | T004-T006, T014 | FR-013 | T008, T040-T043 |
| FR-003 | T005, T011, T016-T018 | FR-014 | T003, T032, T036 |
| FR-004 | T040-T043 | FR-015 | T020-T023, T036, T038 |
| FR-005 | T004, T010, T026, T040 | FR-016 | T020-T023, T032, T034-T036 |
| FR-006 | T006, T010, T025, T030 | FR-017 | T027-T029, T037 |
| FR-007 | T006, T025, T030-T031 | FR-018 | T027-T029, T034, T037 |
| FR-008 | T004, T010, T019, T023, T032, T043 | FR-019 | T007-T009, T017, T033-T034 |
| FR-009 | T003, T006, T010, T025-T026 | FR-020 | T006, T026, T030, T034 |
| FR-010 | T018-T024, T034-T036 | FR-021 | T024, T039, T043-T045 |
| FR-011 | T032, T035-T039 | SC-001 | T014-T024, T045 |
| SC-002 | T019-T023, T035-T038, T045 | SC-003 | T015, T024, T043, T045 |
| SC-004 | T010, T019, T023, T029, T032, T043, T045 | SC-005 | T025-T031, T040, T045 |
| SC-006 | T033-T039, T045 |  |  |

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: T001-T002 can run in parallel; T001 must complete before CShell preparation integration.
- **Foundational (Phase 2)**: Depends on Setup and integrated contract review; blocks all user stories. T003, T005, T007, and T010-T013 have separate files and can proceed in parallel after the reviewed model is fixed; T004, T006, T008, and T009 consume those decisions.
- **User Story 1 (Phase 3)**: Depends on Foundation. It establishes the shared runtime/tooling path with the source/manifest pieces of US4, before the diagnostics split.
- **User Story 2 (Phase 4)**: Baseline tests T025-T026 may run alongside US1 after Foundation; management implementation T027-T031 depends on the preparation seam and resolver from Foundation and the shared materialization behavior from US1.
- **User Story 4 (Phase 5)**: Depends on Foundation and the shared target path from US1; its tooling and management source-context work must complete before US3.
- **User Story 3 (Phase 6)**: Depends on US1 and the applicable validation/evidence from US4; #1969 is blocked until #1968's shared layout is accepted.
- **Polish (Phase 7)**: Depends on the desired stories and their evidence; update evidence only after the corresponding implementation checks pass.

### User Story Dependencies

- **US1 (P1)**: Foundation plus US4 source/manifest integration; this is the central outcome of #1968, released with US2/US4.
- **US2 (P1)**: Foundation for legacy characterization; US1 for resource-mode ambiguity and management refusal integration.
- **US4 (P1)**: Foundation and US1 protocol types; its source loader/manifest pieces integrate before the US1 end-to-end gate. It shares the same effective plan.
- **US3 (P2)**: US1 and US4; diagnostics is a separate physical-layout proof owned by #1969.

### Concrete integration order

After Foundation: establish T020 protocol/context types; implement host source loading T035 before completing T021 context creation/live execution; implement T036 manifest/reader/reporting alongside T022 transport; then run T023/T038 closed-boundary tests and T024 real-host journey. T016/T017 runtime wiring and T027–T030 management/legacy work can proceed in parallel on their separate owners. Complete T033/T034/T037 reload/preflight checks before #1968 release. Phase headings group user concerns; they do not override these implementation dependencies. Run T044/T045 for each delivery PR, first #1968 and later #1969.

### Parallel Opportunities

- **US1**: T014 and T015 can run in parallel. After the contract tests are fixed, Workbench wiring (T016), EF agreement tests (T019), and CLI contract tests (T023) can proceed on separate files; T021/T022 depend on the reviewed tooling contract.
- **US2**: T025 and T026 can run in parallel. Once T027 is implemented, T028-T030 can be split across management, guard, and preservation test files; T031 is the regression gate.
- **US4**: T032 and T033 can run in parallel. T035/T036 can proceed in parallel after context-contract review; T037/T038 then verify management and CLI boundaries separately.
- **US3**: T040 can run in parallel with the validation design review; after T041, T042 and T043 can proceed in parallel because tooling tests and host E2E use separate paths.

### Parallel Example: User Story 1

```text
Task T014: Shared-layout resolver/materialization contract tests
Task T015: Runtime/Design/Publishing composition tests

# After the contract tests pass:
Task T016: Workbench opt-in wiring
Task T019: EF provider-agreement and migration tests
Task T023: CLI/tooling contract tests
```

### Parallel Example: User Story 2

```text
Task T025: Legacy persistence characterization
Task T026: Authored configuration preservation tests

# After T027/T028:
Task T029: Management downstream-side-effect tests
Task T030: Code-default and opaque-configurator tests
```

### Parallel Example: User Story 4

```text
Task T032: Plan/evidence redaction tests
Task T033: Reload recomputation tests

# After the context contract is reviewed:
Task T035: Source-context implementation
Task T036: Manifest v2 and script-check implementation
```

### Parallel Example: User Story 3

```text
Task T040: Diagnostics binding/inheritance tests

# After T041:
Task T042: Per-target tooling tests and implementation
Task T043: Separate-diagnostics host/database journey
```

## Implementation Strategy

### First release: #1968 (US1, US2 and US4)

1. Complete the integrated review gate, Phase 1, and Phase 2.
2. Integrate US1 with the mandatory US2 compatibility and US4 source/reload/evidence work. These are independently testable concerns within one releasable #1968 slice, not separately shippable contracts.
3. Validate every shared-layout acceptance case and the compatibility/reload gates before closing #1968 or starting diagnostics. SC-003 diagnostics evidence remains #1969.

### Incremental Delivery

1. Preserve legacy mode and complete US2 while the shared path is under review.
2. Complete US4 so runtime, management, and tooling explain the same effective sources and redacted evidence.
3. Deliver US3 only after #1968's shared layout is accepted and real target-affinity evidence exists.
4. Finish documentation and traceability; profiles, feature groups, builder UX, generic settings, data relocation, and durable apply/recovery remain later work.

### Scope Guard

Do not add tasks for a general configuration framework, profiles/groups, a runtime builder, arbitrary third-party enrollment, automatic provider migration, generic database identity discovery, secret-provider integration, persistent group inheritance, or duplicate CShells implementation. Do not claim a pure resolver or SQLite regression run proves the PostgreSQL, restart, transaction, or cross-domain layout gates.

## Notes

- `[P]` tasks touch separate files and have no dependency on incomplete work.
- `[US1]`, `[US2]`, `[US3]`, and `[US4]` map directly to the specification's user stories.
- The contracts passed integrated design review. Any implementation-driven change to their public behavior requires an explicit reviewed amendment; file-local implementation choices remain routine engineering decisions.
- #1902, #1895, #1900, #1159, and #1145 remain existing related work/dependencies; this task list does not duplicate their scope.
