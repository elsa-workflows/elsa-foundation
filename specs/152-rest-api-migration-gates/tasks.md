# Tasks: REST API Migration Compatibility and Authoring Gates

**Input**: Design documents from `/specs/152-rest-api-migration-gates/`

**Prerequisites**: `plan.md`, `spec.md`, `research.md`, `data-model.md`, `contracts/`, `quickstart.md`

**Tests**: Every behavior is introduced test-first, including mutation fixtures for each claimed gate.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel because it changes separate files and has no dependency on another incomplete task in the phase.
- **[Story]**: Maps the task to a user story in `spec.md`.

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Establish the thin production metadata package, reusable test package, and repository references.

- [x] T001 Create `src/Elsa/Api/AspNetCore/Elsa.Api.AspNetCore.csproj` and add it to `Elsa.Server.slnx` with only the ASP.NET Core framework dependency
- [x] T002 [P] Create `tests/Elsa/Api/Compatibility/Testing/Elsa.Api.Compatibility.Testing.csproj` and add it to `Elsa.Server.slnx` with TestHost, Roslyn, and test-support dependencies
- [x] T003 [P] Add deterministic JSON serialization and baseline loading helpers in `tests/Elsa/Api/Compatibility/Testing/Serialization/CompatibilityJson.cs` and `tests/Elsa/Api/Compatibility/Testing/Baselines/BaselineFile.cs`
- [x] T004 Add architecture-project references to the production metadata and compatibility-testing projects in `tests/Elsa/Architecture/Elsa.Architecture.Tests.csproj`

---

## Phase 2: Foundational Metadata (Blocking Prerequisites)

**Purpose**: Define the standard endpoint metadata and shared evidence identities used by every story.

**⚠️ CRITICAL**: No user-story implementation begins until these contracts are green.

- [x] T005 [P] Write failing metadata convention tests for owner and public/host/named-policy dispositions in `tests/Elsa/Api/Compatibility/Testing/Tests/EndpointMetadataConventionTests.cs`
- [x] T006 [P] Add immutable endpoint evidence keys and normalized route/method value objects in `tests/Elsa/Api/Compatibility/Testing/Manifests/EndpointIdentity.cs`
- [x] T007 Implement typed ownership and security-disposition metadata in `src/Elsa/Api/AspNetCore/EndpointOwnershipMetadata.cs` and `src/Elsa/Api/AspNetCore/EndpointSecurityDispositionMetadata.cs`
- [x] T008 Implement standard `IEndpointConventionBuilder` extensions in `src/Elsa/Api/AspNetCore/EndpointConventionBuilderExtensions.cs` and make T005 pass
- [x] T009 Enrich Foundation permission conventions and FastEndpoints endpoint bases with standard owner/authoring/security metadata in `src/Elsa/Foundation/Identity/Abstractions/Authorization/PermissionEndpointConventionBuilderExtensions.cs` and `src/Elsa/Api/FastEndpoints/Abstractions/`

**Checkpoint**: Any ASP.NET Core endpoint-authoring model can publish the shared metadata contract.

---

## Phase 3: User Story 1 - Prove a migration preserves its public contract (Priority: P1) 🎯 MVP

**Goal**: Capture and compare deterministic HTTP and consumed OpenAPI evidence with exact approved differences.

**Independent Test**: Compare equivalent before/after TestServer endpoints, then mutate each named contract facet and prove only one exact reviewed approval can accept its matching delta.

### Tests for User Story 1

- [x] T010 [P] [US1] Write failing canonical HTTP evidence tests for binding, JSON, status, ProblemDetails, paging/filtering, and bounded streaming in `tests/Elsa/Api/Compatibility/Testing/Tests/HttpCompatibilityEvidenceTests.cs`
- [x] T011 [P] [US1] Write failing consumed OpenAPI projection tests for parameters, request bodies, responses, media types, and schemas in `tests/Elsa/Api/Compatibility/Testing/Tests/OpenApiCompatibilityEvidenceTests.cs`
- [x] T012 [P] [US1] Write failing mutation and exact-approved-difference tests for every compatibility facet in `tests/Elsa/Api/Compatibility/Testing/Tests/CompatibilityComparerMutationTests.cs`

### Implementation for User Story 1

- [x] T013 [P] [US1] Implement canonical HTTP observation models and capture runner in `tests/Elsa/Api/Compatibility/Testing/Http/HttpCompatibilityCase.cs` and `tests/Elsa/Api/Compatibility/Testing/Http/HttpEvidenceCapture.cs`
- [x] T014 [P] [US1] Implement supplied-document OpenAPI canonical projection in `tests/Elsa/Api/Compatibility/Testing/OpenApi/OpenApiEvidenceCapture.cs`
- [x] T015 [US1] Implement facet-level deltas, exact approved differences, and strict unused-approval validation in `tests/Elsa/Api/Compatibility/Testing/Comparison/CompatibilityComparer.cs`
- [x] T016 [US1] Add an equivalent before/after authoring fixture and committed empty approval registry in `tests/Elsa/Architecture/RestCompatibilityTests.cs` and `tests/Elsa/Architecture/Baselines/rest-compatibility-approved-differences.json`

**Checkpoint**: A canary module can prove authoring-only compatibility without duplicating capture or comparison rules.

---

## Phase 4: User Story 2 - Inspect and gate every enabled first-party endpoint (Priority: P1)

**Goal**: Produce a stable runtime manifest and reject incomplete security, permission ownership, and unapproved FastEndpoints authoring.

**Independent Test**: Capture one representative host ten times, then apply missing/ambiguous security, wildcard/missing/conflicting permissions, and unapproved/stale legacy-registration mutations and verify owner-aware failures.

### Tests for User Story 2

- [x] T017 [P] [US2] Write failing order-independence, route normalization, multi-method, and ten-capture stability tests in `tests/Elsa/Api/Compatibility/Testing/Tests/EndpointManifestBuilderTests.cs`
- [x] T018 [P] [US2] Write failing missing/ambiguous disposition and owner mutation tests in `tests/Elsa/Architecture/EndpointSecurityTests.cs`
- [x] T019 [P] [US2] Write failing absent/conflicting permission owner, cross-owner consumption, and wildcard mutation tests in `tests/Elsa/Api/Compatibility/Testing/Tests/PermissionOwnershipValidatorTests.cs`
- [x] T020 [P] [US2] Write failing new/expanded/stale/ambiguous/dynamic FastEndpoints exception tests in `tests/Elsa/Architecture/FastEndpointsTransitionTests.cs`

### Implementation for User Story 2

- [x] T021 [P] [US2] Implement runtime `EndpointDataSource` manifest capture and deterministic serialization in `tests/Elsa/Api/Compatibility/Testing/Manifests/EndpointManifestBuilder.cs`
- [x] T022 [P] [US2] Implement active permission-catalog provenance reconciliation in `tests/Elsa/Api/Compatibility/Testing/Security/PermissionOwnershipValidator.cs`
- [x] T023 [P] [US2] Implement Roslyn FastEndpoints registration discovery and exact exception reconciliation in `tests/Elsa/Api/Compatibility/Testing/Transitions/FastEndpointsRegistrationScanner.cs` and `tests/Elsa/Api/Compatibility/Testing/Transitions/TransitionExceptionValidator.cs`
- [x] T024 [US2] Replace route-regex security scanning with metadata-driven endpoint validation in `tests/Elsa/Architecture/EndpointSecurityTests.cs`
- [x] T025 [US2] Capture the representative host baseline and current exact transition registry in `tests/Elsa/Architecture/Baselines/endpoint-manifest.json` and `tests/Elsa/Architecture/Baselines/fastendpoints-transition-exceptions.json`
- [x] T026 [US2] Add granular permission contributors for the representative Activities Design, API Capabilities, Expressions, Workflows Design, Workflows Publishing, and Workflows Runtime API features under their feature-owned `Authorization/` directories
- [x] T027 [US2] Wire manifest stability, security disposition, permission ownership, and transition reconciliation into `tests/Elsa/Architecture/DomainManagementApiCompositionTests.cs`, `EndpointSecurityTests.cs`, and `FastEndpointsTransitionTests.cs`

**Checkpoint**: The representative enabled first-party REST surface is deterministic, completely classified, and bounded against legacy expansion.

---

## Phase 5: User Story 3 - Diagnose unloadability regressions (Priority: P2)

**Goal**: Prove collectible endpoint assemblies unload and classify route, DI, serializer, or harness retention.

**Independent Test**: Run repeated clean cycles plus deliberate route, service, and serializer retention fixtures; clean contexts collect and every deliberate failure reports the correct stage.

### Tests for User Story 3

- [x] T028 [P] [US3] Write failing clean-cycle and deliberate route/DI/serializer retention tests in `tests/Elsa/Api/Compatibility/Testing/Tests/CollectibleEndpointHarnessTests.cs`
- [x] T029 [P] [US3] Write a failing architecture-level repeated weak-reference evidence test in `tests/Elsa/Architecture/CollectibleEndpointTests.cs`

### Implementation for User Story 3

- [x] T030 [US3] Implement Roslyn fixture compilation and non-inlined collectible-context lifecycle creation in `tests/Elsa/Api/Compatibility/Testing/Collectibility/CollectibleEndpointFixture.cs`
- [x] T031 [US3] Implement staged route, service, and serializer publication/release probes in `tests/Elsa/Api/Compatibility/Testing/Collectibility/RetentionStageProbe.cs`
- [x] T032 [US3] Implement bounded collection verification and weak-reference-only diagnostics in `tests/Elsa/Api/Compatibility/Testing/Collectibility/UnloadEvidence.cs` and make T028–T029 pass

**Checkpoint**: Unloadability claims use repeatable collectible-context evidence and identify the retention seam.

---

## Phase 6: Polish & Cross-Cutting Gates

**Purpose**: Integrate the reusable gates, remove duplication, document use, and run repository merge gates.

- [x] T033 [P] Update migration-consumer guidance and verification commands in `specs/152-rest-api-migration-gates/quickstart.md` and `docs/reference/rest-api-migration-gates.md`
- [x] T034 Remove superseded security-regex helpers and DRY repeated test-host/baseline setup across `tests/Elsa/Architecture/` and `tests/Elsa/Api/Compatibility/Testing/`
- [x] T035 Run focused compatibility and architecture test projects, full build, architecture guard, and all mutation fixtures documented in `specs/152-rest-api-migration-gates/quickstart.md`
- [x] T036 Regenerate all repository maps with `tools/maps/Elsa.Maps.Generator`, review every generated change, and run the generated-map freshness check
- [x] T037 Reconcile every acceptance criterion in `specs/152-rest-api-migration-gates/spec.md` and issue #1346 against test evidence, then record results in the PR and issue

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: starts immediately; T004 depends on T001 and T002.
- **Foundational (Phase 2)**: depends on setup; T007–T009 make the failing T005 contract green.
- **US1 and US2**: both depend on foundational metadata and can then proceed in parallel. US1 is the minimum compatibility MVP; US2 supplies the architecture enforcement required before migration.
- **US3**: depends only on test-project setup and can run alongside US1/US2 after Phase 2.
- **Polish**: depends on all selected stories; T035 precedes map generation and final acceptance reconciliation.

### User Story Dependencies

- **US1 (P1)**: no dependency on another story.
- **US2 (P1)**: no dependency on US1, but both reuse foundational endpoint identities and serialization.
- **US3 (P2)**: independent of US1 and US2 after setup.

### Parallel Opportunities

- T001/T002/T003 can be split across separate files, followed by T004.
- T005/T006 can proceed together before T007–T009 integrate them.
- US1 tests T010–T012 and implementations T013–T014 are parallel file groups; T015 integrates them.
- US2 tests T017–T020 and services T021–T023 are parallel file groups; T024–T027 integrate host evidence.
- US3 can be implemented by a separate worker while US1/US2 proceed.
- T033 can run while final implementation cleanup is underway.

## Parallel Examples

### User Story 1

```text
Worker A: T010 → T013 (HTTP evidence)
Worker B: T011 → T014 (OpenAPI evidence)
Worker C: T012 → T015 (comparison and approvals, after capture contracts settle)
```

### User Story 2

```text
Worker A: T017 → T021 (runtime manifest)
Worker B: T019 → T022 (permission ownership)
Worker C: T020 → T023 (FastEndpoints transition registry)
Root integration: T018, T024–T027
```

### User Story 3

```text
Worker A: T028, T030–T032 (collectibility helper)
Root integration: T029 (architecture-level evidence)
```

## Implementation Strategy

### MVP First

1. Complete setup and foundational typed metadata.
2. Complete US1 and demonstrate equivalent before/after authoring evidence plus mutation detection.
3. Validate US1 independently before adding enforcement.

### Incremental Delivery

1. Add US2 runtime inventory and all authoring/security/permission gates.
2. Add US3 collectible-context diagnostics independently.
3. Integrate documentation, remove superseded regex authority, run all merge gates, and publish exact evidence.

## Notes

- Tests are written first and observed failing for the claimed mutation before implementation.
- Baseline builders may serialize deterministic output but never overwrite committed expectations automatically.
- Existing FastEndpoints exceptions are transitional records linked to removal work, not permission to expand the surface.
- Completed file-changing work is committed at coherent phase checkpoints; the root workroom lead owns integration and final QA.
