# Tasks: Activity Semantic Versioning

**Input**: Design documents from `specs/004-activity-semantic-versioning/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/surfaces.md, quickstart.md

**Tests**: Tests are **REQUIRED** for this unit — mandated by FR-017 and constitution §2.23.1 (registration tests) / §2.23.2 (branch-covered unit tests) / §2.21.1 (golden-rule preservation of existing tests). xUnit only, **no FluentAssertions** (constitutionally pinned). Test tasks are therefore not optional and appear in each phase.

**Organization**: Tasks are grouped by user story. Because the `int → string` version change must compile atomically across the solution, the *mechanical migration* + structural moves + the semver value type live in **Phase 2 (Foundational)** — this is the shared substrate every story reads. The per-story phases then layer their distinctive **additive behavior** (the CLR scanner, precedence ordering, build-metadata-insensitive exact lookup, the hash-mismatch guard) and the **tests** that prove each story's success criteria.

## Path Conventions

Modular class-library set + host, single solution `Elsa.Server.slnx`. Sources under `src/`, tests under `tests/`. All paths are repo-relative.

**Home decisions made here (research.md left them to tasks):**
- `SemVer` value type + `SemVerComparer` → **`Elsa.Primitives`** (pure, domainless, zero-dep value type used by persistence, the CLR scanner, and lookup — ≥3 call sites; placing it in `Elsa.Primitives` avoids forcing `…Design.Persistence.Core` to take a Design→Runtime edge just for a value type).
- `[Version]` attribute + the moved activity abstractions → **`Elsa.Activities.Runtime.Core`** (annotates activities; co-located with `IActivity`).
- CLR implementation descriptor → **reuse the existing** `src/Elsa.Activities.Design.Core/Models/ClrImplementationDescriptor.cs` (`KindValue = "Clr"`); do **not** add a new descriptor type. `SourceKind = "CLR"` is the reconciliation-source vocabulary; `ImplementationKind` reuses `ClrImplementationDescriptor.KindValue`.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Create the one new project and wire it into the solution before any code lands.

- [X] T001 Create the new project `src/Elsa.Activities.Design.Reconciliation.Clr/Elsa.Activities.Design.Reconciliation.Clr.csproj` (`net10.0`), referencing `Elsa.Activities.Design.Reconciliation.Core` and `Elsa.Activities.Runtime.Core` only (NOT the reconciliation feature project — FR-010, gate G4). Add the project to `Elsa.Server.slnx`.
- [X] T002 [P] Add the `System.Reflection.MetadataLoadContext` package reference to `src/Elsa.Activities.Design.Reconciliation.Clr/Elsa.Activities.Design.Reconciliation.Clr.csproj` only (heavy dep isolated in the impl feature — gate G3/G6). Do NOT add it to any `.Core`.

**Checkpoint**: Empty `.Clr` project builds and is part of the solution.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The complete `int → string` migration + structural moves + the semver value type. The solution must build green and all pre-existing catalog/reconciliation tests must keep passing (§2.21.1) at the end of this phase.

**⚠️ CRITICAL**: No user story phase can begin until this phase is complete — every story reads this substrate.

### 2A — SemVer value type (Elsa.Primitives)

- [X] T003 Create `SemVer` value type in `src/Elsa.Primitives/Versioning/SemVer.cs`: `TryParse` (full SemVer 2.0.0 — `MAJOR.MINOR.PATCH` + optional `-prerelease` + `+buildmetadata`, FR-011), precedence comparison, `ToSortKey()` (normalised zero-padded key where prerelease sorts below the associated release, R1), and equality that **ignores build metadata** (FR-013). Invalid input is signalled via `TryParse`/a parse result the caller can turn into a domain-scoped exception (no raw `FormatException` thrown from here).
- [X] T004 [P] Create `SemVerComparer : IComparer<SemVer>` (Strategy, §2.24.2 row 9) in `src/Elsa.Primitives/Versioning/SemVerComparer.cs`.
- [X] T005 [P] Branch-covered unit tests for `SemVer` (parse valid/invalid, precedence incl. `10.0.0 > 2.0.0` and patch `1.0.10 > 1.0.2`, prerelease < release, build-metadata-ignored equality `1.0.0 == 1.0.0+build`) and `SemVerComparer`, plus the `ToSortKey()` normaliser (lexical sort of keys == precedence), in `tests/Elsa.Activities.Design.Tests/Unit/SemVerTests.cs` (§2.23.2).

### 2B — Move activity abstractions into Elsa.Activities.Runtime.Core (FR-009, FR-018)

- [X] T006 Move `IActivity` from `src/Elsa.Workflows.Runtime.Core/Contracts/IActivity.cs` to `src/Elsa.Activities.Runtime.Core/Contracts/IActivity.cs` and re-type its `int Version { get; set; }` (line 33) to `string Version { get; set; }` (semver, FR-018).
- [X] T007 Move `ActivityBase` from `src/Elsa.Workflows.Runtime.Core/Abstractions/ActivityBase.cs` to `src/Elsa.Activities.Runtime.Core/Abstractions/ActivityBase.cs`, re-typing its `Version` member to `string` (FR-018).
- [X] T008 Move `IActivityExecutionContext` from `src/Elsa.Workflows.Runtime.Core/Contracts/IActivityExecutionContext.cs` to `src/Elsa.Activities.Runtime.Core/Contracts/IActivityExecutionContext.cs`.
- [X] T009 [P] Create the optional `[Version]` attribute `[AttributeUsage(AttributeTargets.Class)] sealed VersionAttribute(string version)` in `src/Elsa.Activities.Runtime.Core/VersionAttribute.cs` (FR-009).
- [X] T010 Update all references to the three moved types across the solution (namespaces / project references), ensuring `Elsa.Workflows.Runtime.*` no longer owns them and no `Workflows.Runtime.* → Workflows.Design.*` edge is introduced (gate G15). Add a `Elsa.Workflows.Runtime.Core → Elsa.Activities.Runtime.Core` reference if the workflow runtime still consumes `IActivity` (Runtime→Runtime, allowed).

### 2C — Reshape the reconciliation feature to the canonical DI-source pattern (FR-021)

- [X] T011 Move `IActivityReconciliationSource` from `src/Elsa.Activities.Design.Reconciliation/Contracts/IActivityReconciliationSource.cs` into `src/Elsa.Activities.Design.Reconciliation.Core/IActivityReconciliationSource.cs` (so provider features reference only `.Core`).
- [X] T012 Move `ActivityVersionReconciliationModel` from `src/Elsa.Activities.Design.Reconciliation/Models/ActivityVersionReconciliationModel.cs` into `src/Elsa.Activities.Design.Reconciliation.Core/Models/ActivityVersionReconciliationModel.cs` and re-type `int Version` (line 7) to `string Version` (FR-004).
- [X] T013 Reshape `src/Elsa.Activities.Design.Reconciliation/ActivitiesDesignReconciliationFeature.cs`: make it **non-abstract**, **remove** the `virtual IEnumerable<IActivityReconciliationSource> Sources` property and any source registration, and keep it registering only the options, the replaceable default hasher (§2.6.2), the reconciler, the **single** startup task, and the universal `ActivityVersionsReconcilingHandler` (FR-021, gate G13). `ActivityVersionsReconcilingHandler` keeps injecting `IEnumerable<IActivityReconciliationSource>` — unchanged.

### 2D — Catalog type migration: int → string + sort key (FR-001..FR-006, FR-008, FR-013, FR-015)

- [X] T014 Re-type `IActivityDefinitionVersion.Version` from `int` to `string` (read-only) in `src/Elsa.Activities.Design.Core/Contracts/IActivityDefinitionVersion.cs` (FR-001). Do **not** add `SemVerSortKey` to this interface (persistence-only, §2.9.1).
- [X] T015 [P] Re-type `ActivityDefinitionVersionInfo.Version` to `string` in `src/Elsa.Activities.Design.Core/Models/ActivityDefinitionVersionInfo.cs` (FR-003).
- [X] T016 In `src/Elsa.Activities.Design.Persistence.Core/Entities/ActivityDefinitionVersion.cs`: re-type `Version` `int → string` (retain `[Immutable]`, constructor-supplied — FR-002) and add a new `[Immutable]` real CLR property `string SemVerSortKey` computed **once at construction** from `Version` via `SemVer.ToSortKey()` (persistence-only normalised key, §2.9.1 / §2.24.2 row 12 — NOT an EF shadow). This adds a `…Design.Persistence.Core → Elsa.Primitives` reference (allowed; Primitives is zero-dep).
- [X] T017 Re-type `ActivityVersionHashMismatchException` version field + ctor param `int → string` in `src/Elsa.Activities.Design.Reconciliation.Core/ActivityVersionHashMismatchException.cs` (FR-006).
- [X] T018 Rework `src/Elsa.Activities.Design.Persistence.Core/OrderDefinitions/ActivityVersionOrderDefinition.cs` to `OrderDefinition<ActivityDefinitionVersion, string>(v => v.SemVerSortKey, OrderDirection.Descending)` (was `<…, int>(v => v.Version)`) — DB-side semver-precedence ordering via the normalised key (FR-008).
- [X] T019 Update `src/Elsa.Activities.Design.Persistence.EFCore/Configurations/ActivityDefinitionVersionConfiguration.cs` to map `Version` as a string column and persist `SemVerSortKey` as a string column with the immutable scanner honoured (gate G28).
- [X] T020 Update the reconciler `src/Elsa.Activities.Design.Reconciliation/Services/ActivityVersionReconciler.cs` so the Model X find-or-create / append / hash-mismatch flow operates on the **string** version (lookup by `(DefinitionId, Version)`), preserving the absent→create / hash-differs→throw / hash-matches→skip-or-throw behavior (FR-013). Build-metadata-insensitive equality of the lookup is refined in US4 (T039); here only the type changes.
- [X] T021 Re-type the API surface to the semver string (FR-005), so the solution builds: `src/Elsa.Activities.Design.Api/Commands/AddVersion.cs`, `Handlers/AddVersionCommandHandler.cs` (the `int version` param + `CreateVersion`), `Handlers/AddDefinitionCommandHandler.cs` (`const int initialVersion = 1` → seed `"1.0.0"`), `Models/ActivityDefinitionVersionDetailsView.cs` (`int Version` → `string`), `Mapping/ActivityDefinitionVersionToDetailsView.cs`, `Requests/GetVersion.cs`, `Requests/ListDefinitionVersions.cs`, `Handlers/ListDefinitionVersionsRequestHandler.cs`, and `Endpoints/Versions/{Add,Get,Delete}.cs`. No `int`-typed version member may remain on any request/response/view model.
- [X] T022 [P] Re-type the Elsa-3 import adapter to map an `int` Elsa-3 version onto a semver string (default `n → "n.0.0"`, FR-007): `src/Elsa3.Activities.Design.Import/Models/ActivityDefinitionVersionImport.cs` and `src/Elsa3.Activities.Design.Import/Handlers/ImportActivityVersions.cs` (and the `Elsa3ActivityToState` mapping if it carries the version).

### 2E — Fresh migration, pins, and golden-rule preservation

- [X] T023 Regenerate the activities-design SQLite initial migration fresh to reflect the string `Version` column + the `SemVerSortKey` string column (FR-015, no int→semver backfill): replace `src/Elsa.Activities.Design.Persistence.EFCore.Sqlite/Migrations/20260529145120_Initial*.cs` and `ActivitiesDesignDbContextModelSnapshot.cs`.
- [X] T024 Update the read-contract surface pin `tests/Elsa.Activities.Design.Tests/Unit/ReadContractSurfaceTests.cs` to assert `IActivityDefinitionVersion.Version` is `string` (FR-017). This is a pin update, not a subject change.
- [X] T025 Update the registration test `tests/Elsa.Activities.Design.Tests/Registration/FeatureRegistrationTests.cs` to resolve every service of the **reshaped** non-abstract reconciliation feature (no `Sources`) and confirm it still wires the single startup task + handler + reconciler + hasher (§2.23.1).
- [X] T026 Build `Elsa.Server.slnx` and run the existing `Elsa.Activities.Design.Tests` suite; confirm all pre-existing catalog/reconciliation tests pass unchanged in subject/objective (§2.21.1, SC-005) and no `int`-typed version member remains on the catalog model, read contract, projection, reconciliation model, or API surface.

**Checkpoint**: Solution builds green on string-typed versions; existing tests pass. Stories can now begin.

---

## Phase 3: User Story 1 — Author owns the version; assembly version is the default (Priority: P1) 🎯 MVP

**Goal**: An assembly-reading reconciliation source resolves each CLR activity's version (the `[Version]` attribute if present, else the declaring assembly's version) and the catalog records it verbatim.

**Independent Test**: (a) a test activity with no attribute in an assembly versioned `2.1.0` → reconcile → persisted `Version == "2.1.0"`; (b) a second activity `[Version("3.0.0")]` in the same assembly → persisted `Version == "3.0.0"` (override wins).

### Implementation for User Story 1

- [X] T027 [P] [US1] Create `ClrReconciliationOptions { string FolderPath; string? SourceId }` in `src/Elsa.Activities.Design.Reconciliation.Clr/ClrReconciliationOptions.cs` (`SourceId` defaults to the normalised `FolderPath`, R3).
- [X] T028 [US1] Create the `ActivityVersionResolver` in `src/Elsa.Activities.Design.Reconciliation.Clr/ActivityVersionResolver.cs` implementing FR-020: use the `[Version]` attribute value if present; else prefer `AssemblyInformationalVersion` when it is a valid SemVer 2.0.0; else map the 4-part assembly version's `Major.Minor.Build` → `MAJOR.MINOR.PATCH`. Invalid attribute semver (FR-011) and unresolvable assembly version raise **domain-scoped** exceptions carrying the activity type + offending value (§2.23.5) — no raw parse/reflection exception escapes.
- [X] T029 [US1] Create domain-scoped exceptions for the resolver/scanner faults in `src/Elsa.Activities.Design.Reconciliation.Clr/Exceptions/` (invalid-semver and unresolvable-assembly-version, §2.23.5).
- [X] T030 [US1] Create the `ClrAssemblyScanner` in `src/Elsa.Activities.Design.Reconciliation.Clr/ClrAssemblyScanner.cs`: scan `FolderPath` via `MetadataLoadContext` + `PathAssemblyResolver` (reflection-only, no ALC pollution, R5); discover `IActivity` implementations by metadata; for each read name/displayName/description/category/inputs/outputs, emit empty design facets, and resolve the version (via `ActivityVersionResolver`); derive `ActivityTypeKey` = the CLR type **full name** (namespace + type name, excludes assembly identity, FR-022). Resilient scan (FR-023): silently skip DLLs with no `IActivity` types; log-and-skip DLLs that fail to load/reflect; never abort the whole scan. Per-activity faults still throw (T028/T029).
- [X] T031 [US1] Create `ClrActivityReconciliationSource : IActivityReconciliationSource` in `src/Elsa.Activities.Design.Reconciliation.Clr/ClrActivityReconciliationSource.cs` with `SourceKind => "CLR"` and `SourceId` from options; its `Read(ct)` drives the scanner and emits one `ActivityVersionReconciliationModel` per discovered activity with `ImplementationKind = ClrImplementationDescriptor.KindValue` and a populated `ClrImplementationDescriptor` (reuse the existing `Elsa.Activities.Design.Core/Models/ClrImplementationDescriptor.cs`).
- [X] T032 [US1] Create the clean feature `ClrActivityReconciliationFeature : IShellFeature` in `src/Elsa.Activities.Design.Reconciliation.Clr/ClrActivityReconciliationFeature.cs` — it does **NOT** derive from the reconciliation feature; `ConfigureServices` registers the options and `services.AddSingleton<IActivityReconciliationSource, ClrActivityReconciliationSource>()` (FR-010, §2.6.1).

### Tests for User Story 1

- [X] T033 [P] [US1] Branch-covered unit tests for `ActivityVersionResolver` (attribute override / `AssemblyInformationalVersion` / 4-part fallback / unresolvable → domain exception / invalid attribute semver → domain exception) in `tests/Elsa.Activities.Design.Tests/Unit/ActivityVersionResolverTests.cs` (§2.23.2).
- [X] T034 [P] [US1] Branch-covered unit tests for `ClrAssemblyScanner` resilient-scan paths (activity DLL discovered; non-activity DLL silently skipped; unresolvable/unreflectable DLL logged-and-skipped; scan completes) in `tests/Elsa.Activities.Design.Tests/Unit/ClrAssemblyScannerTests.cs` (FR-023, §2.23.2).
- [X] T035 [US1] Integration test covering the US1 independent test: reconcile a folder source against a fixture assembly versioned `2.1.0` containing an un-annotated activity and a `[Version("3.0.0")]` activity → assert persisted `Version` is `2.1.0` and `3.0.0` respectively; re-run reconciliation → zero new rows (idempotent, SC-003); bump content+version → new row appended alongside the retained one. In `tests/Elsa.Activities.Design.Tests/Integration/ClrReconciliationTests.cs`.
- [X] T036 [P] [US1] §2.23.1 registration test for `ClrActivityReconciliationFeature` (resolves the registered `IActivityReconciliationSource`) in `tests/Elsa.Activities.Design.Tests/Registration/FeatureRegistrationTests.cs` (extend) or a new `ClrFeatureRegistrationTests.cs`.
- [X] T037 [P] [US1] Add `EXTENSION_POINTS.md` (+ `README.md`) for `src/Elsa.Activities.Design.Reconciliation.Clr/` listing the registered source and the `FolderPath`/`SourceId` option (gate G26).

**Checkpoint**: An assembly folder can be reconciled into the catalog with author-controlled semver versions.

---

## Phase 4: User Story 2 — Catalog stores and exposes versions as semver strings end-to-end (Priority: P1)

**Goal**: Verify the version is a semver string on every read surface — read contract, listing projection, and API details view — with no `int` version member remaining anywhere. (The mechanical retype landed in Phase 2; US2 proves the exposure.)

**Independent Test**: Read a version back through `IActivityDefinitionVersion`, the `ListVersions` projection, and the API details view; confirm each exposes the string semver and no `int` version member remains.

### Tests for User Story 2

- [X] T038 [P] [US2] Integration/surface test reading a persisted version through `IActivityDefinitionVersion`, the `ListDefinitionVersions` projection/handler, and the `ActivityDefinitionVersionDetailsView` (via `ActivityDefinitionVersionToDetailsView`); assert each exposes the author's semver string and that the round trip preserves the author's value verbatim (SC-001). In `tests/Elsa.Activities.Design.Tests/Unit/VersionStringSurfaceTests.cs`.

**Checkpoint**: String semver verified end-to-end across the read/API surface.

---

## Phase 5: User Story 3 — Versions compared and ordered by semver precedence (Priority: P1)

**Goal**: Listings and "latest version" resolution order by semver precedence (DB-side via `SemVerSortKey`), not lexical or numeric-counter order. (The `OrderDefinition` rework + sort-key landed in Phase 2; US3 proves precedence.)

**Independent Test**: Persist `1.0.0, 2.0.0, 10.0.0, 1.2.0` for one definition; query the ordered listing; assert order is `10.0.0, 2.0.0, 1.2.0, 1.0.0` (precedence, not lexical).

### Tests for User Story 3

- [X] T039 [P] [US3] Ordering test: persist `1.0.0, 2.0.0, 10.0.0, 1.2.0` and patch case `1.0.1, 1.0.10, 1.0.2`; assert the listing order follows semver precedence (`10.0.0` before `2.0.0`; `1.0.10` highest patch) and that the SQL `ORDER BY` runs on `SemVerSortKey` DB-side (not after client materialisation) — SC-002. In `tests/Elsa.Activities.Design.Tests/Integration/VersionOrderingTests.cs`.

**Checkpoint**: Precedence ordering verified, including multi-digit segments.

---

## Phase 6: User Story 4 — A consumer can resolve an exact activity version (Priority: P2)

**Goal**: Lookup by `(DefinitionId, exact semver string)` returns the one record or none; build-metadata-equivalent strings resolve as the same logical version (FR-013). This is the seam Unit 4 builds on.

**Independent Test**: `(DefinitionId, "2.1.0")` returns the one record; `(DefinitionId, "9.9.9")` returns none; `1.0.0` and `1.0.0+build` resolve as the same logical version.

### Implementation for User Story 4

- [X] T040 [US4] Implement exact-version resolution by `(DefinitionId, Version)` with **build-metadata-insensitive** equality (FR-013) on the lookup path: `src/Elsa.Activities.Design.Persistence.Core/Filters/ActivityDefinitionVersionFilter.cs` and `Extensions/ActivityDefinitionVersionQueryExtensions.cs` (and the reconciler lookup from T020). Use `SemVer` equality / a normalised match so `1.0.0` and `1.0.0+build` are the same logical version; the match remains exact on precedence (not nearest).

### Tests for User Story 4

- [X] T041 [P] [US4] Tests: `(DefinitionId, "2.1.0")` → the record; `(DefinitionId, "9.9.9")` → none; `(DefinitionId, "1.0.0")` resolves the row persisted as `1.0.0+build` (build metadata ignored) — SC-006, FR-013. In `tests/Elsa.Activities.Design.Tests/Integration/ExactVersionResolutionTests.cs`.

**Checkpoint**: Exact-version resolution works against the string-typed model; Unit 4 is unblocked.

---

## Phase 7: User Story 5 — A mis-versioned source fails loudly, not silently (Priority: P2)

**Goal**: When an author changes content without bumping the version, reconciliation observes the same `(DefinitionId, Version)` with a different hash and throws `ActivityVersionHashMismatchException` reporting the string version — never a silent overwrite. (The exception retype landed in Phase 2; US5 proves the guard.)

**Independent Test**: Reconcile at `1.0.0`; mutate content without changing the version; re-reconcile → `ActivityVersionHashMismatchException` carrying `"1.0.0"`.

### Tests for User Story 5

- [X] T042 [P] [US5] Tests: (a) same `(DefinitionId, "1.0.0")` contributed with a different hash → `ActivityVersionHashMismatchException` reporting `"1.0.0"` (SC-004); (b) same `(DefinitionId, "1.0.0")` with the matching hash → skipped or thrown per the configured duplicate-handling option (unchanged from Model X). In `tests/Elsa.Activities.Design.Tests/Integration/HashMismatchTests.cs`.

**Checkpoint**: The Model X hash-mismatch guard is verified against string versions.

---

## Phase 8: Polish & Cross-Cutting Concerns

**Purpose**: In-unit constitution + documentation updates and the final green-build gate.

- [X] T043 [P] Update constitution §E2.8 in `.specify/memory/constitution.md` (FR-016): activity version is an author-controlled string semver (SemVer 2.0.0) sourced from the declaring assembly's version, optionally overridden per-activity by `[Version]`, read by the assembly reconciliation source; reword any `int`-version / integer-`(DefinitionId, Version)`-lookup wording; update module-decomposition / dependency wording for the adopted `Elsa.Activities.Design`/`Elsa.Activities.Runtime` split. (Optionally tidy the stale `ProvisioningHash` → `ReconcilliationHash` term in the same edit.)
- [X] T044 [P] Update the reconciliation feature's `src/Elsa.Activities.Design.Reconciliation/EXTENSION_POINTS.md` to reflect the reshaped non-abstract feature (sources contributed via DI from independent features, no `Sources` property) — gate G26.
- [X] T045 [P] Update the Unit 3 follow-up `../elsa-foundation-project-management/epic1-elsa-refactor-constitution/follow-up-items/2026-06-02_unit_activity_semantic_versioning.md` to reflect resolution, and `../elsa-foundation-project-management/epic1-elsa-refactor-constitution/PERSONAL_TODO.md` to reflect Unit 3 status (FR-019).
- [X] T046 Run the quickstart.md validation end-to-end: `dotnet build Elsa.Server.slnx` green with no `int`-typed version member remaining (SC-005); execute the full `Elsa.Activities.Design.Tests` suite; confirm US1–US5 acceptance scenarios and SC-001..SC-006 hold.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies.
- **Foundational (Phase 2)**: Depends on Setup. **BLOCKS all user stories** — it is the shared `int → string` substrate and must build green.
- **User Stories (Phases 3–7)**: All depend on Foundational. Given staffing they can proceed in parallel; otherwise run in priority order (US1 → US2/US3 → US4 → US5).
- **Polish (Phase 8)**: Depends on all desired user stories being complete.

### Foundational internal order

- 2A (SemVer) before 2D (entity sort-key uses `SemVer.ToSortKey()`): T003 → T016.
- 2B (moves) and 2C (reshape) are independent of 2A and of each other.
- 2D depends on 2A + 2C (model relocated/retyped) + 2B (string `Version` shape) before the API/import retype compiles.
- 2E (migration/pins/build) is last in the phase: T023–T026 after T014–T022.

### User Story Dependencies

- **US1 (P1)**: Needs the `[Version]` attribute (T009), the relocated source/model (T011–T012), and the string-typed entity (T016). Otherwise self-contained in `.Clr`.
- **US2 (P1)**: Verification-only; needs the Phase-2 read/API retype (T014, T015, T021).
- **US3 (P1)**: Verification-only; needs the Phase-2 sort-key + `OrderDefinition` rework (T016, T018).
- **US4 (P2)**: T040 implements build-metadata-insensitive equality; needs `SemVer` (T003) and the string lookup (T020).
- **US5 (P2)**: Verification-only; needs the Phase-2 exception retype + reconciler (T017, T020).

### Within Each User Story

- Models/options before services; services before the feature registration; implementation before its tests where a test exercises new code.

### Parallel Opportunities

- T002 with T001's tail; T004/T005 alongside T003's API once it exists.
- 2B (T006–T010), 2C (T011–T013) can proceed in parallel tracks; T009, T015, T022 are marked [P].
- US1 implementation: T027 [P]; tests T033/T034/T036/T037 [P] once their subjects exist.
- The verification-only stories (US2 T038, US3 T039, US5 T042) and US4's test (T041) are mutually independent [P] once Foundational is done.
- Polish T043/T044/T045 are independent [P]; T046 runs last.

---

## Parallel Example: User Story 1

```bash
# After Foundational completes, the US1 building blocks that touch distinct files:
Task: "T027 [US1] ClrReconciliationOptions in src/Elsa.Activities.Design.Reconciliation.Clr/ClrReconciliationOptions.cs"
# then, once their subjects exist, the US1 tests in parallel:
Task: "T033 [US1] ActivityVersionResolver tests"
Task: "T034 [US1] ClrAssemblyScanner resilient-scan tests"
Task: "T036 [US1] ClrActivityReconciliationFeature registration test"
```

---

## Implementation Strategy

### MVP First (User Story 1)

1. Phase 1 Setup → Phase 2 Foundational (the type migration — the bulk of the unit).
2. Phase 3 US1 — the assembly scanner produces author-controlled semver versions.
3. **STOP and VALIDATE**: run T035 (US1 independent test). This is the load-bearing capability the unit exists for.

### Incremental Delivery

- Foundational green build (existing tests pass) → US1 (scanner) → US2/US3 verification (string exposure + precedence) → US4 (exact lookup for Unit 4) → US5 (hash-mismatch guard) → Polish (constitution + docs + final gate).

---

## Notes

- **Tests are required** (FR-017 / §2.23) — xUnit only, no FluentAssertions.
- This is a refactor-heavy unit: the `int → string` change cannot compile partially, so the mechanical migration is concentrated in Foundational and several P1 stories are verification-focused. That is intentional and matches §2.21.1 (existing tests keep passing across the migration).
- Do not introduce a new descriptor type or new pattern — reuse the existing `ClrImplementationDescriptor` and the sanctioned §2.6.1 DI-source + §2.24.2 Strategy/§2.9.1 sort-key patterns (no §2.24.3 gate; see plan Constitution Check).
- Constitution + follow-up edits land in-unit (T043, T045) per the standing "constitution updates happen in-unit" rule.
- Commit is the user's call (the `after_tasks` hook is optional and skipped here).

## Constitutional Compliance

Tasks inherit the Constitution Check gates G1–G30 decided in `plan.md`. The one recorded VIOLATION (G13 — `ActivitiesDesignReconciliationFeature` shape change without rename) is realised by T013 and is a justified in-place defect correction (name unchanged). No task introduces a new structural pattern; the FR-008 sort key (T016/T018) is the §2.9.1 pattern and the comparator (T004) is the §2.24.2 Strategy pattern. The new dependency edge `…Reconciliation.Clr → Elsa.Activities.Runtime.Core` (T001) is Design→Runtime, the allowed §E2.2 direction (G15).
