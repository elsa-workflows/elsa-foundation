# Phase 1 Data Model — Descriptor-Type-Driven Activity Construction

Entities and contracts, with their fields, relationships, invariants, and the before/after of the reshape. "Identity anchors" only — exact signatures land in code.

## 1. Descriptor types (the discriminated data)

### `Elsa.Primitives.Models.TypeInformation` — the CLR descriptor *(existing; reused as-is)*
- Fields: `TypeName`, `Namespace`, `AssemblyName`, `AssemblyVersion`. `FromType(Type)`, `LoadType()`.
- Role in 006: **is** the descriptor for CLR-backed activities. `DescriptorType = "Elsa.Primitives.Models.TypeInformation"`; payload = the activity type's own `TypeInformation`.

### `Elsa.Workflows.Primitives.Models.WorkflowIdentity` — the Workflow descriptor *(NEW)*
- Fields: `DefinitionId` (string, stable definition identity), `VersionId` (string, durable `WorkflowDefinitionVersion` row id used to load), `Version` (string, SemVer 2.0.0 for the workflow definition version).
- Invariants: immutable record; carries no runtime-live object. `DescriptorType = "Elsa.Workflows.Primitives.Models.WorkflowIdentity"`.
- Location: `Elsa.Workflows.Primitives` (zero-dep building-block lib) — sharable by producer + consumer with no feature→feature edge.
- Replaces: 005's placeholder `WorkflowAsActivityDescriptor` and the deleted `Elsa.Activities.Design.Core.Models.WorkflowImplementationDescriptor`.

## 2. Construction-seam contracts (`Elsa.Activities.Runtime.Core`)

### `IActivityFactory` *(RESHAPE)* — replacement contract
- Before: `Create(IImplementationDescriptor, IEnumerable<InputState>, IEnumerable<OutputState>, ct)` — **leaked** Design.Core types.
- After: `Create(string descriptorType, JsonElement payload, IDictionary<string,InputArgument>? inputs, IDictionary<string,OutputArgument>? outputs, CancellationToken ct = default) : ValueTask<IActivity>`.
- Behaviour: pure dispatch — resolve constructor from registry by `descriptorType`; delegate. No binding/type-resolution of its own. Unregistered `descriptorType` → domain failure.

### `IActivityConstructor` (non-generic) + `IActivityConstructor<TDescriptor>` *(NEW)* — contribution contract
- Non-generic (registry-stored): `string DescriptorType { get; }`; `ValueTask<IActivity> Construct(JsonElement payload, IDictionary<string,InputArgument>? inputs, IDictionary<string,OutputArgument>? outputs, CancellationToken ct)`.
- Generic: `ValueTask<IActivity> Construct(TDescriptor descriptor, …)`.
- No base class. Each impl supplies a one-line explicit-interface bridge: `DescriptorType => typeof(TDescriptor).FullName!` and `Construct(JsonElement p, …) => Construct(p.Deserialize<TDescriptor>()!, …)`.
- Invariant: `Construct` returns a **whole** activity (type resolved, descriptor state applied, author args bound).

### `IActivityConstructorRegistry` *(NEW)* — replacement contract
- `void Add(IActivityConstructor constructor)` (throws `DuplicateActivityConstructorException` naming the `DescriptorType` if already present); `IActivityConstructor Resolve(string descriptorType)` (throws `UnknownDescriptorTypeException` if absent — a domain failure).
- Populated once at startup (see event below); sync-read thereafter.

### `OnActivityConstructorsInitializing : IEvent` *(NEW)* — Registry + StartUp Task pattern (G21)
- Exposes the registry (or its mutable collection). Published Sequential by a StartUp Task. Single handler `RegisterActivityConstructors` (in `Elsa.Activities.Runtime`) aggregates all `IActivityConstructor` contributors into the registry.

## 3. Runtime-side impl (`Elsa.Activities.Runtime`)
- `ActivityFactory` (public sealed) — dispatch.
- `ActivityConstructorRegistry` (public sealed) — dictionary keyed by `DescriptorType`; dup-guard throw.
- `RegisterActivityConstructors` (public sealed) — single aggregating `IEventHandler<OnActivityConstructorsInitializing>`.
- `ActivityConstructorsStartupTask` — publishes the event once.
- `ActivitiesRuntimeFeature` (public, not sealed) — registers all of the above + the factory.

## 4. CLR kind (`Elsa.Activities.Primitives`, NEW runtime feature — **no Design ref**)
- `ClrActivityConstructor : IActivityConstructor<TypeInformation>` (public sealed) — `Construct`: `var type = descriptor.LoadType(); var activity = (IActivity)ActivatorUtilities.CreateInstance(sp, type); binder.Bind(activity, inputs, outputs); return activity;`.
- `ActivityArgumentBinder` (public sealed, **feature-internal**, not in Core) — binds named args to typed `InputArgument<T>`/`OutputArgument<T>` properties: match by name, check `property.PropertyType.IsAssignableFrom(argument-type)`, invoke set-method. **Fixes the 3 known bugs** (`property.GetType()`→`PropertyType`; `!=`→`IsAssignableFrom`; remove stale `IActivityImplementationResolver` crefs).
- `WriteLine` — **ported from elsa-core** (`src/modules/Elsa.Workflows.Core/Activities/WriteLine.cs`), adapted to this repo's model: a `public InputArgument<string> Text` property, implements `IActivity`, `ExecuteAsync` writes to console. It is the concrete activity the CLR round-trip test binds against. (Only `WriteLine` ported for now — keep it simple.)
- `ActivitiesPrimitivesFeature` (public, not sealed) — registers the constructor + binder + activities. References `Elsa.Activities.Runtime.Core` + `Elsa.Primitives` — **NOT `Design.Core`**.

## 5. Workflow kind — **split** `Elsa.Activities.Composition.{Runtime,Design}` (NEW features)

**Symmetry** (clarification): `WorkflowDefinitionActivity` is an ordinary CLR `IActivity` — catalogued under a `TypeInformation` descriptor and built by the CLR constructor like a primitive. A workflow-as-activity row differs **only** by `(DescriptorType = WorkflowIdentity, constructor = WorkflowActivityConstructor)`.

### `Elsa.Activities.Composition.Runtime` (Design-free, §E2.2)
- `WorkflowDefinitionActivity` (public sealed, runtime-side, **no Design ref**) — single backing CLR activity; typed `WorkflowIdentity`/version state + dynamic bag (`IActivity.SyntheticProperties`). Construct-only this unit (execution body deferred).
- `WorkflowActivityConstructor : IActivityConstructor<WorkflowIdentity>` (public sealed) — produces a `WorkflowDefinitionActivity` configured from the `WorkflowIdentity` (applied as typed state) with author inputs/outputs pre-set into the bag. Does its **own** bag-filling (does NOT use `Primitives`' binder — that would be a feature→feature ref).
- `ActivitiesCompositionRuntimeFeature` (public, not sealed). Refs `Runtime.Core` + `Elsa.Workflows.Primitives` (+ `Elsa.Workflows.Runtime.Core` only if the construct-only type needs it). **No `Design.*` ref.**

### `Elsa.Activities.Composition.Design`
- `WorkflowActivityReconciliationSource : IActivityReconciliationSource` (public sealed) — one model per usable-as-activity workflow version; `DescriptorType="Elsa.Workflows.Primitives.Models.WorkflowIdentity"`, descriptor `WorkflowIdentity(...)`, UI metadata + I/O mirrored (005 FR-005/006). Outcome/port visualization belongs to future module-owned design facets.
- `ActivitiesCompositionDesignFeature` (public, not sealed). Refs `Design.Reconciliation.Core` + `Design.Core` + `Elsa.Workflows.Primitives`.

## 6. Persistence reshape (`Elsa.Activities.Design.Persistence.*`)

### `ActivityDefinitionVersion` entity *(RESHAPE)*
| Before | After |
|---|---|
| `string ImplementationKind` (write-once) | `string DescriptorType` (write-once) — the descriptor type FullName |
| `string? ImplementationDescriptorPayload` (serialized JSON) | `string? DescriptorPayloadSource` (serialized JSON; write-once) |
| `[NotMapped] IImplementationDescriptor ImplementationDescriptor` | `[NotMapped] JsonElement DescriptorPayload` |
- Unchanged: `Version`, `SemVerSortKey`, `DefinitionId`, `*Source` projections, `SourceKind`/`SourceId`, `ReconciledAt/By`, `ReconcilliationHash`, tenant.
- Invariants (G28): `DescriptorType` + `DescriptorPayloadSource` write-once (`PropertySaveBehavior.Throw` in EF config). The design domain never deserializes the payload to a concrete type.
- **Two orthogonal axes — do NOT conflate** (carries a doc comment on the entity): `SourceKind` (provenance — `"CLR"`/`"Workflow"`/`"Json"`; *which source produced the row*) is distinct from `DescriptorType` (the construction key; *which constructor builds the activity*). Both are retained as separate columns. (e.g. a JSON source can produce a row whose `DescriptorType` is `TypeInformation`.)

### `IActivityDefinitionVersion` read contract *(RESHAPE)* — **decided**
- Remove `ImplementationKind` + `IImplementationDescriptor ImplementationDescriptor`.
- **Expose both** `string DescriptorType` **and** `JsonElement DescriptorPayload` on the read-only interface (not domain-shadows). Consumers (e.g. the API detail view, the runtime read path) read the descriptor directly off the contract.
- Consequence: the loading handler MUST populate `DescriptorPayload` (parse `DescriptorPayloadSource` → `JsonElement`) so readers get a hydrated value. This stays **opaque** — `JsonElement` is a BCL type, not a descriptor type, so exposing it introduces **no** descriptor-type dependency and does not touch §E2.2.

### Save/Load handlers *(REWRITE)*
- Saving: `entity.DescriptorPayloadSource = serialize(entity.DescriptorPayload)`; `DescriptorType` already set by the reconciler. No `.Kind` derivation.
- Loading: parse `DescriptorPayloadSource` → `JsonElement` (no type resolution; drop the `IImplementationDescriptorRegistry` dependency). `ActivityDescriptorDeserialisationException` deleted/relocated.

### EF config + migration
- `ActivityDefinitionVersionConfiguration`: rename column mapping `ImplementationKind`→`DescriptorType`; keep payload column; immutability via `PropertySaveBehavior.Throw`.
- SQLite migrations: delete + regenerate fresh `Initial` (no data migration, D8).

## 7. Reconciliation reshape (`Elsa.Activities.Design.Reconciliation*`)

### `ActivityVersionReconciliationModel` *(RESHAPE)*
- Rename `string ImplementationKind` → `string DescriptorType`. Keep `object ImplementationDescriptor` (the descriptor object or a `JsonElement` for the JSON source).
- Each source supplies `DescriptorType` explicitly (D6): CLR scanner → `"Elsa.Primitives.Models.TypeInformation"` + `TypeInformation.FromType(type)`; JSON file → `DescriptorType` field; Workflow source → `"Elsa.Workflows.Primitives.Models.WorkflowIdentity"` + `WorkflowIdentity(...)`.

### Reconciling handler / reconciler *(SIMPLIFY)*
- Drop all descriptor-**type resolution** (no `Kind→Type`); persist `(DescriptorType, payload)` straight through. No per-kind branch (SC-004). Hasher unaffected.

## 8. Deletions (`Elsa.Activities.Design.Core`)
`IImplementationDescriptor`, `IImplementationDescriptorRegistry`, `IImplementationDescriptorSource`, `OnImplementationDescriptorsInitializing`, `ImplementationDescriptorRegistration`, `ImplementationDescriptorRegistry`, `ClrImplementationDescriptor`, `WorkflowImplementationDescriptor`. Plus design-side registration handlers/startup tasks/sources that fed them (any remaining `RegisterImplementationDescriptors`, descriptor-source impls). Repo-wide sweep proves zero references (SC-002).

## 9. Downstream shape updates
- `Elsa.Activities.Design.Api` — `AddDefinition`/`AddVersion` commands + handlers + `ActivityDefinitionVersionToDetailsView` + `ActivityDefinitionVersionDetailsView`: carry `(DescriptorType, payload)` instead of `ImplementationKind` + `IImplementationDescriptor`.
- `Elsa3.Activities.Design.Import/Models/ActivityDefinitionVersionImport` — same shape update (one-way adapter; no new direction, G30 holds).
