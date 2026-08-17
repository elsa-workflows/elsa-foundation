# Tasks: Activities Design API Minimal API Migration

**Input**: Design documents from `specs/166-activities-design-api-migration/`

**Prerequisites**: `plan.md`, `spec.md`, `research.md`, `data-model.md`, `contracts/`

**Tests**: Required. The migration is contract-preserving and unloadability-required; historical capture,
differential comparison, mutation bites, authorization, semantic behavior, integration, collectibility, and live
backend E2E are release gates.

**Ordering rule**: T001–T009 form the immutable FastEndpoints-before checkpoint. Do not edit production endpoint
or feature code until that checkpoint is committed.

## Phase 1: Immutable Before Evidence (Blocking)

**Purpose**: Freeze a reproducible, bite-proof oracle from the real FastEndpoints owner before production changes.

- [ ] T001 Recheck issue #1373 comments, open PRs, red-main gate issues, and exact base health; record any competing claim or inherited failure on #1373 before writing code
- [ ] T002 Add the exact 38 method/template/action/success inventory and one-to-one registration assertions in `tests/Elsa/Activities/Design/Tests/Api/ActivitiesDesignApiBeforeBaselineTests.cs` and `specs/166-activities-design-api-migration/contracts/activities-design-route-manifest.md`
- [ ] T003 [P] Build the deterministic 38-route anonymous plus authenticated success/binding/domain/cancellation corpus in `tests/Elsa/Activities/Design/Tests/Api/Support/ActivitiesDesignCompatibilityCases.cs`
- [ ] T004 [P] Build the real FastEndpoints historical host and deterministic provider/store/auth fixtures in `tests/Elsa/Activities/Design/Tests/Api/Support/ActivitiesDesignCompatibilityHost.cs`
- [ ] T005 Add a clean-content-guarded, self-contained capture runner and script in `tests/Elsa/Activities/Design/Tests/Api/Capture/` and `tools/capture-activities-design-before.sh`; pin branch-durable source-tree and build-input identities without relying on a local or squash-lost commit
- [ ] T006 Capture and commit immutable HTTP, projected OpenAPI, raw OpenAPI, receipt, and initially empty approval artifacts under `tests/Elsa/Activities/Design/Tests/Api/Baselines/`
- [ ] T007 Validate fixture hashes, exact case/operation counts, runner/dependency identities, capture command/environment, parent-before-migration ordering, and clean-checkout reproducibility in `tests/Elsa/Activities/Design/Tests/Api/ActivitiesDesignApiBeforeBaselineTests.cs`
- [ ] T008 Add exact mutation bites for receipt/source identity and for HTTP/OpenAPI status, headers, body, content type, route, operation ID, tags, security, schemas, parameters, and response metadata in `tests/Elsa/Activities/Design/Tests/Api/ActivitiesDesignCompatibilityTests.cs`
- [ ] T009 Recheck #1373/open PRs, run the before-host capture and baseline tests, prove the fixtures reproduce byte-for-byte, and commit the complete baseline checkpoint before touching `src/Elsa/Activities/Design/Api/`

**Checkpoint**: The historical oracle is immutable, reproducible, complete for all 38 routes, and predates migration.

---

## Phase 2: Stable Contract and Lifetime Foundation (Blocking)

**Purpose**: Establish the API-visible lifetime boundary and source-generated metadata before mapping routes.

- [ ] T010 Inventory every public request, response, problem, page, cursor, enum, fork, lifecycle, provider-payload, diff, dependency, availability, and upgrade type exposed by endpoint accepts/produces metadata; record former-assembly compatibility disposition in `specs/166-activities-design-api-migration/contracts/compatibility-evidence.md`
- [ ] T011 Create the contracts-only `src/Elsa/Activities/Design/Api/Core/Elsa.Activities.Design.Api.Core.csproj`, link API-visible contract sources under their existing namespaces, and add only constitution-approved dependency-light references
- [ ] T012 Add `src/Elsa/Activities/Design/Api/ApiContractTypeForwards.cs` and compile/source compatibility tests in `tests/Elsa/Activities/Design/Tests/Api/ActivitiesDesignApiContractCompatibilityTests.cs` for every moved public type/member
- [ ] T013 [P] Add exhaustive source-generated JSON metadata and effective FastEndpoints-compatible serializer options in `src/Elsa/Activities/Design/Api/ActivitiesDesignJsonContext.cs`
- [ ] T014 [P] Add accepts/produces completeness, resolver-chain precedence, casing, dictionary-key, string-enum, explicit-null, optional-type-key, and opaque-provider-payload bites in `tests/Elsa/Activities/Design/Tests/Api/ActivitiesDesignSerializationContractTests.cs`
- [ ] T015 Wire stable API Core into the owner/test/host projects and solution manifests, then prove native OpenAPI metadata contains no collectible implementation contract type in `tests/Elsa/Architecture/OpenApiLifetimeBoundaryTests.cs`

**Checkpoint**: Every public wire type is stable, compatible, source-generated, and safe for native OpenAPI metadata.

---

## Phase 3: User Story 1 — Preserve Every REST Contract (Priority: P1)

**Goal**: Replace all 38 registrations with one standard Minimal API mapping surface and preserve observable behavior.

**Independent Test**: Replay the identical frozen corpus against the migrated real host and deep-compare all HTTP and consumed OpenAPI facets with no unexplained differences.

### Tests first

- [ ] T016 [US1] Add the post-migration host, exact 38-operation manifest, stable names/tags/owner/authoring/security/request/response assertions, and frozen replay entry point in `tests/Elsa/Activities/Design/Tests/Api/ActivitiesDesignApiContractTests.cs`; verify it fails before the mapper exists
- [ ] T017 [P] [US1] Add route-over-body precedence and route-ID JSON-omission cases for all 19 mutating request shapes plus `/definitions/picker` precedence in `tests/Elsa/Activities/Design/Tests/Api/ActivityDefinitionAuthoringApiTests.cs`
- [ ] T018 [P] [US1] Add exact seven `201 + Location`, discard `204`, ordinary `200`, authoring ProblemDetails, legacy mediator error, 5xx sanitization, and same-instance cancellation cases in `tests/Elsa/Activities/Design/Tests/Api/ActivitiesDesignMinimalApiBehaviorTests.cs`

### Implementation

- [ ] T019 [US1] Add public standard route-group mapping in `src/Elsa/Activities/Design/Api/ActivitiesDesignApi.cs` and convert `src/Elsa/Activities/Design/Api/ActivitiesDesignApiFeature.cs` to public non-sealed `IWebShellFeature` composition without a custom endpoint DSL
- [ ] T020 [US1] Map availability, catalog, and authoring-capability routes through existing senders/handlers with their historical shared mediator error semantics in `src/Elsa/Activities/Design/Api/ActivitiesDesignApi.cs`
- [ ] T021 [US1] Map definition, draft, contract-proposal, and fork routes with route-over-body binding, created locations, typed authoring diagnostics, sanitization, logging, and cancellation parity in `src/Elsa/Activities/Design/Api/ActivitiesDesignApi.cs`
- [ ] T022 [US1] Map version, diff, dependency, recommendation, lifecycle, picker, paging, and signed-cursor routes through the existing services in `src/Elsa/Activities/Design/Api/ActivitiesDesignApi.cs`
- [ ] T023 [US1] Map create/get/apply/receipt/refresh upgrade-plan routes with exact tenant/access-profile, idempotency, staged handoff, status, location, and error translation in `src/Elsa/Activities/Design/Api/ActivitiesDesignApi.cs`
- [ ] T024 [US1] Publish stable operation names, Activities Design tag/owner/Minimal-authoring metadata, accepts/produces/content types, success/error statuses, and native OpenAPI security for all 38 routes in `src/Elsa/Activities/Design/Api/ActivitiesDesignApi.cs`
- [ ] T025 [US1] Replay the real after host, deep-compare every HTTP/OpenAPI facet bidirectionally, and add only exact reviewed two-sided differences to `tests/Elsa/Activities/Design/Tests/Api/Baselines/activities-design-approved-differences.json`; reject duplicate, unused, no-op, unknown, wrong-value, one-sided, and stale approvals
- [ ] T026 [US1] Rewire—not delete or weaken—existing endpoint/binder/handler semantic objectives across `tests/Elsa/Activities/Design/Tests/`; add a recorded architect disposition if a subject genuinely ceased to exist
- [ ] T027 [US1] Delete exactly the 38 superseded production endpoint registrations/classes, remove the owner production `Elsa.Api.FastEndpoints` dependency from `src/Elsa/Activities/Design/Api/Elsa.Activities.Design.Api.csproj`, and update `tests/Elsa/Architecture/FastEndpointsTransitionTests.cs` from 61 to 23 registrations without removing retained test oracles/canaries

**Checkpoint**: All 38 migrated routes match the immutable HTTP/OpenAPI oracle and owner semantic suites.

---

## Phase 4: User Story 2 — Preserve Framework-Neutral Authorization (Priority: P1)

**Goal**: Route all Activities Design endpoints through the shared policy provider/evaluator without changing trust, tenant, resource, or provider-payload rules.

**Independent Test**: Run one real-host matrix across representative read/manage routes and a retained FastEndpoints canary, asserting transport outcomes and evaluator/provider/store invocation counts.

- [ ] T028 [P] [US2] Add anonymous, authenticated-untrusted, ambiguous, trusted-denied, exact, implied, evaluator-wildcard, malformed/normalized, cancellation, and evaluator replacement cases in `tests/Elsa/Activities/Design/Tests/Api/ActivitiesDesignAuthorizationIntegrationTests.cs`
- [ ] T029 [P] [US2] Add absent/mismatched tenant, route-resource mismatch, provider-authoring denial, provider-payload present/redacted, and denial-before-provider/store/sender invocation cases in `tests/Elsa/Activities/Design/Tests/Api/ActivitiesDesignAuthorizationBoundaryTests.cs`
- [ ] T030 [US2] Apply exactly one catalog-owned `activity-design.read` or `activity-design.manage` requirement per protected route via the Foundation Identity policy adapter in `src/Elsa/Activities/Design/Api/ActivitiesDesignApi.cs`; keep wildcard and implication out of endpoint metadata
- [ ] T031 [US2] Preserve and catalog any inner provider-authoring/provider-payload action and resource-handler semantics in `src/Elsa/Activities/Design/Api/Authorization/` and `src/Elsa/Activities/Design/Api/ActivitiesDesignApiFeature.cs` without duplicating evaluator logic
- [ ] T032 [US2] Map a retained test-only FastEndpoints canary beside representative Minimal routes and prove both use the same dynamic policy provider, claims normalizer, permission evaluator, resource handlers, cancellation, and replacement registrations in `tests/Elsa/Activities/Design/Tests/Api/ActivitiesDesignAuthorizationIntegrationTests.cs`
- [ ] T033 [US2] Extend `tests/Elsa/Architecture/EndpointSecurityTests.cs` and `tests/Elsa/Activities/Design/Tests/Unit/ActivityDesignEndpointSecurityTests.cs` to assert exact one-action metadata, no wildcard ownership, no anonymous route, and explicit public/security disposition for all 38 mappings

**Checkpoint**: Authentication, permission, tenant/resource, and provider-payload behavior is authoring-framework neutral.

---

## Phase 5: User Story 3 — Compose and Unload Safely (Priority: P2)

**Goal**: Prove real host composition, replacement, native OpenAPI, source generation, service disposal, and generation collection.

**Independent Test**: Complete three collectible generations that each execute mapped delegates, authorization, providers/stores, serialization and native OpenAPI in alternating order, then collect every implementation weak reference.

- [ ] T034 [P] [US3] Add exact-once 38-route combined-host manifest and representative behavior assertions in `tests/Elsa/Architecture/DomainManagementApiCompositionTests.cs`, including coexistence with migrated and retained owners
- [ ] T035 [US3] Add a red three-cycle collectible-host test in `tests/Elsa/Architecture/Wave7ActivitiesDesignMinimalApiCollectibilityTests.cs` covering all route metadata plus mapped catalog, authoring, availability, dependency, lifecycle, and upgrade delegates; authorization; configured providers/stores/adapters; source-generated binding/serialization; real API Explorer/native OpenAPI; disposal; endpoint removal; and weak references
- [ ] T036 [US3] Register dynamic API Explorer refresh, stable-OpenAPI conventions, owner JSON resolver, provider/store disposals, and endpoint ownership through standard host services in `src/Elsa/Activities/Design/Api/ActivitiesDesignApiFeature.cs`
- [ ] T037 [US3] Make the three-cycle proof pass in both OpenAPI-before-serialization and serialization-before-OpenAPI orders with bounded established GC evidence and no sleeps, private/global cache clearing, production GC, reflection cleanup, or omitted OpenAPI
- [ ] T038 [US3] Prove public feature extensibility, exact registration/removal, owner assembly/reference cleanup, and no collectible implementation types in endpoint/OpenAPI metadata in `tests/Elsa/Architecture/Wave7ActivitiesDesignMinimalApiCollectibilityTests.cs` and `tests/Elsa/Architecture/FastEndpointsTransitionTests.cs`

**Checkpoint**: The owner composes exactly once and every retired implementation generation is collectible.

---

## Phase 6: Live Backend Integration

**Purpose**: Validate real Workbench composition, persistence, authoring, publication, execution, and upgrade workflows.

- [ ] T039 Add a focused persisted upgrade-plan create/get/apply/receipt/refresh and exact version-pinning journey in `e2e-tests/reusable-activities/Test-ActivityUpgradePlan.ps1` and document it in `e2e-tests/reusable-activities/README.md`
- [ ] T040 Rebuild Workbench from current source, recreate the SQLite database/schema from scratch, and run `e2e-tests/get-endpoints/Test-DesignActivityGets.ps1` plus `e2e-tests/write-endpoints/Test-DesignActivityWrites.ps1`; record exact SHA/environment/counts in `docs/reports/activities-design-api-migration-2026-08.md`
- [ ] T041 Run the reusable author/publish/execute, nesting, pinning, draft test-run, outcome-limit, sequence, and new upgrade-plan scripts under `e2e-tests/reusable-activities/`; record exact SHA/environment/counts and reconcile stale-test versus product defects in `docs/reports/activities-design-api-migration-2026-08.md`

---

## Phase 7: Documentation, Generated Facts, and Green Gate

**Purpose**: Make the evidence reviewable and close every repository gate before publication.

- [ ] T042 Reconcile ADR `docs/adr/0068-first-party-rest-apis-use-aspnet-core-minimal-apis.md`, owner README/extension points, and canonical docs so they describe the final standard mapper, shared authorization, stable contract lifetime, and coexistence state without duplicating glossary facts
- [ ] T043 Write `docs/reports/activities-design-api-migration-2026-08.md` with before receipt/hashes, exact 38 manifest/removal, route-by-route HTTP/OpenAPI disposition, approvals, permissions, semantic tests, unload weak references, E2E, commands/results, warnings, risks, rollback, and follow-up decisions
- [ ] T044 Regenerate all generated maps with `dotnet run --project tools/maps/Elsa.Maps.Generator -- all`, review findings, explicitly stage every changed map including `docs/maps/manifest.json`, and prove `-- check` is green
- [ ] T045 Run the complete Activities Design test project and targeted compatibility/security/collectibility/transition/host-composition suites; record exact commands and totals in the report
- [ ] T046 Run the full `tests/Elsa/Architecture/Elsa.Architecture.Tests.csproj`, full `Elsa.Server.slnx` build, changed-file formatter, map freshness, `git diff --check`, public API compatibility, and final transition count; resolve every branch-introduced failure/warning
- [ ] T047 Recheck issue comments/open PRs, perform root diff/self-review against `origin/main`, verify all 38 removals and no lost test objectives, and commit the final implementation with a clean worktree
- [ ] T048 Obtain an independent five-axis review of correctness/security, contract evidence, architecture/API compatibility, code quality/DRY/style, and unloadability/dependency cleanup; record each review round and outcome on #1373
- [ ] T049 Address every Critical/Required review finding, add mutation/regression bites, rerun affected/full gates, update the report/checklist, and repeat independent review until the gate is clean

---

## Phase 8: PR, Merge, and Post-Merge Verification

**Purpose**: Publish only a green wave and verify the exact merged main state.

- [ ] T050 Push the organization branch, open the Wave 7 PR linked to #1373, comment the PR URL and exact evidence on #1373, and synchronize its label/Project status to Review
- [ ] T051 Wait for every required PR check and requested review to pass, reconcile concurrent main changes, rerun any invalidated local evidence, and merge only on a clean gate
- [ ] T052 Verify CI, HTTP workflow performance, maps, packages, and code-quality/security workflows on the exact merged main commit; fix forward or revert any red main gate
- [ ] T053 Post the final merged SHA, post-merge run URLs, evidence/report links, exact 38→0 owner result and 61→23 program ratchet on #1373; close the issue, set `status:done`, and move its Project item to Done only after exact-main is green

---

## Dependencies and Execution Order

- T001–T009 are strictly sequential at the checkpoint level and block every production source edit.
- T010–T015 depend on the immutable baseline and block route mapping because OpenAPI metadata must reference stable types from the first implementation.
- User Stories 1 and 2 are both P1, but T016–T027 establish the routes before the complete authorization host can execute; authorization test authoring T028–T029 may proceed in parallel.
- User Story 3 depends on the real mapper, stable contract boundary, and authorization configuration.
- Live backend integration depends on all in-process contract/security/collectibility gates.
- Documentation and generated maps follow executable behavior; final review, PR, merge, and exact-main verification are serial release gates.

## Parallel Opportunities

- T003 and T004 use separate support files after the exact inventory exists.
- T013 and T014 can progress together once the stable type inventory is fixed.
- T017 and T018 cover separate behavior surfaces.
- T028 and T029 cover principal/evaluator versus resource/provider boundaries.
- T034 can be authored while T035 builds the independent collectible-host fixture.

## Implementation Strategy

1. Freeze the genuine FastEndpoints service and commit it before migration.
2. Establish stable API contracts/source generation before exposing Minimal API metadata.
3. Migrate one owner-local route mapper by semantic family, keeping domain handlers intact.
4. Close exact compatibility and authorization matrices before deleting production FastEndpoints.
5. Prove combined native OpenAPI/request execution unloadability, then real Workbench E2E.
6. Publish a reviewable report, pass independent review, merge on green, and verify exact main.
