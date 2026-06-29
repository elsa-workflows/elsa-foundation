---
description: "Task list for Typed Argument Model + Type Descriptor Registry (Backend)"
---

# Tasks: Typed Argument Model + Type Descriptor Registry (Backend)

**Input**: Design documents from `specs/081-typed-argument-model/`

**Prerequisites**: plan.md, spec.md, research.md (D1–D10), data-model.md, contracts/wire-contract.md

**Tests**: REQUIRED. The framework constitution §2.23 mandates feature-registration tests (§2.23.1) and branch-covered implementation tests (§2.23.2); infra exceptions must be wrapped (§2.23.5). Test tasks below are not optional.

**Conventions** (from grounding): xUnit, built-in assertions (no FluentAssertions in these projects); `{Subject}Tests`, `{Method}_{Scenario}_{Expected}`; direct instantiation with stubs. Logic-bearing impls are `public sealed`; feature classes `public`, non-sealed, `virtual ConfigureServices` (§2.23.3).

## Format: `[ID] [P?] [Story] Description`

- **[P]**: parallelizable (different file, no dependency on an incomplete task)
- **[Story]**: US1 / US2 / US3 (setup, foundational, polish carry no story label)

---

## Phase 1: Setup & Investigation

**Purpose**: confirm unknowns the plan deferred, before any dependent edit.

- [X] T001 [P] Confirm the test projects and their conventions for `Elsa.Expressions` and `Elsa.Serialization` (paths, xUnit, assertion style); note them at the top of `tasks.md` notes. Reference existing `tests/Elsa/Activities/Design/Tests` as the convention baseline. **Created** `tests/Elsa/Expressions/Tests/Elsa.Expressions.Tests.csproj` and `tests/Elsa/Serialization/Tests/Elsa.Serialization.Tests.csproj` (xUnit, built-in `Assert`, no FluentAssertions; `Unit/` folder; modeled on the Activities.Design.Tests csproj). Both added to `Elsa.Server.slnx`.
- [ ] T002 [P] Investigation (research D6): determine the exact host project + route group that must serve `GET /_elsa/workflow-management/descriptors/variables` — compare the `descriptors/activities` mapping in `src/Apps/Elsa.Server/ElsaWorkflowManagementApi.cs` against an `Elsa.Activities.Design.Api` endpoint group. Record the chosen host/route in `research.md` (D6).
- [ ] T003 [P] Investigation (research D10): catalog every use site of `src/Elsa/Expressions/Core/Models/VariableDescriptor.cs`; decide whether `TypeDescriptor` folds it in or coexists. Record the decision + impacted files in `research.md` (D10).

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: shared value objects + contracts every story depends on. ⚠️ No story work begins until this phase is complete.

- [X] T004 [P] Create `CollectionKind` enum (`Single|Array|List|HashSet`) in `src/Elsa/Primitives/Primitives/Models/CollectionKind.cs`.
- [X] T005 [P] Create `TypeReference` sealed record `{ string Alias, CollectionKind CollectionKind = Single }` in `src/Elsa/Primitives/Primitives/Models/TypeReference.cs`. (Also added `TypeReferenceFactory` in the same folder — the shared CLR↔TypeReference helper, see T016/scanner.)
- [X] T006 [P] Create `TypeDescriptor` shared shape record `{ string Alias, Type ClrType, string DisplayName, string Category, string DefaultEditor }` in `src/Elsa/Expressions/Core/Models/TypeDescriptor.cs`.
- [X] T007 [P] Create `IVariableTypeDescriptorProvider` contract (`IEnumerable<TypeDescriptor> GetDescriptors()`) in `src/Elsa/Expressions/Core/Contracts/IVariableTypeDescriptorProvider.cs`.
- [X] T008 [P] Create `IVariableTypeDescriptorCatalog` contract (union + grouped-by-category) in `src/Elsa/Expressions/Core/Contracts/IVariableTypeDescriptorCatalog.cs`.
- [X] T009 [P] Create domain exceptions `DuplicateTypeAliasException` and `ReservedAliasNamespaceException` in `src/Elsa/Serialization/Core/Exceptions/` (each carrying the offending alias).

**Checkpoint**: value objects + contracts compile; stories can begin.

---

## Phase 3: User Story 1 — Typed, collection-aware arguments round-trip & resolve (Priority: P1) 🎯 MVP

**Goal**: Variables/Inputs/Outputs persist `{ alias, collectionKind }` only; all four kinds resolve to the correct CLR type for all three argument kinds.

**Independent Test**: serialize each of the 3 records with each of the 4 kinds; assert round-trip carries only `alias`+`collectionKind` (no namespace/assembly/version) and `VariableMapper` yields `T`/`T[]`/`List<T>`/`HashSet<T>`.

### Tests for User Story 1 (write first; must fail before impl)

- [X] T010 [P] [US1] `VariableMapperTests` covering all 12 alias×kind combinations + unknown-alias→`object` fallback, in the Expressions test project `Unit/VariableMapperTests.cs`.
- [X] T011 [P] [US1] Serialization round-trip tests for `VariableDefinition`/`InputDefinition`/`OutputDefinition` asserting the emitted JSON has `alias`+`collectionKind` and **no** `typeName`/`namespace`/`assemblyName`/`assemblyVersion`, in the relevant test project(s).
- [X] T012 [P] [US1] `TypeJsonConverterTests` extending coverage to `HashSet<>` read/write parity, in the Serialization test project `Unit/TypeJsonConverterTests.cs`.

### Implementation for User Story 1

- [X] T013 [US1] Change `VariableDefinition`: replace `TypeInformation TypeInformation` with `TypeReference Type`; change `StorageDriverType` to `string?` (bare alias). File: `src/Elsa/Expressions/Core/Models/VariableDefinition.cs`.
- [X] T014 [P] [US1] Change `InputDefinition`: `Type` → `TypeReference`; `StorageDriverType` → `string?`. File: `src/Elsa/Activities/Design/Core/Models/InputDefinition.cs`.
- [X] T015 [P] [US1] Change `OutputDefinition`: `Type` → `TypeReference`; `StorageDriverType` → `string?`. File: `src/Elsa/Activities/Design/Core/Models/OutputDefinition.cs`.
- [X] T016 [US1] Rewrite `VariableMapper.Map(VariableDefinition)`: resolve `Type.Alias` via `IWellKnownTypeRegistry`; close by `CollectionKind` (`Single→T`, `Array→MakeArrayType`, `List→List<T>`, `HashSet→HashSet<T>`); resolve `StorageDriverType` alias; unknown alias → `object` + warning (alias preserved on the record). File: `src/Elsa/Expressions/Services/VariableMapper.cs`.
- [X] T017 [US1] Rewrite `VariableMapper.Map(IVariable)`: decompose the variable's value type into `(alias, CollectionKind)` (array/`List<>`/`HashSet<>`/scalar). Same file.
- [X] T018 [US1] Add `HashSet<>` read+write to `TypeJsonConverter` (compiled-Type path parity, FR-008). File: `src/Elsa/Serialization/SystemText/JsonConverters/TypeJsonConverter.cs`.
- [X] T019 [US1] Implement `public sealed DefaultVariableTypeDescriptorProvider` returning framework primitive `TypeDescriptor`s (`String/Int32/Boolean/DateTime/Guid/Object/…` with displayName, category="Primitives", defaultEditor). File: `src/Elsa/Expressions/Services/DefaultVariableTypeDescriptorProvider.cs`.
- [X] T020 [US1] Implement `public sealed SeedWellKnownTypesStartupTask` seeding `IWellKnownTypeRegistry` with each provider's `(Alias, ClrType)` (single registration site). File: `src/Elsa/Serialization/SystemText/Startup/SeedWellKnownTypesStartupTask.cs`.
- [X] T021 [US1] Register the framework provider in `ExpressionsFeature.ConfigureServices` and the seed task in `SerializationFeature.ConfigureServices`. Files: `src/Elsa/Expressions/ExpressionsFeature.cs`, `src/Elsa/Serialization/SystemText/SerializationFeature.cs`.
- [X] T022 [US1] Verify/adjust `VariableConverter` (delegates to mapper) for the new model. File: `src/Elsa/Expressions/JsonConverters/VariableConverter.cs`. (No change needed — it delegates fully to `IVariableMapper`; confirmed it compiles and round-trips after the mapper rewrite.)
- [X] T023 [US1] Feature registration test (§2.23.1): `ExpressionsFeature`/`SerializationFeature` resolve the new provider + seed task. File: `*FeatureRegistrationTests.cs` in the matching test projects.

**Checkpoint**: typed arguments round-trip and resolve with framework primitives — MVP complete and independently testable.

---

## Phase 4: User Story 2 — Module-contributed type catalog + descriptors endpoint (Priority: P2)

**Goal**: the aggregated, module-contributed type catalog is served (grouped by category) at `descriptors/variables`, each entry `{ alias, displayName, category, defaultEditor }`.

**Independent Test**: register two providers with distinct aliases; assert the catalog returns their union grouped by category, and the endpoint payload matches `contracts/wire-contract.md`.

### Tests for User Story 2 (write first)

- [ ] T024 [P] [US2] `VariableTypeDescriptorCatalogTests`: aggregation union across providers, grouping by category, and cross-provider duplicate-alias → `DuplicateTypeAliasException`. File: `Unit/VariableTypeDescriptorCatalogTests.cs`.
- [ ] T025 [P] [US2] Endpoint/registration test: `descriptors/variables` returns the catalog in the wire-contract shape. File: matching API test project.

### Implementation for User Story 2

- [ ] T026 [US2] Implement `public sealed VariableTypeDescriptorCatalog : IVariableTypeDescriptorCatalog` — constructor injects `IEnumerable<IVariableTypeDescriptorProvider>`, aggregates once (mirror `ExpressionDescriptorRegistry`), exposes union + grouped view. File: `src/Elsa/Expressions/Services/VariableTypeDescriptorCatalog.cs`.
- [ ] T027 [US2] Register the catalog as a singleton in `ExpressionsFeature.ConfigureServices`. File: `src/Elsa/Expressions/ExpressionsFeature.cs`.
- [ ] T028 [US2] Implement the `descriptors/variables` endpoint (mirror `src/Elsa/Secrets/Api/Endpoints/Secrets/Descriptors.cs`), returning `{ descriptors: [...] }` per the wire contract, at the host/route confirmed in T002.
- [ ] T029 [US2] Wire the endpoint route group + permissions consistent with `descriptors/activities`.
- [ ] T030 [US2] Apply the T003 decision reconciling `VariableDescriptor` with `TypeDescriptor` (fold or coexist); update use sites.
- [ ] T031 [US2] Feature registration test (§2.23.1) for the catalog + endpoint wiring.

**Checkpoint**: the studio (Phase 2) can fetch a real, grouped, module-contributed type list with default-editor hints.

---

## Phase 5: User Story 3 — Rename-proof identity & fail-fast registration (Priority: P3)

**Goal**: duplicate/reserved alias registration fails fast at startup; unknown aliases round-trip without throwing; alias is frozen.

**Independent Test**: (a) register an alias twice → startup throws; (b) register a bare non-primitive alias → throws; (c) load a definition with an unregistered alias → load succeeds and the alias string is preserved on re-save.

### Tests for User Story 3 (write first)

- [ ] T032 [P] [US3] `WellKnownTypeRegistryTests`: duplicate alias throws `DuplicateTypeAliasException`; duplicate type throws; bare non-primitive alias throws `ReservedAliasNamespaceException`; happy-path resolve; nullable auto-registration does not self-collide. File: `Unit/WellKnownTypeRegistryTests.cs`.
- [ ] T033 [P] [US3] Graceful-unknown test: a `VariableDefinition` with an unregistered alias round-trips the alias verbatim and `VariableMapper` resolves it to `object` with a warning (no throw). File: Expressions test project.

### Implementation for User Story 3

- [ ] T034 [US3] Harden `WellKnownTypeRegistry.RegisterType`: throw `DuplicateTypeAliasException` on repeat alias or repeat type (was silent last-writer-wins); keep nullable auto-registration collision-safe. File: `src/Elsa/Serialization/SystemText/Services/WellKnownTypeRegistry.cs`.
- [ ] T035 [US3] Add the reserved-namespace guard: a bare (non-dotted) alias not in the framework-reserved primitive set throws `ReservedAliasNamespaceException`; primitives register through the trusted seed path. Same file.
- [ ] T036 [US3] Audit existing `RegisterType` callers so the seed registers each alias exactly once under the new throw-on-duplicate behavior (grep `RegisterType`, `WellKnownTypeRegistry`).
- [ ] T037 [US3] Wrap `JsonException` at the argument (de)serialization boundary into a domain exception (§2.23.5), preserving inner exception. Files: serialization converters in the authored path.
- [ ] T038 [US3] Document `IWellKnownTypeRegistry` fail-fast + frozen-alias contract in XML docs. File: `src/Elsa/Serialization/Core/IWellKnownTypeRegistry.cs`.

**Checkpoint**: all three stories independently functional.

---

## Phase 6: Polish & Cross-Cutting (follow-through obligations)

- [ ] T039 [P] Add the `IVariableTypeDescriptorProvider` extension-point entry to the owning project's `EXTENSION_POINTS.md` (format per `src/Elsa/Expressions/EXTENSION_POINTS.md`): kind=Source, signature, register, consumed-by catalog, known impl `DefaultVariableTypeDescriptorProvider`.
- [ ] T040 [P] Add glossary entries to `docs/glossary/elsa.md`: **type alias**, **collection kind**, **type descriptor catalog**, **argument descriptor** (link to spec + serialization rule).
- [ ] T041 [P] Add a short note to `docs/serialization.md` that authored-definition type refs are alias-based `{ alias, collectionKind }` (the decomposed `TypeInformation` is compiled-Type-path only).
- [ ] T042 [P] Add a one-line finding to `docs/reports/unfinished-work.md` noting the studio Phase-2 follow-up (none/free-flow) and the separate runtime-variables-persistence dependency.
- [ ] T043 Refresh generated maps: run `bash tools/maps/generate-extension-point-map.sh` and `bash tools/maps/generate-architecture-reference-map.sh`; review the findings reports before committing.
- [ ] T044 Run `quickstart.md` end-to-end verification (build, all new tests green, curl the descriptors endpoint, confirm emitted JSON matches the wire contract).

---

## Dependencies & Execution Order

- **Setup (P1)**: T001–T003 independent, run first (T002/T003 unblock T028/T030).
- **Foundational (P2)**: T004–T009 — block all stories. T004/T005 (Primitives) block the record/mapper edits; T006–T008 block the catalog; T009 blocks registry hardening.
- **US1 (P1)**: after Foundational. Tests T010–T012 before impl T013–T023. T013 before T016/T017 (mapper needs the new record); T019 before T020 (seed reads the provider); T020/T021 before resolution works.
- **US2 (P2)**: after Foundational; T026 needs T006–T008; T028 needs T002. Independent of US3.
- **US3 (P3)**: after Foundational; hardens the registry US1 seeded. T034/T035 must not break the T020 seed (T036 audit).
- **Polish (P6)**: after the stories whose surfaces it documents (T039 after US2; T040–T042 after US1–US3; T043 after T039).

### Parallel opportunities

- T001/T002/T003 together; T004–T009 together; within US1 the three test tasks (T010–T012) together and the two sibling record edits (T014/T015) together.

## Implementation Strategy

- **MVP = US1**: Setup → Foundational → US1, then STOP and validate typed arguments round-trip + resolve for framework primitives.
- **Incremental**: add US2 (catalog/endpoint unblocks studio Phase 2), then US3 (robustness), then Polish (catalog/glossary/maps).
- Commit after each task or logical group; per the operating model, make a local commit when a coherent unit lands.

## Notes

- T001 result (test project paths/conventions) to be recorded here once confirmed.
- Out of scope (do not add tasks): studio/frontend impl, runtime variable/input execution-persistence, `Dictionary<K,V>` generics, per-type kind restrictions.
