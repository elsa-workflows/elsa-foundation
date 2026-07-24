# Tasks: OpenIddict Groundwork Stores

**Input**: Design documents from `/specs/106-openiddict-groundwork-stores/`

**Prerequisites**: `plan.md`, `spec.md`, `research.md`, `data-model.md`, `contracts/`, `quickstart.md`

**Tests**: The specification requires test-first contract, provider, failure, restart, host, performance, and dependency evidence. Test tasks therefore precede the corresponding implementation tasks.

**Organization**: Tasks are grouped by user story. Both P1 stories depend on the shared public-capability and storage foundation, but remain independently testable through their own public seams.

## Phase 1: Setup and Evidence Baseline

**Purpose**: Freeze the external denominator and prove that the exact public Groundwork family can support the feature before implementation begins.

- [X] T001 Record all 145 OpenIddict 7.5 application, authorization, scope, and token store members by capability group in `specs/106-openiddict-groundwork-stores/contracts/openiddict-member-ledger.md`
  - Evidence: the package XML reproduction reports 42 application, 32 authorization, 28 scope, and 43 token members.
- [X] T002 [P] Inventory every existing OpenIddict, bearer, token-service, shell-composition, and development/demo guard test objective with its retained destination in `specs/106-openiddict-groundwork-stores/contracts/test-objective-ledger.md`
  - Evidence: the ledger retains 54 objectives: 23 direct, 7 guard/shell, 9 shared token-endpoint-host, 12 mixed Groundwork-Identity/EF-OpenIddict HTTP, and 3 provider-module objectives.
- [X] T003 [P] Add architecture tests that keep Identity abstractions and the retained OpenIddict behavior project free of Groundwork and new EF dependencies in `tests/Elsa/Architecture/OpenIddictPersistenceArchitectureTests.cs`
  - Evidence: the focused guard passes, excludes the nested `Groundwork/` provider boundary from behavior-source scanning, requires its explicit `Compile Remove`, and freezes the four transitional EF package references.
- [X] T004 Verify, without changing the central pins in this work unit, that the complete Groundwork package and tool family is the exact publicly restorable `0.0.1-preview.81` release in `Directory.Packages.props` and `.config/dotnet-tools.json`
  - Evidence: after merging `main` at `78033cf1167071123cb9fe5ef38653973bd65200`, all seven libraries and the CLI tool are aligned on the publicly restorable preview.81 family.
- [X] T005 Add executable public-API probes for codec admission, physical definitions, naming/fingerprints, schema CLI/readiness, typed compound/range/multivalue routes, bounded mutations with native plans, CAS, and UoW in `tests/Elsa/Persistence/Groundwork/Conformance/Tests/OpenIddictGroundworkCapabilityProbeTests.cs`
  - Evidence: the complete focused preview.81 capability probe passes 9/9; the remaining linked-multivalue choice is the separately enforced T007A Elsa physical-form decision, not a missing public capability.
- [ ] T006 Run the reviewed T005 physical shape against real SQLite, SQL Server, PostgreSQL, and transaction-capable MongoDB and record package hashes, tool output, provider topology, native query/mutation evidence, and capability verdicts in `specs/106-openiddict-groundwork-stores/quickstart.md`
  - Static package capability reports are admission inputs only and do not satisfy native provider conformance.
- [X] T007 Create or link a Groundwork issue for every failed T005 capability and keep all dependent tasks blocked rather than adding provider-specific or client-side fallbacks in `specs/106-openiddict-groundwork-stores/research.md`
  - Evidence: T005 passes 9/9 with no failed Groundwork capability requiring an upstream issue; T007A records the distinct Elsa physical-form blocker and keeps T006 and all dependent implementation work closed.
- [X] T007A Record and enforce an Elsa architecture blocker when the public package offers an alternative physical form but using it would change the approved design
  - Evidence: `research.md`, `data-model.md`, and the focused probe block production scaffolding pending a shared/dedicated-versus-additional-membership-unit decision and #646 verdict.

**Checkpoint**: The exact public package/tool family restores together and every hard prerequisite has executable proof. No later task starts while T006 reports a missing capability.

---

## Phase 2: Foundational Provider Boundary

**Purpose**: Create the concrete package, global physical declarations, canonical records, bounded routes, and registration/error seams shared by both P1 stories.

**⚠️ CRITICAL**: No user-story implementation starts until this phase is green.

- [X] T008 Scaffold the concrete provider package and test project in `src/Elsa/Foundation/Identity/OpenIddict/Groundwork/Elsa.Foundation.Identity.OpenIddict.Groundwork.csproj` and `tests/Elsa/Foundation/Identity/OpenIddict/Groundwork/Tests/Elsa.Foundation.Identity.OpenIddict.Groundwork.Tests.csproj`
  - Evidence: both projects restore and compile directly without solution registration; the provider project references only the retained OpenIddict behavior and Groundwork provider-boundary projects.
- [X] T008A Exclude nested `Groundwork/**/*.cs`, resources, and project-local build artifacts from `src/Elsa/Foundation/Identity/OpenIddict/Elsa.Foundation.Identity.OpenIddict.csproj` before adding source below that directory
  - Evidence: the behavior project now removes nested provider source/resources/build artifacts, and the focused architecture guard enforces the source exclusion.
- [X] T009 Add both projects to `Elsa.Server.slnx` and add only provider-boundary project references in `src/Elsa/Foundation/Identity/OpenIddict/Groundwork/Elsa.Foundation.Identity.OpenIddict.Groundwork.csproj`
  - Evidence: both projects occupy their canonical collapsed OpenIddict solution folders; the provider project references the retained OpenIddict behavior plus the Groundwork provider boundary and composition projects, with no concrete provider leaf.
- [X] T010 [P] Write red canonical JSON, descriptor round-trip, current/minimum-readable version, upcast, and corrupt/future payload tests for all four record kinds in `tests/Elsa/Foundation/Identity/OpenIddict/Groundwork/Tests/OpenIddictGroundworkCodecTests.cs`
  - Red evidence: 14 compiled tests fail because `Serialization.OpenIddictGroundworkJson` does not exist; no codec, model, manifest, or fallback implementation was added.
- [ ] T011 [P] After T007A is resolved, write red manifest tests for four global logical units using the reviewed physical forms, stable identities, SQL Server-safe finite lengths, unique/compound/range/multivalue indexes, bounded query/mutation routes, naming-policy output, and schema fingerprint in `tests/Elsa/Foundation/Identity/OpenIddict/Groundwork/Tests/OpenIddictGroundworkStorageManifestTests.cs`
- [X] T012 [P] Write red registration tests proving all four OpenIddict stores/managers resolve while existing server, validation, scheme selector, and `ITokenService` registrations remain unchanged in `tests/Elsa/Foundation/Identity/OpenIddict/Groundwork/Tests/OpenIddictGroundworkRegistrationTests.cs`
  - Red evidence: 4 compiled tests fail on the missing feature and Groundwork registration extension before any store or manager can resolve.
- [X] T013 [P] Write red branch tests for unsupported generic delegates, readiness rejection, cancellation propagation, concurrency translation, corrupt payloads, and provider failures in `tests/Elsa/Foundation/Identity/OpenIddict/Groundwork/Tests/OpenIddictGroundworkFailureMappingTests.cs`
  - Red evidence: 6 compiled tests fail on the missing adapter failure mapper; cancellation, OpenIddict concurrency, corrupt-payload context, readiness, capability, and provider-failure contracts are fixed without client evaluation.
- [ ] T014 Implement the four canonical global record types, opaque concurrency value, deterministic operation identity, and descriptor mappings in `src/Elsa/Foundation/Identity/OpenIddict/Groundwork/Models/`
- [ ] T015 Bind Groundwork's version-aware document codec to per-kind OpenIddict version policies and JSON options without duplicating the generic codec in `src/Elsa/Foundation/Identity/OpenIddict/Groundwork/Serialization/OpenIddictGroundworkJson.cs`
- [ ] T016 Declare the four logical units using the T007A-reviewed `PhysicalTableDefinition` forms, projected fields, linked multivalue relationships/units, indexes, bounded queries, bounded mutations, versions, and provider requirements in `src/Elsa/Foundation/Identity/OpenIddict/Groundwork/OpenIddictGroundworkStorageManifest.cs`
- [ ] T017 Contribute the OpenIddict declaration through `src/Elsa/Foundation/Identity/OpenIddict/Groundwork/OpenIddictGroundworkStorageManifestSource.cs` to the current unified runtime/deployment composition consumed by `src/Elsa/Persistence/Groundwork/Unified/Composition/GroundworkPhysicalSchemaManifestSource.cs`
- [ ] T018 Implement one immutable global OpenIddict store-session/UoW factory with readiness admission and no ambient tenant filtering in `src/Elsa/Foundation/Identity/OpenIddict/Groundwork/Stores/OpenIddictGroundworkStoreSessionFactory.cs`
- [ ] T019 Implement stable adapter-scoped capability, readiness, serialization, and provider exceptions plus OpenIddict concurrency mapping in `src/Elsa/Foundation/Identity/OpenIddict/Groundwork/Exceptions/`
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
- [ ] T035 [P] [US2] Write red 28-member scope-store contract tests including name uniqueness, name-set/resource membership, generic delegates, CAS, pages, and descriptors in `tests/Elsa/Foundation/Identity/OpenIddict/Groundwork/Tests/GroundworkOpenIddictScopeStoreTests.cs`
- [ ] T036 [P] [US2] Write red cross-store uniqueness races, dependent application cleanup, orphan prevention, cancellation, failure-window, acknowledgement-loss, recovery, and restart tests in `tests/Elsa/Foundation/Identity/OpenIddict/Groundwork/Tests/OpenIddictRegistryAtomicityTests.cs`
- [ ] T037 [P] [US2] Build the shared black-box 145-member scenario fixture and provider-neutral result digest in `tests/Elsa/Foundation/Identity/OpenIddict/Conformance/OpenIddictStoreContractSuite.cs`

### Implementation for User Story 2

- [ ] T038 [P] [US2] Implement all 42 OpenIddict application-store members, descriptors, bounded queries, unique client id, redirect membership, CAS, and generic-delegate rejection in `src/Elsa/Foundation/Identity/OpenIddict/Groundwork/Stores/GroundworkOpenIddictApplicationStore.cs`
- [ ] T039 [P] [US2] Implement all 32 OpenIddict authorization-store members, descriptors, compound/scope routes, CAS, bounded revoke/prune, exact counts, and generic-delegate rejection in `src/Elsa/Foundation/Identity/OpenIddict/Groundwork/Stores/GroundworkOpenIddictAuthorizationStore.cs`
- [ ] T040 [P] [US2] Implement all 28 OpenIddict scope-store members, descriptors, unique name, name-set/resource membership, CAS, pages, and generic-delegate rejection in `src/Elsa/Foundation/Identity/OpenIddict/Groundwork/Stores/GroundworkOpenIddictScopeStore.cs`
- [ ] T041 [US2] Implement application-dependent authorization/token delete and revoke decisions as one recoverable UoW with no orphaned relationships in `src/Elsa/Foundation/Identity/OpenIddict/Groundwork/Stores/OpenIddictGroundworkRelationshipCoordinator.cs`
- [ ] T042 [US2] Prove every row in `openiddict-member-ledger.md` is implemented and directly covered, then record method/test identities in `specs/106-openiddict-groundwork-stores/contracts/openiddict-member-ledger.md`
- [ ] T043 [US2] Run the complete registry suite on durable SQLite with independent clients, cancellation, all failure windows, disposal/reopen, and process restart and record the canonical digest in `specs/106-openiddict-groundwork-stores/quickstart.md`

**Checkpoint**: The full OpenIddict store surface is independently complete on SQLite with bounded execution, exact counts, atomic relationships, and no generic-query fallback.

---

## Phase 5: User Story 3 — Choose and Operate One Durable Provider (Priority: P2)

**Goal**: Make SQLite, SQL Server, PostgreSQL, and transaction-capable MongoDB truthful host choices using one schema/readiness path and one behavior suite.

**Independent Test**: Compose each provider independently, plan/validate/apply/status the same declaration, run both P1 suites, close/reopen/restart, and compare domain digests plus native plan evidence.

### Tests for User Story 3

- [ ] T044 [P] [US3] Reuse the existing SQLite, SQL Server, PostgreSQL, and replica-set MongoDB drivers in `tests/Elsa/Persistence/Groundwork/Testing/` from the shared OpenIddict suite; add an OpenIddict-specific driver only if the shared contract cannot express a required scenario
- [ ] T045 [P] [US3] Write schema plan/validate/status/apply, naming transformation/collision, fingerprint, drift, capability, topology, and runtime validate-only tests in `tests/Elsa/Foundation/Identity/OpenIddict/Groundwork/Tests/OpenIddictGroundworkSchemaReadinessTests.cs`
- [ ] T046 [P] [US3] Write native query/mutation-plan admission tests for every scale-bearing route on all four providers in `tests/Elsa/Foundation/Identity/OpenIddict/Conformance/OpenIddictNativePlanEvidenceTests.cs`
- [ ] T047 [P] [US3] Write production-shaped sign-in, protected request, issue, refresh, replay rejection, revoke, and restart scenarios in `tests/Elsa/Foundation/Identity/OpenIddict/Conformance/OpenIddictHostAcceptanceTests.cs`

### Implementation for User Story 3

- [ ] T048 [US3] Wire the OpenIddict manifest into the shared schema CLI and runtime readiness composition behind `src/Elsa/Persistence/Groundwork/Unified/Composition/GroundworkPhysicalSchemaManifestSource.cs` for all four providers
- [ ] T049 [US3] Apply host naming policy once, provider normalization once, and collision diagnostics naming both logical owners in `src/Elsa/Foundation/Identity/OpenIddict/Groundwork/OpenIddictGroundworkStorageManifestSource.cs`
- [ ] T050 [P] [US3] Add provider-specific OpenIddict composition adapters only if an executable host-registration test proves the provider-independent unified substrate is insufficient; otherwise close this task as not required with evidence
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
- [ ] T056 [P] [US4] Extend the existing `tests/Elsa/Architecture/EfCoreSurfaceRatchetTests.cs` and scanner so final cleanup reports the complete direct/transitive dependency path for any `Microsoft.EntityFrameworkCore*` reintroduction

### Implementation for User Story 4

- [ ] T057 [US4] Declare every benchmark dataset, payload, concurrency, warm/cold mode, provider, physical form, correctness digest, and mandatory observation before timing in `specs/106-openiddict-groundwork-stores/evidence/performance-manifest.json`
- [ ] T058 [US4] Run the shared #646 benchmark protocol for all OpenIddict workloads and physical forms, preserving raw results and native plans in `specs/106-openiddict-groundwork-stores/evidence/performance/`
- [ ] T059 [US4] Record the #646 pass/redesign/blocked decision for every workload and remediate any rejected physical form before continuing in `specs/106-openiddict-groundwork-stores/quickstart.md`
- [ ] T060 [US4] Switch `Elsa.Server` OpenIddict composition and development/demo behavior from EF/InMemory to the Groundwork implementation in `src/Apps/Elsa.Server/shells.json`, `src/Apps/Elsa.Server/shells.Production.json`, and `src/Elsa/Foundation/Identity/OpenIddict/Extensions/OpenIddictIdentityServiceCollectionExtensions.cs`
- [ ] T061 [US4] Migrate every retained EF-backed OpenIddict test objective to Groundwork/shared fixtures and obtain recorded approval for any deletion in `specs/106-openiddict-groundwork-stores/contracts/test-objective-ledger.md`
- [ ] T062 [US4] Delete the OpenIddict EF DbContext, initializer, SQLite factory, migrations, EF/InMemory registration, and EF-only source under `src/Elsa/Foundation/Identity/OpenIddict/EntityFrameworkCore/`
- [ ] T063 [US4] Remove OpenIddict EF/InMemory/SQLite package references, project references, settings, and build artifacts from `src/Elsa/Foundation/Identity/OpenIddict/Elsa.Foundation.Identity.OpenIddict.csproj`, `Directory.Packages.props`, `Elsa.Server.slnx`, and `src/Apps/Elsa.Server/`
- [ ] T064 [US4] Run source, project, package-lock/assets, host, and resolved dependency audits and make the final EF absolute-zero guard fail with a full path for a deliberate temporary reintroduction before removing the mutation in `tests/Elsa/Architecture/EfCoreSurfaceRatchetTests.cs`
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

- **Setup (Phase 1)**: T001–T003 may begin immediately. T004–T006 form the hard public-capability gate; T007 records upstream gaps and T007A records an Elsa design blocker when the public alternative would change the approved shape.
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
- Public preview.81 capability proof is a hard prerequisite, not a documentation formality.
- Static provider capability reports prove package declarations only. T006 requires real native-provider execution and evidence after T007A resolves the physical form.
