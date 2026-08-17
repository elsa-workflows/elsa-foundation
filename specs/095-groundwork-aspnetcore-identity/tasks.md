---

description: "Dependency-ordered implementation tasks for authoritative Groundwork-backed ASP.NET Core Identity"

---

# Tasks: Groundwork ASP.NET Core Identity

**Input**: Design documents from `/specs/095-groundwork-aspnetcore-identity/`

**Prerequisites**: `plan.md`, `spec.md`, `research.md`, `data-model.md`, `contracts/`, `quickstart.md`

**Tests**: Required and red-first. Capture the intended failing behavior before implementing each story.
The clean-break #269 direction supersedes the earlier migration-era preservation wording: valid behavior
must have an exact public-v2 replacement in `AspNetCoreIdentityV2AcceptanceCatalog`, while v1 shape,
unconditional-upsert, migration, and compatibility objectives are intentionally retired rather than
carried forward.

**Organization**: Tasks are grouped by user story and seven ratcheted delivery boundaries. The EF
implementation remains a source-only frozen oracle until the clean-break boundary removes it atomically;
it is not a runtime fallback or migration path.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel only when it owns different files and has no incomplete dependency.
- **[Story]**: Maps to the user stories in `spec.md`.
- Every task names its authoritative file or directory.

## Phase 1: Setup And Exact Denominator

**Purpose**: Pin the branch, dependency generation, framework capability denominator, preserved tests, and project shells before behavior changes.

- [X] T001 Record exact base/candidate commits, .NET/OS architecture, Groundwork package/tool version, provider image digests, MongoDB topology, and current focused/full test counts in `specs/095-groundwork-aspnetcore-identity/quickstart.md`
- [X] T002 Reconcile every .NET 10 user/role store interface and method, every Elsa IAM adapter operation, and every current EF/Groundwork test identity against `specs/095-groundwork-aspnetcore-identity/contracts/identity-store-contract.md` and `contracts/test-objective-ledger.md`
- [X] T003 Create `src/Elsa/Foundation/Identity/AspNetCoreIdentity/Groundwork/Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.csproj` and `tests/Elsa/Foundation/Identity/AspNetCoreIdentity/Groundwork/Tests/Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.Tests.csproj`, add both to `Elsa.Server.slnx`, and exclude nested `Groundwork/**/*` globs from `src/Elsa/Foundation/Identity/AspNetCoreIdentity/Elsa.Foundation.Identity.AspNetCoreIdentity.csproj`
- [X] T004 Add the new provider feature project references to `src/Apps/Elsa.Server/Elsa.Server.csproj` and Groundwork reference composition/test projects without enabling it in `src/Apps/Elsa.Server/shells.json` or modifying the frozen EF oracle
- [X] T005 Add deterministic users, roles, claims, logins, tokens, memberships, tenant scopes, clocks, IDs, Unicode cases, canonical fingerprints, and result observations in `tests/Elsa/Foundation/Identity/AspNetCoreIdentity/Groundwork/Tests/Fixtures/AspNetCoreIdentityScenarioData.cs` and `AspNetCoreIdentityScenarioResult.cs`

**Checkpoint**: The new projects restore on exact `e4d61bef`; the capability and behavioral denominators are complete; no production registration changes exist.

---

## Phase 2: Foundational Authority And Composition (Blocking)

**Purpose**: Establish the sole authority document model, physical schema, atomic primitives, and explicit feature-selection boundary used by every story.

**Critical gate**: No framework store behavior starts until T006–T017 are green and independently reviewed.

### Foundational Red Tests

- [X] T006 [P] Write failing exact framework-capability, unsupported-interface absence, scoped lifetime, feature registration, and EF-plus-Groundwork conflict tests in `tests/Elsa/Foundation/Identity/AspNetCoreIdentity/Groundwork/Tests/AspNetCoreIdentityGroundworkRegistrationTests.cs`
- [X] T007 [P] Write failing single-authority, legacy duplicate document/registration, non-growing EF surface, and provider-neutral core-dependency tests in `tests/Elsa/Architecture/GroundworkPersistenceCoverageTests.cs`, `GroundworkPersistenceLifetimeTests.cs`, and `EfCoreSurfaceRatchetTests.cs`
- [X] T008 [P] Write failing manifest compilation, route binding, resolved-name collision, SQL Server key-budget, missing capability, and physical-unit identity tests in `tests/Elsa/Foundation/Identity/Persistence/Groundwork/Tests/IdentityStorageManifestTests.cs`

### Foundational Implementation

- [X] T009 [P] Replace the legacy authority payloads with the twelve manifest `1.0.4` units: user, role, user-claim, role-claim, external-login, user-role, user-token, tenant-membership, user-name reservation, email reservation, role-name reservation, and mutation receipt, plus owner child registries in `src/Elsa/Foundation/Identity/Persistence/Groundwork/Documents/IdentityAuthorityDocuments.cs` and the owning store records
- [X] T010 Declare physical entity tables, finite string lengths, scoped unique indexes, bounded routes, result operations, deterministic ordering, and receipt expiry cleanup for every T009 unit in `src/Elsa/Foundation/Identity/Persistence/Groundwork/IdentityStorageManifest.cs` and `IdentityGroundworkStorageManifestSource.cs`
- [X] T011 [P] Implement collision-safe scoped document/link identities, opaque revision stamps, and canonical request fingerprints in `src/Elsa/Foundation/Identity/Persistence/Groundwork/Stores/IdentityDocumentId.cs`, `IdentityRevisionStamp.cs`, and `IdentityRequestFingerprint.cs`
- [X] T012 Implement one exact-load/read coordinator and one conditional atomic-write coordinator over `IGroundworkStoreSessionFactory` in `src/Elsa/Foundation/Identity/Persistence/Groundwork/Stores/GroundworkIdentityAuthority.cs` and `GroundworkIdentityAtomicWrite.cs`
- [X] T013 [P] Add Foundation Identity-scoped readiness, serialization, conflict, corrupt-state, and uncertain-commit exceptions plus Identity result translation in `src/Elsa/Foundation/Identity/Persistence/Groundwork/Exceptions/` and `Stores/GroundworkIdentityFailureMapper.cs`
- [X] T014 Implement the public non-sealed `AspNetCoreIdentityGroundworkFeature`, virtual registration path, and explicit manifest/store contribution in `src/Elsa/Foundation/Identity/AspNetCoreIdentity/Groundwork/AspNetCoreIdentityGroundworkFeature.cs` and `DependencyInjection/AspNetCoreIdentityGroundworkRegistration.cs`
- [X] T015 Remove unconditional `AddGroundworkIdentityStores()` calls and stale Foundation Identity project references from the four `src/Elsa/Persistence/Groundwork/{Sqlite,PostgreSql,SqlServer,MongoDb}/Unified/` provider projects; update `tests/Elsa/Persistence/Groundwork/UnifiedHost/Tests/` and `tests/Elsa/Persistence/Groundwork/Composition/Tests/` to prove unified substrate registration does not implicitly select Identity and explicit feature selection remains available
- [X] T016 Complete direct branch tests for every T009–T015 logic-bearing class and registration path in `tests/Elsa/Foundation/Identity/Persistence/Groundwork/Tests/` and `tests/Elsa/Foundation/Identity/AspNetCoreIdentity/Groundwork/Tests/`; update spec 094 authority links without advancing evidence state in `specs/094-harden-groundwork-stores/coverage-ledger.json`
- [X] T017 Capture the intended red baselines, then the exact foundational green counts and independent review verdict in `specs/095-groundwork-aspnetcore-identity/quickstart.md`; commit the reviewed foundation without touching EF source

**Checkpoint**: One explicit physical authority compiles; dual authority is rejected; the new provider feature exists; no user/role behavior is falsely claimed yet.

---

## Phase 3: User Story 1 — Authenticate And Manage Identities Reliably (Priority: P1) 🎯 MVP

**Goal**: Implement the complete advertised framework user/role surface and Elsa adapters over one authority, then pass default-scope sign-in and authorization behavior on real SQLite.

**Independent Test**: Configure Groundwork Identity on file-backed SQLite, exercise user/role CRUD, passwords/stamps/contact/2FA, claims, roles, logins, tokens/recovery codes, Elsa IAM projections, cookie sign-in and permission claims, close/reopen, and obtain the expected public result digest.

### Tests For User Story 1

- [X] T018 [P] [US1] Write failing user CRUD, normalized lookup, password/security-stamp, email, phone, two-factor, and lockout-accessor contracts in `tests/Elsa/Foundation/Identity/AspNetCoreIdentity/Groundwork/Tests/AspNetCoreIdentityUserStoreContractTests.cs`
- [X] T019 [P] [US1] Write failing role CRUD, normalized lookup, role-claim, description, permission, and system-role contracts in `tests/Elsa/Foundation/Identity/AspNetCoreIdentity/Groundwork/Tests/AspNetCoreIdentityRoleStoreContractTests.cs`
- [X] T020 [P] [US1] Write failing user-claim, external-login, user-role, authentication-token, authenticator-key, recovery-code, and dependent-list contracts in `tests/Elsa/Foundation/Identity/AspNetCoreIdentity/Groundwork/Tests/AspNetCoreIdentityRelationshipContractTests.cs`
- [X] T021 [P] [US1] Write failing same-authority Elsa `IUserStore`/`IRoleStore`/`IExternalIdentityStore`/`ITenantMembershipStore` projection tests in `tests/Elsa/Foundation/Identity/AspNetCoreIdentity/Groundwork/Tests/AspNetCoreIdentityAuthorityAdapterTests.cs`
- [X] T022 [P] [US1] Write failing default-scope username/email sign-in, bad-password/unknown-user equivalence, cookie, principal claims, and permission-protected endpoint scenarios in `tests/Elsa/Foundation/Identity/AspNetCoreIdentity/Groundwork/Tests/AspNetCoreIdentityHighestSeamTests.cs`

### Implementation For User Story 1

- [X] T023 [P] [US1] Implement a Groundwork-free mapper over the existing `AspNetCoreIdentityUser` and framework `IdentityRole` types, adding only demonstrably required provider-neutral state to `src/Elsa/Foundation/Identity/AspNetCoreIdentity/Models/AspNetCoreIdentityUser.cs`, in `src/Elsa/Foundation/Identity/AspNetCoreIdentity/Groundwork/Stores/AspNetCoreIdentityAuthorityMapper.cs`; do not introduce a new role model
- [X] T024 [US1] Implement user create/read/update/delete, normalized name/email, password, security stamp, contact, two-factor, and lockout accessor interfaces in `src/Elsa/Foundation/Identity/AspNetCoreIdentity/Groundwork/Stores/GroundworkIdentityUserStore.cs`
- [X] T025 [P] [US1] Implement role create/read/update/delete, normalized lookup, and role-claim interfaces in `src/Elsa/Foundation/Identity/AspNetCoreIdentity/Groundwork/Stores/GroundworkIdentityRoleStore.cs`
- [X] T026 [P] [US1] Implement user claim add/replace/remove/list and users-for-claim behavior through scalar claim documents in `src/Elsa/Foundation/Identity/AspNetCoreIdentity/Groundwork/Stores/GroundworkIdentityUserClaims.cs`
- [X] T027 [P] [US1] Implement external-login add/remove/list/find behavior through deterministic login documents in `src/Elsa/Foundation/Identity/AspNetCoreIdentity/Groundwork/Stores/GroundworkIdentityUserLogins.cs`
- [X] T028 [P] [US1] Implement role membership add/remove/check/list/users-in-role behavior through deterministic user-role links in `src/Elsa/Foundation/Identity/AspNetCoreIdentity/Groundwork/Stores/GroundworkIdentityUserRoles.cs`
- [X] T029 [P] [US1] Implement set/get/remove authentication tokens plus authenticator-key and recovery-code conventions in `src/Elsa/Foundation/Identity/AspNetCoreIdentity/Groundwork/Stores/GroundworkIdentityUserTokens.cs`
- [X] T030 [US1] Coordinate T026–T029 relationship intents with owner registry updates and framework manager call sequences in `src/Elsa/Foundation/Identity/AspNetCoreIdentity/Groundwork/Stores/GroundworkIdentityRelationshipCoordinator.cs`
- [X] T031 [P] [US1] Adapt Elsa user/role operations to the authority without constant/default field loss in `src/Elsa/Foundation/Identity/Persistence/Groundwork/Stores/GroundworkUserStore.cs` and `GroundworkRoleStore.cs`
- [X] T032 [P] [US1] Adapt Elsa external identity and tenant membership operations to authority link/membership documents in `src/Elsa/Foundation/Identity/Persistence/Groundwork/Stores/GroundworkExternalIdentityStore.cs` and `GroundworkTenantMembershipStore.cs`
- [X] T033 [US1] Register every implemented framework interface explicitly, remove false queryable/passkey/protected registrations, and register Elsa adapters exactly once in `src/Elsa/Foundation/Identity/AspNetCoreIdentity/Groundwork/DependencyInjection/AspNetCoreIdentityGroundworkRegistration.cs`
- [X] T034 [US1] Add provider-neutral Identity core/cookie services without an EF store and preserve default token providers in `src/Elsa/Foundation/Identity/AspNetCoreIdentity/Extensions/AspNetCoreIdentityServiceCollectionExtensions.cs` and the Groundwork registration extension
- [X] T035 [US1] Make the default-scope highest seam use the new authority for manager, Elsa projection, cookie, claims, roles, and protected endpoint in `tests/Elsa/Foundation/Identity/AspNetCoreIdentity/Groundwork/Tests/AspNetCoreIdentityHighestSeamTests.cs`
- [X] T036 [US1] Complete direct branch coverage for T023–T035 and exact feature registration coverage in the owning Groundwork/Identity test projects; preserve all existing EF test cases unchanged
- [X] T037 [US1] Run the complete new unit suite, legacy Groundwork IAM suite, existing Identity suite, and file-backed SQLite close/reopen scenario; record exact counts/digest in `specs/095-groundwork-aspnetcore-identity/quickstart.md`
- [X] T038 [US1] Commit and independently review the US1 exact HEAD for capability truthfulness, one authority, field preservation, manager call sequences, and test-objective continuity; record the verdict in `specs/095-groundwork-aspnetcore-identity/quickstart.md`

**Checkpoint**: The complete advertised Identity surface and Elsa adapters operate over one SQLite authority and survive reopen; false optional capabilities do not resolve.

---

## Phase 4: User Story 2 — Isolate Tenants And Resolve Concurrency Safely (Priority: P1)

**Goal**: Enforce storage-boundary tenancy, create-only uniqueness, revision-aware mutation, relationship atomicity, and fail-closed reconciliation under independent-client races.

**Independent Test**: Create equal normalized identities across tenants; race duplicates, stale updates/deletes, lockout increments, links, dependent deletes, cancellation, and lost acknowledgement; prove one allowed outcome, no disclosure, no orphan, and persistence after reopen.

### Tests For User Story 2

- [X] T039 [P] [US2] Write failing tenant binding, same-name cross-tenant success, wrong-scope non-disclosure, and privileged-scope audit tests in `tests/Elsa/Foundation/Identity/AspNetCoreIdentity/Groundwork/Tests/AspNetCoreIdentityTenantContractTests.cs`
- [X] T040 [P] [US2] Write failing duplicate user/role/login/link/token, stale user/role update/delete, stamp replay, and concurrent lockout tests in `tests/Elsa/Foundation/Identity/AspNetCoreIdentity/Groundwork/Tests/AspNetCoreIdentityConcurrencyContractTests.cs`
- [X] T041 [P] [US2] Write failing before/during/after-decision relationship failure windows, concurrent link/delete, and no-orphan contracts in `tests/Elsa/Foundation/Identity/AspNetCoreIdentity/Groundwork/Tests/AspNetCoreIdentityAtomicityContractTests.cs`
- [X] T042 [P] [US2] Write failing duplicate-email ambiguity and unique-email reservation race tests in `tests/Elsa/Foundation/Identity/AspNetCoreIdentity/Groundwork/Tests/AspNetCoreIdentityEmailContractTests.cs`
- [X] T043 [P] [US2] Write failing cancellation and lost-acknowledgement committed/not-committed/unknown reconciliation tests in `tests/Elsa/Foundation/Identity/AspNetCoreIdentity/Groundwork/Tests/AspNetCoreIdentityReconciliationTests.cs`

### Implementation For User Story 2

- [X] T044 [US2] Add an optional, provider-neutral revision/conflict capability needed by Elsa user/role/external/membership adapters in `src/Elsa/Foundation/Identity/Abstractions/Iam/IamContracts.cs`; consume it in Groundwork while preserving the existing contract for in-memory and EF implementations through an additive compatibility path with no new EF behavior or Groundwork dependency
- [X] T045 [US2] Bind the effective sign-in tenant before manager lookup and remove global-lookup/post-filter behavior in `src/Elsa/Foundation/Identity/AspNetCoreIdentity/Services/AspNetCoreIdentitySignInService.cs`; validate entity scope in every Groundwork framework/Elsa store
- [X] T046 [US2] Make Groundwork envelope version the authoritative user/role revision, validate stamp identity/scope, rotate stamps on success, and map stale operations in `src/Elsa/Foundation/Identity/Persistence/Groundwork/Stores/IdentityRevisionStamp.cs` and Groundwork framework stores
- [X] T047 [US2] Implement native unique preflight/reconciliation and conditional user-name, email, and role-name reservations through `src/Elsa/Foundation/Identity/Persistence/Groundwork/Stores/GroundworkIdentityAuthorityAggregateCoordinator.cs`
- [X] T048 [US2] Make relationship links and affected owner-registry updates one expected-version unit of work through `src/Elsa/Foundation/Identity/Persistence/Groundwork/Stores/GroundworkIdentityAuthorityRelationshipCoordinator.cs`
- [X] T049 [US2] Implement atomic user/role dependent deletion from owner registries, including cross-owner registry CAS, through `src/Elsa/Foundation/Identity/Persistence/Groundwork/Stores/GroundworkIdentityAuthorityAggregateCoordinator.cs`
- [X] T050 [US2] Implement CAS-safe access-failure increment/reset and lockout transitions with bounded retry in `src/Elsa/Foundation/Identity/AspNetCoreIdentity/Groundwork/Stores/GroundworkIdentityUserStore.cs`
- [X] T051 [US2] Implement deterministic uncertain-commit reconciliation with a mutation receipt staged in the same unit of work, keyed by operation/request fingerprint, recording the durable outcome, and cleaned through a bounded expiry query in `src/Elsa/Foundation/Identity/Persistence/Groundwork/Stores/GroundworkIdentityAtomicWrite.cs`
- [X] T052 [US2] Run at least 100 independent-client iterations for duplicate, stale-revision, lockout, link/delete, and seed-like create races on file-backed SQLite in `tests/Elsa/Foundation/Identity/AspNetCoreIdentity/Groundwork/Tests/AspNetCoreIdentityConcurrencyContractTests.cs`
- [X] T053 [US2] Complete direct branch coverage for scope binding, stamp decoding, duplicate classification, owner registries, delete coordination, cancellation, timeout, and every failure mapping in the owning test files
- [X] T054 [US2] Run all US1/US2 suites plus SQLite reopen/restart; record exact counts, race digests, and zero-orphan evidence in `specs/095-groundwork-aspnetcore-identity/quickstart.md`
- [X] T055 [US2] Commit and independently review the US2 exact HEAD for tenant non-disclosure, concurrency linearization, email ambiguity, manager call sequencing, and uncertain-commit truthfulness; record the verdict

**Checkpoint**: Same normalized identities coexist across tenants; stale actors and duplicate racers cannot corrupt or disclose state; relationship failure windows converge without orphans.

---

## Phase 5: User Story 3 — Select Any Supported Host Database (Priority: P2)

**Goal**: Prove one production implementation and schema contract produces equivalent behavior on all four supported providers.

**Independent Test**: Run the complete public Identity scenario catalog on SQLite, SQL Server, PostgreSQL, and MongoDB replica set with independent clients, close/reopen, process restart, topology rejection, and native bounded-route evidence.

### Tests And Fixture Work For User Story 3

- [X] T056 [P] [US3] Generalize identity-specific physical manifest/client opening while preserving existing runtime behavior in `tests/Elsa/Persistence/Groundwork/Testing/GroundworkProviderDriver.cs`, `GroundworkProviderDriverFactory.cs`, and the four provider driver implementations
- [X] T057 [P] [US3] Add an Identity child-process scenario protocol for normalized lookup, duplicate create, and reopen evidence in `tests/Elsa/Persistence/Groundwork/Conformance/Tests/Probes/AspNetCoreIdentityRestartProbe.cs`
- [X] T058 [P] [US3] Add file-backed SQLite full scenario, native plan, dispose/reopen, and process-restart cases in `tests/Elsa/Persistence/Groundwork/Conformance/Tests/AspNetCoreIdentitySqliteProviderTests.cs`
- [X] T059 [P] [US3] Add SQL Server full scenario, compound-key-budget, native plan, independent-client race, and restart cases in `tests/Elsa/Persistence/Groundwork/Conformance/Tests/AspNetCoreIdentitySqlServerProviderTests.cs`
- [X] T060 [P] [US3] Add PostgreSQL full scenario, native plan, independent-client race, and restart cases in `tests/Elsa/Persistence/Groundwork/Conformance/Tests/AspNetCoreIdentityPostgreSqlProviderTests.cs`
- [X] T061 [P] [US3] Add MongoDB replica-set full scenario, winning-plan, independent-client race, restart, and standalone/topology rejection cases in `tests/Elsa/Persistence/Groundwork/Conformance/Tests/AspNetCoreIdentityMongoDbProviderTests.cs`
- [X] T062 [US3] Add provider-independent native-plan assertions and real provider-driver routes at the 100,000-record acceptance dataset proving scope/predicate/order/limit before materialization and no unbounded scan or post-materialization tenant filtering for every normalized lookup and scale-bearing relationship route in `tests/Elsa/Persistence/Groundwork/Conformance/Tests/AspNetCoreIdentityNativePlanTests.cs`; historical preview.56 evidence was invalidated and replaced by the accepted preview.60 exact-candidate generation owned by T083-T085

### Implementation And Evidence For User Story 3

- [X] T063 [US3] Reconcile provider-specific identifier lengths, SQL Server index byte limits, parameter limits, Unicode comparison evidence, and MongoDB capability requirements in `src/Elsa/Foundation/Identity/Persistence/Groundwork/IdentityStorageManifest.cs` without provider-specific domain outcomes
- [X] T064 [US3] Add offline/live plan/validate/status/apply and read-only runtime readiness contract tests for the selected Identity manifest in `tests/Elsa/Persistence/Groundwork/Conformance/Tests/AspNetCoreIdentitySchemaCliTests.cs`
- [X] T065 [US3] Include the explicit Identity manifest source in the public deployment schema only when the Identity Groundwork feature is selected in `src/Elsa/Persistence/Groundwork/ReferenceComposition/GroundworkAllFeaturesDeploymentSchema.cs` and composition tests
- [X] T066 [US3] Historical preview.56 execution: run the then-current provider scenarios and retain package/topology/fingerprint/restart provenance. T084 invalidated the complete-catalog, digest, and physical 100,000-record acceptance conclusions; T083-T085 produced the replacement preview.60 exact-candidate generation under `specs/095-groundwork-aspnetcore-identity/evidence/providers/`
- [X] T067 [US3] Historical preview.56 linkage: link the then-current #644 provider artifacts into the `iam-user`, `iam-role`, `iam-external-identity`, and dependent membership rows. T084 withdrew those active `pass` links; the preview.60 omnibus artifacts remain spec-095 authority evidence and are deliberately not copied into spec 094's per-obligation provider-evidence arrays
- [X] T068 [US3] Historical review: record the preview.56 counts/durations/verdict. T084 invalidated those acceptance conclusions; T083-T085 independently reviewed and accepted the replacement preview.60 generation for semantic equivalence, native execution, and schema evidence

**Checkpoint**: The shared implementation and admission guards exist, and the preview.60 exact-head generation records accepted evidence for all four supported provider topologies. Unsupported MongoDB still cannot become ready.

---

## Phase 6: User Story 4 — Operate And Prove Replacement Readiness (Priority: P2)

**Goal**: Make deployment schema operations, concurrent admin initialization, highest-seam host behavior, test-objective continuity, and #646 handoff reproducible without expanding or coenabling EF.

**Independent Test**: Validate/status/plan/apply the exact schema, race two seeders, run login-to-protected-endpoint after restart, produce the Groundwork correctness digest, and prove the EF implementation tree remains frozen and separately selectable. The checked-in EF contract baseline is non-executed; #646 owns live equality.

### Tests For User Story 4

- [X] T069 [P] [US4] Write failing partial/missing seed configuration, password policy, concurrent two-instance idempotency, wildcard/catalog grants, dual lifecycle, and secret-safe logging tests in `tests/Elsa/Foundation/Identity/AspNetCoreIdentity/Groundwork/Tests/AspNetCoreIdentitySeederContractTests.cs`
- [X] T070 [P] [US4] Write the production-shaped Groundwork login/email/bad-password/lockout/cookie/claims/protected-endpoint/reopen scenario in `tests/Elsa/Foundation/Identity/AspNetCoreIdentity/Groundwork/Tests/AspNetCoreIdentityHighestSeamTests.cs`
- [X] T071 [P] [US4] Write the deterministic physical `iam-normalized-lookup-update` correctness schema/digest tests and explicit non-executed EF contract baseline in `tests/Elsa/Persistence/Groundwork/Conformance/Tests/AspNetCoreIdentityPerformanceWorkloadTests.cs`

### Implementation For User Story 4

- [X] T072 [US4] Move `IdentitySeedOptions` and Groundwork account/role orchestration out of the EF namespace into `src/Elsa/Foundation/Identity/AspNetCoreIdentity/Seeding/IdentitySeedOptions.cs` and `IdentitySeedCoordinator.cs`; keep EF seeding behavior/schema frozen for the temporary oracle
- [X] T073 [US4] Implement create-only/CAS admin role, user, wildcard/catalog permissions, membership, and role link convergence under explicit privileged scope in `src/Elsa/Foundation/Identity/AspNetCoreIdentity/Groundwork/Seeding/GroundworkIdentitySeeder.cs`
- [X] T074 [US4] Register one seeder instance through `IHostedService` and `IShellInitializer`, validate environment/credentials, and preserve development-only password logging in `src/Elsa/Foundation/Identity/AspNetCoreIdentity/Groundwork/DependencyInjection/AspNetCoreIdentityGroundworkRegistration.cs`
- [X] T075 [US4] Define the correctness-proven Identity workload and provider prerequisites in `specs/094-harden-groundwork-stores/workloads/iam-secrets.json` and link its digest contract from `specs/094-harden-groundwork-stores/contracts/performance-handoff.md`
- [X] T076 [US4] Extract shared observable scenario inputs/results without adding EF behavior or dependencies; replace the deleted fixed-metadata temporary-oracle test with `AspNetCoreIdentityEfContractBaselineTests.cs` and the frozen EF source-tree baseline/ratchet owned by the architecture tests
- [X] T077 [US4] Update Foundation Identity Groundwork README/extension-point catalogs, explicit feature selection, unsupported capabilities, topology, and CI/CD schema commands in `src/Elsa/Foundation/Identity/Persistence/Groundwork/EXTENSION_POINTS.md`, `src/Elsa/Foundation/Identity/AspNetCoreIdentity/Groundwork/README.md`, and root `EXTENSION_POINTS.md`
- [X] T078 [US4] Add opt-in host composition and direct conflict tests for `FoundationIdentityAspNetCoreIdentityGroundwork` in `tests/Elsa/Foundation/Identity/Tests/Api/EnabledShellCompositionTests.cs` while leaving default `shells.json` and production EF/OpenIddict selection unchanged for #646
- [X] T079 [US4] Historical preview.56 operational run: execute 100 consecutive two-instance seeding races, highest seam, schema CLI, provider workload, fixed-metadata EF contract test, and architecture ratchets. T084 invalidated its four-provider and EF-equality acceptance conclusions; T083-T085 accepted the preview.60 replacement for Groundwork correctness, while live EF equality remains owned by #646
- [X] T080 [US4] Commit and independently review the US4 exact HEAD for deployment-owned schema, seed concurrency/security, highest-seam parity, EF-oracle freeze, and #646 reproducibility; record the verdict

**Checkpoint**: The #644 implementation candidate has accepted preview.60 exact-head correctness evidence. The EF implementation tree is frozen and never active with Groundwork; its contract baseline is non-executed, and #646 owns live equality/timing.

---

## Phase 7: Polish, Cross-Cutting Review, And Landing

**Purpose**: Reconcile durable docs/maps/issues, run every quality gate, independently audit the exact candidate, and land through Model B.

- [X] T081 [P] Update #644/#629 links and current state in `docs/program-goals/zero-ef-persistence.md`, `docs/decision-maps/zero-ef-groundwork.md`, and `specs/094-harden-groundwork-stores/research.md` without claiming #646/#647 completion
- [X] T082 Historical map refresh before T084 remediation. Repeat the narrowest relevant architecture, feature-dependency, extension-point, and test map generation after preview.60 adoption and all T085 code/docs settle; review `docs/reports/maps-v2-findings.md` before continuing
- [X] T083 On preview.60, run `git diff --check`, format analyzers, Release build, all Identity/Groundwork/architecture tests, complete solution tests, pack validation, four-provider matrix, schema CLI matrix, restart probes, and quickstart commands; record exact counts/durations in `specs/095-groundwork-aspnetcore-identity/quickstart.md`
- [X] T084 Run an independent requirement-by-requirement audit of FR-001–FR-023, SC-001–SC-010, every advertised store interface, test-objective row, authority invariant, and provider evidence against exact branch HEAD; record findings in `specs/095-groundwork-aspnetcore-identity/quickstart.md`
- [X] T085 Remediate every blocking independent-review, CI, provider, schema, security, architecture, or correctness finding in its owning file, including the 25-objective/15-capability exact catalog, deterministic physical failure injection, mutation receipts and cleanup, real 100,000-record native routes, highest-seam HTTP coverage, four-provider schema parity/read-only hashes, preview.60 generic-codec/native-query-explain/schema-admission adoption behind the Elsa marker, coverage/evidence reconciliation, and generated-map refresh; then repeat T083-T084
- [X] T086 Commit the exact reviewed candidate with a useful message; verify `.specify/feature.json` selects spec 095, artifacts bind the clean tested code candidate, the post-candidate delta contains only evidence/Markdown ratification records, tasks/checklists are consistent, and the worktree is clean
- [X] T087 Push `codex/095-groundwork-aspnetcore-identity`, open a Model B draft PR linked to #644/#629, and include exact provider/schema/restart/test-objective evidence plus explicit #646/#647 remaining gates
- [ ] T088 Obtain every required GitHub check, resolve all actionable review findings, rerun affected local gates, promote the draft only after exact-head review, and record the candidate commit/check results
- [ ] T089 Merge the approved PR, verify remote `main` contains the reviewed result, update issue #644 with the merge/evidence and #646 handoff, and return the control room to the next zero-EF dependency without marking the overall program complete

---

## Dependencies And Execution Order

### Phase Dependencies

- **Setup (T001–T005)** starts from exact `e4d61bef`.
- **Foundation (T006–T017)** depends on Setup and blocks all user stories.
- **US1 (T018–T038)** depends on Foundation and is the MVP.
- **US2 (T039–T055)** depends on the US1 store/manager surface; its red tests may be authored while late US1 implementation stabilizes.
- **US3 (T056–T068)** fixture generalization may begin after Foundation, but accepted provider evidence depends on US1 and US2 semantics.
- **US4 (T069–T080)** seeder tests may begin after Foundation; final highest-seam and #646 handoff depend on US1–US3.
- **Polish/Landing (T081–T089)** depends on all four stories and exact reviewed evidence.

### User Story Dependencies

- **US1**: independent after Foundation; proves complete Identity behavior on SQLite.
- **US2**: consumes US1 stores but is independently verifiable through tenant/concurrency/failure-window contracts.
- **US3**: reuses US1/US2 public scenarios; provider fixtures/native plans can develop in parallel.
- **US4**: consumes the completed authority/provider path for operational seeding, highest seam, and performance handoff.

### Critical Path

`T001–T017 -> T018–T038 -> T039–T055 -> T056–T068 -> T069–T080 -> T081–T089`

## Parallel Opportunities

### Foundation

```text
Worker A: T006 then T014 registration/capability path
Worker B: T007 then authority architecture path
Worker C: T008, T009, T010 manifest/document path
Integrator: T011–T017 atomic primitives, registration ownership, review
```

### User Story 1

```text
Worker A: T018, T024 user fundamentals
Worker B: T019, T025 role fundamentals
Worker C: T020, T026–T030 relationships
Integrator: T021–T023, T031–T038 adapters/composition/highest seam
```

### User Story 2

```text
Worker A: T039, T045 tenant binding/non-disclosure
Worker B: T040, T042, T046–T047 revision/uniqueness/email
Worker C: T041, T043, T048–T051 atomicity/reconciliation
Integrator: T044, T052–T055 contracts/matrix/review
```

### User Story 3

```text
Worker A: T058 SQLite
Worker B: T059 SQL Server
Worker C: T060 PostgreSQL
Worker D: T061 MongoDB
Integrator: T056–T057, T062–T068 shared fixture/plans/evidence
```

## Implementation Strategy

### MVP First

1. Complete Setup and Foundation.
2. Complete US1 on real SQLite.
3. Stop and independently verify the full advertised capability set and one authority.
4. Do not enable the default host or remove the EF oracle at this checkpoint.

### Incremental Delivery

1. Land/review the authority foundation.
2. Land/review complete SQLite Identity behavior.
3. Land/review tenant/concurrency/atomicity.
4. Land/review four-provider evidence.
5. Land/review seeding/highest seam/#646 handoff.
6. Land one Model B PR for #644 only after all boundaries are integrated and green.

### Agent Handoff Rule

Every worker prompt names task IDs, exact file ownership, immutable base commit, red test, provider scope, conflicting sibling files, evidence it may update, and an independent review checkpoint. Workers do not edit the shared spec 094 ledger, manifests, solution files, or DI registrations concurrently; the root integrator owns those serialized surfaces.

## Notes

- `[P]` means file-level independence, not permission to merge before prerequisites.
- Tests fail for the missing behavior before implementation; infrastructure failures do not count.
- No task authorizes new EF behavior, migrations, packages, or deletion.
- The unconditional-upsert test remains until explicit architect approval is recorded for #647.
- Groundwork API/provider gaps land upstream and are consumed through a released version; none is currently known for #644.
- Commit and independently review each delivery boundary before push; root owns final integration, QA, PR promotion, and merge.
