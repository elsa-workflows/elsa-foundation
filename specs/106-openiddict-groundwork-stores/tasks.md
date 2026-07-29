# Tasks: OpenIddict Groundwork Stores

**Input**: Design documents from `/specs/106-openiddict-groundwork-stores/`

**Prerequisites**: `plan.md`, `spec.md`, `research.md`, `data-model.md`, `contracts/`, `quickstart.md`

**Tests**: The specification requires test-first contract, provider, failure, restart, host, performance, and dependency evidence. Test tasks therefore precede the corresponding implementation tasks.

**Organization**: Tasks are grouped by user story. Both P1 stories depend on the shared public-capability and storage foundation, but remain independently testable through their own public seams.

## Phase 1: Setup and Evidence Baseline

**Purpose**: Freeze the external denominator and prove that the exact public Groundwork family can support the feature before implementation begins.

- [X] T001 Record all 145 OpenIddict 7.5 application, authorization, scope, and token store members by capability group in `specs/106-openiddict-groundwork-stores/contracts/openiddict-member-ledger.md` — Evidence: 2026-07-25 root re-verification against the restored 7.5.0 contract XML returned Application=42, Authorization=32, Scope=28, and Token=43; the ledger freezes that 145-member denominator and its evidence owners without claiming implementation.
- [X] T002 [P] Inventory every existing OpenIddict, bearer, token-service, shell-composition, and development/demo guard test objective with its retained destination in `specs/106-openiddict-groundwork-stores/contracts/test-objective-ledger.md` — Evidence: 2026-07-25 preparation checkpoint retains the 54-objective baseline plus one current-main shared-host addendum (55 total); no deletion approved.
- [X] T003 [P] Add architecture tests that keep Identity abstractions and the retained OpenIddict behavior package free of Groundwork and new EF dependencies in `tests/Elsa/Architecture/OpenIddictPersistenceArchitectureTests.cs` — Evidence: the focused architecture selection passed 4/4 on 2026-07-25 after restoring the test project; the retained behavior package is EF/Groundwork-free and the frozen oracle remains isolated.
- [X] T004 Reconcile the complete configured Groundwork package and tool family without changing serialized package files in `Directory.Packages.props` and `.config/dotnet-tools.json` — Evidence: 2026-07-25 static inspection found one configured family and matching tool version; public restore/probe evidence remains T006.
- [X] T005 Add executable public-API probes for codec admission, physical entity definitions, naming/fingerprints, schema CLI/readiness, typed compound/range/multivalue routes, bounded mutations with native plans, CAS, and UoW in `tests/Elsa/Persistence/Groundwork/Conformance/Tests/OpenIddictGroundworkCapabilityProbeTests.cs` — Evidence: 2026-07-25 preview.88 execution passed 17/17, covering the exact package/tool family, four physical entities, codec admission, naming/fingerprint transformation, real CLI/readiness, multivalue membership and projection limits, CAS, cross-unit UoW, bounded mutation count/cancellation/native plans, reopen, and the four-provider contract. Exact-source preview.90 recertification at `a95362f33ca272e353088e37263e8a310bc0fd9a` passed the same 17/17 gate.
- [ ] T006 Run T005 against SQLite, SQL Server, PostgreSQL, and transaction-capable MongoDB and record package hashes, tool output, provider topology, and capability verdicts in `specs/106-openiddict-groundwork-stores/quickstart.md` — Historical evidence: preview.88 and preview.90 each passed 17/17 on all four providers. The current package line must be recertified after the serialized preview.97 import before this task can close.
- [X] T007 Create or link a Groundwork issue for every failed T005 capability and keep all dependent tasks blocked rather than adding provider-specific or client-side fallbacks in `specs/106-openiddict-groundwork-stores/research.md` — Evidence: implementation-oracle re-verification exposed two capabilities that the earlier synthetic probe did not cover; Groundwork #141 now owns fenced cross-unit relationship guards and Groundwork #143 owns fixed-value bounded assignment with matched-count semantics. Dependent application/authorization/token operations remain blocked instead of using client orchestration.

**Checkpoint**: The exact public package/tool family restores together and every hard prerequisite has executable proof. No later task starts while T006 reports a missing capability.

---

## Phase 2: Foundational Provider Boundary

**Purpose**: Create the concrete package, global physical declarations, canonical records, bounded routes, and registration/error seams shared by both P1 stories.

**⚠️ CRITICAL**: No user-story implementation starts until this phase is green.

- [X] T008 Scaffold the concrete provider package and test project in `src/Elsa/Foundation/Identity/OpenIddict/Groundwork/Elsa.Foundation.Identity.OpenIddict.Groundwork.csproj` and `tests/Elsa/Foundation/Identity/OpenIddict/Groundwork/Tests/Elsa.Foundation.Identity.OpenIddict.Groundwork.Tests.csproj` — Evidence: 2026-07-25 source-only scaffold references behavior and Groundwork boundaries without the EF oracle; it contains no production store code.
- [X] T009 Add both projects to `Elsa.Server.slnx` and add only provider-boundary project references in `src/Elsa/Foundation/Identity/OpenIddict/Groundwork/Elsa.Foundation.Identity.OpenIddict.Groundwork.csproj` — Evidence: the current-main replay registers the Behavior, Groundwork adapter, and adapter test projects in their canonical solution folders; the adapter project references only the provider-neutral Behavior package and Groundwork persistence boundaries.
- [X] T010 [P] Write red canonical JSON, descriptor round-trip, current/minimum-readable version, upcast, and corrupt/future payload tests for all four record kinds in `tests/Elsa/Foundation/Identity/OpenIddict/Groundwork/Tests/OpenIddictGroundworkCodecTests.cs` — Evidence: the 2026-07-29 current-main replay passed the 51-test adapter project; the codec selection covers all four canonical kinds, descriptor groups, current/minimum-readable/upcast policies, camel-case output, and corrupt, future, wrong-kind, and identity-mismatch rejection.
- [ ] T011 [P] Write red manifest tests for four global physical entity tables, stable logical identities, finite lengths, unique/compound/range/multivalue indexes, bounded query/mutation routes, naming-policy output, and schema fingerprint in `tests/Elsa/Foundation/Identity/OpenIddict/Groundwork/Tests/OpenIddictGroundworkStorageManifestTests.cs`
- [ ] T012 [P] Write red registration tests proving all four OpenIddict stores/managers resolve while existing server, validation, scheme selector, and `ITokenService` registrations remain unchanged in `tests/Elsa/Foundation/Identity/OpenIddict/Groundwork/Tests/OpenIddictGroundworkRegistrationTests.cs`
- [ ] T013 [P] Write red branch tests for unsupported generic delegates, readiness rejection, cancellation propagation, concurrency translation, corrupt payloads, and provider failures in `tests/Elsa/Foundation/Identity/OpenIddict/Groundwork/Tests/OpenIddictGroundworkFailureMappingTests.cs`
- [X] T014 Implement the four canonical global record types, opaque concurrency value, deterministic operation identity, and descriptor mappings in `src/Elsa/Foundation/Identity/OpenIddict/Groundwork/Models/` — Evidence: 2026-07-25 foundation commit `3bfdd4537` added four global record kinds, separate opaque/public concurrency and envelope versions, complete descriptor groups, and length-framed SHA-256 operation identities; focused tests passed.
- [X] T015 Bind Groundwork's version-aware document codec to per-kind OpenIddict version policies and JSON options without duplicating the generic codec in `src/Elsa/Foundation/Identity/OpenIddict/Groundwork/Serialization/OpenIddictGroundworkJson.cs` — Evidence: commit `3bfdd4537` binds the shared `VersionedJsonDocumentCodec` to four v1 policies and web JSON options; the post-merge adapter suite passed 28/28.
- [ ] T016 Declare four `PhysicalTableDefinition` entity tables, projected fields, linked multivalue relationships, indexes, bounded queries, bounded mutations, versions, and provider requirements in `src/Elsa/Foundation/Identity/OpenIddict/Groundwork/OpenIddictGroundworkStorageManifest.cs`
- [ ] T017 Contribute the OpenIddict declaration to the unified runtime/deployment schema source in `src/Elsa/Foundation/Identity/OpenIddict/Groundwork/OpenIddictGroundworkStorageManifestSource.cs` and `src/Elsa/Persistence/Groundwork/Unified/GroundworkUnifiedManifest.cs`
- [X] T018 Implement one immutable global OpenIddict store-session/UoW factory with readiness admission and no ambient tenant filtering in `src/Elsa/Foundation/Identity/OpenIddict/Groundwork/Stores/OpenIddictGroundworkStoreSessionFactory.cs` — Evidence: the 2026-07-29 current-main replay passed focused coverage for pre-session readiness rejection, cross-unit-atomic admission, ordinary-global-only acquisition, cancellation preservation, and provider-failure translation.
- [X] T019 Implement stable adapter-scoped capability, readiness, serialization, and provider exceptions plus OpenIddict concurrency mapping in `src/Elsa/Foundation/Identity/OpenIddict/Groundwork/Exceptions/` — Evidence: the replay exposes stable capability code `ELSA-OIDC-GW-001`, readiness/serialization/provider exception types, cancellation preservation, and OpenIddict concurrency translation; the current 51/51 adapter run covers the failure mapper and fail-closed generic translator.
- [ ] T020 Implement all-four-store replacement registration while preserving existing server/validation behavior in `src/Elsa/Foundation/Identity/OpenIddict/Groundwork/Extensions/OpenIddictGroundworkServiceCollectionExtensions.cs`
- [ ] T021 Extend the live §2.23 implementation/registration coverage ledger with every new manifest, mapper, store, session, route, mutation, exception, and provider branch in `specs/106-openiddict-groundwork-stores/research.md`
- [ ] T022 Run the foundational codec, manifest, registration, failure-mapping, and architecture tests and record the exact commands/results in `specs/106-openiddict-groundwork-stores/quickstart.md`

**Checkpoint**: Four global entities compile, every declared operation binds to a certified handler, unsupported shapes fail before I/O, all four stores resolve, and provider-neutral projects remain concrete-provider-free.

---

## Phase 3: User Story 1 — Issue, Refresh, Validate, and Revoke Tokens Reliably (Priority: P1) 🎯 MVP

**Goal**: Preserve the public token lifecycle, fail-closed validation, exactly-one-winner refresh redemption, revocation, and restart behavior.

**Independent Test**: Compose durable storage, issue access and refresh tokens through `ITokenService`, validate a protected request, race refresh redemption, revoke both token kinds, restart, and verify identical public outcomes.

### Tests for User Story 1

- [ ] T023 [P] [US1] Write red direct token-store tests for instantiate/CRUD/CAS, every scalar/collection accessor, id/reference/relationship/compound lookup, count, deterministic page, prune, revoke, and generic-delegate rejection in `tests/Elsa/Foundation/Identity/OpenIddict/Groundwork/Tests/GroundworkOpenIddictTokenStoreTests.cs`
- [ ] T024 [P] [US1] Rewire the existing token-service objectives into a provider-neutral fixture and add issue/lookup/expiry/redeem/revoke/restart scenarios in `tests/Elsa/Foundation/Identity/Tests/OpenIddict/OpenIddictTokenServiceContractTests.cs`
- [ ] T025 [P] [US1] Write red 100-race refresh redemption, revoke-versus-redeem, stale CAS, duplicate reference id, cancellation, and lost-acknowledgement recovery tests in `tests/Elsa/Foundation/Identity/OpenIddict/Groundwork/Tests/OpenIddictTokenAtomicityTests.cs`
- [ ] T026 [P] [US1] Write red public bearer tests for valid, revoked, redeemed, expired, unknown, and malformed entries with non-disclosure assertions in `tests/Elsa/Foundation/Identity/Tests/OpenIddict/OpenIddictGroundworkBearerAuthenticationTests.cs`

### Implementation for User Story 1

- [ ] T027 [US1] Implement all 43 OpenIddict token-store members and descriptor round trips in `src/Elsa/Foundation/Identity/OpenIddict/Groundwork/Stores/GroundworkOpenIddictTokenStore.cs`
- [ ] T028 [US1] Bind token point, unique-reference, relationship, compound, count, and deterministic page operations to declared server-side routes in `src/Elsa/Foundation/Identity/OpenIddict/Groundwork/Stores/GroundworkOpenIddictTokenStore.Queries.cs`
- [ ] T029 [US1] Implement expected-version create/update/delete, unique reference enforcement, concurrency-value rotation, and stale-write translation in `src/Elsa/Foundation/Identity/OpenIddict/Groundwork/Stores/GroundworkOpenIddictTokenStore.Mutations.cs`
- [ ] T030 [US1] Implement atomic redeem/revoke and bounded prune/revoke with exact counts, cancellation, operation fingerprints, lost-acknowledgement inspection, and restart convergence in `src/Elsa/Foundation/Identity/OpenIddict/Groundwork/Stores/GroundworkOpenIddictTokenStore.Atomic.cs`
- [ ] T031 [US1] Rebind existing `OpenIddictTokenService` and token-entry validation to the Groundwork store without changing their public caller workflow in `src/Elsa/Foundation/Identity/OpenIddict/Extensions/OpenIddictIdentityServiceCollectionExtensions.cs`
- [ ] T032 [US1] Run direct token, token-service, bearer, race, failure, disposal/reopen, and restart tests on SQLite and record the result digest in `specs/106-openiddict-groundwork-stores/quickstart.md`

**Checkpoint**: Token lifecycle behavior is independently complete on durable SQLite, every refresh race has exactly one winner, and invalid tokens fail closed.

---

## Phase 4: User Story 2 — Manage the Complete Authorization Registry (Priority: P1)

**Goal**: Complete every application, authorization, scope, and token store contract operation with deterministic bounded execution and atomic relationship behavior.

**Independent Test**: Run one black-box suite over all four stores covering the 145-member denominator, descriptor round trips, uniqueness, relationships, pages/counts, filters, cleanup, cancellation, concurrency, failure recovery, and restart.

### Tests for User Story 2

- [ ] T033 [P] [US2] Write red 42-member application-store contract tests including client-id uniqueness, redirect/post-logout membership, generic delegates, CAS, pages, and descriptors in `tests/Elsa/Foundation/Identity/OpenIddict/Groundwork/Tests/GroundworkOpenIddictApplicationStoreTests.cs`
- [ ] T034 [P] [US2] Write red 32-member authorization-store contract tests including compound filters, scope membership, revoke/prune, generic delegates, CAS, pages, and descriptors in `tests/Elsa/Foundation/Identity/OpenIddict/Groundwork/Tests/GroundworkOpenIddictAuthorizationStoreTests.cs`
- [X] T035 [P] [US2] Write red 28-member scope-store contract tests including name uniqueness, name-set/resource membership, generic delegates, CAS, pages, and descriptors in `tests/Elsa/Foundation/Identity/OpenIddict/Groundwork/Tests/GroundworkOpenIddictScopeStoreTests.cs` — Evidence: 2026-07-25 root verification passed the direct Scope/manifest selection 23/23 on durable SQLite with a fresh leased provider connection per session; it covers all 28 public members, exact duplicate-name/concurrency behavior, descriptor round trips, deterministic named/page queries, empty/oversized fail-before-session bounds, cancellation, and fail-closed generic delegates.
- [ ] T036 [P] [US2] Write red cross-store uniqueness races, dependent application cleanup, orphan prevention, cancellation, failure-window, acknowledgement-loss, recovery, and restart tests in `tests/Elsa/Foundation/Identity/OpenIddict/Groundwork/Tests/OpenIddictRegistryAtomicityTests.cs`
- [ ] T037 [P] [US2] Build the shared black-box 145-member scenario fixture and provider-neutral result digest in `tests/Elsa/Foundation/Identity/OpenIddict/Conformance/OpenIddictStoreContractSuite.cs`

### Implementation for User Story 2

- [ ] T038 [P] [US2] Implement all 42 OpenIddict application-store members, descriptors, bounded queries, unique client id, redirect membership, CAS, and generic-delegate rejection in `src/Elsa/Foundation/Identity/OpenIddict/Groundwork/Stores/GroundworkOpenIddictApplicationStore.cs`
- [ ] T039 [P] [US2] Implement all 32 OpenIddict authorization-store members, descriptors, compound/scope routes, CAS, bounded revoke/prune, exact counts, and generic-delegate rejection in `src/Elsa/Foundation/Identity/OpenIddict/Groundwork/Stores/GroundworkOpenIddictAuthorizationStore.cs`
- [X] T040 [P] [US2] Implement all 28 OpenIddict scope-store members, descriptors, unique name, name-set/resource membership, CAS, pages, and generic-delegate rejection in `src/Elsa/Foundation/Identity/OpenIddict/Groundwork/Stores/GroundworkOpenIddictScopeStore.cs` — Evidence: the 2026-07-25 Scope slice uses only admitted Groundwork document/query routes, pages full enumerations in fixed 256-row provider requests with the compiled provider-side identity tie-break, caps name-set input at 900 distinct values, preserves expected-version CAS and opaque token rotation, and passed 23/23 direct plus 12/12 non-mutation public-capability tests.
- [ ] T041 [US2] Implement application-dependent authorization/token delete and revoke decisions as one recoverable UoW with no orphaned relationships in `src/Elsa/Foundation/Identity/OpenIddict/Groundwork/Stores/OpenIddictGroundworkRelationshipCoordinator.cs`
- [ ] T042 [US2] Prove every row in `openiddict-member-ledger.md` is implemented and directly covered, then record method/test identities in `specs/106-openiddict-groundwork-stores/contracts/openiddict-member-ledger.md`
- [ ] T043 [US2] Run the complete registry suite on durable SQLite with independent clients, cancellation, all failure windows, disposal/reopen, and process restart and record the canonical digest in `specs/106-openiddict-groundwork-stores/quickstart.md`

**Checkpoint**: The full OpenIddict store surface is independently complete on SQLite with bounded execution, exact counts, atomic relationships, and no generic-query fallback.

---

## Phase 5: User Story 3 — Choose and Operate One Durable Provider (Priority: P2)

**Goal**: Make SQLite, SQL Server, PostgreSQL, and transaction-capable MongoDB truthful host choices using one schema/readiness path and one behavior suite.

**Independent Test**: Compose each provider independently, plan/validate/apply/status the same declaration, run both P1 suites, close/reopen/restart, and compare domain digests plus native plan evidence.

### Tests for User Story 3

- [ ] T044 [P] [US3] Add SQLite, SQL Server, PostgreSQL, and replica-set MongoDB drivers for the shared OpenIddict suite in `tests/Elsa/Foundation/Identity/OpenIddict/Conformance/Providers/`
- [ ] T045 [P] [US3] Write schema plan/validate/status/apply, naming transformation/collision, fingerprint, drift, capability, topology, and runtime validate-only tests in `tests/Elsa/Foundation/Identity/OpenIddict/Groundwork/Tests/OpenIddictGroundworkSchemaReadinessTests.cs`
- [ ] T046 [P] [US3] Write native query/mutation-plan admission tests for every scale-bearing route on all four providers in `tests/Elsa/Foundation/Identity/OpenIddict/Conformance/OpenIddictNativePlanEvidenceTests.cs`
- [ ] T047 [P] [US3] Write production-shaped sign-in, protected request, issue, refresh, replay rejection, revoke, and restart scenarios in `tests/Elsa/Foundation/Identity/OpenIddict/Conformance/OpenIddictHostAcceptanceTests.cs`

### Implementation for User Story 3

- [ ] T048 [US3] Wire the OpenIddict manifest into the shared schema CLI and runtime readiness source for all four providers in `src/Elsa/Persistence/Groundwork/Unified/ElsaGroundworkSchema.cs`
- [ ] T049 [US3] Apply host naming policy once, provider normalization once, and collision diagnostics naming both logical owners in `src/Elsa/Foundation/Identity/OpenIddict/Groundwork/OpenIddictGroundworkStorageManifestSource.cs`
- [ ] T050 [P] [US3] Add provider-specific OpenIddict composition adapters only where host registration requires them in `src/Elsa/Persistence/Groundwork/Sqlite/Unified/`, `src/Elsa/Persistence/Groundwork/SqlServer/Unified/`, `src/Elsa/Persistence/Groundwork/PostgreSql/Unified/`, and `src/Elsa/Persistence/Groundwork/MongoDb/Unified/`
- [ ] T051 [US3] Run CLI plan/validate/status/apply and both shared P1 suites on all four real providers with independent clients, failure windows, close/reopen, process restart, and Mongo transaction-topology admission in `specs/106-openiddict-groundwork-stores/evidence/provider-matrix.json`
- [ ] T052 [US3] Capture provider-native query and mutation plans, resolved physical targets, manifest fingerprint, topology, result digest, and bounded-execution verdict for every scale-bearing operation in `specs/106-openiddict-groundwork-stores/evidence/provider-plans/`
- [ ] T053 [US3] Switch the production-shaped identity acceptance host to the selected Groundwork provider path and run T047 on all four providers in `tests/Elsa/Foundation/Identity/OpenIddict/Conformance/OpenIddictHostAcceptanceTests.cs`

**Checkpoint**: All four advertised providers produce identical public outcomes and truthful schema, topology, restart, and native-plan evidence.

---

## Phase 6: User Story 4 — Maintain One First-Party Persistence Path (Priority: P3)

**Goal**: Obtain the shared performance verdict, remove the OpenIddict EF implementation and host escape hatches, and ratchet the repository against reintroduction.

**Independent Test**: Run the benchmark acceptance catalog and audit source/project/package/host/resolved dependency graphs; the repository contains no OpenIddict EF artifact and core projects contain no concrete-provider dependency.

### Tests for User Story 4

- [ ] T054 [P] [US4] Add fixed 1K correctness and 100K/1M scale dataset generators plus result digests for the accepted OpenIddict workload catalog in `tests/Elsa/Persistence/Groundwork/Benchmarks/OpenIddict/OpenIddictBenchmarkDataset.cs`
- [ ] T055 [P] [US4] Add token issue/lookup/refresh/revoke/prune, authorization filter/revoke, application-client, and scope-resource workload adapters for EF oracle and Groundwork physical forms in `tests/Elsa/Persistence/Groundwork/Benchmarks/OpenIddict/`
- [ ] T056 [P] [US4] Add a repository guard that reports the complete direct/transitive dependency path for any `Microsoft.EntityFrameworkCore*` reintroduction in `tests/Elsa/Architecture/Tests/ZeroEfCoreArchitectureTests.cs`

### Implementation for User Story 4

- [ ] T057 [US4] Declare every benchmark dataset, payload, concurrency, warm/cold mode, provider, physical form, correctness digest, and mandatory observation before timing in `specs/106-openiddict-groundwork-stores/evidence/performance-manifest.json`
- [ ] T058 [US4] Run the shared #646 benchmark protocol for all OpenIddict workloads and physical forms, preserving raw results and native plans in `specs/106-openiddict-groundwork-stores/evidence/performance/`
- [ ] T059 [US4] Record the #646 pass/redesign/blocked decision for every workload and remediate any rejected physical form before continuing in `specs/106-openiddict-groundwork-stores/quickstart.md`
- [ ] T060 [US4] Switch `Elsa.Server` OpenIddict composition and development/demo behavior from EF/InMemory to the Groundwork implementation in `src/Apps/Elsa.Server/Program.cs` and `src/Elsa/Foundation/Identity/OpenIddict/Extensions/OpenIddictIdentityServiceCollectionExtensions.cs`
- [ ] T061 [US4] Migrate every retained EF-backed OpenIddict test objective to Groundwork/shared fixtures and obtain recorded approval for any deletion in `specs/106-openiddict-groundwork-stores/contracts/test-objective-ledger.md`
- [ ] T062 [US4] Delete the OpenIddict EF DbContext, initializer, SQLite factory, migrations, EF/InMemory registration, and EF-only source under `src/Elsa/Foundation/Identity/OpenIddict/EntityFrameworkCore/`
- [ ] T063 [US4] Remove OpenIddict EF/InMemory/SQLite package references, project references, settings, and build artifacts from `src/Elsa/Foundation/Identity/OpenIddict/Elsa.Foundation.Identity.OpenIddict.csproj`, `Directory.Packages.props`, `Elsa.Server.slnx`, and `src/Apps/Elsa.Server/`
- [ ] T064 [US4] Run source, project, package-lock/assets, host, and resolved dependency audits and make `ZeroEfCoreArchitectureTests` fail with a full path for a deliberate temporary reintroduction before removing the mutation in `tests/Elsa/Architecture/Tests/ZeroEfCoreArchitectureTests.cs`
- [ ] T065 [US4] Run the full OpenIddict, Identity, API, shell, architecture, Groundwork fast-gate, and Release solution suites on the exact cleanup head and record results in `specs/106-openiddict-groundwork-stores/quickstart.md`

**Checkpoint**: OpenIddict has one first-party Groundwork implementation, no EF artifact or escape hatch remains, and the dependency ratchet is executable.

---

## Phase 7: Documentation, Maps, and Landing

**Purpose**: Finish operator/extender guidance, update generated facts, independently audit the reviewed head, and land through Model B.

- [ ] T066 [P] Document provider selection, global classification, topology, schema CLI/readiness, naming policy, bounded/generic-query boundary, failure/recovery behavior, and EF absence in `src/Elsa/Foundation/Identity/OpenIddict/Groundwork/README.md`
- [ ] T067 [P] Update the Groundwork and Identity extension-point catalogs and architecture navigation in `src/Elsa/Persistence/Groundwork/EXTENSION_POINTS.md`, `EXTENSION_POINTS.md`, and `docs/maps/README.md`
- [ ] T068 Refresh the authorized project, domain, extension-point, architecture-reference, and feature-dependency maps with `tools/maps/generate-maps.sh`, `tools/maps/generate-domain-map.sh`, `tools/maps/generate-extension-point-map.sh`, `tools/maps/generate-architecture-reference-map.sh`, and `tools/maps/generate-feature-dependency-map.sh`
- [ ] T069 Review generated findings and update only canonical source-of-truth layers, including the zero-EF constitution transition state when repository-wide compliance is actually reached, in `docs/reports/` and `.specify/memory/constitution.md`
- [ ] T070 Run Speckit analysis plus an independent exact-HEAD review of FR-001–FR-027, SC-001–SC-008, coverage-ledger rows, evidence artifacts, and dependency boundaries in `specs/106-openiddict-groundwork-stores/`
- [ ] T071 Remediate every blocker from T070, rerun the affected focused/full checks, and commit a clean reviewed head in `specs/106-openiddict-groundwork-stores/quickstart.md`
- [ ] T072 Push the organization branch, open a draft PR, pass required checks, mark ready, merge to `main`, and verify the reviewed commit is an ancestor of `origin/main`
- [ ] T073 Close or update the owning zero-EF/OpenIddict issue with merged commit, evidence locations, performance verdicts, and any explicitly deferred external work, then record the issue link in `specs/106-openiddict-groundwork-stores/quickstart.md`

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: T001–T003 may begin immediately. T004–T006 form the hard public-capability gate; T007 records upstream gaps if that gate fails.
- **Foundational (Phase 2)**: Depends on a passing T006 and blocks every user story.
- **User Story 1 (Phase 3)** and **User Story 2 (Phase 4)**: Both depend on Phase 2. Their red tests and store implementations can proceed in parallel by file, but T041 consumes the completed token mutation seam.
- **User Story 3 (Phase 5)**: Depends on both P1 stories so the same complete suite can run on every provider.
- **User Story 4 (Phase 6)**: Performance setup may begin after the P1 stores stabilize; host switch and EF deletion depend on all Phase 5 evidence and passing T059 verdicts.
- **Documentation/Landing (Phase 7)**: Documentation may start after interfaces stabilize; map refresh, exact-HEAD audit, and landing depend on Phase 6.

### User Story Dependencies

- **US1 (P1)**: Independently demonstrates the security-critical public token lifecycle after Phase 2.
- **US2 (P1)**: Independently demonstrates the complete registry surface after Phase 2; dependent application cleanup integrates the token seam from US1.
- **US3 (P2)**: Proves both P1 stories across the four advertised providers and the shared deployment path.
- **US4 (P3)**: Consumes provider and performance verdicts, then removes the transitional EF lane.

### Within Each User Story

- Write the story's red tests before implementation and preserve their red-before-green evidence.
- Complete canonical models/declarations before store behavior.
- Complete named reads before compound atomic mutations that depend on them.
- Run SQLite correctness before the real four-provider matrix.
- Do not delete EF until correctness, readiness, provider, host, and performance exit gates all pass.

### Parallel Opportunities

- T002 and T003 are independent inventories/guards.
- T010–T013 cover separate foundational seams.
- T023–T026 cover separate US1 contract levels.
- T033–T037 cover separate US2 store/cross-store seams.
- T038–T040 implement separate store files after their red tests.
- T044–T047 cover separate provider/schema/plan/host evidence.
- T054–T056 cover separate performance and dependency-guard seams.
- T066 and T067 update separate documentation surfaces.

---

## Parallel Example: User Story 2

```text
Task: "T033 application-store contract tests"
Task: "T034 authorization-store contract tests"
Task: "T035 scope-store contract tests"
Task: "T036 registry atomicity tests"
Task: "T037 shared 145-member contract fixture"

After the corresponding red tests:
Task: "T038 application store"
Task: "T039 authorization store"
Task: "T040 scope store"
```

---

## Implementation Strategy

### MVP First

1. Complete the public Groundwork capability gate.
2. Complete the shared provider boundary.
3. Complete User Story 1 on durable SQLite.
4. Stop and verify token issue/refresh/validate/revoke, race, failure, and restart behavior independently.

### Incremental Delivery

1. Add the complete application/authorization/scope registry surface.
2. Run both P1 suites on all four providers and through the deployment readiness path.
3. Obtain reproducible #646 physical-form verdicts.
4. Switch the real host and delete the EF OpenIddict lane.
5. Refresh facts, audit exact HEAD, and merge through Model B.

### Workroom Execution

1. The root agent owns architecture, integration, review, verification, and landing.
2. Bounded manifest, individual-store, provider-driver, benchmark-adapter, and documentation tasks may run in parallel on isolated worktrees.
3. Every delegated result is reviewed and re-tested by the root before integration.
4. Commit coherent work units; do not push or open a PR until their local acceptance gate is green.

---

## Notes

- `[P]` tasks touch independent files and have no dependency on another incomplete task in the same phase.
- `[USn]` labels map tasks to the four user stories in `spec.md`.
- Every task names an exact file or directory and is intended to be executable without rediscovering the architecture.
- The Elsa/framework constitutions are draft/provisional; ADR 0042 governs the accepted zero-EF product target.
- Exact-head public capability proof is a hard prerequisite, not a documentation formality.
