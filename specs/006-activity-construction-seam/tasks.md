# Tasks: Descriptor-Type-Driven Activity Construction

**Input**: Design documents from `specs/006-activity-construction-seam/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/

**Tests**: INCLUDED. Mandated by constitution G27 (registration tests per feature + branch-covering unit tests) and because the spec's Success Criteria (SC-001…006) are structural/behavioural test outcomes. xunit only — **no FluentAssertions**.

**Organization**: Tasks are grouped by user story. NOTE on independence: this is a refactor, not greenfield. The runtime seam (Phase 2) is a hard prerequisite for every kind. The CLR and Workflow kinds (Phases 3–4) are additive and keep the build green. The design-domain purge + reshape (Phase 5) must land as **one coherent red→green unit** — deleting `IImplementationDescriptor` breaks persistence/reconciliation/API until they are reshaped in the same pass. US1's *guarantee* (§E2.2) is achieved by Phases 2+5 and *verified* in Phase 6.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no incomplete dependencies)
- **[Story]**: US1–US6 from spec.md (Setup/Foundational/Polish have no story label)

## Path Conventions
Modular .NET feature framework. Code under `src/<Project>/...`, tests under `tests/<Project>.Tests/...`. Build: `dotnet build Elsa.Server.slnx`.

---

## Implementation Status Note (2026-07-02) — read before resuming

An audit reconciled this file against the actual tree. Two drifts affect the **open** tasks below:

1. **Layout is nested, not flat.** The repo was reorganised to `src/Elsa/Activities/...` after this file was authored. Translate every flat path: `src/Elsa.Activities.Primitives` → `src/Elsa/Activities/Primitives`; `tests/Elsa.Activities.Primitives.Tests` → `tests/Elsa/Activities/Primitives/Tests`; etc.
2. **FR-009 CLR descriptor superseded by spec 081.** The CLR descriptor is **not** `TypeInformation`; the shipped scanner emits an alias-based `ClrActivityDescriptor` (`DescriptorType = typeof(ClrActivityDescriptor).FullName`, payload = `TypeAliasConvention.CanonicalAlias(type)`). Implement/verify against the current code, not FR-009's literal text. The Workflow kind still correctly uses `WorkflowIdentity`.

**Done (verified green):** Phases 1–5 seam/CLR/Workflow-runtime/design-purge; plus T002/T028/T029/T031 — the `Composition.Design` reconciliation source + feature + host wiring landed on branch `006-activity-construction-seam` (commit `96e8072e`). Full solution built green as of this note.

**Remaining open:** T004, T018, T023, T025, T041, T042, T046, T050–T055 (a new `tests/Elsa/Activities/Primitives/Tests` project + binder/registration tests; the SC-proving architecture tests T050–T053; persistence/read-contract tests; JSON reconciliation field verify; feature docs; final `dotnet build Elsa.Server.slnx` + all-tests + quickstart validation). None depend on undecided design.

### Code review (2026-07-02, xhigh)

Fixed on `006-b1-port-adapter`: **soft-delete leak** — the adapter now skips `WorkflowDefinition.DeletedAt != null` (the list port doesn't filter soft-deleted rows; a deleted usable workflow was being re-catalogued permanently), with a regression test; **test DRY** — store fakes/builders extracted to `tests/.../Tests/TestSupport/WorkflowDesignStubs.cs`.

Deferred for the architect / fresh pass (judgment calls, recorded so they aren't lost):
- **Feature dependency not declared.** `ActivitiesCompositionDesign` needs a Workflows.Design persistence provider at runtime; a `DependsOn` is intentionally omitted because the stores are a provider-neutral contract with no single feature to name (documented on the feature). Decide whether composition validation should enforce it another way.
- **Startup ordering.** Activity reconciler is `[Order(1)]`, workflow reconciler `[Order(2)]` — workflows provisioned at startup won't appear as activities until the second reconciliation.
- **Tenancy.** The scan uses a tenant-scoped filter; a no-tenant startup reconciliation sees only the default tenant.
- **Per-version category.** `ActivityCategory` is per-version but the reconciler collapses versions to one definition (first-seen wins).
- **`SourceId` isolation.** `SourceId`/`SourceKind` are provenance only; a cross-source `ActivityTypeKey` collision would abort reconciliation (collision practically improbable — definition GUIDs vs CLR FullNames).

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Create the new projects, the shared descriptor model, and test scaffolding.

- [x] T001 Create project `src/Elsa.Activities.Primitives/Elsa.Activities.Primitives.csproj` (net10.0; runtime feature, **NO `Design.*` ref**; refs: `Elsa.Activities.Runtime.Core`, `Elsa.Primitives`, `CShells.Abstractions`, `Microsoft.Extensions.DependencyInjection.Abstractions`)
- [x] T002 [P] Create TWO projects (Workflow kind split per §E2.2). **Actual nested paths** (repo reorganised since spec authored): `src/Elsa/Activities/Composition/Runtime` (Design-free) and `src/Elsa/Activities/Composition/Design`. **Dependency note:** Composition.Design also references `Elsa.Workflows.Design.Core` + `Elsa.Workflows.Design.Persistence.Core` beyond the originally-listed refs — required for T028 discovery (list definitions/versions, read authored `WorkflowActivityOptions`/I-O). This is Design→Design (contract/model libs only); no §E2.2 (Runtime→Design) or SC-006 (feature→feature) violation.
- [x] T003 [P] Add `WorkflowIdentity` record `(string DefinitionId, string VersionId, string Version)` in `src/Elsa.Workflows.Primitives/Models/WorkflowIdentity.cs`
- [ ] T004 [P] Create test projects `tests/Elsa.Activities.Runtime.Tests`, `tests/Elsa.Activities.Primitives.Tests`, `tests/Elsa.Activities.Composition.Tests` (xunit only, NO FluentAssertions); confirm `tests/Elsa.Activities.Design.Tests` exists for reshape tests
- [ ] T005 Register the new src + test projects in `Elsa.Server.slnx` and add the two features to the host composition where appropriate

**Checkpoint**: Solution builds with empty new projects.

---

## Phase 2: Foundational — Runtime construction seam (BLOCKS all kinds)

**Purpose**: The Design-free dispatch seam every kind plugs into. Serves US2 (round-trip infra) and US5 (uniqueness guard); removes the leaked descriptor-carrying factory signature (US1).

**⚠️ CRITICAL**: No kind (Phase 3/4) can begin until this completes.

- [x] T006 [P] Define `IActivityConstructor` (non-generic) + `IActivityConstructor<TDescriptor>` in `src/Elsa.Activities.Runtime.Core/Contracts/IActivityConstructor.cs` (contribution contract; `DescriptorType => typeof(TDescriptor).FullName!`; bridge owns `Deserialize<TDescriptor>`)
- [x] T007 [P] Define `IActivityConstructorRegistry` in `src/Elsa.Activities.Runtime.Core/Contracts/IActivityConstructorRegistry.cs` (replacement contract; `Add`/`Resolve`)
- [x] T008 [P] Define `OnActivityConstructorsInitializing : IEvent` in `src/Elsa.Activities.Runtime.Core/Events/OnActivityConstructorsInitializing.cs` (exposes the registry/collection)
- [x] T009 [P] Define `DuplicateActivityConstructorException` + `UnknownDescriptorTypeException` in `src/Elsa.Activities.Runtime.Core/Exceptions/`
- [x] T010 Reshape `IActivityFactory` in `src/Elsa.Activities.Runtime.Core/Contracts/IActivityFactory.cs` → `Create(string descriptorType, JsonElement payload, IDictionary<string,InputArgument>?, IDictionary<string,OutputArgument>?, CancellationToken)`; remove stale `IActivityImplementationResolver*` doc crefs
- [x] T011 [US5] Implement `ActivityConstructorRegistry` (public sealed) in `src/Elsa.Activities.Runtime/Services/ActivityConstructorRegistry.cs` — keyed by `DescriptorType`; `Add` throws `DuplicateActivityConstructorException`; `Resolve` throws `UnknownDescriptorTypeException`
- [x] T012 [US2] Implement `ActivityFactory` (public sealed) in `src/Elsa.Activities.Runtime/Services/ActivityFactory.cs` — pure dispatch via the registry (delete the old `Type`/reflection-binding body)
- [x] T013 Implement single aggregating handler `RegisterActivityConstructors` in `src/Elsa.Activities.Runtime/Handlers/RegisterActivityConstructors.cs` (`IEventHandler<OnActivityConstructorsInitializing>`; adds every registered `IActivityConstructor`)
- [x] T014 Implement `ActivityConstructorsStartupTask` in `src/Elsa.Activities.Runtime/Tasks/ActivityConstructorsStartupTask.cs` (publishes the event Sequential, once)
- [x] T015 Wire `src/Elsa.Activities.Runtime/ActivitiesRuntimeFeature.cs` — register factory, registry (singleton), handler, startup task
- [x] T016 [P] [US5] Unit test: `ActivityConstructorRegistry` throws on a 2nd constructor for one `DescriptorType`; resolves distinct types (`tests/Elsa.Activities.Runtime.Tests/`)
- [x] T017 [P] [US2] Unit test: `ActivityFactory` resolves + delegates; unknown `descriptorType` → `UnknownDescriptorTypeException`
- [ ] T018 [P] Registration test: `ActivitiesRuntimeFeature` registers and resolves every service (G27)

**Checkpoint**: The seam exists, Design-free; round-trip not yet provable (no kinds).

---

## Phase 3: User Story 4 + CLR half of US2 — CLR kind (Priority: P2 / P1) 🎯 MVP

**Goal**: `TypeInformation` is the CLR descriptor; `Elsa.Activities.Primitives` constructs hand-written activities.

**Independent Test**: Route `("Elsa.Primitives.Models.TypeInformation", <TypeInformation payload>)` + args through `IActivityFactory.Create`; assert the named CLR activity instantiates with `InputArgument<T>`/`OutputArgument<T>` bound.

- [x] T019 [P] [US4] Implement `ActivityArgumentBinder` (public sealed, **feature-internal**) in `src/Elsa.Activities.Primitives/Binding/ActivityArgumentBinder.cs` — match by name + `PropertyType.IsAssignableFrom(arg type)` → invoke set-method. **FIX bugs**: use `property.PropertyType` (not `property.GetType()`), use `IsAssignableFrom` (not `!=`)
- [x] T020 [US4] Implement `ClrActivityConstructor : IActivityConstructor<TypeInformation>` (public sealed) in `src/Elsa.Activities.Primitives/Constructors/ClrActivityConstructor.cs` — `descriptor.LoadType()` + `ActivatorUtilities.CreateInstance` + binder; one-line bridge
- [x] T021 [P] [US4] **Port `WriteLine`** from `C:\Users\JoeyBarten\source\repos\elsa-core\src\modules\Elsa.Workflows.Core\Activities\WriteLine.cs` into `src/Elsa.Activities.Primitives/Activities/WriteLine.cs`, adapting to this repo's model: drop the `CodeActivity`/`Input<string>`/`[Activity]` elsa-core types; implement `IActivity` with a `public InputArgument<string> Text` property and an `ExecuteAsync` that writes to console. (Only `WriteLine` for now.) This is the concrete activity the CLR binding round-trip (T024) binds against.
- [x] T022 [US4] Wire `src/Elsa.Activities.Primitives/ActivitiesPrimitivesFeature.cs` — register `ClrActivityConstructor`, `ActivityArgumentBinder`, activities
- [ ] T023 [P] [US4] Unit tests: `ActivityArgumentBinder` branches — match-and-set, type-mismatch throw, missing-property throw, no-public-setter throw (`tests/Elsa.Activities.Primitives.Tests/`)
- [x] T024 [P] [US4] Unit test: `ClrActivityConstructor` round-trip — payload → loaded type → bound instance
- [ ] T025 [P] Registration test: `ActivitiesPrimitivesFeature` resolves (G27)

**Checkpoint**: CLR round-trip provable end-to-end through the factory (MVP slice).

---

## Phase 4: Workflow half of US2 — Workflow kind (Priority: P1)

**Goal**: One backing `WorkflowDefinitionActivity` for every workflow-backed activity, selected by `WorkflowIdentity`. **Construct-only** (execution body deferred).

**Independent Test**: Route `("Elsa.Workflows.Primitives.Models.WorkflowIdentity", <identity payload>)` + args; assert a `WorkflowDefinitionActivity` with the identity applied and author args in the bag; two identities → two instances differing only by identity.

- [x] T026 [US2] Implement `WorkflowDefinitionActivity` (public sealed, runtime-side, **no Design ref**) in `src/Elsa.Activities.Composition.Runtime/Activities/WorkflowDefinitionActivity.cs` — an ordinary CLR `IActivity` (so it is also catalogued under a `TypeInformation` descriptor); construct-only; typed `WorkflowIdentity`/version state + dynamic bag (`IActivity.SyntheticProperties`)
- [x] T027 [US2] Implement `WorkflowActivityConstructor : IActivityConstructor<WorkflowIdentity>` (public sealed) in `src/Elsa.Activities.Composition.Runtime/Constructors/WorkflowActivityConstructor.cs` — produces a `WorkflowDefinitionActivity` configured from the identity (typed state) + author args pre-set in the bag; does its **own** bag-filling (no ref to `Primitives`' binder); one-line bridge
- [x] T028 [US2] Implemented the Workflow-kind design side as a **port + adapter (§2.7)** so the reconciliation source carries no Workflows.Design dependency:
  - `IUsableAsActivityWorkflowSource` port + neutral `UsableAsActivityWorkflow` record (I/O reuses `InputDefinition`/`OutputDefinition`).
  - `WorkflowDefinitionUsableAsActivitySource` adapter — the **only** class touching Workflows.Design read ports. Discovery = full scan (`IWorkflowDefinitionStore.ListAsync` → `IWorkflowDefinitionVersionStore.ListByDefinitionAsync`) filtered on `WorkflowActivityOptions.UsableAsActivity`. **The confirmed Decision-1 follow-up (persisted `UsableAsActivity` shadow column for EF Core + index for Groundwork, plus a targeted store query) lands here — the reconciliation source and its tests do not change.**
  - `WorkflowActivityReconciliationSource` is now a pure mapper over the port → one row per usable version (`ActivityTypeKey = definitionId`, `Version` = SemVer, `DescriptorType = typeof(WorkflowIdentity).FullName`, descriptor `WorkflowIdentity(defId, versionId, version)`, I/O mirrored).
  - Tests: `WorkflowActivityReconciliationSourceTests` (mapper, fake port), `WorkflowDefinitionUsableAsActivitySourceTests` (adapter, store stubs). (outcome/port visualization is future module-owned facet work, 005 FR-005/006)
- [x] T029 [US2] Wired `ActivitiesCompositionRuntimeFeature` (constructor + activity, pre-existing) and `ActivitiesCompositionDesignFeature` (registers the `IUsableAsActivityWorkflowSource` adapter + the `IActivityReconciliationSource`). Host wiring added: `Elsa.Server.slnx`, `Elsa.Server.csproj` project ref, `Program.cs` feature-assembly registration, and `shells.baseline.json` (`ActivitiesCompositionDesign`).
- [x] T030 [P] [US2] Unit test: `WorkflowActivityConstructor` → `WorkflowDefinitionActivity` with identity applied + bag filled; two identities → distinct instances
- [x] T031 [P] Registration test: Composition Design feature registers its reconciliation source (`ActivitiesCompositionDesignFeatureTests`); Composition Runtime constructor registration covered via `WorkflowActivityConstructorTests` + `BoundedContextReferenceTests` (G27)
- [x] T032 [P] [US2] §E2.2 reference test: `Elsa.Activities.Composition.Runtime` references **no** `Elsa.*.Design.*` project (now a true project-reference test, enabled by the split)

**Checkpoint**: Both kinds construct through one factory; no `Kind` string anywhere.

---

## Phase 5: User Story 3 — Design-domain purge + persistence reshape (Priority: P1) ⚠️ coherent landing

**Goal**: The design domain treats descriptors as opaque `(DescriptorType, payload)`; `IImplementationDescriptor` and the design-side registries are gone.

**Independent Test**: Persist a reconciled `ActivityDefinitionVersion`; reload; assert descriptor survives as `(DescriptorType, JsonElement)` with no deserialization-to-type; assert `Elsa.Activities.Design.Core` defines no descriptor type / `IImplementationDescriptor`.

**⚠️ Land T033–T048 together** — the build is red between the deletion and the reshapes.

- [x] T033 [US3] Delete from `src/Elsa.Activities.Design.Core/`: `Contracts/IImplementationDescriptor.cs`, `Contracts/IImplementationDescriptorRegistry.cs`, `Contracts/IImplementationDescriptorSource.cs`, `Events/OnImplementationDescriptorsInitializing.cs`, `Models/ClrImplementationDescriptor.cs`, `Models/WorkflowImplementationDescriptor.cs`, `Models/ImplementationDescriptorRegistration.cs`, `Models/ImplementationDescriptorRegistry.cs` (+ any design-side `RegisterImplementationDescriptors`/descriptor-source impls/startup tasks)
- [x] T034 [US3] Reshape `src/Elsa.Activities.Design.Core/Contracts/IActivityDefinitionVersion.cs` — remove `ImplementationKind` + `IImplementationDescriptor`; **add `string DescriptorType` + `JsonElement DescriptorPayload`** (decided)
- [x] T035 [US3] Reshape `src/Elsa.Activities.Design.Persistence.Core/Entities/ActivityDefinitionVersion.cs` — `ImplementationKind`→`DescriptorType`; `ImplementationDescriptorPayload`→`DescriptorPayloadSource`; `[NotMapped] IImplementationDescriptor`→`[NotMapped] JsonElement DescriptorPayload`; update explicit interface impls
- [x] T036 [US3] Delete `src/Elsa.Activities.Design.Persistence.Core/Exceptions/ActivityDescriptorDeserialisationException.cs`; update `Filters/ActivityDefinitionVersionFilter.cs` to the new shape
- [x] T037 [US3] Rewrite `src/Elsa.Activities.Design.Persistence.EFCore/EntityHandlers/ActivityDefinitionVersionSavingHandler.cs` — serialize `DescriptorPayload`→`DescriptorPayloadSource`; remove `.Kind` derivation
- [x] T038 [US3] Rewrite `src/Elsa.Activities.Design.Persistence.EFCore/EntityHandlers/ActivityDefinitionVersionLoadingHandler.cs` — parse `DescriptorPayloadSource`→`JsonElement`; **remove `IImplementationDescriptorRegistry` dependency** (the §E2.2-leak's reason to exist)
- [x] T039 [US3] Update `src/Elsa.Activities.Design.Persistence.EFCore/Configurations/ActivityDefinitionVersionConfiguration.cs` — column rename + `PropertySaveBehavior.Throw` immutability on `DescriptorType` + `DescriptorPayloadSource`
- [x] T040 [US3] Delete `src/Elsa.Activities.Design.Persistence.EFCore.Sqlite/Migrations/*` and regenerate a fresh `Initial` migration (no data migration, D8)
- [ ] T041 [P] [US3] Unit test: save→load round-trips `(DescriptorType, JsonElement)` with no type resolution (`tests/Elsa.Activities.Design.Tests/`)
- [ ] T042 [P] [US3] Read-contract surface test: `IActivityDefinitionVersion` exposes `DescriptorType` + `DescriptorPayload`, and NOT `ImplementationKind`/`IImplementationDescriptor`

### Reconciliation + API + import reshape (same landing)

- [x] T043 [US3] Rename `ActivityVersionReconciliationModel.ImplementationKind`→`DescriptorType` in `src/Elsa.Activities.Design.Reconciliation.Core/Models/ActivityVersionReconciliationModel.cs`
- [x] T044 [US3] Simplify `src/Elsa.Activities.Design.Reconciliation/Services/ActivityVersionReconciler.cs` + `Handlers/ActivityVersionsReconcilingHandler.cs` — drop descriptor-type resolution; persist `(DescriptorType, payload)`; adjust `DefaultActivityDefinitionHasher.cs` + `InvalidActivityVersionReconciliationEntryException.cs` to the new field
- [x] T045 [US4] Update `src/Elsa.Activities.Design.Reconciliation.Clr/Services/ClrAssemblyScanner.cs` — emit `TypeInformation.FromType(type)` + `DescriptorType="Elsa.Primitives.Models.TypeInformation"`; drop `ClrImplementationDescriptor`
- [ ] T046 [US3] Update `src/Elsa.Activities.Design.Reconciliation.Json/` — catalog field `implementationKind`→`descriptorType`; adjust `JsonActivityCatalogReader` doc + any sample catalog JSON
- [x] T047 [US3] Update `src/Elsa.Activities.Design.Api/` — `Commands/AddDefinition.cs`, `Commands/AddVersion.cs`, `Handlers/AddDefinitionCommandHandler.cs`, `Handlers/AddVersionCommandHandler.cs`, `Mapping/ActivityDefinitionVersionToDetailsView.cs`, `Models/ActivityDefinitionVersionDetailsView.cs` → `(DescriptorType, payload)` shape
- [x] T048 [US3] Update `src/Elsa3.Activities.Design.Import/Models/ActivityDefinitionVersionImport.cs` → `(DescriptorType, payload)` shape (one-way adapter; no new direction)
- [x] T049 [US3] Migrate existing reconciliation/persistence tests to the new shape, preserving subject/objective (G20); record any test deletion approval (Joey, this session) in the test file/PR

**Checkpoint**: Build green again; design domain is descriptor-opaque.

---

## Phase 6: User Story 1 + User Story 6 — Verification & Polish (Priority: P1 / P3)

**Purpose**: Prove the invariants the whole unit exists for, and document the seam.

- [ ] T050 [US1] Reference/structural test: no project in the runtime construction path (`Elsa.Activities.Runtime`, `Elsa.Activities.Runtime.Core`, `Elsa.Activities.Primitives`, `Elsa.Activities.Composition.Runtime`) references any `Elsa.*.Design.*` project (SC-001) (`tests/Elsa.Activities.Design.Tests/` or a dedicated architecture-test project)
- [ ] T051 [US6] Structural test: `ActivityFactory`, `ActivityConstructorRegistry`, and `ActivityVersionsReconcilingHandler` contain no per-`DescriptorType`/per-kind branch (SC-004)
- [ ] T052 [P] [US1] Repo-wide sweep (test or script) asserting zero production references to `IImplementationDescriptor`, `ClrImplementationDescriptor`, `WorkflowImplementationDescriptor`, the two registries, `IImplementationDescriptorSource`, and `IActivityImplementationResolver*` (SC-002)
- [ ] T053 [P] Reference test: no feature project references another feature project — `Primitives`, `Composition.Runtime`, `Composition.Design`, `Reconciliation.Clr` (SC-006)
- [ ] T054 [P] [US6] Add feature documentation (G26) for `Runtime`, `Primitives`, `Composition` (registered handlers + startup task) and a seam-walk note showing a hypothetical new kind touches only 3 new types
- [ ] T055 Run `dotnet build Elsa.Server.slnx` + all tests; validate `quickstart.md` round-trip; confirm all SCs

---

## Dependencies & Execution Order

- **Phase 1 (Setup)**: no deps.
- **Phase 2 (Foundational seam)**: after Setup. **Blocks Phases 3, 4.**
- **Phase 3 (CLR kind)**: after Phase 2. Independent of Phase 4.
- **Phase 4 (Workflow kind)**: after Phase 2. Independent of Phase 3. (T028 reconciliation source has a soft tie to T043's renamed model — sequence T043 before T028, or stub then align in Phase 5.)
- **Phase 5 (purge + reshape)**: after the new seam exists (Phase 2) so nothing depends on the deleted descriptor-carrying factory. T033–T048 land **together** (build red→green). T045 depends on `TypeInformation` being the descriptor (Phase 3 design) but is edited here.
- **Phase 6 (verify/polish)**: after all desired phases.

### Within phases
- Tests for a story marked [P] are independent files — parallelizable.
- Contracts (T006–T009) before impls (T011–T014). `IActivityFactory` reshape (T010) before `ActivityFactory` impl (T012).

## Parallel Opportunities

- **Setup**: T002, T003, T004 in parallel (T001 first only if shared folder ordering matters).
- **Phase 2 contracts**: T006, T007, T008, T009 in parallel; then T011–T014.
- **Phase 2 tests**: T016, T017, T018 in parallel.
- **Phase 3**: T019 + T021 in parallel; tests T023/T024/T025 in parallel.
- **Phase 5 tests**: T041, T042 in parallel; verification tests T050/T052/T053 in parallel.

## Implementation Strategy

### MVP (smallest demonstrable slice)
Phases 1 → 2 → 3. Delivers the descriptor-type-driven factory + the CLR round-trip — provable end-to-end without touching the design domain. Stop and validate.

### Incremental
1. Setup + Foundational → seam ready.
2. + CLR kind (Phase 3) → CLR round-trip (MVP).
3. + Workflow kind (Phase 4) → both kinds construct.
4. + Design purge/reshape (Phase 5) → **the §E2.2 payoff** (US1/US3); build green.
5. + Verification (Phase 6) → invariants proven (SC-001…006).

---

## Constitutional Compliance

Tasks inherit the G1–G30 gates decided in `plan.md`. Specifically:
- New packages (`Elsa.Activities.Primitives`, `Elsa.Activities.Composition`) use domain-language names; no `Features.*`/`.Contracts`/`.Abstractions` segments (G2). No feature references another feature (G4, T053).
- `IActivityArgumentBinder`/`ActivityArgumentBinder` stay in `Elsa.Activities.Primitives`, NOT in any `.Core` (core-not-a-bucket rule; G3-adjacent).
- The constructor registry is populated via Registry + StartUp Task + Domain Event (G21) — no `IEnumerable<TProvider>` consumption injection.
- No `Elsa.Activities.Runtime.*` → `Elsa.Activities.Design.*` reference (Elsa §E2.2 / G15, T050).
- Logic-bearing classes `public sealed`; feature classes `public` not sealed (G27). xunit only, no FluentAssertions.
- Test deletions (T049) require recorded architect approval (G20).

If any task appears to require a constitutional exception, flag it in the task and escalate rather than implementing the violation.
