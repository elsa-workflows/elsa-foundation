# Tasks: Publishing API Minimal API Migration

**Input**: Design documents from `specs/167-publishing-api-migration/`

**Prerequisites**: `plan.md`, `spec.md`, `research.md`, `data-model.md`, `contracts/`

**Tests**: Required. The migration is contract-preserving and unloadability-required; historical capture, differential comparison, mutation bites, authorization, semantic behavior, integration, collectibility, and live backend E2E are release gates.

**Ordering rule**: T001–T009 form the immutable FastEndpoints-before checkpoint. Do not edit production endpoint or feature code until that checkpoint is committed.

## Phase 1: Immutable Before Evidence (Blocking)

**Purpose**: Freeze a reproducible, bite-proof oracle from the real FastEndpoints owner before production changes.

- [ ] T001 Recheck issue #1374 comments, open PRs, red-main gate issues, and exact base health; record any competing claim or inherited failure on #1374 before writing code
- [ ] T002 Add exact 23 method/template/action/request/response/success inventory and one-to-one registration assertions in `tests/Elsa/Workflows/Publishing/Api/Tests/PublishingBeforeBaselineTests.cs` and `specs/167-publishing-api-migration/contracts/publishing-route-manifest.md`
- [ ] T003 [P] Build the deterministic 23-route anonymous plus authenticated success/binding/domain/cancellation corpus in `tests/Elsa/Workflows/Publishing/Api/Tests/Support/PublishingCompatibilityCases.cs`
- [ ] T004 [P] Build the real FastEndpoints historical host and deterministic publication/store/compiler/auth/test-run fixtures in `tests/Elsa/Workflows/Publishing/Api/Tests/Support/PublishingCompatibilityHost.cs`
- [ ] T005 Add a clean-content-guarded, self-contained capture runner and script in `tests/Elsa/Workflows/Publishing/Api/Tests/Capture/` and `tools/capture-publishing-before.sh`; pin branch-durable source-tree and build-input identities without relying on a local or squash-lost commit
- [ ] T006 Capture and commit immutable HTTP, projected OpenAPI, raw OpenAPI, receipt, and initially empty approval artifacts under `tests/Elsa/Workflows/Publishing/Api/Tests/Baselines/`
- [ ] T007 Validate fixture hashes, exact case/operation counts, runner/dependency identities, capture command/environment, ancestor-before-migration ordering, and clean-checkout reproducibility in `tests/Elsa/Workflows/Publishing/Api/Tests/PublishingBeforeBaselineTests.cs`
- [ ] T008 Add exact mutation bites for receipt/source identity and HTTP/OpenAPI status, headers, body, content type, route, operation ID, tags, security, schemas, parameters, and response metadata in `tests/Elsa/Workflows/Publishing/Api/Tests/PublishingCompatibilityTests.cs`
- [ ] T009 Recheck #1374/open PRs, run capture and baseline tests twice, prove fixtures reproduce byte-for-byte, and commit the complete baseline checkpoint before touching `src/Elsa/Workflows/Publishing/Api/`

**Checkpoint**: The historical oracle is immutable, reproducible, complete for all 23 routes, and predates migration.

---

## Phase 2: Stable Contract and Lifetime Foundation (Blocking)

**Purpose**: Establish the API-visible lifetime boundary and source-generated metadata before mapping routes.

- [ ] T010 Inventory every public request, response, problem, enum, JSON payload, publication, slot/policy, preflight, receipt, conversion, incident, and test-run type exposed by accepts/produces metadata; record former-assembly compatibility disposition in `specs/167-publishing-api-migration/contracts/compatibility-evidence.md`
- [ ] T011 Create contracts-only `src/Elsa/Workflows/Publishing/Api/Core/Elsa.Workflows.Publishing.Api.Core.csproj`, link API-visible contract sources under existing namespaces, reuse genuine existing Publishing Core types, and add only constitution-approved dependency-light references
- [ ] T012 Add `src/Elsa/Workflows/Publishing/Api/ApiContractTypeForwards.cs` and compile/source compatibility tests in `tests/Elsa/Workflows/Publishing/Api/Tests/PublishingApiContractCompatibilityTests.cs` for every moved public type/member
- [ ] T013 [P] Add exhaustive source-generated JSON metadata and effective FastEndpoints-compatible serializer options in `src/Elsa/Workflows/Publishing/Api/WorkflowsPublishingJsonContext.cs`
- [ ] T014 [P] Add accepts/produces completeness, resolver-chain precedence, casing, dictionary-key, string-enum, explicit-null, route-field omission, and opaque-`JsonElement` bites in `tests/Elsa/Workflows/Publishing/Api/Tests/PublishingSerializationContractTests.cs`
- [ ] T015 Wire stable API Core into owner/test/host projects and solution manifests, then prove native OpenAPI metadata contains no collectible implementation contract type in `tests/Elsa/Architecture/OpenApiLifetimeBoundaryTests.cs`

**Checkpoint**: Every public wire type is stable, compatible, source-generated, and safe for native OpenAPI metadata.

---

## Phase 3: User Story 1 — Preserve Every REST Contract (Priority: P1)

**Goal**: Replace all 23 registrations with one standard Minimal API mapping surface and preserve observable behavior.

**Independent Test**: Replay the identical frozen corpus against the migrated real host and deep-compare all HTTP and consumed OpenAPI facets with no unexplained differences.

### Tests first

- [ ] T016 [US1] Add the post-migration host, exact 23-operation manifest, stable names/tags/owner/authoring/security/request/response assertions, and frozen replay entry point in `tests/Elsa/Workflows/Publishing/Api/Tests/PublishingApiContractTests.cs`; verify it fails before the mapper exists
- [ ] T017 [P] [US1] Add reserved `drafts` route selection plus route-over-body authority and JSON-omission cases for workflow/activity preflight, publication, policy, slot, and test-run shapes in `tests/Elsa/Workflows/Publishing/Api/Tests/PublishingMinimalApiBindingTests.cs`
- [ ] T018 [P] [US1] Add dynamic workflow `201/200`, activity `201 + Location`, test-run `202`, ordinary `200`, generic mediator, slot, workflow-expression/conversion, activity diagnostics, runtime-preflight, 5xx sanitization, and same-instance cancellation cases in `tests/Elsa/Workflows/Publishing/Api/Tests/PublishingMinimalApiBehaviorTests.cs`

### Implementation

- [ ] T019 [US1] Add public standard route-group mapping in `src/Elsa/Workflows/Publishing/Api/WorkflowsPublishingApi.cs` and convert `src/Elsa/Workflows/Publishing/Api/WorkflowsPublishingApiFeature.cs` to public non-sealed `IWebShellFeature` composition without a custom endpoint DSL
- [ ] T020 [US1] Map activity catalog/construction, incident-strategy, and value-conversion discovery routes through existing mediator handlers with deterministic ordering, cancellation, and generic error parity in `src/Elsa/Workflows/Publishing/Api/WorkflowsPublishingApi.cs`
- [ ] T021 [US1] Map workflow version/snapshot and runtime-requirement preflight routes with reserved route constraints, route-authoritative identity, review tokens, conversion/expression diagnostics, safe custom ProblemDetails, and cancellation parity in `src/Elsa/Workflows/Publishing/Api/WorkflowsPublishingApi.cs`
- [ ] T022 [US1] Map workflow publish with exact tenant/preflight/concurrency/idempotent replay, dynamic 201/200 status, activation/indexing/compensation error translation, and cancellation parity in `src/Elsa/Workflows/Publishing/Api/WorkflowsPublishingApi.cs`
- [ ] T023 [US1] Map publication slot list/get/unpublish/restore and policy get/set with exact method overlap, visible-state projection, revision CAS, host fallback, compensation, 404/409/500 translation, and cancellation parity in `src/Elsa/Workflows/Publishing/Api/WorkflowsPublishingApi.cs`
- [ ] T024 [US1] Map workflow version/draft test-run start routes with non-overlapping `drafts` selection, exact retained resource/expiry behavior, JSON binding, status, and cancellation in `src/Elsa/Workflows/Publishing/Api/WorkflowsPublishingApi.cs`
- [ ] T025 [US1] Map activity draft publication preflight/publish/receipt and activity test-run start/get/idempotency/cancel with route authority, tenant/resource checks, receipt replay/fingerprint semantics, 201/202/200 statuses, escaped Location, diagnostics, and cancellation in `src/Elsa/Workflows/Publishing/Api/WorkflowsPublishingApi.cs`
- [ ] T026 [US1] Publish stable operation names, Publishing tag/owner/Minimal-authoring metadata, accepts/produces/content types, success/error statuses, and native OpenAPI security for all 23 routes in `src/Elsa/Workflows/Publishing/Api/WorkflowsPublishingApi.cs`
- [ ] T027 [US1] Replay the real after host, deep-compare every HTTP/OpenAPI facet bidirectionally, and add only exact reviewed two-sided differences to `tests/Elsa/Workflows/Publishing/Api/Tests/Baselines/publishing-approved-differences.json`; reject duplicate, unused, no-op, unknown, wrong-value, one-sided, and stale approvals
- [ ] T028 [US1] Rewire—not delete or weaken—existing endpoint/handler semantic objectives across `tests/Elsa/Workflows/Publishing/Api/Tests/`; preserve publication compiler/activation/preflight/projection/slot/policy/activity/upgrade/test-run coverage and add a recorded architect disposition if a subject genuinely ceased to exist
- [ ] T029 [US1] Delete exactly 23 superseded production endpoint registrations/classes, remove owner production `Elsa.Api.FastEndpoints` dependency from `src/Elsa/Workflows/Publishing/Api/Elsa.Workflows.Publishing.Api.csproj`, and update `tests/Elsa/Architecture/FastEndpointsTransitionTests.cs` from 23 to zero without removing retained test oracles/canaries

**Checkpoint**: All 23 migrated routes match the immutable HTTP/OpenAPI oracle and owner semantic suites.

---

## Phase 4: User Story 2 — Preserve Framework-Neutral Authorization (Priority: P1)

**Goal**: Route all Publishing endpoints through the shared policy provider/evaluator without changing trust, tenant, resource, or activity-publication rules.

**Independent Test**: Run one real-host matrix across representative read/manage/resource routes and a retained FastEndpoints canary, asserting transport outcomes and evaluator/authorizer/store/compiler invocation counts.

- [ ] T030 [P] [US2] Add anonymous, authenticated-untrusted, ambiguous, trusted-denied, exact read/manage, configured implication, evaluator-wildcard, malformed/normalized, cancellation, and evaluator replacement cases in `tests/Elsa/Workflows/Publishing/Api/Tests/PublishingAuthorizationIntegrationTests.cs`
- [ ] T031 [P] [US2] Add absent/mismatched tenant, route-resource mismatch, activity publication denial, payload redaction, and denial-before-sender/store/compiler/publisher/test-run invocation cases in `tests/Elsa/Workflows/Publishing/Api/Tests/PublishingAuthorizationBoundaryTests.cs`
- [ ] T032 [US2] Apply exactly one catalog-owned `workflow-publishing.read` or `workflow-publishing.manage` requirement per protected route via the Foundation Identity policy adapter in `src/Elsa/Workflows/Publishing/Api/WorkflowsPublishingApi.cs`; keep wildcard and implication out of endpoint metadata and do not invent `manage -> read`
- [ ] T033 [US2] Preserve `IActivityPublishingAuthorizationContext` and resource/tenant semantics in `src/Elsa/Workflows/Publishing/Api/Services/` and `src/Elsa/Workflows/Publishing/Api/WorkflowsPublishingApiFeature.cs` without duplicating evaluator logic or moving transport authorization into the engine
- [ ] T034 [US2] Map a retained test-only FastEndpoints canary beside representative Minimal routes and prove both use the same dynamic policy provider, claims normalizer, permission evaluator, resource handlers, cancellation, and replacement registrations in `tests/Elsa/Workflows/Publishing/Api/Tests/PublishingAuthorizationIntegrationTests.cs`
- [ ] T035 [US2] Extend `tests/Elsa/Architecture/EndpointSecurityTests.cs` and owner security tests to assert exact one-action metadata, no wildcard ownership, no anonymous route, explicit security disposition, and single permission contributor ownership for all 23 mappings

**Checkpoint**: Authentication, permission, tenant/resource, and activity-publication behavior is authoring-framework neutral.

---

## Phase 5: User Story 3 — Compose and Unload Safely (Priority: P2)

**Goal**: Prove real host composition, replacement, native OpenAPI, source generation, service/resource disposal, and generation collection.

**Independent Test**: Complete three collectible generations that each execute mapped delegates, authorization, stores/compilers/publishers/test runs, serialization and native OpenAPI in alternating order, then collect every implementation weak reference.

- [ ] T036 [P] [US3] Add exact-once 23-route combined-host manifest and representative behavior assertions in `tests/Elsa/Architecture/DomainManagementApiCompositionTests.cs`, including migrated Workflows/Activities/Runtime owners and retained test infrastructure
- [ ] T037 [US3] Add a red three-cycle collectible-host test in `tests/Elsa/Architecture/Wave8PublishingMinimalApiCollectibilityTests.cs` covering route metadata plus mapped catalog, preflight, policy, publication, slot and test-run delegates; authorization; configured stores/compilers/publishers/authorizers; source-generated binding/serialization; native OpenAPI; disposal; endpoint removal; and weak references
- [ ] T038 [US3] Register dynamic API Explorer refresh, stable-OpenAPI conventions, owner JSON resolver, test-run/store disposal, and endpoint ownership through standard host services in `src/Elsa/Workflows/Publishing/Api/WorkflowsPublishingApiFeature.cs`
- [ ] T039 [US3] Make the three-cycle proof pass in both OpenAPI-before-serialization and serialization-before-OpenAPI orders with bounded established GC evidence and no sleeps, private/global cache clearing, production GC, reflection cleanup, or omitted OpenAPI
- [ ] T040 [US3] Prove public feature extensibility, exact registration/removal, owner assembly/reference cleanup, unsafe metadata rejection/rollback, and no collectible implementation types in endpoint/OpenAPI metadata in `tests/Elsa/Architecture/Wave8PublishingMinimalApiCollectibilityTests.cs` and `tests/Elsa/Architecture/FastEndpointsTransitionTests.cs`

**Checkpoint**: The owner composes exactly once and every retired implementation generation is collectible.

---

## Phase 6: Live Backend Integration

**Purpose**: Validate real Workbench composition, persistence, publication, execution, policy/slot lifecycle, and test-run behavior.

- [ ] T041 Add a Publishing lifecycle journey for runtime preflight, snapshot review/publish, policy update/stale CAS, slot get/unpublish/restore, activity publication receipt replay, activity test-run lookup/cancel, and route/body precedence in `e2e-tests/reusable-activities/Test-PublishingLifecycle.ps1` and document it in `e2e-tests/reusable-activities/README.md`
- [ ] T042 Rebuild Workbench from current source, recreate SQLite database/schema from scratch, and run `e2e-tests/get-endpoints/Test-PublishingGets.ps1` plus `e2e-tests/write-endpoints/Test-PublishingWrites.ps1`; record exact source/build/environment/counts in `docs/reports/publishing-api-migration-2026-08.md`
- [ ] T043 Run affected reusable activity publication, deep/nested authoring, pinning, upgrade, workflow/activity draft test-run, outcome-limit, sequence, set-outcome, and new Publishing lifecycle scripts; record exact source/build/environment/counts and reconcile stale-test versus product defects in `docs/reports/publishing-api-migration-2026-08.md`

---

## Phase 7: Documentation, Generated Facts, and Green Gate

**Purpose**: Make the evidence reviewable and close every repository gate before publication.

- [ ] T044 Correct `src/Elsa/Workflows/Publishing/Api/README.md` and `EXTENSION_POINTS.md`, reconcile ADR 0068/0069 and canonical docs, and describe the final mapper, permissions, stable contract lifetime, route ownership, and coexistence state without duplicating domain rules
- [ ] T045 Write `docs/reports/publishing-api-migration-2026-08.md` with before receipt/hashes, exact 23 manifest/removal, route-by-route HTTP/OpenAPI disposition, approvals, permissions, semantic tests, unload weak references, E2E, commands/results, warnings, risks, rollback, and #1376 handoff
- [ ] T046 Regenerate all generated maps with `dotnet run --project tools/maps/Elsa.Maps.Generator -- all`, review findings, explicitly stage every changed map including `docs/maps/manifest.json`, and prove `-- check` is green
- [ ] T047 Run the complete Publishing API test project and targeted compatibility/security/collectibility/transition/host-composition suites; record exact commands and totals in the report
- [ ] T048 Run full `tests/Elsa/Architecture/Elsa.Architecture.Tests.csproj`, full `Elsa.Server.slnx` build, changed-file formatter, map freshness, `git diff --check`, public API compatibility, and final transition count; resolve every branch-introduced failure/warning
- [ ] T049 Recheck issue comments/open PRs, perform root diff/self-review against `origin/main`, verify all 23 removals and no lost test objectives, and commit the final implementation with a clean worktree
- [ ] T050 Obtain an independent five-axis review of correctness/security, contract evidence, architecture/API compatibility, code quality/DRY/style, and unloadability/dependency cleanup; record each review round and outcome on #1374
- [ ] T051 Address every Critical/Required review finding, add mutation/regression bites, rerun affected/full gates, update report/checklist, and repeat independent review until the gate is clean

---

## Phase 8: PR, Merge, and Post-Merge Verification

**Purpose**: Publish only a green wave and verify the exact merged main state.

- [ ] T052 Push the organization branch, open a draft Wave 8 PR linked to #1374, comment the PR URL and exact evidence on #1374, and synchronize label/Project status to Review
- [ ] T053 Wait for every required PR check and requested review to pass, reconcile concurrent main changes, rerun invalidated local evidence, and merge only on a clean gate
- [ ] T054 Verify CI, HTTP workflow performance, maps, packages, code-quality/security, and Docker workflows on the exact merged main commit; fix forward or revert any red main gate
- [ ] T055 Post final merged SHA, post-merge run URLs, evidence/report links, exact 23→0 owner and 23→0 program ratchet on #1374; close issue, set `status:done`, and move its Project item to Done only after exact-main is green

---

## Dependencies and Execution Order

- T001–T009 are strictly sequential at checkpoint level and block every production source edit.
- T010–T015 depend on immutable baseline and block route mapping because OpenAPI metadata must reference stable types from the first implementation.
- User Stories 1 and 2 are both P1, but T016–T029 establish routes before the complete authorization host executes; authorization test authoring T030–T031 may proceed in parallel.
- User Story 3 depends on the real mapper, stable contract boundary, authorization configuration, and owner resource graph.
- Live backend integration depends on all in-process contract/security/collectibility gates.
- Documentation/maps follow executable behavior; final review, PR, merge, and exact-main verification are serial release gates.

## Parallel Opportunities

- T003 and T004 use separate support files after the exact inventory exists.
- T013 and T014 can progress together once the stable type inventory is fixed.
- T017 and T018 cover separate binding/status surfaces.
- T030 and T031 cover principal/evaluator versus tenant/resource/inner-authorizer boundaries.
- T036 can be authored while T037 builds the independent collectible-host fixture.

## Implementation Strategy

1. Freeze the genuine FastEndpoints service and commit it before migration.
2. Establish stable API contracts/source generation before exposing Minimal API metadata.
3. Migrate one owner-local route mapper by semantic family, keeping engine/domain handlers intact.
4. Close exact compatibility and authorization matrices before deleting production FastEndpoints.
5. Prove combined native OpenAPI/request execution/test-run unloadability, then real Workbench E2E.
6. Publish a reviewable report, pass independent review, merge on green, and verify exact main before starting #1376.
