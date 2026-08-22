# Tasks: Wave 6 Workflows Design API Review Corrections

## Phase 1: Setup

- [X] T001 Read review round 1, issue #1372 comments, ADR 0068, constitutions, migration reports, and current Workflows Design routes in `docs/`, `src/Elsa/Workflows/Design/Api/`, and `tests/`.
- [X] T002 Create this official Spec Kit correction work unit and record its scope in `specs/163-wave6-workflows-design-review/`.
- [X] T003 [P] Add the review reconciliation and owner migration report in `docs/reports/workflows-design-api-migration-2026-08.md`.

## Phase 2: Foundational baseline and comparer

- [X] T004 Add a real FastEndpoints-era capture host and deterministic expanded case corpus for all 27 routes in `tests/Elsa/Workflows/Design/Api/Tests/Support/` and `tests/Elsa/Workflows/Design/Api/Tests/WorkflowDesignApiBeforeBaselineTests.cs`.
- [X] T005 [P] Capture immutable success, binding, ProblemDetails/domain-error, paging/filtering, concurrency, header, content-type, and all-route OpenAPI fixtures with provenance/hashes in `tests/Elsa/Workflows/Design/Api/Tests/Baselines/`.
- [X] T006 [P] Add two-sided consumed approval, unused-approval, one-sided-approval, fixture-mutation, and no-content-normalization bites to the compatibility test support in `tests/Elsa/Workflows/Design/Api/Tests/Support/`.
- [X] T007 Commit the expanded before evidence separately before corrective production implementation changes.

## Phase 3: User Story 1 - Contract-preserving mapped API

- [X] T008 [US1] Add `PreflightDraftPromotion` and every mapped request/response payload to `src/Elsa/Workflows/Design/Api/WorkflowsDesignJsonContext.cs`.
- [X] T009 [US1] Add a successful mapped preflight HTTP regression plus missing/malformed/non-JSON binding cases in `tests/Elsa/Workflows/Design/Api/Tests/WorkflowDesignApiBeforeBaselineTests.cs`.
- [X] T010 [US1] Replace route `RequireAnyPermission` metadata with catalog-action-only `RequirePermission` metadata in `src/Elsa/Workflows/Design/Api/WorkflowsDesignApi.cs` and update exact metadata assertions in `tests/Elsa/Workflows/Design/Api/Tests/`.
- [X] T011 [US1] Expand consumed OpenAPI comparison to operation IDs, owner-local tags, parameters, request bodies, responses, and security for all 27 routes in `tests/Elsa/Workflows/Design/Api/Tests/`.

## Phase 4: User Story 2 - Shared authorization and coexistence

- [X] T012 [US2] Add exact, implied, wildcard-evaluator, truly untrusted external, tenant/resource allow-deny, and retained FastEndpoints canary cases in `tests/Elsa/Workflows/Design/Api/Tests/WorkflowsDesignApiContractTests.cs` and `tests/Elsa/Architecture/`.
- [X] T013 [US2] Verify owner-local tags/names and catalog-only permission dispositions through the architecture manifest and metadata tests in `tests/Elsa/Architecture/`.
- [X] T014 [US2] Correct the stale `EndpointSecurityTests` directory assumption and make the focused Architecture authorization suite pass in `tests/Elsa/Architecture/`.

## Phase 5: User Story 3 - Semantic behavior and lifecycle safety

- [X] T015 [US3] Restore expression-tooling provider empty/failure/cache/cancellation/authorization semantic tests in `tests/Elsa/Workflows/Design/Api/Tests/Unit/ExpressionToolingEndpointTests.cs` and mapped HTTP support.
- [X] T016 [US3] Restore lifecycle semantic tests for preflight nonmutation/conflicts, promotion status mapping, synthetic draft IDs, and permanent-delete 404/409/501/500 in `tests/Elsa/Workflows/Design/Api/Tests/WorkflowDefinitionLifecycleContractTests.cs` and handler tests.
- [X] T017 [US3] Expand three real collectible cycles to invoke mapped delegates and exercise authorization, stores/adapters/providers, OpenAPI, source-generated serialization, DI disposal, and weak references in `tests/Elsa/Architecture/Wave1MinimalApiCollectibilityTests.cs` and support fixtures.

## Phase 6: Integration and evidence

- [X] T018 [P] Run the documented Workflows Design backend E2E against a rebuilt Workbench and fresh SQLite database, recording command/result in `docs/reports/workflows-design-api-migration-2026-08.md`.
- [X] T019 [P] Run import ordering/format, affected tests, Architecture, full build, maps check, and diff review; record results in `docs/reports/workflows-design-api-migration-2026-08.md`.
- [X] T020 Recheck issue comments/open PRs, review the final diff, commit clean local correction changes, and release the issue claim without push/PR.

## Dependencies

T001-T003 precede T004-T007. The baseline commit T007 precedes T008-T014. T008-T014 precede T015-T017. T018-T020 are final integration gates.

## Parallel opportunities

T005/T006 can proceed independently after T004's capture shape. T012/T013/T014 can proceed in parallel after T010. T015/T016/T017 can proceed independently once the mapper and baseline harness are stable. T018/T019 are independent final gates.

## Implementation strategy

The MVP is the real immutable before capture plus missing source-gen/preflight parity and exact action metadata. The complete correction requires all three stories, semantic restoration, lifecycle evidence, documented E2E, and every final gate before handoff.
