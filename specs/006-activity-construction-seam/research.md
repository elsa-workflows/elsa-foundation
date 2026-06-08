# Phase 0 Research — Descriptor-Type-Driven Activity Construction

All "NEEDS CLARIFICATION" from Technical Context resolved (none remained after `/speckit.clarify`). This file records the load-bearing decisions and the alternatives rejected, plus findings from reading the current code that refine the spec.

## D1 — Discriminate by descriptor type, not a `Kind` string

- **Decision**: Persist `DescriptorType` = the descriptor type's `FullName` (namespace + type, excluding assembly identity). It is the registry key, derived from `typeof(TDescriptor)`.
- **Rationale**: Removes the invented 3-place `Kind`-string agreement; the descriptor's CLR identity is the natural discriminator. Restores §E2.2 by removing the design-side `Kind→Type` registry entirely.
- **Alternatives rejected**: (a) Keep `Kind` — the rejected Unit-4 coupling. (b) Assembly-qualified name — breaks rows on version bump; `FullName` survives version bumps, only true renames migrate (accepted cost, mirrors `TypeInformation` already pinning type identity).

## D2 — `TypeInformation` is the CLR descriptor; `ClrImplementationDescriptor` is deleted

- **Decision**: The CLR descriptor is `Elsa.Primitives.Models.TypeInformation` (confirmed present, with `FromType()`/`LoadType()`). `DescriptorType = "Elsa.Primitives.Models.TypeInformation"`.
- **Rationale**: `TypeInformation` already *is* a persistence-stable handle to a CLR type — exactly a CLR activity's descriptor. `Elsa.Primitives` is a zero-dep building-block lib both `Reconciliation.Clr` (already references it) and `Elsa.Activities.Primitives` can use → no feature→feature edge. The wrapper added nothing.
- **Alternatives rejected**: A CLR-only joining module — extra ceremony; CLR is the universal "primitive" default, so it lives in `Elsa.Activities.Primitives`.

## D3 — Workflow descriptor is `WorkflowIdentity` in `Elsa.Workflows.Primitives`

- **Decision**: New record `WorkflowIdentity(DefinitionId, VersionId, Version)` in `Elsa.Workflows.Primitives` (verified zero-dep, no `IShellFeature` → a building-block lib, sibling of `Elsa.Primitives`). `Version` is the SemVer 2.0.0 string for the workflow definition version (sourced from `WorkflowDefinitionVersion.Version` via Unit 4's `n → "n.0.0"`). `VersionId` is the durable row id used to load; `DefinitionId` is the stable definition identity. `DescriptorType = "Elsa.Workflows.Primitives.Models.WorkflowIdentity"`.
- **Rationale**: Lightweight, sharable anywhere; placing it in a building-block lib keeps both producer (reconciliation source) and consumer (constructor) free of a feature→feature reference. Replaces 005's placeholder `WorkflowAsActivityDescriptor` and the existing `WorkflowImplementationDescriptor` (deleted).
- **Alternatives rejected**: Carrying only `VersionId` — loses cheap access to identity/version for display and consumer binding; the user wants the full identity available anywhere.

## D4 — Registry populated via Registry + StartUp Task + Domain Event (G21)

- **Decision**: `IActivityConstructorRegistry` (`DescriptorType → IActivityConstructor`) is populated by a single aggregating handler `RegisterActivityConstructors` that handles `OnActivityConstructorsInitializing` (`IEvent`, published Sequential by a StartUp Task), aggregating all registered `IActivityConstructor` contributors; sync-read afterward. All declared in `Elsa.Activities.Runtime.Core` / `Elsa.Activities.Runtime` — zero Design references.
- **Rationale**: G21/framework §2.6.1 forbid introducing `IEnumerable<TProvider>` provider injection for new code; the canonical sync-access shape is Registry + StartUp Task + Domain Event (Elsa §E3.3). This is exactly the pattern the experiment deleted (`OnActivityImplementationResolversInitializing` + startup task + register handler) — we **resurrect the pattern, drop the Design coupling**.
- **Invariant**: registration throws a domain exception on a second contributor for the same `DescriptorType` (FR-006). The throw happens during aggregation, i.e. at startup — loud, not last-wins.
- **Alternatives rejected**: Inject `IEnumerable<IActivityConstructor>` directly into the factory — violates G21. Open-generic DI resolution of `IActivityConstructor<TDescriptor>` by reflected type — needless reflection; the contributor already knows its `TDescriptor` and exposes `DescriptorType`.

## D5 — Persisted descriptor shape: `DescriptorType` column + opaque JSON; entity exposes `JsonElement`

- **Decision**: On `ActivityDefinitionVersion`: rename `ImplementationKind` → `DescriptorType` (string, write-once); keep the serialized payload as a string column (`DescriptorPayloadSource`); replace `[NotMapped] IImplementationDescriptor ImplementationDescriptor` with `[NotMapped] JsonElement DescriptorPayload`. The **loading handler no longer resolves a descriptor type** (deletes its `IImplementationDescriptorRegistry` dependency); it parses string → `JsonElement` (or leaves the string, parsing lazily). The **saving handler** sets `DescriptorPayloadSource` from the `JsonElement` and persists `DescriptorType` (set by the reconciler). The design domain never deserializes to a concrete type.
- **Rationale**: Matches the C3 decision — `JsonElement` is a provably-STJ-serializable, round-trippable view; `object` would admit non-STJ values (e.g. `JToken`). Removes design-side deserialization (the §E2.2 leak's reason to exist).
- **Finding (refines spec)**: the current loading handler casts to `IImplementationDescriptor` after a registry lookup ([ActivityDefinitionVersionLoadingHandler.cs:39-47]); the saving handler derives `ImplementationKind = ImplementationDescriptor.Kind` ([SavingHandler:27]). Both are rewritten. `ActivityDescriptorDeserialisationException` (design-side) is deleted/relocated — the only deserialization failure now occurs runtime-side, in the constructor's deserialize bridge.

## D6 — `DescriptorType` is an explicit field on the reconciliation model (not derived)

- **Decision**: Rename `ActivityVersionReconciliationModel.ImplementationKind` → `DescriptorType` (string); keep the descriptor as `object`. The reconciler persists `entity.DescriptorType = model.DescriptorType` and `entity.DescriptorPayload = SerializeToElement(model.<descriptor>)`.
- **Rationale (important refinement of FR-014)**: `DescriptorType` **cannot** be universally derived from the descriptor object. The JSON source ([JsonActivityCatalogReader.cs]) binds its descriptor to a `JsonElement` (the model's `object` slot), so `GetType().FullName` would yield `"System.Text.Json.JsonElement"`, not the real descriptor type. Therefore each source supplies `DescriptorType` explicitly: the CLR scanner sets `"Elsa.Primitives.Models.TypeInformation"`; the JSON catalog file carries a `DescriptorType` field; the Workflow source sets `"Elsa.Workflows.Primitives.Models.WorkflowIdentity"`. The reconciling handler/reconciler drop all descriptor-**type resolution** (no more design-side `Kind→Type`).
- **Alternatives rejected**: Derive from the object — breaks for the `JsonElement`-typed JSON source. Keep `ImplementationKind` name — the value is now a type FullName, so the rename is for honesty.

## D7 — Construct-only `WorkflowDefinitionActivity`; seam + tests only

- **Decision**: `WorkflowDefinitionActivity` must be correctly *constructed* (type resolved, `WorkflowIdentity` applied as typed state, author args bag-filled); its execution body (load-and-run the workflow version) is **out of scope**. The factory is delivered with its own unit/integration tests; wiring into the live execution/graph-materialization path is a later unit.
- **Rationale**: Clarification answers. Keeps the §E2.2 boundary the focus and bounds the unit.
- **Note**: `IActivity` already exposes `SyntheticProperties` (a `Dictionary<string,object>`) — the natural **dynamic bag** for the Workflow kind's author inputs/outputs. The CLR kind binds to typed `InputArgument<T>`/`OutputArgument<T>` properties instead. The binder decides per kind (it lives in `Primitives`; the Workflow constructor bag-fills directly).

## D8 — No data migration; reset the EF initial migration

- **Decision**: Delete `Elsa.Activities.Design.Persistence.EFCore.Sqlite/Migrations/*` and regenerate a fresh `Initial` reflecting `(DescriptorType, DescriptorPayloadSource)` (drop `ImplementationKind`). No transform of existing rows; catalog regenerates from reconciliation.
- **Rationale**: Clarification answer; consistent with the repo's standing "no preserved production data" convention (Unit B/C, Unit 3 SIR).

## D9 — Contract kinds + the core-not-a-bucket rule

- **Decision**: `IActivityConstructor(+<T>)` = **contribution** contract → `Runtime.Core`. `IActivityFactory` + `IActivityConstructorRegistry` = **replacement** contracts (single swappable runtime services) → `Runtime.Core`. `IActivityArgumentBinder` = **neither** (feature-internal) → stays in `Elsa.Activities.Primitives`.
- **Rationale**: Strengthened framework rule — a `*.Core` is for contributor/replacement contracts (+ shared models), not a bucket for every interface. Recorded as an in-unit framework-§2 amendment.
