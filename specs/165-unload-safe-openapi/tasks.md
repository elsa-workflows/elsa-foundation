# Tasks: Unload-Safe OpenAPI Boundary

**Input**: Design documents from `specs/165-unload-safe-openapi/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md

**Tests**: Required. Behavioral and retention tests are written first and must demonstrate the unsafe control before the production convention is implemented.

**Organization**: Tasks are grouped by user story so the operator, compatibility, and diagnostic outcomes can be reviewed independently.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel because it edits independent files after its prerequisites
- **[Story]**: User story from `spec.md`
- Every task names the concrete file or evidence path it changes or verifies

## Phase 1: Setup and Evidence Baseline

**Purpose**: Pin the work unit, current framework behavior, and test scaffolding before production edits.

- [x] T001 Confirm issue #1392 has no competing claim or open pull request and record the exact branch/base in `docs/reports/unload-safe-openapi-boundary-2026-08.md`
- [x] T002 Set `specs/165-unload-safe-openapi/spec.md` to `In progress` and record the approved stable-contract hypothesis in the report
- [x] T003 [P] Add a framework-only collectible endpoint fixture with stable-host and collectible-contract controls in `tests/Elsa/Architecture/Support/OpenApiLifetimeFixture.cs`
- [x] T004 [P] Capture the installed .NET/OpenAPI versions and public/private seam findings in `docs/reports/unload-safe-openapi-boundary-2026-08.md`

---

## Phase 2: Foundational Red Gates

**Purpose**: Prove the causal lifetime boundary and define the public validation contract before implementation.

**⚠️ CRITICAL**: No production convention is implemented until these tests establish the unsafe control and the desired accepted path.

- [x] T005 Add a red framework-only test proving real API Explorer/OpenAPI retains a collectible request/response contract in `tests/Elsa/Architecture/OpenApiLifetimeCollectibilityTests.cs`
- [x] T006 Add a stable-contract control that maps and invokes the same collectible implementation but expects its implementation load context to collect in `tests/Elsa/Architecture/OpenApiLifetimeCollectibilityTests.cs`
- [x] T007 [P] Add red public-contract tests for accepted metadata, marker shape, null guards, and final-convention ordering in `tests/Elsa/Architecture/OpenApiLifetimeBoundaryTests.cs`
- [x] T008 [P] Add red branch tests for collectible request, response, metadata-object, member/method, delegate/transformer, serializer metadata, duplicate ownership, and missing ownership in `tests/Elsa/Architecture/OpenApiLifetimeBoundaryTests.cs`
- [x] T009 Mutation-test the stable control by substituting one collectible contract artifact and prove the unload assertion fails in `tests/Elsa/Architecture/OpenApiLifetimeCollectibilityTests.cs`

**Checkpoint**: The unsafe framework path is reproducibly red, stable contract metadata is the only green hypothesis, and every public validation branch has an expected failure.

---

## Phase 3: User Story 1 - Reload a documented module safely (Priority: P1) 🎯 MVP

**Goal**: A documented endpoint implementation can be replaced and unloaded after real API Explorer/OpenAPI use when all documentation artifacts have stable lifetime.

**Independent Test**: Run `OpenApiLifetimeCollectibilityTests`; the unsafe control remains intentionally characterized, while the accepted path maps, invokes, serializes, documents, disposes, unloads, and collects in three consecutive cycles.

### Tests for User Story 1

- [x] T010 [US1] Extend the stable control to execute source-generated request/response JSON and real `IOpenApiDocumentProvider` generation in the same cycle in `tests/Elsa/Architecture/OpenApiLifetimeCollectibilityTests.cs`
- [x] T011 [US1] Add three-cycle weak-reference assertions for the load context, assembly, mapper/handler types, delegates, serializer context, endpoint metadata, and provider across `tests/Elsa/Architecture/OpenApiLifetimeCollectibilityTests.cs` and `tests/Elsa/Diagnostics/StructuredLogs/Tests/StructuredLogsApiCollectibilityTests.cs`
- [x] T012 [US1] Add a successful replacement test proving document requests observe complete old or complete new endpoint generations with no missing-operation window in `tests/Elsa/Architecture/OpenApiLifetimeCollectibilityTests.cs`

### Implementation for User Story 1

- [x] T013 [P] [US1] Implement immutable accepted-boundary metadata in `src/Elsa/Api/AspNetCore/OpenApiLifetimeMetadata.cs`
- [x] T014 [P] [US1] Implement the domain-scoped rejection exception and deterministic violation fields in `src/Elsa/Api/AspNetCore/UnsafeOpenApiMetadataException.cs`
- [x] T015 [US1] Implement completed-metadata lifetime inspection in `src/Elsa/Api/AspNetCore/OpenApiLifetimeValidator.cs`
- [x] T016 [US1] Add the final endpoint convention `RequireStableOpenApi` in `src/Elsa/Api/AspNetCore/EndpointConventionBuilderExtensions.cs`
- [x] T017 [US1] Apply the production convention to all three reference routes in `src/Elsa/Diagnostics/StructuredLogs/Endpoints/StructuredLogsApi.cs`
- [x] T018 [US1] Re-run the combined Structured Logs query/SSE/authorization/serialization/OpenAPI lifecycle and record the result in `docs/reports/unload-safe-openapi-boundary-2026-08.md`

**Checkpoint**: User Story 1 is independently complete when three native OpenAPI lifecycle cycles collect without cleanup workarounds.

---

## Phase 4: User Story 2 - Preserve the public API contract (Priority: P2)

**Goal**: Applying the lifetime boundary changes no consumed HTTP or OpenAPI behavior.

**Independent Test**: Run the Structured Logs contract suite and the new architecture comparison; every route, operation identity, tag, schema, response, content type, security declaration, and header remains equal.

### Tests for User Story 2

- [x] T019 [P] [US2] Reuse the immutable FastEndpoints-before HTTP/OpenAPI oracle and compare all three Structured Logs routes in `tests/Elsa/Diagnostics/StructuredLogs/Tests/StructuredLogsApiContractTests.cs`
- [x] T020 [P] [US2] Assert source-generated nested collection, dictionary, enum, nullable, and polymorphic schemas remain specific in `tests/Elsa/Architecture/OpenApiLifetimeCollectibilityTests.cs`
- [x] T021 [US2] Add a mutation bite that replaces one stable response schema with `object` and prove the contract comparer fails in `tests/Elsa/Diagnostics/StructuredLogs/Tests/StructuredLogsApiContractTests.cs`

### Implementation for User Story 2

- [x] T022 [US2] Ensure the convention preserves standard endpoint builders and adds no binding, serialization, authorization, routing, or result behavior in `src/Elsa/Api/AspNetCore/EndpointConventionBuilderExtensions.cs`
- [x] T023 [US2] Document the `*.Api.Core` namespace, type-forwarding, SemVer, and restart rules in `docs/adr/0069-openapi-contract-types-use-stable-api-core.md`

**Checkpoint**: User Story 2 is complete when the boundary is behaviorally invisible to clients and rejects all schema weakening.

---

## Phase 5: User Story 3 - Diagnose unsafe documentation metadata (Priority: P3)

**Goal**: Module authors receive one deterministic, owner-aware failure before unsafe metadata becomes visible.

**Independent Test**: Map one endpoint per unsafe category; each build fails with the exact owner, shell/generation when present, endpoint, category, artifact, and load-context identity, while the prior generation remains callable and documented.

### Tests for User Story 3

- [x] T024 [P] [US3] Assert exact deterministic diagnostics for every unsafe category and stable ordering across repeated runs in `tests/Elsa/Architecture/OpenApiLifetimeBoundaryTests.cs`
- [x] T025 [US3] Add a real candidate-rejection test proving unsafe generation N+1 never becomes visible and generation N remains callable/documented in `tests/Elsa/Architecture/OpenApiLifetimeCollectibilityTests.cs`
- [x] T026 [US3] Add a final-convention regression proving metadata added after the ordinary conventions is still inspected in `tests/Elsa/Architecture/OpenApiLifetimeBoundaryTests.cs`

### Implementation for User Story 3

- [x] T027 [US3] Complete owner/shell/generation/endpoint diagnostic extraction in `src/Elsa/Api/AspNetCore/OpenApiLifetimeValidator.cs`
- [x] T028 [US3] Add fail-closed handling for unknown collectible metadata shapes without recursively retaining inspected objects in `src/Elsa/Api/AspNetCore/OpenApiLifetimeValidator.cs`

**Checkpoint**: User Story 3 is complete when every unsafe candidate fails before visibility and the previous generation remains healthy.

---

## Phase 6: Decision Record, Upstream Reproduction, and Program Handoff

**Purpose**: Make the boundary durable, reviewable, and immediately consumable by the blocked waves.

- [x] T029 [P] Publish the accepted decision, rejected snapshot alternative, framework evidence, test matrix, and remaining risks in `docs/reports/unload-safe-openapi-boundary-2026-08.md`
- [x] T030 [P] Record the stable API Core boundary and third-party snapshot deferral in `docs/adr/0069-openapi-contract-types-use-stable-api-core.md`
- [x] T031 Package the Elsa-independent one-endpoint reproduction from `tests/Elsa/Architecture/Support/OpenApiLifetimeFixture.cs`, file/link the upstream ASP.NET Core issue if retention reproduces, and record it in `docs/reports/unload-safe-openapi-boundary-2026-08.md`
- [x] T032 Update #1372 and #1375 handoff comments with their exact API Core/type-forwarding/convention obligations derived from `specs/165-unload-safe-openapi/contracts/openapi-lifetime-boundary.md`
- [x] T033 Update #1392 and parent #1342 with the review evidence from `docs/reports/unload-safe-openapi-boundary-2026-08.md` and synchronize Project 45 status/blocked reasons

---

## Phase 7: Quality Gates and Delivery

**Purpose**: Close the implementation and public record only on a full green gate.

- [x] T034 Run focused boundary and Structured Logs suites from `specs/165-unload-safe-openapi/quickstart.md` and record exact counts in `docs/reports/unload-safe-openapi-boundary-2026-08.md`
- [x] T035 Run the full Architecture suite, `Elsa.Server.slnx` build, generated-map check, changed-file/full formatter checks, and diff check from `specs/165-unload-safe-openapi/quickstart.md`
- [x] T036 Run the relevant backend REST end-to-end suite from `e2e-tests/README.md` and record command/result in `docs/reports/unload-safe-openapi-boundary-2026-08.md`
- [x] T037 Perform a five-axis Critical/Required/Advisory review of `origin/main...HEAD` and resolve every Critical/Required finding in the affected source/spec/report files
- [ ] T038 Re-check issue #1392 comments/open PRs, set `specs/165-unload-safe-openapi/spec.md` to `Implemented` only when merged, and complete every checkbox in this file honestly
- [ ] T039 Commit, push, open the PR, post the green evidence from `docs/reports/unload-safe-openapi-boundary-2026-08.md`, complete review rounds, merge only when green, and verify post-merge main gates

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: starts immediately.
- **Foundational red gates (Phase 2)**: depends on T003; blocks production implementation.
- **User Story 1 (Phase 3)**: depends on all Phase 2 red gates.
- **User Story 2 (Phase 4)**: depends on the convention contract from US1 but its contract tests can begin after Phase 2.
- **User Story 3 (Phase 5)**: depends on the validator from US1; negative tests can begin after Phase 2.
- **Decision/program handoff (Phase 6)**: depends on all three user stories.
- **Quality/delivery (Phase 7)**: depends on the complete implementation and evidence.

### User Story Dependencies

- **US1 (P1)**: no story dependency after the foundational red gates; it is the MVP.
- **US2 (P2)**: consumes US1's convention but remains independently verifiable through exact contract comparison.
- **US3 (P3)**: consumes US1's validator but remains independently verifiable through rejection and rollback scenarios.

### Parallel Opportunities

- T003 and T004 can run in parallel.
- T007 and T008 can run in parallel with the retention controls T005-T006.
- After the public contract is fixed, T013 and T014 can run in parallel.
- US2 contract comparisons (T019-T021) and US3 diagnostic tests (T024-T026) can run in parallel once US1's validator shape exists.
- T029 and T030 can run in parallel after implementation evidence is final.

## Parallel Example: User Story 1

```text
Task A: T013 implement OpenApiLifetimeMetadata.cs
Task B: T014 implement UnsafeOpenApiMetadataException.cs
Then: T015-T018 integrate the validator, extension, canary, and lifecycle evidence sequentially.
```

## Implementation Strategy

### MVP First

1. Establish the unsafe and stable controls (Phases 1-2).
2. Implement only US1's shared boundary and production canary.
3. Stop and prove three combined native OpenAPI lifecycle cycles.
4. Continue to compatibility and diagnostics only after the causal hypothesis is green.

### Incremental Delivery

1. Stable implementation unloads after real documentation.
2. Exact public contract remains unchanged.
3. Unsafe candidates fail before visibility with actionable diagnostics.
4. ADR/report and blocked-wave handoffs make the result reusable.
5. Full delivery loop merges only after independent review and post-merge gates.

## Notes

- `[P]` means independent files or evidence paths, not permission to bypass prerequisites.
- Tests are written before the corresponding implementation and must bite when the safe boundary is weakened.
- Do not modify private ASP.NET Core caches or add GC sleeps/eviction hooks.
- Do not broaden #1392 into the W6/W9 endpoint migrations; hand their exact owner-local work back after the shared boundary merges.
