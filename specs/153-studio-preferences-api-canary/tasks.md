# Tasks: Studio Preferences API Canary

**Input**: Design documents from `/specs/153-studio-preferences-api-canary/`

**Prerequisites**: [plan.md](plan.md), [spec.md](spec.md), [research.md](research.md), [data-model.md](data-model.md), [contracts/](contracts/)

**Tests**: Contract, authorization, coexistence, and collectibility evidence are mandatory for this migration. Replacement tests are written and observed failing before production implementation; the legacy capture tasks intentionally run green before removal because they create the immutable before evidence.

**Organization**: Tasks are grouped by user story. Shared setup and legacy evidence block production endpoint removal.

## Phase 1: Setup (Shared Test Infrastructure)

**Purpose**: Prepare one reusable canary host and baseline location without changing production endpoint behavior.

- [x] T001 Add TestHost, CShells ASP.NET Core/FastEndpoints, Microsoft.AspNetCore.OpenApi, Foundation Identity, and `Elsa.Api.Compatibility.Testing` test dependencies plus baseline copy rules in `tests/Elsa/Studio/Preferences/Tests/Elsa.Studio.Preferences.Tests.csproj`
- [x] T002 Create deterministic authentication, normalized session, namespace, store/quota, and host-lifecycle fixtures shared by all canary tests in `tests/Elsa/Studio/Preferences/Tests/Support/StudioPreferencesCanaryHost.cs`
- [x] T003 [P] Create stable HTTP case definitions for read/write success and failure branches in `tests/Elsa/Studio/Preferences/Tests/Support/StudioPreferencesCompatibilityCases.cs`

---

## Phase 2: Foundational Legacy Evidence (Blocking)

**Purpose**: Capture and review the current FastEndpoints contract before deleting or rewriting any endpoint.

**⚠️ CRITICAL**: Do not modify `src/Elsa/Studio/Preferences/Api/Endpoints/` or its production references until this phase is complete and its baselines have been reviewed.

- [x] T004 Capture canonical FastEndpoints GET/PUT HTTP observations, verify repeated stability, and commit the reviewed result in `tests/Elsa/Studio/Preferences/Tests/Baselines/studio-preferences-http-fastendpoints.json`
- [x] T005 Capture the current two consumed OpenAPI operations and commit their canonical projection in `tests/Elsa/Studio/Preferences/Tests/Baselines/studio-preferences-openapi-fastendpoints.json`
- [x] T006 Pin the legacy endpoint manifest, permission policies, owner/authoring metadata, exact route count, and ten-capture stability in `tests/Elsa/Studio/Preferences/Tests/StudioPreferencesApiContractTests.cs`
- [x] T007 Write a failing test in `tests/Elsa/Studio/Preferences/Tests/StudioPreferencesApiContractTests.cs` requiring `StudioPreferencesApiFeature` to implement `IWebShellFeature`, publish exactly one GET and PUT through `MapEndpoints`, and mark both as Minimal API owned by `Elsa.Studio.Preferences.Api`

**Checkpoint**: Immutable before evidence exists; the Minimal API seam test fails for the expected reason.

---

## Phase 3: User Story 1 - Read preferences without contract drift (Priority: P1) 🎯 MVP

**Goal**: Map the GET route explicitly through Minimal APIs with unchanged read behavior and shared authorization.

**Independent Test**: Run the read subset against the migrated host and compare it with the committed FastEndpoints HTTP/OpenAPI evidence.

### Tests for User Story 1

- [x] T008 [P] [US1] Write failing GET tests for success JSON/ETag, missing document, unknown namespace, malformed/missing host, and route-namespace authority in `tests/Elsa/Studio/Preferences/Tests/StudioPreferencesApiReadContractTests.cs`
- [x] T009 [P] [US1] Write failing GET authorization cases for anonymous 401, missing permission 403, exact read, implied write-to-read, wildcard, untrusted principal, and resource denial in `tests/Elsa/Studio/Preferences/Tests/StudioPreferencesApiAuthorizationTests.cs`

### Implementation for User Story 1

- [x] T010 [US1] Replace the API feature's FastEndpoints base with CShells `IWebShellFeature`, preserve scoped service registration, and delegate endpoint mapping in `src/Elsa/Studio/Preferences/Api/StudioPreferencesApiFeature.cs`
- [x] T011 [US1] Add the public standard route-builder entry point, GET handler, owner/authoring metadata, and canonical wildcard-or-read policy in `src/Elsa/Studio/Preferences/Api/StudioPreferencesApi.cs`
- [x] T012 [US1] Implement evidence-matched GET exception/result translation for 400, 401, and 404 without a shared endpoint framework in `src/Elsa/Studio/Preferences/Api/StudioPreferencesApi.cs`
- [x] T013 [US1] Compare migrated GET manifest, HTTP observations, and consumed OpenAPI operation with the committed FastEndpoints baseline and require zero unapproved differences in `tests/Elsa/Studio/Preferences/Tests/StudioPreferencesApiReadContractTests.cs`

**Checkpoint**: GET is independently functional, policy-protected, and wire-compatible.

---

## Phase 4: User Story 2 - Write preferences with concurrency protection (Priority: P2)

**Goal**: Map the PUT route explicitly while preserving body binding, conditional writes, validation, quota, conflict, and no-mutation guarantees.

**Independent Test**: Run the write subset against the migrated host, verify storage state after rejected writes, and compare it with the committed FastEndpoints evidence.

### Tests for User Story 2

- [x] T014 [P] [US2] Write failing PUT tests for create, compare-and-swap update, quoted ETag, stale revision/no mutation, missing/malformed/ambiguous preconditions, unknown namespace, malformed host, validation, quota, empty body, and malformed JSON in `tests/Elsa/Studio/Preferences/Tests/StudioPreferencesApiWriteContractTests.cs`
- [x] T015 [P] [US2] Extend authorization tests for anonymous 401, missing write 403, exact write, wildcard, read-only denial, untrusted principal, resource denial, and no mutation in `tests/Elsa/Studio/Preferences/Tests/StudioPreferencesApiAuthorizationTests.cs`

### Implementation for User Story 2

- [x] T016 [US2] Bind route namespace separately from the write body while preserving existing web-JSON names and preventing body scope override in `src/Elsa/Studio/Preferences/Api/Models/PreferenceApiModels.cs` and `src/Elsa/Studio/Preferences/Api/StudioPreferencesApi.cs`
- [x] T017 [US2] Add the PUT handler with existing precondition parsing, service call, quoted ETag, owner/authoring metadata, and canonical wildcard-or-write policy in `src/Elsa/Studio/Preferences/Api/StudioPreferencesApi.cs`
- [x] T018 [US2] Implement evidence-matched PUT exception/result translation for 400, 401, 404, 412, 413, and 422 in `src/Elsa/Studio/Preferences/Api/StudioPreferencesApi.cs`
- [x] T019 [US2] Compare migrated PUT manifest, HTTP observations, and consumed OpenAPI operation with the committed FastEndpoints baseline and require zero unapproved differences in `tests/Elsa/Studio/Preferences/Tests/StudioPreferencesApiWriteContractTests.cs`

**Checkpoint**: GET and PUT work independently with unchanged storage and concurrency semantics.

---

## Phase 5: User Story 3 - Operate alongside unmigrated modules (Priority: P3)

**Goal**: Prove one unambiguous Minimal API owner, mixed-host operation, transition removal, and collectible route/service lifecycles.

**Independent Test**: Start a CShells host containing the migrated feature and one unrelated FastEndpoints feature, inspect/ping both surfaces, then release isolated route/service references and verify weak-reference collection.

### Tests for User Story 3

- [x] T020 [P] [US3] Write a failing mixed-host test proving the Studio Preferences Minimal routes and an unrelated FastEndpoints route coexist and use the shared Foundation evaluator in `tests/Elsa/Studio/Preferences/Tests/StudioPreferencesApiCoexistenceTests.cs`
- [x] T021 [P] [US3] Write failing repeated route and service-provider release tests with weak-reference-only evidence in `tests/Elsa/Studio/Preferences/Tests/StudioPreferencesApiCollectibilityTests.cs`
- [x] T022 [P] [US3] Add a guard that the Studio Preferences production assembly has no FastEndpoints endpoint base, discovery interface, or package dependency in `tests/Elsa/Studio/Preferences/Tests/StudioPreferencesApiContractTests.cs`

### Implementation for User Story 3

- [x] T023 [US3] Replace the production FastEndpoints reference with direct `CShells.AspNetCore.Abstractions` and `Elsa.Api.AspNetCore` dependencies in `src/Elsa/Studio/Preferences/Api/Elsa.Studio.Preferences.Api.csproj`
- [x] T024 [US3] Delete the obsolete endpoint registrations in `src/Elsa/Studio/Preferences/Api/Endpoints/GetStudioPreference.cs` and `src/Elsa/Studio/Preferences/Api/Endpoints/PutStudioPreference.cs` after their replacement tests pass
- [x] T025 [US3] Remove only the two #1347 Studio Preferences records from `tests/Elsa/Architecture/Baselines/fastendpoints-transition-exceptions.json` and verify the transition scanner reports no Studio Preferences registration
- [x] T026 [US3] Complete the collectible production-mapper fixture and release route/service owners before bounded collection in `tests/Elsa/Studio/Preferences/Tests/Support/StudioPreferencesCollectibleFixture.cs`
- [x] T027 [US3] Document explicit mapping, registered services, permissions, event handlers/contributors, tasks, and coexistence scope in `src/Elsa/Studio/Preferences/Api/README.md`

**Checkpoint**: The canary operates in a mixed host, owns exactly two Minimal routes, carries no production FastEndpoints dependency, and supplies collectibility evidence.

---

## Phase 6: Evidence Report and Repository Gates

**Purpose**: Make the canary decision reviewable and verify the complete repository-facing change.

- [x] T028 Publish the compatibility matrix, authorization results, coexistence route inventory, collectibility evidence, remaining risks, and proceed/revise/stop recommendation in `docs/reports/studio-preferences-minimal-api-canary-2026-08.md`
- [x] T029 [P] Add the canary report to the discoverable report index in `docs/reports/README.md`
- [x] T030 Review `src/Elsa/Studio/Preferences/Api/StudioPreferencesApi.cs` and `tests/Elsa/Studio/Preferences/Tests/Support/` for repetition and extract only justified module-local helpers/fixtures
- [x] T031 Run `dotnet test tests/Elsa/Studio/Preferences/Tests/Elsa.Studio.Preferences.Tests.csproj --no-restore` and retain exact pass/fail evidence
- [x] T032 Run `dotnet test tests/Elsa/Api/Compatibility/Testing/Tests/Elsa.Api.Compatibility.Testing.Tests.csproj --no-restore` and `dotnet test tests/Elsa/Architecture/Elsa.Architecture.Tests.csproj --no-restore`
- [x] T033 Run the affected repository build through `Elsa.Server.slnx`, the architecture guard in `tests/Elsa/Architecture/`, the generated-maps check in `tools/maps/Elsa.Maps.Generator/`, and an explicit diff review; fix every in-scope failure
- [x] T034 Regenerate generated maps deliberately with `dotnet run --project tools/maps/Elsa.Maps.Generator -- all`, review the findings report, and stage every changed map including `docs/maps/manifest.json`
- [x] T035 Re-run the full focused/architecture/map gate after regeneration and verify `git diff --check`

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: starts immediately.
- **Foundational legacy evidence (Phase 2)**: depends on Phase 1 and blocks production endpoint edits.
- **User Story 1 (Phase 3)**: depends on reviewed legacy evidence and the failing seam test.
- **User Story 2 (Phase 4)**: depends on the shared mapper introduced by US1, but its write tests can be authored in parallel with US1 read tests.
- **User Story 3 (Phase 5)**: depends on both replacement routes existing before legacy deletion; coexistence and collectibility tests can be authored earlier.
- **Evidence/report gates (Phase 6)**: depend on all stories.

### User Story Dependencies

- **US1** delivers the module mapping seam and GET independently.
- **US2** reuses only that mapping entry point and is independently verified through PUT/storage evidence.
- **US3** integrates the completed GET/PUT surface with transitional FastEndpoints and lifecycle evidence; it does not change their business behavior.

### Parallel Opportunities

- T002 and T003 touch separate support files after T001.
- T008/T009, T014/T015, and T020/T021/T022 are test-first tasks in separate files.
- T028 and T029 can proceed in parallel once final evidence is known.
- Root integration remains responsible for baseline review, production edits, legacy deletion, and all final gates.

## Implementation Strategy

1. Capture and review the legacy surface before production changes.
2. Deliver GET as the smallest independently testable canary slice.
3. Add PUT and prove no-mutation concurrency/error behavior.
4. Remove the legacy registrations only after both replacements pass.
5. Prove mixed-host and collectible lifecycles, publish the report, then run all repository gates.

## Notes

- `[P]` means separate files and no dependency on another incomplete task in the same phase.
- Baselines are never auto-accepted or regenerated from the Minimal API implementation.
- No test objective is removed; setup/wiring may change under the refactoring golden rule.
- FastEndpoints remains test-only for coexistence after T023/T024.
