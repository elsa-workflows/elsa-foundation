# Tasks: Pinned profile selection planner

**Input**: [spec](spec.md), [plan](plan.md), [research](research.md), [data model](data-model.md), [v1 contract](contracts/selection-planner-v1.md). Tests are required by the spec and #1986.

## Phase 1: Setup

- [x] T001 Add the non-activatable `src/essentials/Modularity/Planning/Elsa.Modularity.Planning.csproj` with only BCL dependencies and include it in the repository solution/filter conventions.
- [x] T002 Add `tests/essentials/Modularity/Planning/Tests/Elsa.Modularity.Planning.Tests.csproj` referencing only the planning library and the repository test stack.

## Phase 2: Shared document and evidence model

- [x] T003 Define immutable catalog, definition, ref, authored-intent and accepted-lock records in `src/essentials/Modularity/Planning/Models/SelectionDocuments.cs` using the reviewed v1 fields.
- [x] T004 [P] Define target/time-stamped inventory, nullable runtime edges, manifest read/edge evidence, explicit package/bundled identity, compatibility and redacted persistence records in `src/essentials/Modularity/Planning/Models/HostInventory.cs`.
- [x] T005 Implement typed validation exceptions/refusal codes and strict JSON duplicate-key/Unicode checks in `src/essentials/Modularity/Planning/Json/SelectionJsonReader.cs`.

**Checkpoint**: The public data boundary can express known-empty runtime dependencies without converting missing evidence to an empty list.

## Phase 3: User Story 1 — exact pinned selection (P1)

**Independent test**: The four named fixtures and overlap/removal vector return exact sorted IDs and complete provenance independent of declaration order and formatting.

- [x] T006 [P] [US1] Add fixture and negative import tests for catalog, workspace profile and authored JSON in `tests/essentials/Modularity/Planning/Tests/SelectionDocumentTests.cs`.
- [x] T007 [P] [US1] Add exact-set/provenance tests for Embedded, Authoring, Worker, Custom and overlap/removal in `tests/essentials/Modularity/Planning/Tests/SelectionExpansionTests.cs`.
- [x] T008 [US1] Implement RFC 8785 string/array/object canonicalization and SHA-256 pin checks in `src/essentials/Modularity/Planning/Json/SelectionDigest.cs`; prove independent ASCII and Unicode vectors, duplicate keys, invalid Unicode and tampering in `tests/essentials/Modularity/Planning/Tests/SelectionDigestTests.cs`.
- [x] T009 [US1] Parse and validate catalog/definition/custom-profile/authored documents in `src/essentials/Modularity/Planning/Json/SelectionJsonReader.cs`, refusing duplicate members/definitions, unknown schema and malformed fields.
- [x] T010 [US1] Expand profile, flat groups, additions and removals with every reason and stable ordering in `src/essentials/Modularity/Planning/Services/SelectionPlanner.cs`.

**Checkpoint**: US1 exact selection works without an inventory and makes no availability claim.

## Phase 4: User Story 2 — honest host assessment (P1)

**Independent test**: Synthetic snapshots yield exact required/advisory/unresolved findings and locks; no missing edge is silently added.

- [x] T011 [P] [US2] Add required-removed, optional-companion, unknown, absent/unreadable/incompatible package, authoritative-empty descriptor and descriptor/manifest-conflict tests in `tests/essentials/Modularity/Planning/Tests/HostAssessmentTests.cs`.
- [x] T012 [US2] Validate duplicate inventory rows/edges, evidence source/time and explicit package identity in `src/essentials/Modularity/Planning/Models/HostInventory.cs`.
- [x] T013 [US2] Assess descriptor authority, manifest fallback, optional advice, package/compatibility uncertainty and observed feature locks in `src/essentials/Modularity/Planning/Services/HostAssessment.cs`.
- [x] T014 [US2] Compose the exact selection and host assessment into one side-effect-free result in `src/essentials/Modularity/Planning/Services/SelectionPlanner.cs` without a runtime-ready flag.

**Checkpoint**: US2 consumes only supplied values; a compiled dependency audit of the planner project shows no host, package-fetch, EF or file-write capability.

## Phase 5: User Story 3 — explicit re-resolution (P2)

**Independent test**: Old and candidate inputs produce a stable comparison while the original authored bytes/pins remain unchanged.

- [x] T015 [P] [US3] Add same-ID/version content-conflict, membership/rationale/edge/lock drift and unchanged-old-document tests in `tests/essentials/Modularity/Planning/Tests/ReresolutionTests.cs`.
- [x] T016 [US3] Implement explicit old/candidate planning and diff of IDs, reasons, dependency findings and package locks in `src/essentials/Modularity/Planning/Services/SelectionReresolver.cs`.
- [x] T017 [US3] Mark settings/resource impact known only from supplied redacted evidence, otherwise unverified, in `src/essentials/Modularity/Planning/Services/SelectionReresolver.cs`.

**Checkpoint**: US3 never silently upgrades a pin and refuses a changed published ID/version.

## Phase 6: User Story 4 — reusable safe result (P3)

**Independent test**: Two in-process consumer presentations of the same public result see identical selection and findings; neither receives opaque settings or a connection value.

- [x] T018 [P] [US4] Add no-secret, no-persistence-evidence and supplied-redacted-persistence tests in `tests/essentials/Modularity/Planning/Tests/SelectionPlanContractTests.cs`.
- [x] T019 [US4] Attach only supplied redacted effective-persistence context and unchecked markers in `src/essentials/Modularity/Planning/Services/SelectionPlanner.cs`.
- [x] T020 [US4] Stabilize public result serialization and reason/finding ordering in `src/essentials/Modularity/Planning/Models/SelectionPlan.cs` for future command/API consumers.

**Checkpoint**: US4 proves shared semantics at the library boundary, not an implemented developer command or web UI.

## Phase 7: Integration and governance

- [x] T021 Run the Release planning test project and affected existing Modularity tests per `specs/174-profile-selection-planner/quickstart.md`; resolve failures without weakening existing tests.
- [x] T022 Run architecture guards, solution-filter freshness, `git diff --check`, and generated-map check; if map facts changed, refresh and stage all changed files including `docs/maps/manifest.json`.
- [x] T023 Review source references, package dependency surface, public JSON error cases and spec FR/SC traceability; set `specs/174-profile-selection-planner/spec.md` to `Implemented` only in the final implementation PR.
- [ ] T024 Record the final PR and exact-head/post-merge evidence in `specs/174-profile-selection-planner/quickstart.md`; push and merge one scoped PR for #1986, then close #1986 and update #1961/#1959 and Project 51.

## Dependencies and execution order

T001–T005 establish the shared boundary. US1 is the first runnable slice; US2 consumes its exact set; US3 compares two completed plans; US4 finalizes the consumer-safe result. The tasks marked `[P]` have separate primary files and may be researched or drafted independently, but only #1986 is an active GitHub delivery item. Tests for each story should fail for the missing behavior before that behavior is implemented. T021–T024 follow the completed story phases.

The MVP is US1 with a deterministic, verified exact set. It is not a usable runtime profile until US2's honest host assessment and a separate rebuilt-host proof exist. No story in this task list releases Embedded, Authoring or Worker membership as supported host presets.
