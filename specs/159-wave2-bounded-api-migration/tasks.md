# Tasks: Wave 2 Bounded API Migration

**Input**: [plan.md](plan.md) and [spec.md](spec.md)

## Phase 1: Before Evidence (blocking)

- [x] T001 Review issue #1368 comments, current claims/open PRs, Wave 2 inventory, constitution gates, and Wave 1 dependency/ratchet assumptions.
- [x] T002 Capture the exact 13 FastEndpoints route manifest and permission/owner/authoring metadata in `tests/Elsa/Architecture/Wave2FastEndpointsBaselineTests.cs`.
- [x] T003 Capture deterministic anonymous, wildcard-authenticated success, malformed/validation/conflict/error, multipart, XML, JSON, paging, polling, delete, idempotency, and tenant-scope HTTP evidence through `Elsa.Api.Compatibility.Testing`.
- [x] T004 Capture the real FastEndpoints OpenAPI document and committed operation/schema projections in `tests/Elsa/Architecture/Baselines/wave2-openapi-fastendpoints.json`.
- [x] T005 Commit immutable before fixtures and review their hashes/content before editing production endpoint registrations.

## Phase 2: BPMN Interchange and Elsa 3 Import (P1)

- [x] T006 Add failing after-host compatibility cases for the three BPMN routes, preserving XML analysis/import/export results and 400 diagnostics.
- [x] T007 Add failing after-host compatibility cases for five Elsa 3 routes, preserving multipart upload/location, JSON plans, paging, ProblemDetails, idempotency, and scoped identities.
- [x] T008 Convert `ActivitiesBpmnInterchangeFeature` to `IWebShellFeature`, add explicit owned Minimal mappings, response/OpenAPI metadata, and read/manage catalog permissions.
- [x] T009 Convert `Elsa3ImportActivitiesFeature` to `IWebShellFeature`, add explicit owned Minimal mappings, preserve raw streams and `ReusableActivityImportHttp.Scope`, and remove FE endpoint classes.
- [x] T010 Add BPMN/Elsa3 normalized-identity and cross-tenant isolation tests and route manifest reconciliation.

## Phase 3: Modularity and Execution Evidence (P1)

- [x] T011 Add explicit Modularity Minimal mappings for list/apply with `module-management.read` and `module-management.manage`, preserving revision/error handling.
- [x] T012 Add Execution Evidence catalog contributor and implications for read/delete/manage; map reads/deletes with one action permission each.
- [x] T013 Add explicit Execution Evidence Minimal mappings preserving correlation/workflow queries, after/wait polling, terminal pages, delete one/all, and plain-text validation errors.
- [x] T014 Add exact/implied/wildcard/normalized/anonymous/unrelated authorization tests and verify no request reaches the store after denial.

## Phase 4: Integration, Unloadability, and Retirement

- [x] T015 Build mixed-host coexistence tests with the four owners plus one unrelated FastEndpoints route; verify one evaluator and no route collisions.
- [x] T016 Add repeated collectible route/DI/serializer/disposal tests for each owner and retain only weak-reference/scalar evidence.
- [x] T017 Add endpoint manifest assertions for exactly one owner, Minimal authoring, Foundation security disposition, catalog provenance, operation IDs/tags, and schema refs.
- [x] T018 Replace temporary capture with committed before/after `CompatibilityComparer` tests that fail unapproved and unused deltas.
- [x] T019 Remove unused owner FastEndpoints project/package references and delete exactly 13 transition entries; rebase Wave 1 and ratchet 156 to 143.
- [x] T020 Publish `docs/reports/wave-2-minimal-api-migration-2026-08.md` with evidence, risks, unloadability findings, and #1323 separation.

## Phase 5: Repository Gates

- [x] T021 Run affected feature suites, compatibility tests, architecture/security/collectibility tests, record the backend E2E applicability disposition, full build, architecture guard, maps check, formatter, and diff/self-review.
- [x] T022 Re-run focused and full gates after the map refresh; make a local commit with the final evidence and report. Do not push/open a PR without root direction.
