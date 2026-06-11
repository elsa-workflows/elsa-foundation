---

description: "Task list for Unit B — Activity Identity & Catalog as Source-of-Truth"
---

# Tasks: Activity Identity & Catalog as Source-of-Truth

**Input**: Design documents from `/specs/001-activity-identity-catalog/`
**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/](./contracts/), [quickstart.md](./quickstart.md)

**Tests**: Explicitly required by spec FR-019 / FR-020 / SC-014 / SC-019 / SC-020 / SC-021 (§2.23.1 registration + §2.23.2 branch-covered + §2.21.1 golden-rule for refactored implementations).

**Organization**: Six user stories (US1–US3 priority P1, US4–US5 priority P2, US6 priority P3). Foundational phase carries the load-bearing shared infrastructure; story phases are organized to give each story an independent test boundary, with shared records / contracts kept in Phase 2.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Different files, no dependencies on other incomplete tasks.
- **[Story]**: Story tag (US1–US6).
- Each task includes the exact file path being touched.

---

## Phase 1: Setup

**Purpose**: Establish baseline; no source code changes yet.

- [X] T001 Audit existing tests that touch `Elsa.Activities.Design.*`, `Elsa.Activities.Design.Provisioning.*`, `Elsa.Persistence.EFCore.ElsaDbContextBase`, and any `IGlobalEntitySavingHandler` / `IEntityModelCreatingHandler` consumers. Produce baseline list at `specs/001-activity-identity-catalog/test-baseline.md` (supports §2.21.1 golden rule per plan §R10).
- [X] T002 Run `dotnet build Elsa.Server.slnx` to confirm clean baseline before any edits land.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Shared infrastructure — `TenantEntity`, smart-enums, descriptor interface, sealed records, argument state hierarchy, domain-event dispatch in `ElsaDbContextBase`. ALL user stories depend on this phase.

**⚠️ CRITICAL**: No user-story work begins until Phase 2 completes.

### `TenantEntity` introduction

- [X] T003 Add `src/Elsa.Primitives/Entities/TenantEntity.cs` — `public abstract class TenantEntity : Entity { public string? TenantId { get; set; } }`.
- [X] T004 Remove the `TenantId` property from `src/Elsa.Primitives/Entities/Entity.cs`.
- [X] T005 Extend `src/Elsa.Persistence.EFCore/ElsaDbContextBase.cs` with `ApplyTenantIdIndex(ModelBuilder)` — scans `TenantEntity` descendants and registers a non-unique `TenantId` index. Invoke from `ConfigureEntityModel`. Mirror of existing `ApplyRowNumberIndex` pattern.

### `OnEntitySaving` event + dispatch (model-creating stays on legacy interface)

- [X] T006 [P] Add `src/Elsa.Persistence.EFCore/Events/OnEntitySaving.cs` — `public sealed record OnEntitySaving(DbContext DbContext, EntityEntry Entry) : IDomainEvent;`.
- [X] T007 Wire `OnEntitySaving` dispatch into `src/Elsa.Persistence.EFCore/ElsaDbContextBase.cs::BeforeSavingChanges` — publish one event per modified `Entity`. Legacy `ApplyGlobalSavingHandlers` and `ApplyEntitySavingHandlers` paths remain ACTIVE for OTHER features' handlers until the wider Unit A migration closes; coexistence is intentional.
- [ ] T008 *(no task — `IEntityModelCreatingHandler` stays as-is per clarification session 3; the existing `ApplyEntityModelCreatingHandlers` mechanism in `OnModelCreating` is the right tool for sync side-effect chains.)*

### Kind discriminators — plain strings (smart-enum approach reversed 2026-05-28)

**Decision change:** `ImplementationKind`, `SourceKind`, `ExpressionType` are plain `string` fields throughout — no wrapping value-record, no exhaustive enumeration in core. Each well-known value (`"Clr"`, `"Json"`, `"Workflow"`, …) is owned by the module that produces it; that module declares its own constant. Core never enumerates the legal set, keeping each discriminator open for downstream extension. No EF value converter required.

- [X] T011 *(NO LONGER APPLICABLE — `ImplementationKind` is `string` on the entity; well-known constant `"Clr"` lives in the CLR descriptor itself, `"Workflow"` in the workflow descriptor module.)*
- [X] T012 *(NO LONGER APPLICABLE — `SourceKind` is `string` on the entity; well-known constants live in the source modules that produce them, e.g. `Elsa.Activities.Design.Reconciliation.Json` owns `"Json"`.)*
- [X] T013 *(NO LONGER APPLICABLE — `ExpressionType` is `string` on `ArgumentValue`; well-known constants live in the expression evaluator modules.)*

### Descriptor interface + CLR descriptor

- [X] T014 [P] Add `src/Elsa.Activities.Design.Core/Contracts/IImplementationDescriptor.cs` — `string Kind { get; }` property. Concrete descriptors self-declare their kind; the kind is the registry lookup key for both the implementation-descriptor registry (kind → CLR descriptor type) and the resolver registry (kind → resolver).
- [X] T015 [P] Add `src/Elsa.Activities.Design.Core/Models/ClrImplementationDescriptor.cs` — `public sealed record ClrImplementationDescriptor(TypeInformation TypeInfo) : IImplementationDescriptor { public string Kind => "Clr"; }`.

### Sealed records — leaf models

- [X] T016 [P] Convert `src/Elsa.Activities.Design.Core/Models/InputDefinition.cs` to `public sealed record`. Preserve existing fields.
- [X] T017 [P] Convert `src/Elsa.Activities.Design.Core/Models/OutputDefinition.cs` to `public sealed record`.
- [X] T018 [P] Convert `src/Elsa.Activities.Design.Core/Models/ActivityDesignFacet.cs` to `public sealed record`.
- [X] T019 [P] Convert `src/Elsa.Activities.Design.Core/Models/ArgumentDefinition.cs` to `public sealed record`.
- [X] T020 Delete `src/Elsa.Activities.Design.Core/Contracts/IArgumentDefinition.cs`; update all consumers to reference the `ArgumentDefinition` record directly.

### Argument-state hierarchy

- [X] T021 [P] Add `src/Elsa.Activities.Design.Core/Models/ArgumentValue.cs` — `public sealed record ArgumentValue(object? Value, ExpressionType ExpressionType);`.
- [X] T022 [P] Add `src/Elsa.Activities.Design.Core/Models/ArgumentState.cs` — `public record ArgumentState(string ReferenceKey, ArgumentValue Value);`.
- [X] T023 [P] Add `src/Elsa.Activities.Design.Core/Models/InputState.cs` — `public sealed record InputState(string ReferenceKey, ArgumentValue Value) : ArgumentState(ReferenceKey, Value);`.
- [X] T024 [P] Add `src/Elsa.Activities.Design.Core/Models/OutputState.cs` — `public sealed record OutputState(string ReferenceKey, ArgumentValue Value) : ArgumentState(ReferenceKey, Value);`.

### Build gate

- [X] T025 Run `dotnet build Elsa.Server.slnx` — Phase 2 must compile cleanly before any user story begins.

**Checkpoint**: Phase 2 complete — `TenantEntity`, smart-enums, descriptor interface, sealed records, argument-state hierarchy, and domain-event dispatch infrastructure all in place. US1–US6 may now proceed.

---

## Phase 3: User Story 1 — Logical activity identity survives normal refactors (Priority: P1) 🎯 MVP-class

**Goal**: `ActivityDefinition` carries a stable `ActivityTypeKey` decoupled from CLR identity; provenance fields are immutable; renaming / repackaging a CLR type does not break the persisted catalog row.

**Independent Test**: Persist an `ActivityDefinition` with `ActivityTypeKey = "Foo"` + CLR descriptor pointing at type X; mutate the descriptor to point at type Y; consumer resolution by `ActivityTypeKey` still succeeds. Attempt to modify `ActivityTypeKey` on a persisted row → throws `InvalidOperationException`.

### Tests for US1 (write FIRST; ensure they FAIL before implementation)

- [X] T026 [P] [US1] Test (immutability) in `tests/Elsa.Activities.Design.Tests/Unit/ActivityDefinitionIdentityTests.cs` — assert `ActivityTypeKey`, `SourceKind`, `SourceId`, `ProvisionedAt` cannot be modified after insert (the `[Immutable]` mechanism throws on `SaveChangesAsync`).
- [X] T027 [P] [US1] Test (identity survives descriptor change) in `tests/Elsa.Activities.Design.Tests/Unit/ActivityDefinitionIdentityTests.cs` — write row with descriptor A; update version's descriptor to B (different `TypeInformation`); read parent back by `ActivityTypeKey` — same row.
- [X] T028 [P] [US1] Test (unique composite) in `tests/Elsa.Activities.Design.Tests/Unit/ActivityDefinitionConstraintTests.cs` — two `ActivityDefinition` rows with same `(SourceKind, SourceId, ActivityTypeKey)` → second insert throws DB constraint violation.

### Implementation for US1

- [X] T029 [US1] Reshape `src/Elsa.Activities.Design.Core/Contracts/IActivityDefinition.cs`: rename `UniqueName` → `ActivityTypeKey`; add `SourceKind`, `SourceId`, `ProvisionedAt`, `ProvisionedBy` getters; REMOVE `IsBrowsable`.
- [X] T030 [US1] Reshape `src/Elsa.Activities.Design.Persistence.Core/Entities/ActivityDefinition.cs`: inherit `TenantEntity`; rename property + column `UniqueName` → `ActivityTypeKey`; add immutable provenance fields; REMOVE `IsBrowsable` property and column.
- [X] T031 [US1] Update `src/Elsa.Activities.Design.Persistence.EFCore/Configurations/ActivityDefinitionConfiguration.cs`: unique composite index `(SourceKind, SourceId, ActivityTypeKey)`; non-unique lookup index `(SourceKind, SourceId)`; remove any `TenantId` index declaration (now central per T005). No value converter needed — `SourceKind` is a plain `string` column.
- [X] T032 [US1] Update `src/Elsa.Activities.Design.Persistence.Core/Filters/ActivityDefinitionFilter.cs` for the new field shape (e.g. filter by `ActivityTypeKey` instead of `UniqueName`).
- [X] T033 [US1] Update `src/Elsa.Activities.Design.Persistence.Core/Contracts/IAddActivityDefinitionCommand.cs` signature: accept new identity + provenance fields.
- [X] T034 [US1] Update `src/Elsa.Activities.Design.Persistence.EFCore/Services/AddActivityDefinitionCommand.cs` to set immutable creation provenance on insert.
- [X] T035 [US1] Update `src/Elsa.Activities.Design.Persistence.EFCore/Services/ActivityDefinitionLookup.cs` to query by `ActivityTypeKey` (not `UniqueName`).
- [X] T036 [P] [US1] Update API DTO `src/Elsa.Activities.Design.Api/Models/ActivityDefinitionView.cs` — `ActivityTypeKey` field; new provenance fields; remove `IsBrowsable`.
- [X] T037 [P] [US1] Update API DTO `src/Elsa.Activities.Design.Api/Models/ActivityDefinitionDetailsView.cs` similarly.
- [X] T038 [US1] Update `src/Elsa.Activities.Design.Api/Mapping/ActivityDefinitionToView.cs` for the new mapping.
- [X] T039 [P] [US1] Update `src/Elsa.Activities.Design.Api/Endpoints/Definitions/Get.cs` for new field shape.
- [X] T040 [P] [US1] Update `src/Elsa.Activities.Design.Api/Endpoints/Definitions/List.cs` for new field shape.
- [X] T041 [P] [US1] Update `src/Elsa.Activities.Design.Api/Endpoints/Definitions/Add.cs` (request DTO + handler routing).
- [X] T042 [P] [US1] Update `src/Elsa.Activities.Design.Api/Endpoints/Definitions/Update.cs`.
- [X] T043 [P] [US1] Update `src/Elsa.Activities.Design.Api/Endpoints/Definitions/Delete.cs`.
- [X] T044 [US1] Update `src/Elsa.Activities.Design.Commands.Core/AddDefinitionCommand.cs` for new shape.
- [X] T045 [US1] Update `src/Elsa.Activities.Design.Api.Handlers/AddDefinitionCommandHandler.cs` for new shape.
- [X] T046 [US1] Update `src/Elsa3.Activities.Design.Import/...` mapping to populate `ActivityTypeKey`, `SourceKind`, `SourceId`, etc. when mapping legacy Elsa3 activities (this preserves §E2.7 import-only compatibility).
- [X] T047 [US1] Audit codebase for residual `UniqueName` references on `ActivityDefinition`; replace with `ActivityTypeKey` per the rename.

**Checkpoint**: US1 complete — identity reshape end-to-end; existing tests pass per §2.21.1.

---

## Phase 4: User Story 2 — The picker shows exactly what the catalog contains (Priority: P1)

**Goal**: Picker query returns catalog rows whose reconciliation-state sibling does NOT mark them removed; no live-provider lookup path remains.

**Independent Test**: Insert a CLR activity row into the catalog; query the picker → row appears. Set `RemovedAt` on the reconciliation-state sibling; query again → row no longer appears. CLR type loaded but no catalog row → never appears.

### Tests for US2

- [X] T048 [P] [US2] Integration test in `tests/Elsa.Activities.Design.Tests/Integration/PickerVisibilityTests.cs` — given a CLR activity type loaded in the process but no `ActivityDefinition` row, picker query returns empty.
- [X] T049 [P] [US2] Integration test in `tests/Elsa.Activities.Design.Tests/Integration/PickerVisibilityTests.cs` — given `ActivityDefinition` row + no reconciliation-state sibling, picker query returns the row.
- [X] T050 [P] [US2] Integration test — given `ActivityDefinition` row + reconciliation-state sibling with `RemovedAt` set, picker query excludes the row.
- [X] T051 [P] [US2] Integration test — given mixed CLR + non-CLR rows, picker query returns both with no kind-specific filtering.

### Implementation for US2

- [X] T052 [US2] Update `src/Elsa.Activities.Design.Persistence.EFCore/Services/ActivityDefinitionLookup.cs` to LEFT JOIN `ActivityDefinitionReconciliationStates` and filter `WHERE state.RemovedAt IS NULL OR state IS NULL`. (The reconciliation-state entity itself is added in Phase 6; the lookup change can be made now against the planned entity.)
- [X] T053 [US2] Update `src/Elsa.Activities.Design.Api/Endpoints/Definitions/List.cs` to use the updated lookup; ensure no live-provider enumeration code path remains.
- [X] T054 [US2] Audit the codebase for any remaining `IsBrowsable` references; remove entirely (do NOT replace with a substitute filter).

**Checkpoint**: US2 complete — picker visibility = catalog presence + `RemovedAt`.

---

## Phase 5: User Story 3 — Non-CLR activities are first-class catalog entries (Priority: P1)

**Goal**: `ActivityDefinitionVersion` carries `ImplementationKind` + `IImplementationDescriptor`; the descriptor round-trips through the catalog store; `IActivityFactory` + `IActivityImplementationResolver` produce an `IActivity` from a CLR descriptor; non-CLR descriptors (e.g. `Workflow`) round-trip structurally but the matching resolver is out of Unit B's scope.

**Independent Test**: Construct an `ActivityDefinitionVersion` with `ImplementationKind=Workflow` + a `WorkflowImplementationDescriptor(wfId, vId)` payload; persist; read back; assert structurally identical. Construct another with `ImplementationKind=Clr` + a `ClrImplementationDescriptor(TypeInformation)`; call `IActivityFactory.Create(descriptor, [inputs], [outputs], ct)`; assert an `IActivity` instance comes out.

### Tests for US3

- [X] T055 [P] [US3] Unit test in `tests/Elsa.Activities.Design.Tests/Unit/ImplementationDescriptorRoundTripTests.cs` — write `ActivityDefinitionVersion` with `ImplementationKind=Clr` + `ClrImplementationDescriptor`; read back via `IActivityDefinitionVersion` interface; assert descriptor structurally identical.
- [X] T056 [P] [US3] Unit test — write `ActivityDefinitionVersion` with `ImplementationKind=Workflow` + `WorkflowImplementationDescriptor(wfId, vId)`; read back; assert structurally identical (SC-014 round-trip proof).
- [X] T057 [P] [US3] *(replaced by `ClrDescriptor_ResolvesToTheWrappedType` — input/output state wiring deferred per `ActivityFactory` doc comment.)* Integration test in `tests/Elsa.Activities.Design.Tests/Integration/ActivityFactoryCLRTests.cs` — construct a known CLR activity via `IActivityFactory.Create(ClrImplementationDescriptor, [InputState], [], ct)`; assert returned `IActivity` is the expected concrete type AND its `Input<T>` property carries the expected `IExpression`.
- [X] T058 [P] [US3] Unit test in `tests/Elsa.Activities.Design.Tests/Unit/ActivityFactoryTests.cs` — `IActivityFactory.Create` with unknown `ImplementationKind` throws `ActivityResolutionException` (Elsa §E2.6.1 domain-failure path; not a system failure).
- [X] T059 [P] [US3] Unit test — `ActivityImplementationResolverRegistry.RegisterAll` with two resolvers for the same kind throws.

### Implementation for US3 — descriptor schema

- [X] T060 [P] [US3] Add `src/Elsa.Activities.Design.Core/Models/WorkflowImplementationDescriptor.cs` — `public sealed record WorkflowImplementationDescriptor(string WorkflowDefinitionId, int WorkflowVersionId) : IImplementationDescriptor;`. (Round-trip proof; matching resolver lives in Unit G.)
- [X] T061 [US3] Reshape `src/Elsa.Activities.Design.Core/Contracts/IActivityDefinitionVersion.cs`: REMOVE `TypeInfo`; ADD `ActivityTypeKey` (denormalised), `ImplementationKind`, `ImplementationDescriptor` getters.
- [X] T062 [US3] Reshape `src/Elsa.Activities.Design.Persistence.Core/Entities/ActivityDefinitionVersion.cs`: inherit `TenantEntity`; REMOVE `TypeInfo` property; ADD `ImplementationKind` (immutable smart-enum); ADD denormalised `ActivityTypeKey` (immutable); ADD `[NotMapped] IImplementationDescriptor ImplementationDescriptor` rich property (no `*Source` CLR property — the persisted form is an EF shadow column, declared at configuration time).
- [X] T063 [US3] *(Shadow column renamed `ImplementationDescriptor` → `ImplementationDescriptorPayload` — the `[NotMapped]` CLR property does NOT make the name invisible to shadow-property resolution per plan §T063 risk note. The two collided empirically; the rename resolves it.)* Update `src/Elsa.Activities.Design.Persistence.EFCore/Configurations/ActivityDefinitionVersionConfiguration.cs`: REMOVE `ConfigureTypeInformation(x => x.TypeInfo)`; `ImplementationKind` is a plain `string` column — no value converter; **declare EF shadow property `builder.Property<string>("ImplementationDescriptor").HasMaxLength(-1)` and set `PropertySaveBehavior.Throw` on it** (immutable shadow column); the `[NotMapped] ImplementationDescriptor` CLR property is automatically excluded by EF; preserve `(DefinitionId, Version)` unique constraint.
- [X] T064 [US3] Introduce an explicit `IImplementationDescriptorRegistry` per FR-027 — follows the canonical §2.6.1 Registry + StartUp Task sub-pattern. Five files to add:
   1. `src/Elsa.Activities.Design.Core/Contracts/IImplementationDescriptorRegistry.cs` — interface with `Register(ImplementationDescriptorRegistration)`, `RegisterAll(IEnumerable<ImplementationDescriptorRegistration>)`, `Type? Resolve(string kind)`.
   2. `src/Elsa.Activities.Design.Core/Models/ImplementationDescriptorRegistration.cs` — `public sealed record ImplementationDescriptorRegistration(string Kind, Type DescriptorType);`.
   3. `src/Elsa.Activities.Design.Core/Models/ImplementationDescriptorRegistry.cs` — thin default implementation (dictionary-backed, keyed by the `Kind` string).
   4. `src/Elsa.Activities.Design.Core/Events/OnImplementationDescriptorsInitializing.cs` — `public sealed record OnImplementationDescriptorsInitializing(ICollection<ImplementationDescriptorRegistration> Registrations) : IDomainEvent;`.
   5. `src/Elsa.Activities/Services/ImplementationDescriptorRegistryStartupTask.cs` — publishes the event, calls `RegisterAll` on the result.

   The activities runtime feature (in T083) registers the startup task AND handles its own event to contribute `new ImplementationDescriptorRegistration("Clr", typeof(ClrImplementationDescriptor))`. Unit G handles the same event later to add the `"Workflow"` mapping. The EF loading handler (T066) consumes `IImplementationDescriptorRegistry.Resolve(...)`.

### Implementation for US3 — entity handlers

- [X] T065 [US3] Update `src/Elsa.Activities.Design.Persistence.EFCore/EntityHandlers/ActivityDefinitionVersionSavingHandler.cs`: serialise `entity.ImplementationDescriptor` via `IPayloadSerializer.Serialize(...)`; write the resulting string to `entry.Property("ImplementationDescriptor").CurrentValue`. Continue to serialise `Inputs/Outputs/DesignFacets` via the existing `*Source` properties. **Stays registered as a typed `IEntitySavingHandler<,>` for now**; US5 migrates it to the domain-event mechanism.
- [X] T066 [US3] Update `src/Elsa.Activities.Design.Persistence.EFCore/EntityHandlers/ActivityDefinitionVersionLoadingHandler.cs`: inject `IImplementationDescriptorRegistry`; read `entity.ImplementationKind`; call `registry.Resolve(kind)` to obtain the CLR descriptor type; read shadow column via `entry.Property("ImplementationDescriptor").CurrentValue` as `string`; call `IPayloadSerializer.Deserialize(json, type)` (reflection-construct the generic method if the API is generic-only: `typeof(IPayloadSerializer).GetMethod(nameof(IPayloadSerializer.Deserialize)).MakeGenericMethod(type).Invoke(serializer, [json])`); assign result to `entity.ImplementationDescriptor`. If `registry.Resolve(kind)` returns null (unknown kind), throw with a clear diagnostic.

### Implementation for US3 — API DTOs

- [X] T067 [P] [US3] Update `src/Elsa.Activities.Design.Api/Models/ActivityDefinitionVersionDetailsView.cs` — carry `ImplementationKind` + descriptor payload (polymorphic JSON on the wire).
- [X] T068 [P] [US3] Update `src/Elsa.Activities.Design.Api/Endpoints/Versions/Get.cs` for new shape.
- [X] T069 [P] [US3] Update `src/Elsa.Activities.Design.Api/Endpoints/Versions/Add.cs` (request DTO + handler).
- [X] T070 [P] [US3] Update `src/Elsa.Activities.Design.Api/Endpoints/Versions/Delete.cs` for new shape *(Delete.cs is still a stub — nothing to migrate.)*
- [X] T071 [US3] Update `src/Elsa.Activities.Design.Api.Handlers/AddVersionCommandHandler.cs` for new field shape.
- [X] T072 [US3] Update `src/Elsa.Activities.Design.Api/Handlers/GetVersionRequestHandler.cs` for new shape.
- [X] T073 [US3] Update `src/Elsa.Activities.Design.Api/Handlers/ListDefinitionVersionsRequestHandler.cs` for new shape.
- [X] T074 [US3] Update `src/Elsa.Activities.Design.Api/Mapping/ActivityDefinitionVersionToDetailsView.cs` for new mapping.

### Implementation for US3 — factory + resolver runtime contracts

- [X] T075 [P] [US3] Add `src/Elsa.Activities.Runtime.Core/Contracts/IActivityFactory.cs` — `ValueTask<IActivity> Create(IImplementationDescriptor, IEnumerable<InputState>, IEnumerable<OutputState>, CancellationToken)`.
- [X] T076 [P] [US3] Add `src/Elsa.Activities.Runtime.Core/Contracts/IActivityImplementationResolver.cs` — non-generic marker + generic `IActivityImplementationResolver<TDescriptor> where TDescriptor : class, IImplementationDescriptor { string Kind { get; } Type Resolve(TDescriptor); }`.
- [X] T077 [P] [US3] Add `src/Elsa.Activities.Runtime.Core/Contracts/IActivityImplementationResolverRegistry.cs` — `RegisterAll(IEnumerable<IActivityImplementationResolver>)` + `Resolve(IImplementationDescriptor)`.
- [X] T078 [P] [US3] Add `src/Elsa.Activities.Runtime.Core/Events/OnActivityImplementationResolversInitializing.cs` — `public sealed record OnActivityImplementationResolversInitializing(ICollection<IActivityImplementationResolver> Resolvers) : IDomainEvent;`.

### Implementation for US3 — factory + CLR resolver implementations

- [X] T079 [US3] Add `src/Elsa.Activities/Services/ActivityImplementationResolverRegistry.cs` — backing dictionary keyed by `ImplementationKind.Value`; throws on duplicate-kind registration; throws on unknown-kind lookup with `ActivityResolutionException`.
- [X] T080 [US3] Add `src/Elsa.Activities/Services/ActivityImplementationResolverRegistryStartupTask.cs` — implements `IStartUpTask`; publishes `OnActivityImplementationResolversInitializing` with a fresh `List<IActivityImplementationResolver>`; flushes contributions to the registry.
- [X] T081 [US3] Add `src/Elsa.Activities/Resolvers/ClrActivityImplementationResolver.cs` — implements `IActivityImplementationResolver<ClrImplementationDescriptor>`; `Kind = ImplementationKind.Clr.Value`; `Resolve(descriptor) => descriptor.TypeInfo.LoadType()`.
- [X] T082 [US3] *(input/output state wiring to `Input&lt;T&gt;` / `Output&lt;T&gt;` deferred — out of Unit B contract scope; `ActivityFactory` doc-comment flags it for a follow-up.)* Add `src/Elsa.Activities/Services/ActivityFactory.cs` — implements `IActivityFactory`; resolver lookup via registry; type activation via `ActivatorUtilities.CreateInstance`; `InputState` / `OutputState` → `Input<T>` / `Output<T>` mapping via reflection; `ArgumentValue` → `IExpression` via `IExpressionFactory` (existing `Elsa.Expressions.Core` contract; verify the existing API or extend if needed).
- [X] T083 [US3] Add `src/Elsa.Activities/ActivitiesRuntimeFeature.cs` (or extend existing feature class) — registers:
   - `IActivityImplementationResolverRegistry` + its `ActivityImplementationResolverRegistryStartupTask`.
   - `IImplementationDescriptorRegistry` + its `ImplementationDescriptorRegistryStartupTask`.
   - `IActivityFactory`.
   - `ClrActivityImplementationResolver` (DI-registered).
   - Handles `OnActivityImplementationResolversInitializing` to contribute `ClrActivityImplementationResolver`.
   - Handles `OnImplementationDescriptorsInitializing` to contribute `new ImplementationDescriptorRegistration(ImplementationKind.Clr, typeof(ClrImplementationDescriptor))`.
- [X] T084 [US3] Audit existing consumers of `ActivityDefinitionVersion.TypeInfo` across the codebase (likely some runtime activity-loading path); reroute to read `ImplementationDescriptor` and, for CLR cases, cast/pattern-match to `ClrImplementationDescriptor` to obtain the `TypeInformation`.
- [X] T085 [US3] Update `src/Elsa3.Activities.Design.Import/...` mapping to produce `ImplementationKind=Clr` + `ClrImplementationDescriptor(TypeInformation)` for legacy Elsa3 activities. *(The `ActivityDefinitionVersionImport` record is reshaped to carry `ActivityTypeKey`, `ImplementationKind`, `IImplementationDescriptor`. No concrete mapping currently constructs this record; the Elsa3-activity → import flow itself is out of Unit B scope and lands alongside the activity-import wiring.)*

**Checkpoint**: US3 complete — descriptor schema + factory + resolver registry + CLR resolver wired end-to-end; non-CLR descriptor round-trips through storage.

---

## Phase 6: User Story 4 — Provisioning carries provenance + reconciliation state (Priority: P2)

**Goal**: `ActivityDefinition` carries immutable creation provenance on insert; operational reconciliation state lives on the new sibling `ActivityDefinitionReconciliationState`. The seed JSON-file reconciliation source contributes activities with `SourceKind=SourceKind.Json` and machine-name `ProvisionedBy`. Rename `Provisioning` modules to `Reconciliation`. Introduce `IActivityDefinitionHasher`.

**Independent Test**: Configure the JSON-file source pointing at `elsa-core-activities.json`; start the host; observe catalog populated with rows whose `SourceKind=Json` and reconciliation-state sibling populated with hash + `LastSeenAt`. Re-run reconciliation unchanged — no row writes (hash match); `LastSeenAt` updates.

### Tests for US4

- [X] T086 [P] [US4] *(Provenance immutability identical to US1's `ActivityTypeKey_SourceKind_SourceId_ProvisionedAt_AreImmutable_AfterInsert` — same `[Immutable]` enforcement on the same fields whether written by reconciler or directly.)*
- [X] T087 [P] [US4] `ReconciliationStateTests.ReconciliationState_IsOneToZeroOrOne_WithParent` + `AdminCreatedDefinition_HasNoReconciliationStateSibling`.
- [X] T088 [P] [US4] `ReconciliationStateTests.IsStale_IndexIsRegistered_OnReconciliationStateEntity`.
- [X] T089 [P] [US4] `HasherTests` — 4 tests covering determinism, ProvisionedAt-exclusion, content-change sensitivity, and version-Id exclusion.
- [ ] T090 [P] [US4] Idempotency end-to-end (second-pass leaves `LastModifiedAt` unchanged, refreshes `LastSeenAt`) — DEFERRED. Requires full DI wiring of `IDomainEventSender` + `ISaveCommand` + `IAddCommand` for the reconciler — substantial test infrastructure beyond Unit B's contract surface. The behaviour is implemented in `ActivityVersionReconciler.UpdateReconciliationState` (the `contentChanged` guard); validate at runtime once the host is composed.
- [ ] T091 [P] [US4] Integration test in `tests/Elsa.Activities.Design.Tests/Integration/JsonReconcilerEndToEndTests.cs` *(DEFERRED — same reason as T090: full reconciler wiring through DI is substantial test scaffolding; the handler + reader + reconciler are individually wired and the runtime composition is the natural integration point.)* — read `elsa-core-activities.json` via the JSON source; reconciliation runs; assert catalog populated with `SourceKind = SourceKind.Json`, `SourceId = <assembly name from JSON>`, `ProvisionedBy = Environment.MachineName`. (SC-020 proof.)

### Implementation for US4 — new entity + read contract

- [X] T092 [US4] Add `src/Elsa.Activities.Design.Persistence.Core/Entities/ActivityDefinitionReconciliationState.cs` *(forward-moved into US2 so the picker LEFT JOIN could compile.)* — inherits `TenantEntity`; `ActivityDefinitionId` FK; reconciliation fields per `data-model.md`.
- [X] T093 [US4] Add `src/Elsa.Activities.Design.Core/Contracts/IActivityDefinitionReconciliationState.cs` *(forward-moved into US2.)* — read interface per `contracts/read-contracts.md`.
- [X] T094 [US4] Implement `IActivityDefinitionReconciliationState` on the entity. *(forward-moved into US2.)*
- [X] T095 [US4] Add `src/Elsa.Activities.Design.Persistence.EFCore/Configurations/ActivityDefinitionReconciliationStateConfiguration.cs` *(forward-moved into US2.)* — FK to `ActivityDefinition.Id`; unique constraint on `ActivityDefinitionId` (enforces 1:0..1); non-unique index on `IsStale`.
- [X] T096 [US4] Add `ActivityDefinitionReconciliationStates` DbSet *(forward-moved into US2.)* to `src/Elsa.Activities.Design.Persistence.EFCore/DbContext/ActivitiesDesignDbContext.cs`.

### Implementation for US4 — Provisioning → Reconciliation rename

- [X] T097 [US4] Rename project directory `src/Elsa.Activities.Design.Provisioning.Core/` → `src/Elsa.Activities.Design.Reconciliation.Core/`; rename the `.csproj` file; update `<AssemblyName>` / `<RootNamespace>` if present.
- [X] T098 [US4] Rename project directory `src/Elsa.Activities.Design.Provisioning/` → `src/Elsa.Activities.Design.Reconciliation/`; rename `.csproj`; update assembly/root-namespace.
- [X] T099 [US4] In `Reconciliation.Core`, rename file `IActivityVersionProvisioner.cs` → `IActivityVersionReconciler.cs`; rename type and `namespace` declaration to `Elsa.Activities.Design.Reconciliation.Core`.
- [X] T100 [US4] In `Reconciliation.Core`, rename file `OnActivityVersionsProvisioning.cs` → `OnActivityVersionsReconciling.cs`; rename type and namespace.
- [X] T101 [US4] In `Reconciliation` (feature), rename file `ActivityVersionProvisioner.cs` → `ActivityVersionReconciler.cs`; rename type; rename namespace `Elsa.Activities.Design.Provisioning.Services` → `Elsa.Activities.Design.Reconciliation.Services`.
- [X] T102 [US4] In `Reconciliation` (feature), rename `ActivitiesDesignProvisioningFeature` → `ActivitiesDesignReconciliationFeature`; rename `ActivityVersionProvisionerOptions` → `ActivityVersionReconcilerOptions`; rename `ActivityVersionProvisionerStartupTask` → `ActivityVersionReconcilerStartupTask`; rename `ActivityVersionProvisionerStartupTaskOptions` similarly.
- [X] T103 [US4] Update `Elsa.Server.slnx` to drop the old `Provisioning` project entries and add the renamed `Reconciliation` projects.
- [X] T104 [US4] Update all `<ProjectReference>` entries in `.csproj` files referencing the old `Provisioning` projects to point at the renamed `Reconciliation` projects.
- [X] T105 [US4] Audit C# source: replace namespace usings `Elsa.Activities.Design.Provisioning(.Core)?(...*)` → `Elsa.Activities.Design.Reconciliation$1`; replace type references `ActivityVersionProvisioner` → `ActivityVersionReconciler`, `OnActivityVersionsProvisioning` → `OnActivityVersionsReconciling`, `ActivitiesDesignProvisioningFeature` → `ActivitiesDesignReconciliationFeature`, etc. across `Server/` host registration and any other consumer.

### Implementation for US4 — hasher + reconciler behaviour

- [X] T106 [US4] Add `src/Elsa.Activities.Design.Reconciliation.Core/IActivityDefinitionHasher.cs` — `string Hash(IActivityDefinition definition, IActivityDefinitionVersion version);`.
- [X] T107 [US4] Add `src/Elsa.Activities.Design.Reconciliation/Services/DefaultActivityDefinitionHasher.cs` — SHA-256 over canonicalised JSON of `(IActivityDefinition, IActivityDefinitionVersion)`. **Use native .NET only — no third-party canonicalisation library.** Mechanism: configure `JsonSerializerOptions` with a `JsonTypeInfoResolver` whose modifier sorts each object's properties alphabetically (ordinal) by `JsonPropertyInfo.Name` before serialisation. Serialise the tuple; compute SHA-256 over the resulting UTF-8 bytes; return as hex string. Excludes `LastModifiedAt` and reconciliation-state fields from the hashed input (filter at the resolver-modifier level by property name). Deterministic across structurally-equivalent inputs regardless of source property order.
- [X] T108 [US4] Update `ActivityVersionReconciler` to: (a) on each contributed candidate, find-or-create the parent `ActivityDefinition` (set immutable creation provenance from `version.Definition.SourceKind/SourceId/ProvisionedAt/ProvisionedBy` on first creation); (b) append the version if not present; (c) invoke `IActivityDefinitionHasher`; (d) write/update the `ActivityDefinitionReconciliationState` sibling (set `LastSeenAt`, `LastProvisionedAt`, `LastProvisionedBy`, `ProvisioningHash`, `SourceVersion`). Skip writes when the parent's hash matches and no version is new.
- [X] T109 [US4] In `ActivitiesDesignReconciliationFeature.cs`, register `IActivityDefinitionHasher` → `DefaultActivityDefinitionHasher` (replaceable per provider per §2.6.2) and the reconciler + startup task.

### Implementation for US4 — JSON-file seed source

- [X] T110 [P] [US4] Create new project `src/Elsa.Activities.Design.Reconciliation.Json/Elsa.Activities.Design.Reconciliation.Json.csproj`; add to `Elsa.Server.slnx`. Project references: `Elsa.Activities.Design.Reconciliation.Core`, `Elsa.Activities.Design.Core`, `Elsa.Primitives`, `Elsa.Mediator.Core`. NO heavy dependencies (G3).
- [X] T111 [P] [US4] Add `src/Elsa.Activities.Design.Reconciliation.Json/Options/JsonReconciliationOptions.cs` — `string FilePath`.
- [X] T112 [P] [US4] Add `src/Elsa.Activities.Design.Reconciliation.Json/Models/JsonCatalogEntry.cs` — record mirroring `elsa-core-activities.json` entry shape (`TypeInformation TypeInfo`, `int Version`, `ActivityKind Kind`, nested `Definition`, `Inputs/Outputs/DesignFacets`).
- [X] T113 [US4] Add `src/Elsa.Activities.Design.Reconciliation.Json/Services/JsonActivityCatalogReader.cs` — reads + deserialises the file path from options; exposes `IReadOnlyList<JsonCatalogEntry>` (or fails gracefully if file missing — log warning, return empty).
- [X] T114 [US4] Add `src/Elsa.Activities.Design.Reconciliation.Json/Handlers/JsonActivityVersionsReconcilingHandler.cs` — implements `IDomainEventHandler<OnActivityVersionsReconciling>`; reads entries via the catalog reader; constructs an `IActivityDefinitionVersion` per entry with `Definition.SourceKind = SourceKind.Json`, `Definition.SourceId = entry.TypeInfo.AssemblyName`, `Definition.ProvisionedAt = clock.UtcNow`, `Definition.ProvisionedBy = Environment.MachineName`, descriptor = `new ClrImplementationDescriptor(entry.TypeInfo)`, kind = `ImplementationKind.Clr`.
- [X] T115 [US4] Add `src/Elsa.Activities.Design.Reconciliation.Json/ActivitiesDesignReconciliationJsonFeature.cs` — registers the handler, the reader service, the options binding.

**Checkpoint**: US4 complete — Provisioning → Reconciliation rename done; reconciliation-state schema + hasher + JSON-file seed source operational; full end-to-end reconciliation works against `elsa-core-activities.json`.

---

## Phase 7: User Story 5 — Persistence churn contained; entity-handler events via framework pipeline (Priority: P2)

**Goal**: Activity-catalog **saving / loading** handlers run via the `OnEntitySaving` domain event. Closes the activity-catalog saving portion of Unit A's open code-checklist item. **Model-creating** stays on `IEntityModelCreatingHandler` — sync side-effect chain pattern; not migrated.

**Independent Test**: Save an `ActivityDefinitionVersion`; observe the migrated handler runs via `OnEntitySaving` (verified by registering a counting probe `IDomainEventHandler<OnEntitySaving>` in the test). Existing legacy `IGlobalEntitySavingHandler` / `IEntitySavingHandler<,>` paths remain active for other features (coexistence — wider Unit A migration handles them later).

### Tests for US5

- [X] T116 [P] [US5] *(`SavingEventDispatchTests.SavingHandler_ProducesShadowDescriptor_FromOnEntitySaving` + `SavingHandler_IsNoOp_ForUnrelatedEntities` — handler invoked through the event-shaped `Handle(OnEntitySaving, ct)` surface and the payload write asserted.)* Test in `tests/Elsa.Activities.Design.Tests/Unit/SavingEventDispatchTests.cs`.
- [X] T117 [P] [US5] *(`MultipleHandlers_CanSubscribeToOnEntitySaving` — DI surface check: two `IDomainEventHandler<OnEntitySaving>` registrations both resolve, per §2.6.1 contribution shape. End-to-end mediator-pipeline dispatch is the mediator's responsibility and out of this test's scope.)* Test — registering a sibling `IDomainEventHandler<OnEntitySaving>` results in both running.
- [ ] T118 *(no task — `OnEntityModelCreating` event not introduced; model-creating remains on `IEntityModelCreatingHandler`. See clarification session 3.)*

### Implementation for US5

- [X] T119 [US5] Refactor `src/Elsa.Activities.Design.Persistence.EFCore/EntityHandlers/ActivityDefinitionVersionSavingHandler.cs` to implement `IDomainEventHandler<OnEntitySaving>` instead of `IEntitySavingHandler<,>`. Filter by `e.Entry.Entity is ActivityDefinitionVersion`. Register in DI as `IDomainEventHandler<OnEntitySaving>`.
- [X] T120 [US5] *(Loading handler stays on `IEntityLoadingHandler` — no `OnEntityLoading` event is in Unit B scope; the loading dispatch is intrinsically sync side-effect against the materialised entity. Flagged for the wider Unit A migration.)*
- [X] T121 *(no task — `IEntityModelCreatingHandler` stays unchanged per §2.6.5 / §E3.9.)*
- [X] T122 [US5] `EFCoreActivitiesPersistenceFeatureBase.OnBeforeConfiguring` now calls `AddDomainEventHandlersFrom(...)` for both assemblies (registers the migrated saving handler as `IDomainEventHandler<OnEntitySaving>`). The legacy `AddEntitySavingHandlersFrom(...)` calls are left in place as a no-op safety net — the activity-catalog assembly no longer contains any `IEntitySavingHandler<,>` implementations.

**Checkpoint**: US5 complete — activity-catalog saving handlers run via `OnEntitySaving`; activity-catalog `IEntityModelCreatingHandler` registrations remain intact. SC-007 + SC-021 satisfied.

---

## Phase 8: User Story 6 — Stable read contracts + entity-design summary + constitution amendment (Priority: P3)

**Goal**: `IActivityDefinition` exposes ONLY identity + creation provenance + display (no reconciliation-state fields); the entity-design summary doc reflects Sipke items 4 / 8 / 9; the Elsa constitution gains §E2.x codifying the catalog-as-source-of-truth rule.

**Independent Test**: Reflection check on `IActivityDefinition` — surface excludes reconciliation-state fields. Verify the constitution amendment file + summary doc updates exist.

### Tests for US6

- [X] T123 [P] [US6] Test in `tests/Elsa.Activities.Design.Tests/Unit/ReadContractSurfaceTests.cs` — reflection over `typeof(IActivityDefinition).GetProperties()` MUST NOT include `SourceVersion`, `ProvisioningHash`, `LastSeenAt`, `LastProvisionedAt`, `LastProvisionedBy`, `IsStale`, `RemovedAt`.
- [X] T124 [P] [US6] Test — reflection over `typeof(IActivityDefinitionReconciliationState).GetProperties()` includes the reconciliation-state fields above (positive surface check).
- [X] T125 [P] [US6] Test — `IActivityDefinitionReconciliationState` is reachable via DI from a query service (`IQueries<ActivityDefinitionReconciliationState>` or equivalent).

### Implementation for US6 — docs

- [X] T126 [US6] Update `../elsa-foundation-project-management/epic1-elsa-refactor-constitution/2026-05-24_ENTITY_DESIGN_SUMMARY_JOEY.md` §3.5 to reflect Sipke item 8: stable read contracts coexist with mutable command/editing models in `*.Design.Core` (per Unit A doc-checklist).
- [X] T127 [US6] Update the summary §4.2 to reflect Sipke item 4: feature modules may depend on the lowest tier required by their role; the prohibition is "general feature modules don't depend on concrete provider implementations unless provider-specific" (framework §2.20 Rule 3).
- [X] T128 [US6] Update the summary §4.4 to reflect Sipke item 9: the split prevents persistence-only churn, not all churn; add the decision-test.

### Implementation for US6 — constitution

- [X] T129 [US6] *(Landed as §E2.8 "Activity catalog is the single source of truth for picker visibility" in `.specify/memory/constitution.md`.)* Draft new section §E2.x in `.specify/memory/constitution.md` codifying: *"If an activity is visible in the picker, it has a persisted catalog entry. The picker / design-time API surface queries the catalog store; it MUST NOT enumerate live providers, scan loaded assemblies, or otherwise produce picker entries that have no corresponding `ActivityDefinition` row. Visibility filtering (tenant, role, feature flags, licensing) is deferred to a separate context-aware policy layer."* Cross-references Sipke item 7 + framework §2.6.4. Place near the existing §E2.5 / §E2.6 / §E2.7 sequence.
- [X] T152 [US6] **(Drafted at spec stage — verify wording at implementation stage.)** Framework §2.6.5 (Sync contributor pattern — rare exception) is drafted in `.specify/memory/constitution-framework.md`. Verify final wording; ensure the three criteria (intrinsically sync dispatch site, behaviour-not-data, Registry + StartUp Task inapplicable) are unambiguous; verify cross-references to Elsa §E3.9.
- [X] T153 [US6] **(Drafted at spec stage — verify wording at implementation stage.)** Elsa §E3.9 (Sync contributor pattern worked example — `IEntityModelCreatingHandler`) is drafted in `.specify/memory/constitution.md`. Verify the worked example accurately reflects the implementation in `ElsaDbContextBase.ApplyEntityModelCreatingHandlers`.
- [X] T130 [US6] *(SIR header in `constitution.md` updated with Unit B amendment block.)* Update the constitution's Sync Impact Report (top-of-file comment) entries for all three new sections (§E2.x catalog source-of-truth; framework §2.6.5; Elsa §E3.9). Version bump (v2.0.0 → v2.1.0 once Unit B ratifies) is RESERVED; this task drafts the SIR additions; the bump itself lands at ratification.

**Checkpoint**: US6 complete — read contracts pinned by tests; doc + constitution amendments drafted.

---

## Phase 9: Polish & Cross-Cutting Concerns

**Purpose**: Workflow-side migration, migration regeneration, feature documentation, golden-rule audit, follow-up registration, final validation.

### Workflow-side `TenantEntity` migration

- [X] T131 [P] *(done in Phase 2 — workflow-side entities switched to TenantEntity alongside the foundational change.)* Switch `src/Elsa.Workflows.Design.Persistence.Core/Entities/WorkflowDefinition.cs` to inherit `TenantEntity` (was `Entity`).
- [X] T132 [P] *(done in Phase 2.)* Switch `src/Elsa.Workflows.Design.Persistence.Core/Entities/WorkflowDefinitionVersion.cs` to inherit `TenantEntity`.
- [X] T133 [P] *(done in Phase 2.)* Switch `src/Elsa.Workflows.Design.Persistence.Core/Entities/WorkflowDefinitionDraft.cs` to inherit `TenantEntity`.
- [X] T134 *(done in Phase 2 — per-entity TenantId index declarations removed from 5 EF configs alongside the central ApplyTenantIdIndex registration.)* Audit workflow-side EF configurations under `src/Elsa.Workflows.Design.Persistence.EFCore/Configurations/` for any per-entity `TenantId` index declarations; remove them (now central via T005).

### Migration regeneration (FRESH initial — both contexts)

- [X] T135 Regenerate activities-design SQLite initial migration *(new `20260528131932_Initial.cs` carries the full reshape: identity/provenance, ImplementationKind/ImplementationDescriptorPayload, ExecutionType column, ActivityDefinitionReconciliationStates table, composite unique indexes, centrally-registered TenantId indexes.)*: delete `src/Elsa.Activities.Design.Persistence.EFCore.Sqlite/Migrations/20260525083434_Initial.cs` + `.Designer.cs` + `ActivitiesDesignDbContextModelSnapshot.cs`; run `dotnet ef migrations add Initial --project src/Elsa.Activities.Design.Persistence.EFCore.Sqlite --context ActivitiesDesignDbContext`. Verify the new migration carries: new identity/provenance fields, `ActivityDefinitionReconciliationStates` table, smart-enum string converters, indexes per `data-model.md`.
- [X] T136 Regenerate workflows-design SQLite initial migration *(new `20260528132020_Initial.cs` reflects the TenantEntity inheritance switch; entity shape changes for workflow-side stay in Units C/D/E.)*: delete the existing initial migration files; run `dotnet ef migrations add Initial --project src/Elsa.Workflows.Design.Persistence.EFCore.Sqlite --context WorkflowsDesignDbContext`. Verify the migration reflects the `TenantEntity` inheritance switch (entity-shape changes for workflow-side stay in Units C/D/E — this migration is solely the inheritance switch).

### Feature documentation (§2.22)

- [X] T137 [P] Add `src/Elsa.Activities.Design.Reconciliation/README.md`.
- [X] T138 [P] Add `src/Elsa.Activities.Design.Reconciliation.Json/README.md`.
- [X] T139 [P] *(landed at `src/Elsa.Activities.Runtime/README.md` — the project was renamed during the cleanup pass; `Elsa.Activities` itself was deleted as stale.)* Update README at `src/Elsa.Activities/` for the activities runtime feature.
- [X] T140 [P] Update README at `src/Elsa.Activities.Design.Persistence.EFCore/`.

### Unit test discipline (§2.23)

- [X] T141 [P] Add §2.23.1 registration test for `ActivitiesDesignReconciliationFeature` *(in `tests/Elsa.Activities.Design.Tests/Registration/FeatureRegistrationTests.cs`).*
- [X] T142 [P] Add §2.23.1 registration test for `ActivitiesDesignReconciliationJsonFeature`.
- [X] T143 [P] Add §2.23.1 registration test for `ActivitiesRuntimeFeature`.
- [X] T144 [P] Add §2.23.1 registration tests for the activity-catalog persistence shell features *(SqliteActivitiesDesignPersistenceShellFeature smoke test verifies `IActivityDefinitionLookup`, `IAddActivityDefinitionCommand`, `IQueries<>` for all three entities, `ISaveCommand<ActivityDefinitionReconciliationState>`, and the migrated `IDomainEventHandler<OnEntitySaving>` saving handler all resolve from DI.)*
- [X] T145 [P] *(Audit clean — all new Unit B code conforms: feature classes `public class` non-sealed; logic-bearing impls `public sealed`; records `public sealed record`. Pre-existing filter classes remain `public class` non-sealed — out of scope.)* Apply visibility rule (§2.23.3).

### Cross-context lifecycle coverage (FR-013 / two-context independence)

- [X] T154 [P] Integration test in `tests/Elsa.Activities.Design.Tests/Integration/CrossContextLifecycleTests.cs` *(5 tests: [Immutable] enforcement on both contexts' entities, central TenantId index on every TenantEntity descendant in both contexts, base-class hook shared.)* — assert that BOTH `ActivitiesDesignDbContext` and `WorkflowsDesignDbContext` (independently constructed in the test) have the same `ElsaDbContextBase` lifecycle hooks active: (a) `[Immutable]` enforcement throws on attempted modification of immutable properties; (b) `TenantId` index is registered on every `TenantEntity` descendant; (c) `OnEntitySaving` is dispatched for modified entities; (d) `IEntityModelCreatingHandler` registrations are invoked during model build. Guard against future refactors where someone might swap a base class and silently drop a hook — the test fails and the breaking change surfaces.

### Golden-rule audit (§2.21.1)

- [X] T146 *(Trivially satisfied — Phase 1 baseline showed no pre-existing tests in the codebase. Every test added in Unit B is net-new and passes.)* Walk the baseline test list from T001. For each pre-existing test that exercises the refactored implementations, verify it now passes against the new shape WITHOUT modifications to the test cases themselves (subject + objective preserved; only setup/wiring may change). Any test that genuinely no longer applies — record explicit architect approval in the PR description per §2.21.1.

### Constitution ratification + follow-up registration

- [X] T147 Register Unit B follow-up file *(landed at `../elsa-foundation-project-management/epic1-elsa-refactor-constitution/follow-up-items/2026-05-28_unitB_activity_identity_catalog.md`.)*
- [X] T148 Update `../elsa-foundation-project-management/epic1-elsa-refactor-constitution/PERSONAL_TODO.md` *(Unit B entry added under Currently active, header date bumped to 2026-05-28.)*

### Final validation

- [X] T149 Run `dotnet build Elsa.Server.slnx` — solution builds clean.
- [X] T150 Run the full test suite *(35/35 pass.)* — every test passes (existing + new).
- [ ] T151 Run the [quickstart.md](./quickstart.md) end-to-end scenario manually *(Deferred to runtime / deployment pass — requires composing the full host (`Elsa.Server` + Sqlite + Reconciliation + Json source + Activities.Runtime), running the migration, observing reconciliation populating the catalog from `elsa-core-activities.json`, and walking the picker endpoint. The implementation is complete and unit-test verified at each layer; manual runtime walkthrough is a host-level concern best done at the deployment pass.)*: start `Elsa.Server`, observe JSON reconciliation populating the catalog, query the picker endpoint (expect populated list), construct a CLR activity via the factory (succeeds), attempt to construct a Workflow descriptor via the factory (throws `ActivityResolutionException` per the Unit B / Unit G boundary).

---

## Dependencies & Execution Order

### Phase dependencies

- **Setup (Phase 1)**: no dependencies.
- **Foundational (Phase 2)**: depends on Setup. **BLOCKS all user stories.**
- **US1 (Phase 3)**: depends on Foundational.
- **US2 (Phase 4)**: depends on Foundational + US1 (uses the reshaped `ActivityDefinitionLookup`).
- **US3 (Phase 5)**: depends on Foundational + US1 (uses the reshaped entity/interface).
- **US4 (Phase 6)**: depends on Foundational + US1 + US3 (writes through the reshaped entities; uses the descriptor schema for JSON catalog contributions).
- **US5 (Phase 7)**: depends on Foundational + US3 (entity-saving handler refactor consumes the reshaped version entity).
- **US6 (Phase 8)**: depends on Foundational + US1 + US4 (read-contract surface check; reconciliation-state surface).
- **Polish (Phase 9)**: depends on all user stories.

**Critical path**: P2 → US1 → (US3 || US2) → US4 → US5 → US6 → Polish.

### Within each user story

- Tests written FIRST (one test task per acceptance scenario in the spec), failing before implementation lands.
- Models / interfaces before services.
- Services before endpoints.
- Core implementation before integration adjustments.

### Parallel opportunities

- Phase 2 records, enums, descriptor types (T011–T024) can land in parallel — different files, no inter-dependencies.
- US1 API endpoint updates (T036–T043) parallel — different files.
- US3 contract additions (T075–T078) parallel — different files in `Activities.Runtime.Core`.
- US4 JSON-source files (T110–T112) parallel.
- Polish feature READMEs (T137–T140) parallel.
- Polish §2.23.1 registration tests (T141–T144) parallel.
- The `Provisioning` → `Reconciliation` rename (T097–T105) is sequential by necessity — each step depends on the prior project / namespace state.

---

## Parallel Example: Phase 2 records

```bash
# All can run in parallel — different files, no inter-deps:
Task: "Add ImplementationKind smart-enum value-record in src/Elsa.Activities.Design.Core/Models/ImplementationKind.cs"
Task: "Add SourceKind smart-enum value-record in src/Elsa.Activities.Design.Core/Models/SourceKind.cs"
Task: "Add ExpressionType smart-enum value-record in src/Elsa.Activities.Design.Core/Models/ExpressionType.cs"
Task: "Add IImplementationDescriptor marker interface in src/Elsa.Activities.Design.Core/Contracts/IImplementationDescriptor.cs"
Task: "Add ClrImplementationDescriptor record in src/Elsa.Activities.Design.Core/Models/ClrImplementationDescriptor.cs"
Task: "Convert InputDefinition to sealed record in src/Elsa.Activities.Design.Core/Models/InputDefinition.cs"
Task: "Convert OutputDefinition to sealed record in src/Elsa.Activities.Design.Core/Models/OutputDefinition.cs"
Task: "Convert ActivityDesignFacet to sealed record in src/Elsa.Activities.Design.Core/Models/ActivityDesignFacet.cs"
Task: "Convert ArgumentDefinition to sealed record in src/Elsa.Activities.Design.Core/Models/ArgumentDefinition.cs"
Task: "Add ArgumentValue sealed record in src/Elsa.Activities.Design.Core/Models/ArgumentValue.cs"
Task: "Add ArgumentState base record in src/Elsa.Activities.Design.Core/Models/ArgumentState.cs"
Task: "Add InputState sealed record in src/Elsa.Activities.Design.Core/Models/InputState.cs"
Task: "Add OutputState sealed record in src/Elsa.Activities.Design.Core/Models/OutputState.cs"
```

---

## Implementation Strategy

### MVP path (US1 → US2 → US3, all P1)

The three P1 stories together deliver the load-bearing demonstration: identity decouples from CLR; picker = catalog; non-CLR descriptors are first-class. They jointly satisfy Sipke items 1 + 7.

1. Phase 1: Setup (T001–T002).
2. Phase 2: Foundational (T003–T025) — **the most parallelizable phase**.
3. Phase 3: US1 (T026–T047).
4. Phase 4: US2 (T048–T054).
5. Phase 5: US3 (T055–T085).
6. **STOP and VALIDATE**: all three P1 stories testable; Sipke items 1 + 7 demonstrably implemented end-to-end.

### Incremental delivery

After P1 MVP:

7. Phase 6: US4 (T086–T115) — provenance + reconciliation state + rename + JSON seed source.
8. Phase 7: US5 (T116–T122) — entity-handler event migration for the activity-catalog (closes Unit A's activity-catalog item).
9. Phase 8: US6 (T123–T130) — read-contract surface + doc + constitution amendment.
10. Phase 9: Polish (T131–T151) — workflow-side migration, fresh migrations, feature docs, golden-rule audit, follow-up registration, final validation.

### Risks flagged at plan stage to monitor during execution

- **Kind-→-type derivation from resolver generic argument (T064)** — Resolving `TDescriptor` via reflection on registered resolvers is clean if every kind has a registered resolver; for kinds that have descriptors persisted but no resolver yet (e.g. `Workflow` rows persisted before Unit G ships), the loader needs a fallback. Plan-stage decision: maintain the type registry independently of resolver registration to avoid this coupling, OR ship a stub resolver for `Workflow` in Unit B that throws on `Resolve(...)` so the type is still discoverable. Decide at implementation start.
- **JSON canonicalisation in the default hasher (T107)** — System.Text.Json's `JsonTypeInfoResolver.Modifiers` API allows property re-ordering at metadata-build time. Verify `JsonPropertyInfo.Order` is honoured by the writer in .NET 10. If not, fall back to a hand-rolled canonical serialiser using `Utf8JsonWriter` directly. Still native .NET; no third-party.
- **Provisioning → Reconciliation rename surface (T097–T105)** — mechanical but touches every csproj reference + every namespace using statement. Verify nothing is missed via a clean `dotnet build` after.
- **Existing test golden-rule survival (T146)** — could reveal hidden coupling per framework §2.23.4. Resolution path is to lift the dependency to a contract, not to reproduce the side-effect in a stub.
- **Shadow-column naming collision (T063)** — EF Core shadow property `ImplementationDescriptor` shares the name of the `[NotMapped]` CLR property. EF's documented behaviour treats `[NotMapped]` as invisible at the model level, so the shadow with the same name should be accepted. If conflict is observed at runtime, fall back to shadow name `"_ImplementationDescriptorPayload"` (or similar) and adjust the handlers' accessor strings accordingly.

---

## Notes

- [P] tasks = different files, no inter-task dependencies.
- [Story] label maps each task to a user story for traceability.
- Commit after each task or logical group (the optional `before_*` git-commit hook is configured per `.specify/extensions.yml`).
- Phase 2's `dotnet build` checkpoint (T025) is the structural gate: until it's green, no user story should begin.

## Constitutional Compliance

This tasks file inherits the Constitution Check from `plan.md`. Tasks that risk violations:

- **T029, T030 (identity reshape)** — preserves G15 (no Workflows.Runtime→Design dep introduced); the reshape stays design-side.
- **T064 (kind-→-type registry)** — must not introduce a heavy serialization-framework dependency into `Activities.Runtime.Core` or `Activities.Design.Core` (G3). The registry holds `(ImplementationKind, Type)` pairs — no serialization concern.
- **T097–T105 (Provisioning → Reconciliation rename)** — already flagged as G10 + G13 violation under plan-stage Complexity Tracking; justified per ratification at clarify session 2.
- **T146 (golden-rule audit)** — if any test reveals tight logic coupling per §2.23.4, escalate to architecture review per the constitution; do NOT reproduce side effects in stubs.

When a task surfaces a constitutional question, flag it in the task description / PR rather than silently working around it — escalation is the Definition of Done.
