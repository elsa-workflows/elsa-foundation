# Tasks: Secrets API Minimal API Migration

**Input**: Design documents from `/specs/154-secrets-api-migration/`

**Prerequisites**: [plan.md](plan.md), [spec.md](spec.md), [research.md](research.md), [data-model.md](data-model.md), [contracts/](contracts/)

**Tests**: Immutable before evidence, contract tests, tenant/redaction bite tests, authorization/catalog tests, coexistence, and exercised collectibility evidence are mandatory. Replacement tests must fail for the expected reason before production implementation. Legacy capture tasks intentionally pass before removal because they establish the reviewed baseline.

**Organization**: Tasks are grouped by independently testable user story. Foundational real-host evidence blocks production endpoint edits.

## Phase 1: Setup (Shared Evidence Infrastructure)

**Purpose**: Prepare one deterministic real host, stable case catalog, and baseline locations without changing production endpoint behavior.

- [x] T001 Add TestHost, CShells ASP.NET Core/FastEndpoints, ASP.NET Core OpenAPI, Foundation Identity, `Elsa.Api.Compatibility.Testing`, logging capture, and baseline copy dependencies in `tests/Elsa/Secrets/Tests/Elsa.Secrets.Tests.csproj`
- [x] T002 Create deterministic authentication, two-tenant repository, clock, ids, registries, stores/types, audit, and host-lifecycle fixtures in `tests/Elsa/Secrets/Tests/Support/SecretsCanaryHost.cs`
- [x] T003 [P] Define stable HTTP cases for all ten routes, authorization/tenant branches, filters, lifecycle outcomes, malformed input, conflicts, and sensitive markers in `tests/Elsa/Secrets/Tests/Support/SecretsCompatibilityCases.cs`

---

## Phase 2: Foundational Legacy Evidence (Blocking)

**Purpose**: Capture and review the current FastEndpoints surface before rewriting or deleting any endpoint.

**⚠️ CRITICAL**: Do not modify production endpoint registrations or `Elsa.Secrets.Api.csproj` until T004-T006 have produced reviewed immutable evidence and T007 fails only because the replacement seam is absent.

- [x] T004 Capture canonical FastEndpoints HTTP observations for every reviewed case, assert sensitive-marker absence and repeated stability, and commit `tests/Elsa/Secrets/Tests/Baselines/secrets-http-fastendpoints.json`
- [x] T005 Capture the actual standard ASP.NET Core OpenAPI document, project all ten consumed operations, assert response schemas/examples contain no sensitive fields, and commit `tests/Elsa/Secrets/Tests/Baselines/secrets-openapi-fastendpoints.json`
- [x] T006 Pin the ten-route manifest, methods, permission policies, owner/authoring metadata, descriptor tenant exception, exact route count, volatility validity, and ten-capture stability in `tests/Elsa/Secrets/Tests/SecretsApiContractTests.cs`
- [x] T007 Add a failing replacement-seam test requiring `SecretsApiFeature` to implement `IWebShellFeature`, publish all ten Minimal API routes exactly once, and uniquely catalog all eight stable Secrets permissions in `tests/Elsa/Secrets/Tests/SecretsApiContractTests.cs`

**Checkpoint**: Real immutable before evidence exists and has been reviewed; replacement requirements fail for the intended missing implementation only.

---

## Phase 3: User Story 1 - Discover Secret Metadata Safely (Priority: P1) 🎯 MVP

**Goal**: Replace list, get, descriptor, and picker routes with explicit Minimal APIs while preserving binding, tenant visibility, safe metadata, and read authorization.

**Independent Test**: Compare all four replacement read/discovery operations with immutable HTTP/OpenAPI evidence across two tenants, filtering/paging, lifecycle visibility, authorization, and sensitive-marker checks.

### Tests for User Story 1

- [x] T008 [P] [US1] Add failing list/get tests for defaults, singular/plural filters, status, active-only, paging bounds, missing/deleted records, same-name two-tenant isolation, and safe metadata in `tests/Elsa/Secrets/Tests/SecretsApiReadContractTests.cs`
- [x] T009 [P] [US1] Add failing descriptor/picker tests for registry ordering, tenant-independent descriptors, picker filters, fixed bound, inline-create value, deleted exclusion, and redaction in `tests/Elsa/Secrets/Tests/SecretsApiReadContractTests.cs`
- [x] T010 [P] [US1] Add failing read authorization tests for anonymous 401, missing/adjacent permission 403, exact read, implied write-to-read, wildcard, untrusted/ambiguous principal, resource denial, and missing tenant in `tests/Elsa/Secrets/Tests/SecretsApiAuthorizationTests.cs`

### Implementation for User Story 1

- [x] T011 [US1] Replace `FastEndpointsFeatureBase` with `IWebShellFeature`, preserve `AddSecrets()` registration, register the permission contributor, and delegate endpoint mapping in `src/Elsa/Secrets/Api/Features/SecretsApiFeature.cs`
- [x] T012 [US1] Add the public standard mapper, shared explicit-`RequestDelegate` conventions, owner/authoring metadata, list/get/descriptor/picker routes, and canonical read policy in `src/Elsa/Secrets/Api/SecretsApi.cs`
- [x] T013 [US1] Implement evidence-matched query/body/route binding and web-JSON result handling for list/get/descriptor/picker in `src/Elsa/Secrets/Api/SecretsApi.cs`
- [x] T014 [US1] Enforce normalized-principal tenant authority on list/get/picker while retaining the descriptor exception and no-service-call forbidden behavior in `src/Elsa/Secrets/Api/SecretsApi.cs`
- [x] T015 [US1] Compare replacement read/discovery manifest, HTTP observations, and consumed OpenAPI operations with the immutable baseline and require zero unapproved differences in `tests/Elsa/Secrets/Tests/SecretsApiReadContractTests.cs`

**Checkpoint**: The four read/discovery operations are independently functional, tenant-safe, metadata-only, policy-protected, and compatible.

---

## Phase 4: User Story 2 - Manage the Secret Lifecycle Without Disclosure (Priority: P2)

**Goal**: Replace create, update, rotate, revoke, delete, and test routes while preserving granular privileges, route authority, lifecycle/conflict behavior, safe errors, and non-disclosure.

**Independent Test**: Exercise every lifecycle operation before/after with exact state assertions, no-mutation checks, two tenants, unique sensitive markers, and the full permission matrix.

### Tests for User Story 2

- [x] T016 [P] [US2] Add failing create/update tests for encrypted/configuration inputs, normalization, duplicate conflict, route authority, metadata-only changes, malformed/empty JSON, validation, and cross-tenant isolation in `tests/Elsa/Secrets/Tests/SecretsApiLifecycleContractTests.cs`
- [x] T017 [P] [US2] Add failing rotate/revoke/delete/test tests for version/lifecycle transitions, repeated/missing operations, safe test results, tombstone invisibility, malformed input, and cross-tenant isolation in `tests/Elsa/Secrets/Tests/SecretsApiLifecycleContractTests.cs`
- [x] T018 [P] [US2] Extend authorization tests across write, update-value, delete, and test for anonymous, missing/adjacent, exact, wildcard, untrusted/resource-denied, missing-tenant, cross-tenant, and rejected-operation no mutation in `tests/Elsa/Secrets/Tests/SecretsApiAuthorizationTests.cs`
- [x] T019 [P] [US2] Add response/header/ProblemDetails/OpenAPI/audit bite tests proving submitted value, configuration key, provider metadata, protected payload, and unsafe exception markers are never disclosed in `tests/Elsa/Secrets/Tests/SecretsApiDisclosureTests.cs`
- [x] T020 [P] [US2] Add catalog tests for all eight stable permission keys, unique `Elsa.Secrets.Api` provenance, only write-to-read implication, wildcard exclusion, and endpoint-to-catalog reconciliation in `tests/Elsa/Secrets/Tests/SecretsPermissionsTests.cs`

### Implementation for User Story 2

- [x] T021 [US2] Implement `SecretsPermissionContributor` for all eight stable keys with only write-to-read implication in `src/Elsa/Secrets/Api/Authorization/SecretsPermissionContributor.cs`
- [x] T022 [US2] Add create/update explicit request delegates, route-authoritative update binding, metadata-only responses, owner/authoring metadata, and canonical write policies in `src/Elsa/Secrets/Api/SecretsApi.cs`
- [x] T023 [US2] Add rotate/revoke/delete/test explicit request delegates, route-authoritative identity, lifecycle results, owner/authoring metadata, and action-specific policies in `src/Elsa/Secrets/Api/SecretsApi.cs`
- [x] T024 [US2] Implement evidence-matched malformed-body, forbidden, not-found, validation, conflict, ProblemDetails, empty, 201, 200, and 204 translations without disclosing sensitive request or provider material in `src/Elsa/Secrets/Api/SecretsApi.cs`
- [x] T025 [US2] Compare all six replacement lifecycle operations and consumed OpenAPI projections with immutable baselines and require zero unapproved differences in `tests/Elsa/Secrets/Tests/SecretsApiLifecycleContractTests.cs`

**Checkpoint**: All ten operations preserve HTTP/OpenAPI and state behavior, use shared granular authorization, isolate tenants, and disclose no sensitive material.

---

## Phase 5: User Story 3 - Operate in Transitional Hosts (Priority: P3)

**Goal**: Prove one Minimal API owner, mixed-host operation, transition retirement, exercised route/service/documentation release, and architecture coverage.

**Independent Test**: Compose the complete replacement with one unrelated FastEndpoints route, inspect and exercise both, generate OpenAPI, release isolated owners, and verify bounded collectible-context evidence.

### Tests for User Story 3

- [x] T026 [P] [US3] Add a failing mixed-host test proving all ten Secrets Minimal routes and an unrelated secured FastEndpoints route coexist and reach the same Foundation evaluator outcomes in `tests/Elsa/Secrets/Tests/SecretsApiCoexistenceTests.cs`
- [x] T027 [P] [US3] Add failing repeated materialized-route, exercised-JSON, service-provider, serializer, and OpenAPI-documentation release tests with weak-reference-only evidence in `tests/Elsa/Secrets/Tests/SecretsApiCollectibilityTests.cs`
- [x] T028 [P] [US3] Add guards that the production Secrets API assembly/project contains no FastEndpoints endpoint base, discovery interface, package dependency, or transition registration after migration in `tests/Elsa/Secrets/Tests/SecretsApiDependencyTests.cs`
- [x] T029 [P] [US3] Extend architecture permission/security coverage so all enabled Secrets routes require one owner, one security disposition, and one uniquely cataloged non-wildcard permission in `tests/Elsa/Architecture/EndpointSecurityTests.cs`

### Implementation for User Story 3

- [x] T030 [US3] Replace the production FastEndpoints reference with direct CShells ASP.NET Core abstractions, `Elsa.Api.AspNetCore`, Foundation Identity abstractions, and required OpenAPI dependencies in `src/Elsa/Secrets/Api/Elsa.Secrets.Api.csproj`
- [x] T031 [US3] Delete all ten obsolete endpoint registrations and the now-unused endpoint-only tenant helper under `src/Elsa/Secrets/Api/Endpoints/Secrets/`
- [x] T032 [US3] Remove exactly the ten #1348 Secrets records from `tests/Elsa/Architecture/Baselines/fastendpoints-transition-exceptions.json` and verify source reconciliation reports no Secrets legacy registration
- [x] T033 [US3] Complete the production-mapper collectible fixture, retain/release classified owners, materialize routes, exercise JSON traffic, generate OpenAPI, and preserve weak-reference-only diagnostics in `tests/Elsa/Secrets/Tests/Support/SecretsCollectibleFixture.cs`
- [x] T034 [US3] Document explicit mapping, route/tenant inventory, registered services, permission catalog/implications, handlers/contributors, tasks, coexistence, disclosure, and collectibility constraints in `src/Elsa/Secrets/Api/README.md`

**Checkpoint**: Secrets has one explicit Minimal API surface, operates beside unmigrated routes, carries no production FastEndpoints dependency, and supplies exercised unload evidence.

---

## Phase 6: Evidence Report and Repository Gates

**Purpose**: Make the representative-module decision reviewable and verify the complete repository-facing change.

- [x] T035 Publish the compatibility matrix, catalog/authorization results, tenant and disclosure bite evidence, coexistence inventory, collectibility stages, remaining risks, and proceed/revise/stop recommendation in `docs/reports/secrets-minimal-api-migration-2026-08.md`
- [x] T036 [P] Add the Secrets representative-migration report to `docs/reports/README.md`
- [x] T037 Review `src/Elsa/Secrets/Api/SecretsApi.cs` and `tests/Elsa/Secrets/Tests/Support/` for repetition and extract only justified module-local helpers without introducing a shared endpoint framework
- [x] T038 Run `dotnet test tests/Elsa/Secrets/Tests/Elsa.Secrets.Tests.csproj --no-restore` and retain exact pass/fail evidence
- [x] T039 Run `dotnet test tests/Elsa/Api/Compatibility/Testing/Tests/Elsa.Api.Compatibility.Testing.Tests.csproj --no-restore` and `dotnet test tests/Elsa/Architecture/Elsa.Architecture.Tests.csproj --no-restore`
- [x] T040 Run the affected repository build through `Elsa.Server.slnx`, architecture guard, generated-maps check, and explicit diff review; fix every in-scope failure
- [x] T041 Regenerate all generated maps deliberately, review `docs/reports/maps-v1-findings.md`, and stage every changed map including `docs/maps/manifest.json`
- [x] T042 Re-run focused, compatibility, architecture, map-freshness, and diff gates after regeneration and verify a clean worktree after the coherent local commit

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)** starts immediately.
- **Legacy evidence (Phase 2)** depends on setup and blocks every production endpoint edit.
- **US1 (Phase 3)** depends on reviewed legacy evidence and delivers the shared mapper/read surface.
- **US2 (Phase 4)** reuses the mapper but its test files can be authored in parallel with US1 after baselines exist.
- **US3 (Phase 5)** depends on all ten replacements before legacy deletion; coexistence/collectibility/dependency tests can be authored earlier.
- **Evidence/gates (Phase 6)** depends on all stories.

### User Story Dependencies

- **US1** independently delivers safe metadata discovery and the module mapping seam.
- **US2** depends only on the shared mapper/conventions from US1 and independently proves lifecycle/security behavior.
- **US3** integrates the complete surface with transition and lifecycle evidence without changing business behavior.

### Parallel Opportunities

- T002 and T003 touch separate support files after T001.
- T008-T010 and T016-T020 are test-first tasks in separate files after baseline capture.
- T026-T029 are independent failing gates in separate files.
- T035 and T036 can proceed together after final evidence exists.
- Root integration owns baseline review, permission/implication decisions, production mapper changes, legacy deletion, and final gates.

## Implementation Strategy

1. Capture and review the complete legacy surface before production edits.
2. Deliver the four read/discovery operations as the smallest independently verifiable slice.
3. Add six lifecycle operations with tenant, authorization, and disclosure bite tests.
4. Remove legacy registrations/dependencies only after all replacements pass.
5. Exercise mixed-host and materialized route/service/documentation lifecycles.
6. Publish the report, regenerate maps, and run every repository gate before review.

## Notes

- `[P]` means separate files and no dependency on another incomplete task in the same phase.
- Baselines are never auto-accepted or regenerated from the replacement implementation.
- Volatile-value normalization must retain separate presence/validity assertions and must never normalize sensitive markers.
- No test objective is removed; setup/wiring may change under the refactoring golden rule.
- FastEndpoints remains test-only for coexistence after production transition removal.
