# Tasks: Structured Logs API Minimal API Migration

**Input**: Design documents from `/specs/155-structured-logs-api-migration/`

**Prerequisites**: [plan.md](plan.md), [spec.md](spec.md), [research.md](research.md), [data-model.md](data-model.md), [contracts/](contracts/)

**Tests**: Immutable FastEndpoints-before HTTP/SSE/OpenAPI evidence, real-host streaming tests, authorization/catalog tests, coexistence, dependency retirement, and exercised collectibility/OpenAPI-cache evidence are mandatory. Replacement tests must fail for the expected reason before production implementation. Legacy capture tasks intentionally pass before removal because they establish the reviewed baseline.

**Organization**: Tasks are grouped by independently testable user story. Foundational real-host evidence blocks production endpoint edits.

## Phase 1: Setup (Shared Evidence Infrastructure)

**Purpose**: Prepare one deterministic real host, stable cases, bounded stream capture, and baseline locations without changing production endpoint behavior.

- [ ] T001 Add TestHost, CShells ASP.NET Core/FastEndpoints, ASP.NET Core OpenAPI, Foundation Identity, `Elsa.Api.Compatibility.Testing`, and baseline copy dependencies in `tests/Elsa/Diagnostics/StructuredLogs/Tests/Elsa.Diagnostics.StructuredLogs.Tests.csproj`
- [ ] T002 Create deterministic authentication, source/store/feed, shared-writer, cursor, timing, and host-lifecycle fixtures in `tests/Elsa/Diagnostics/StructuredLogs/Tests/Support/StructuredLogsApiHost.cs`
- [ ] T003 [P] Define stable ordinary HTTP and bounded SSE cases for default/custom paths, filters, invalid/repeated inputs, authorization, initial/resume streams, cursor failures, remote commits, heartbeat, and cancellation in `tests/Elsa/Diagnostics/StructuredLogs/Tests/Support/StructuredLogsCompatibilityCases.cs`
- [ ] T004 [P] Add a reusable bounded stream reader that records status, headers, complete frames, reviewed timing bounds, cancellation, and terminal state without waiting for EOF in `tests/Elsa/Diagnostics/StructuredLogs/Tests/Support/StructuredLogsStreamReader.cs`

---

## Phase 2: Foundational Legacy Evidence (Blocking)

**Purpose**: Capture and review the current FastEndpoints surface before rewriting or deleting any endpoint.

**⚠️ CRITICAL**: Do not modify production endpoint registrations, feature base type, SSE helper use, or `Elsa.Diagnostics.StructuredLogs.csproj` until T005-T008 have produced reviewed immutable evidence and T009 fails only because the replacement seam is absent.

- [ ] T005 Capture canonical FastEndpoints observations for recent and sources across valid/invalid/default/custom-path cases, assert repeated stability, and commit `tests/Elsa/Diagnostics/StructuredLogs/Tests/Baselines/structured-logs-http-fastendpoints.json`
- [ ] T006 Capture canonical bounded FastEndpoints SSE observations for headers, first entry, valid resume, generic cursor failures, exact frame bytes, heartbeat, and terminal state; commit them with the HTTP baseline in `tests/Elsa/Diagnostics/StructuredLogs/Tests/Baselines/structured-logs-http-fastendpoints.json`
- [ ] T007 Capture the actual standard ASP.NET Core OpenAPI document, project all three consumed operations, and commit `tests/Elsa/Diagnostics/StructuredLogs/Tests/Baselines/structured-logs-openapi-fastendpoints.json`
- [ ] T008 Pin the three-route default/custom manifest, methods, permission policy, owner/authoring metadata, query/serialization behavior, cursor volatility validity, no-public-drop-frame rule, and ten-capture stability in `tests/Elsa/Diagnostics/StructuredLogs/Tests/StructuredLogsApiContractTests.cs`
- [ ] T009 Add a failing replacement-seam test requiring `StructuredLogsFeature` to implement `IWebShellFeature`, publish three Minimal API routes exactly once, and uniquely catalog `Diagnostics:StructuredLogs` in `tests/Elsa/Diagnostics/StructuredLogs/Tests/StructuredLogsApiContractTests.cs`

**Checkpoint**: Real immutable before evidence exists and has been reviewed; replacement requirements fail for the intended missing implementation only.

---

## Phase 3: User Story 1 - Inspect Recent Logs and Sources (Priority: P1) 🎯 MVP

**Goal**: Replace recent and source-discovery routes with explicit Minimal APIs while preserving configured paths, binding, JSON, validation, ordering, bounds, and authorization.

**Independent Test**: Compare both replacement routes with immutable HTTP/OpenAPI evidence across default/custom paths, valid and invalid filters, empty/populated stores, exact/wildcard/denied callers, and serialization cases.

### Tests for User Story 1

- [ ] T010 [P] [US1] Add failing real-host recent tests for omitted/blank/repeated/mixed-case/culture-sensitive/zero/negative/invalid inputs, filter combinations, store clamp, ordering, and empty results in `tests/Elsa/Diagnostics/StructuredLogs/Tests/StructuredLogsApiQueryTests.cs`
- [ ] T011 [P] [US1] Add failing real-host sources tests for empty/populated providers, stable ordering, configured paths, exact JSON/status/content type, and no duplicate default route in `tests/Elsa/Diagnostics/StructuredLogs/Tests/StructuredLogsApiQueryTests.cs`
- [ ] T012 [P] [US1] Add failing route authorization tests for anonymous 401, missing/adjacent 403, exact permission, wildcard, untrusted/ambiguous principal, resource denial, and no store/feed invocation after rejection in `tests/Elsa/Diagnostics/StructuredLogs/Tests/StructuredLogsApiAuthorizationTests.cs`
- [ ] T013 [P] [US1] Add failing permission catalog tests for unique `Elsa.Diagnostics.StructuredLogs` provenance, no implication, wildcard exclusion, and endpoint-to-catalog reconciliation in `tests/Elsa/Diagnostics/StructuredLogs/Tests/StructuredLogsPermissionsTests.cs`

### Implementation for User Story 1

- [ ] T014 [US1] Replace `FastEndpointsFeatureBase` with `IWebShellFeature`, preserve existing capture/store/feed/sink/source/serializer registrations, register the permission contributor, and delegate endpoint mapping in `src/Elsa/Diagnostics/StructuredLogs/StructuredLogsFeature.cs`
- [ ] T015 [US1] Implement `StructuredLogsPermissionContributor` for the stable permission with module provenance and no implication in `src/Elsa/Diagnostics/StructuredLogs/Authorization/StructuredLogsPermissionContributor.cs`
- [ ] T016 [US1] Add public `StructuredLogsApi.MapStructuredLogsApi(IEndpointRouteBuilder)`, shared explicit-`RequestDelegate` conventions, configured recent/sources routes, owner/authoring metadata, and canonical wildcard-or-diagnostics policy in `src/Elsa/Diagnostics/StructuredLogs/Endpoints/StructuredLogsApi.cs`
- [ ] T017 [US1] Implement evidence-matched raw-query binding, validation failures, store calls, explicit serializer output, source JSON results, and stable OpenAPI metadata for recent/sources in `src/Elsa/Diagnostics/StructuredLogs/Endpoints/StructuredLogsApi.cs`
- [ ] T018 [US1] Compare replacement recent/sources manifest, HTTP observations, and consumed OpenAPI operations with immutable baselines and require zero unapproved differences in `tests/Elsa/Diagnostics/StructuredLogs/Tests/StructuredLogsApiQueryTests.cs`

**Checkpoint**: Recent history and source discovery are independently functional, explicitly owned, Foundation-policy protected, and contract-compatible.

---

## Phase 4: User Story 2 - Tail and Resume Live Logs (Priority: P2)

**Goal**: Replace the streaming route while preserving validation-before-start, durable ordering, resume, response headers/framing, heartbeat, polling fallback, cancellation, bounded cleanup, and the current public event set.

**Independent Test**: Exercise the replacement through real HTTP for initial handoff, valid resume, remote-only commits, filtering, invalid/unavailable cursors, idle heartbeat, feed failure, slow consumer, cancellation, and repeated cleanup, then compare bounded observations with the immutable baseline.

### Tests for User Story 2

- [ ] T019 [P] [US2] Add failing real-host tests for exact SSE status/headers/entry bytes, initial boundary race, committed cursor order, valid `Last-Event-ID` resume, remote-only commits, filtered tails, and configured path/polling in `tests/Elsa/Diagnostics/StructuredLogs/Tests/StructuredLogsApiStreamingTests.cs`
- [ ] T020 [P] [US2] Add failing pre-stream tests for invalid filters, malformed/NUL/stale/wrong-binding cursors, invalid store pages, generic 409 text, and proof that no SSE response starts or subscription leaks on rejection in `tests/Elsa/Diagnostics/StructuredLogs/Tests/StructuredLogsApiStreamingTests.cs`
- [ ] T021 [P] [US2] Add failing lifecycle tests for 15-second production heartbeat via bounded evidence, an injected short test interval, per-frame flush, client disconnect, request cancellation, feed failure/completion, cancellation-ignoring pending wake, five-second writer cleanup bound, and repeated connect/disconnect release in `tests/Elsa/Diagnostics/StructuredLogs/Tests/StructuredLogsApiStreamingLifecycleTests.cs`
- [ ] T022 [P] [US2] Add a failing compatibility assertion that durable-tail traffic emits entry/heartbeat frames only and never begins emitting formatter-supported process-local dropped events in `tests/Elsa/Diagnostics/StructuredLogs/Tests/StructuredLogsApiStreamingTests.cs`

### Implementation for User Story 2

- [ ] T023 [US2] Remove `ISseStreamFormatter<StructuredLogStreamItem>` from `StructuredLogSseFormatter` while preserving exact entry, dropped and heartbeat formatting methods in `src/Elsa/Diagnostics/StructuredLogs/Endpoints/StructuredLogSseFormatter.cs`
- [ ] T024 [US2] Implement module-local `StructuredLogSseWriter` with legacy 15-second heartbeat, five-second pending-read cleanup bound, linked cancellation, per-frame flush, and safe async-enumerator disposal in `src/Elsa/Diagnostics/StructuredLogs/Endpoints/StructuredLogSseWriter.cs`
- [ ] T025 [US2] Add the configured stream mapping and evidence-matched filter/cursor validation, first durable read, response preamble, initial flush, cancellation handling, and generic cursor rejection in `src/Elsa/Diagnostics/StructuredLogs/Endpoints/StructuredLogsApi.cs`
- [ ] T026 [US2] Move the durable-tail iterator and page validation into the mapper without semantic redesign: durable payload authority, wake-only feed, immediate `HasMore` drain, polling fallback, remote commits, and bounded wake cleanup in `src/Elsa/Diagnostics/StructuredLogs/Endpoints/StructuredLogsApi.cs`
- [ ] T027 [US2] Compare replacement bounded SSE observations and consumed OpenAPI operation with immutable baselines and require zero unapproved differences in `tests/Elsa/Diagnostics/StructuredLogs/Tests/StructuredLogsApiStreamingTests.cs`

**Checkpoint**: All three routes preserve HTTP/OpenAPI behavior, and the live stream preserves durable replay, wire framing, heartbeat, cancellation, and cleanup through real HTTP.

---

## Phase 5: User Story 3 - Operate in Transitional and Dynamic Hosts (Priority: P3)

**Goal**: Prove one Minimal API owner, shared authorization with transitional routes, production dependency retirement, actual OpenAPI cache classification, and exercised route/service/stream/document release.

**Independent Test**: Compose the complete replacement with one unrelated FastEndpoints route, inspect and exercise both, generate OpenAPI, inspect cached metadata, release isolated owners, and verify bounded collectible-context evidence or a GC-root-backed supported boundary.

### Tests for User Story 3

- [ ] T028 [P] [US3] Add a failing mixed-host test proving all three Structured Logs Minimal routes and an unrelated secured FastEndpoints route coexist and reach the same instrumented Foundation evaluator instance/outcomes in `tests/Elsa/Diagnostics/StructuredLogs/Tests/StructuredLogsApiCoexistenceTests.cs`
- [ ] T029 [P] [US3] Add failing repeated materialized-route, exercised-query, live-SSE, service-provider, serializer, and actual OpenAPI-document release tests with weak-reference-only evidence in `tests/Elsa/Diagnostics/StructuredLogs/Tests/StructuredLogsApiCollectibilityTests.cs`
- [ ] T030 [P] [US3] Add OpenAPI document-service cache inspection that records module-owned `Type`, `MethodInfo`, and transformer delegate references without retaining them in `tests/Elsa/Diagnostics/StructuredLogs/Tests/Support/StructuredLogsCollectibleFixture.cs`
- [ ] T031 [P] [US3] Add guards that the production Structured Logs assembly/project contains no FastEndpoints endpoint base, discovery interface, package/project dependency, SSE helper, or transition registration after migration in `tests/Elsa/Diagnostics/StructuredLogs/Tests/StructuredLogsApiDependencyTests.cs`
- [ ] T032 [P] [US3] Extend architecture permission/security coverage so every enabled Structured Logs route has one owner, one Minimal authoring disposition, and one uniquely cataloged non-wildcard permission in `tests/Elsa/Architecture/EndpointSecurityTests.cs`

### Implementation for User Story 3

- [ ] T033 [US3] Replace production `CShells.FastEndpoints.Abstractions` and `Elsa.Api.FastEndpoints` references with direct CShells ASP.NET Core, `Elsa.Api.AspNetCore`, and Foundation Identity references in `src/Elsa/Diagnostics/StructuredLogs/Elsa.Diagnostics.StructuredLogs.csproj`
- [ ] T034 [US3] Delete `RecentEndpoint.cs`, `SourcesEndpoint.cs`, `StreamEndpoint.cs`, and the obsolete internal permission-policy holder under `src/Elsa/Diagnostics/StructuredLogs/Endpoints/`
- [ ] T035 [US3] Remove exactly the three #1349 Structured Logs records from `tests/Elsa/Architecture/Baselines/fastendpoints-transition-exceptions.json` and verify source reconciliation reports no Structured Logs legacy registration
- [ ] T036 [US3] Complete the production-mapper collectible fixture: materialize routes, execute JSON/query traffic, start/cancel SSE, generate the actual document, inspect OpenAPI contexts, retain/release classified owners, and preserve weak-reference-only diagnostics in `tests/Elsa/Diagnostics/StructuredLogs/Tests/Support/StructuredLogsCollectibleFixture.cs`
- [ ] T037 [US3] If stable metadata still retains the collectible context, capture `dotnet-dump gcroot` evidence and document/verify the supported host-owned serialized-document or non-unloadable-documentation boundary; otherwise record repeated clean collection evidence in `tests/Elsa/Diagnostics/StructuredLogs/Tests/StructuredLogsApiCollectibilityTests.cs`
- [ ] T038 [US3] Document explicit mapping, configurable route inventory, services, permission owner, SSE lifecycle, coexistence, compatibility, and collectibility/OpenAPI constraints in `src/Elsa/Diagnostics/StructuredLogs/README.md`

**Checkpoint**: Structured Logs has one explicit Minimal API surface, operates beside unmigrated routes, carries no production FastEndpoints dependency, and supplies honest exercised unload/OpenAPI evidence.

---

## Phase 6: Evidence Report and Repository Gates

**Purpose**: Make the streaming proof reviewable and verify the complete repository-facing change.

- [ ] T039 Publish the compatibility matrix, query/serialization results, SSE lifecycle evidence, catalog/authorization results, coexistence inventory, OpenAPI cache/GC-root findings, collectibility stages, remaining risks, and proceed/revise/stop recommendation in `docs/reports/structured-logs-minimal-api-migration-2026-08.md`
- [ ] T040 [P] Add the Structured Logs streaming-migration report to `docs/reports/README.md`
- [ ] T041 Review `src/Elsa/Diagnostics/StructuredLogs/Endpoints/StructuredLogsApi.cs`, `StructuredLogSseWriter.cs`, and test support for repetition; extract only justified module-local helpers without introducing a shared endpoint or SSE framework
- [ ] T042 Run `dotnet test tests/Elsa/Diagnostics/StructuredLogs/Tests/Elsa.Diagnostics.StructuredLogs.Tests.csproj --no-restore` and retain exact pass/fail evidence
- [ ] T043 Run Structured Logs EF/Groundwork persistence suites, `Elsa.Api.Compatibility.Testing` tests, and `Elsa.Architecture.Tests` with `--no-restore`
- [ ] T044 Run the affected repository build through `Elsa.Server.slnx`, architecture guard, generated-maps check, and explicit diff review; fix every in-scope failure
- [ ] T045 Regenerate all generated maps deliberately, review `docs/reports/maps-v1-findings.md`, and stage every changed map including `docs/maps/manifest.json`
- [ ] T046 Re-run focused, persistence, compatibility, architecture, map-freshness, and diff gates after regeneration and verify a clean worktree after the coherent local commit

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)** starts immediately.
- **Legacy evidence (Phase 2)** depends on setup and blocks every production endpoint edit.
- **US1 (Phase 3)** depends on reviewed legacy evidence and delivers the mapping/query foundation.
- **US2 (Phase 4)** reuses the mapper but its test files can be authored after baselines while US1 implementation progresses.
- **US3 (Phase 5)** depends on all three replacements before legacy deletion; coexistence/collectibility/dependency tests can be authored earlier.
- **Evidence/gates (Phase 6)** depends on all stories.

### User Story Dependencies

- **US1** independently delivers recent/source inspection, permission ownership, and the module mapping seam.
- **US2** depends only on the mapper/conventions from US1 and independently proves the stream contract.
- **US3** integrates the complete surface with transition and dynamic-lifecycle evidence without changing domain behavior.

### Parallel Opportunities

- T002-T004 touch separate support files after T001.
- T010-T013 and T019-T022 are test-first tasks in separate files after baseline capture.
- T028-T032 are independent failing gates in separate files.
- T039 and T040 can proceed together after final evidence exists.
- Root integration owns baseline review, public permission identity, production mapper/writer changes, legacy deletion, retention conclusion, and final gates.

## Implementation Strategy

1. Capture and review the complete legacy HTTP/SSE/OpenAPI surface before production edits.
2. Deliver recent/source queries plus catalog ownership as the smallest independently verifiable slice.
3. Move the durable SSE endpoint with real-host lifecycle tests and no public-event expansion.
4. Remove legacy registrations/dependencies only after all replacements pass.
5. Exercise mixed-host and materialized route/service/stream/document lifecycles; inspect OpenAPI cache metadata and obtain a GC root if still retained.
6. Publish the report, regenerate maps, and run every repository gate before review.

## Notes

- `[P]` means separate files and no dependency on another incomplete task in the same phase.
- Baselines are never auto-accepted or regenerated from the replacement implementation.
- Volatile cursor/timing normalization must retain separate presence/validity/bound assertions.
- No test objective is removed; setup/wiring may change under the refactoring golden rule.
- FastEndpoints remains test-only for coexistence after production transition removal.
- A retained OpenAPI context is not waived; it requires root evidence and an explicit supported documentation lifetime/boundary.
