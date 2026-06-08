# Feature Specification: Descriptor-Type-Driven Activity Construction

**Feature Branch**: `006-activity-construction-seam` *(spec authored on `main`; no feature branch cut — consistent with units 001–005)*
**Created**: 2026-06-05
**Status**: Draft
**Input**: Unit 5 of the Elsa entity-design refactor. Replace the rejected Unit-4 construction machinery (a `Kind` string + an `IImplementationDescriptor` interface in the design core + two registries + a descriptor-carrying factory) with a single, descriptor-**type**-driven runtime construction seam, so that `Elsa.Activities.Runtime.Core` is fully independent of `Elsa.Activities.Design.*`. **Supersedes [`005-workflow-as-activity`](../005-workflow-as-activity/spec.md).** Works against constitution v3.0.0 (draft).

## Why This Supersedes 005

005 specified the workflow-as-activity producer/runtime round-trip on top of an implementation-kind/descriptor mechanism that, on attempted implementation, was found to be structurally unsound. 005 remains as the historical record of *what the workflow-as-activity feature must achieve*; 006 is the *re-designed construction seam* that achieves it without the defects. The rejected elements of 005 and the reason each was rejected:

| 005 element | Why rejected |
|---|---|
| `IImplementationDescriptor` interface living in `Elsa.Activities.Design.Core` | Every runtime-side resolver/registry that interpreted descriptors had to reference it, forcing `Elsa.Activities.Runtime.Core → Elsa.Activities.Design.Core` — a direct violation of Elsa §E2.2 (Runtime.* MUST NOT depend on Design.*). The dependency was baked into the runtime contract (`IActivityFactory.Create(IImplementationDescriptor, …)`), not an incidental import. |
| A `Kind` string discriminator, agreed across three places (descriptor `KindValue`, resolver `Kind`, descriptor-source registration) | An *invented* coupling: a hand-authored identifier with a typo/refactor-fragility surface, sitting next to a perfectly good natural discriminator — the descriptor's own CLR type. |
| Two registries (design-side `IImplementationDescriptorRegistry` mapping `Kind → descriptor Type`, and a runtime-side resolver registry mapping `Kind → resolver`) | The design-side registry existed only so the *design* domain could deserialize the polymorphic descriptor column. Once the design domain is forbidden from knowing descriptor shapes at all, that registry has no reason to exist; the runtime registry alone remains. |
| `ClrImplementationDescriptor` (wrapping `TypeInformation`) in `Elsa.Activities.Design.Core.Models` | A redundant wrapper. `TypeInformation` (in `Elsa.Primitives`, zero-dep) already *is* a persistence-stable handle to a CLR type — exactly and only what a CLR activity's descriptor needs to carry. The wrapper added a second type and a `Kind` with no payload of its own. |

The producer-side outcomes of 005 (US1–US4 of that spec — marked workflows become version-distinct catalog rows with mirrored I/O, one well-known backing type, generalizable to future kinds) are **retained as requirements** here and re-expressed against the new seam.

## Clarifications

### Session 2026-06-05

- Q: How is a persisted descriptor discriminated for reconstruction if there is no `Kind`? → A: **Resolved — by the descriptor's own CLR type.** Persist a `DescriptorType` string (the descriptor type's `FullName` — namespace + type, **not** assembly-qualified) alongside an opaque serialized payload. The `DescriptorType` is the registry lookup key and is **derived** from `typeof(TDescriptor)`, never hand-authored. (FR-001, FR-002.)
- Q: What carries a CLR activity's descriptor now that `ClrImplementationDescriptor` is deleted? → A: **Resolved — `Elsa.Primitives.Models.TypeInformation` itself is the descriptor.** Its persisted `DescriptorType` is `"Elsa.Primitives.Models.TypeInformation"`; its payload is the activity type's own `TypeInformation`. (FR-008, FR-009.)
- Q: Where does the descriptor payload get materialized into a concrete typed object? → A: **Resolved — only in the runtime feature that owns the descriptor type**, inside that feature's constructor (which deserializes the payload into its known `TDescriptor`). The design domain serializes on write and **never** deserializes; it only ever knows a `DescriptorType` string and a `JsonElement`. (FR-002, FR-014.)
- Q: What happens if two construction contributors claim the same descriptor type? → A: **Resolved — the registry throws on the second registration.** One-constructor-per-descriptor-type is a guarded domain invariant, not silent last-wins. (FR-006.)
- Q: Is there a post-construction lifecycle event for third-party contributors? → A: **Deferred — no.** No `ActivityCreated` event is introduced; there is no consumer (YAGNI). Construction is atomic: a constructor returns a fully-wired activity. If a contributor seam is needed later it is a separate, gated change. (Out of Scope.)
- Q: Where does the shared argument-binding helper live? → A: **Resolved — inside `Elsa.Activities.Primitives`** (its only consumer), not in a `*.Core` library. This instantiates a recorded framework rule: interfaces used only within one feature's implementation stay in that feature. (FR-011, Constitutional Compliance.)
- Q: What is the Workflow kind's descriptor type? → A: **Resolved — `WorkflowIdentity`**, a lightweight dependency-free model in `Elsa.Workflows.Primitives` (a zero-dep building-block library, structurally a sibling of `Elsa.Primitives` — confirmed by inspection: empty csproj, no `IShellFeature`), carrying `DefinitionId`, `VersionId`, and `Version`. It replaces the placeholder `WorkflowAsActivityDescriptor`. Persisted `DescriptorType = "Elsa.Workflows.Primitives.Models.WorkflowIdentity"`. Because it lives in a shared building-block lib, no feature → feature reference is introduced. (FR-012.)
- Q: What is `WorkflowIdentity.Version`? → A: **Resolved — the SemVer version** for the workflow definition version (the `Version` property on `WorkflowDefinitionVersion`, expressed as the catalog SemVer 2.0.0 string per Unit 4's `n → "n.0.0"` mapping). `VersionId` remains the durable row id used to load the version; `DefinitionId` is the stable definition identity. (FR-012, Key Entities.)
- Q: Must `WorkflowDefinitionActivity` actually load-and-run the workflow in this unit, or only be correctly constructed? → A: **Resolved — construct-only.** 006 requires the descriptor→constructor→`IActivity` round-trip (right type, identity applied, args bound); the execution body that loads and runs the referenced workflow version is deferred to the consumer/pinning unit. (FR-013, Out of Scope.)
- Q: Is wiring the factory into the live execution / graph-materialization path in scope? → A: **Resolved — no; seam + tests only.** 006 delivers the factory, registry, and CLR + Workflow constructors with their own unit/integration tests; integrating into the running executor is a later unit. (Out of Scope.)
- Q: Do existing persisted catalog rows need a data migration? → A: **Resolved — no migration.** Pre-release refactor: delete the existing EF migration and create a fresh initial migration reflecting the `(DescriptorType, payload)` shape; rows regenerate from reconciliation sources. (Assumptions.)
- Q: How does `WorkflowDefinitionActivity` relate to the CLR kind? → A: **Resolved — it IS a CLR activity.** `WorkflowDefinitionActivity` is an ordinary `IActivity`, catalogued under a `TypeInformation` descriptor and built by the CLR constructor, exactly like a primitive. A *workflow-as-activity* row differs **only** by its descriptor type (`WorkflowIdentity`) and its constructor (`WorkflowActivityConstructor`, which produces a configured `WorkflowDefinitionActivity`). Nothing else distinguishes the two kinds. (FR-012, FR-013.)
- Q: Does the runtime activity live with design-referencing code (§E2.2)? → A: **Resolved — split.** `Elsa.Activities.Composition.Runtime` (Design-free: `WorkflowDefinitionActivity` + `WorkflowActivityConstructor`) and `Elsa.Activities.Composition.Design` (the reconciliation source; references `Design.Core`); shared `WorkflowIdentity` lives in `Elsa.Workflows.Primitives`. The `Composition` **name is retained** — it is its own activity sub-domain (composing bundles of activities/workflows), so §E3.10's `Elsa.Workflows.Activities.*` model-prefix naming is intentionally not adopted. (FR-013.)
- Q: Does `Elsa.Activities.Primitives` need a `Design.Core` reference? → A: **Resolved — no.** It holds only runtime constructors/binder/activities; once `TypeInformation` (in `Elsa.Primitives`) became the CLR descriptor, the `Design.Core` reference is vestigial and is removed (keeps the runtime feature Design-free). (FR-011.)

## User Scenarios & Testing *(mandatory)*

### User Story 1 — `Runtime.Core` carries no design dependency (Priority: P1)

A framework maintainer builds `Elsa.Activities.Runtime.Core` and the runtime construction seam in isolation. No project in the runtime construction path references any `Elsa.*.Design.*` project; the activity factory, the constructor contracts, and the constructor registry are expressible and compilable with zero design references.

**Why this priority**: This is the load-bearing reason Unit 5 exists. The §E2.2 boundary is the architectural invariant the whole refactor protects, and it is exactly what 005's design broke. Every other story is downstream of a clean runtime seam.

**Independent Test**: Inspect the project references of every project in the construction path (`Runtime.Core`, the runtime feature hosting the factory/registry) and assert none references an `Elsa.*.Design.*` project; build the runtime construction path without the design assemblies present.

**Acceptance Scenarios**:

1. **Given** the runtime construction seam, **When** its project references are inspected, **Then** none references any `Elsa.*.Design.*` project.
2. **Given** `IActivityFactory`, `IActivityConstructor`, `IActivityConstructor<TDescriptor>`, and `IActivityConstructorRegistry`, **When** their signatures are inspected, **Then** no member's type is sourced from `Elsa.*.Design.*` (the factory takes a `DescriptorType` string + a `JsonElement` payload + runtime-side argument models, never a design descriptor).
3. **Given** the deleted types (`IImplementationDescriptor`, `ClrImplementationDescriptor`, the design-side descriptor registry/source, the runtime resolver registry/resolver), **When** a repository-wide search is run, **Then** no production code references any of them.

---

### User Story 2 — An activity is constructed from a persisted descriptor with no `Kind` (Priority: P1)

The execution path holds a `(DescriptorType, payload)` pair plus the author-filled input/output arguments for an activity node. It asks the factory to construct the activity; the factory routes to the one constructor registered for that descriptor type, which deserializes the payload, resolves the CLR type, activates it, and binds the arguments — returning a fully-wired `IActivity`. No `Kind` string is consulted anywhere.

**Why this priority**: This is the runtime half of the seam and the round-trip that proves the design works for the two concrete kinds (CLR and Workflow). Without it, descriptor-type discrimination is theoretical.

**Independent Test**: Route a `("Elsa.Primitives.Models.TypeInformation", <type payload>)` pair and a `("Elsa.Workflows.Primitives.Models.WorkflowIdentity", <WorkflowIdentity payload>)` pair through `IActivityFactory.Create`; assert each yields the correct `IActivity` (a hand-written CLR activity instance; a `WorkflowDefinitionActivity`) with arguments bound.

**Acceptance Scenarios**:

1. **Given** a persisted CLR descriptor (`DescriptorType = "Elsa.Primitives.Models.TypeInformation"`, payload = an activity type's `TypeInformation`) plus author inputs/outputs, **When** the factory constructs, **Then** the resolved CLR activity instance is returned with its `InputArgument<T>`/`OutputArgument<T>` properties bound by name + argument type.
2. **Given** a persisted Workflow descriptor (`DescriptorType = "Elsa.Workflows.Primitives.Models.WorkflowIdentity"`, payload carrying `DefinitionId` + `VersionId` + `Version`) plus author inputs/outputs, **When** the factory constructs, **Then** a single well-known `WorkflowDefinitionActivity` is returned with the `WorkflowIdentity` applied as typed state and the author inputs/outputs placed in its dynamic bag.
3. **Given** two Workflow descriptors carrying different `WorkflowIdentity` values, **When** each is constructed, **Then** both yield a `WorkflowDefinitionActivity`, differing only by the applied identity (one backing type, many workflows).
4. **Given** a returned activity from either kind, **When** it is inspected, **Then** it is whole — every author-supplied argument is bound; construction never returns a half-built instance.
5. **Given** a `DescriptorType` for which no constructor is registered, **When** the factory is asked to construct, **Then** it raises a domain failure (not a generic `KeyNotFoundException`/`NullReferenceException`).

---

### User Story 3 — The design domain treats descriptors as opaque (Priority: P1)

The design domain persists and returns an activity version's descriptor as nothing more than a `DescriptorType` string and a serialized payload. It never references a concrete descriptor type, never deserializes a payload, and needs no registry of descriptor kinds.

**Why this priority**: This is the structural change that makes US1 possible. As long as the design domain knows descriptor shapes, the runtime/design entanglement returns. The opacity is the firewall.

**Independent Test**: Persist an `ActivityDefinitionVersion` produced by reconciliation; reload it; assert the descriptor survives as `(DescriptorType, payload)` round-trippable JSON; inspect `Elsa.Activities.Design.Core` and assert it defines no descriptor type and no `IImplementationDescriptor`.

**Acceptance Scenarios**:

1. **Given** `Elsa.Activities.Design.Core`, **When** its public surface is inspected, **Then** it contains no `IImplementationDescriptor`, no `ClrImplementationDescriptor`, and no implementation-descriptor registry/source.
2. **Given** an activity version with a descriptor, **When** it is saved and reloaded, **Then** the design domain transports it as a `DescriptorType` string + a `JsonElement` payload, performing serialization on write and **no** deserialization on read.
3. **Given** any future descriptor type contributed by any feature, **When** the design domain persists it, **Then** the design code requires no change (it is shape-agnostic).

---

### User Story 4 — `TypeInformation` is the CLR descriptor; the CLR scanner emits it (Priority: P2)

The generic CLR assembly scanner produces catalog rows whose descriptor is the scanned activity type's `TypeInformation`, with `DescriptorType = "Elsa.Primitives.Models.TypeInformation"`. The `Elsa.Activities.Primitives` feature contributes the matching constructor that loads the type and activates it. No `ClrImplementationDescriptor` exists, and no feature references another feature.

**Why this priority**: Proves the descriptor-as-existing-primitive idea end-to-end and validates the no-feature-to-feature-reference constraint for the most common (CLR) kind.

**Independent Test**: Scan a folder of activity DLLs; assert each produced row has `DescriptorType = "Elsa.Primitives.Models.TypeInformation"` and a payload equal to `TypeInformation.FromType(activityType)`; route one such row through the factory and assert the activity instantiates.

**Acceptance Scenarios**:

1. **Given** the CLR scanner, **When** it reads an activity type, **Then** it emits a descriptor of `TypeInformation.FromType(type)` and references only `Elsa.Primitives` for it (no reference to `Elsa.Activities.Primitives` or any other feature).
2. **Given** the `Elsa.Activities.Primitives` feature, **When** it configures services, **Then** it registers a constructor for descriptor type `TypeInformation` that activates the loaded CLR type via the host's activation utility.
3. **Given** a CLR descriptor routed through the factory, **When** constructed, **Then** the activity type named by the `TypeInformation` payload is loaded and instantiated.

---

### User Story 5 — One constructor per descriptor type, guarded (Priority: P2)

Registering a second construction contributor for a descriptor type already claimed is a configuration error that fails loudly at registration/startup, not a silent override.

**Why this priority**: The descriptor type *is* the dispatch key; a duplicate makes dispatch ambiguous. Catching it at composition time prevents non-deterministic runtime behavior.

**Independent Test**: Register two constructors that both declare descriptor type `T`; assert the registry build throws a domain exception naming `T` and both contributors.

**Acceptance Scenarios**:

1. **Given** two constructors declaring the same descriptor type, **When** the registry is built, **Then** it throws a domain exception identifying the conflicting descriptor type.
2. **Given** constructors declaring distinct descriptor types, **When** the registry is built, **Then** all register and each is resolvable by its descriptor type name.

---

### User Story 6 — Adding a new kind touches only new per-feature types (Priority: P3)

An architect adds a hypothetical future kind (e.g. an OpenAPI-operation-backed activity) by introducing only: a new descriptor type, its constructor, and its reconciliation source — all inside the owning feature. The factory, the registry, the design domain, and the reconciliation handler are unchanged.

**Why this priority**: The generalization claim inherited from 005 US4. If a new kind required editing a universal component, the seam is leaky and the defect must be fixed now.

**Independent Test**: A documented seam-walk plus a structural check that the universal components contain no per-descriptor-type branch; repeat the walk on paper for a second hypothetical kind and show it touches only new types.

**Acceptance Scenarios**:

1. **Given** the Workflow kind fully wired, **When** the factory, registry, design domain, and reconciliation handler are inspected, **Then** none contains a per-descriptor-type branch.
2. **Given** a hypothetical new kind, **When** the seam-walk is applied, **Then** it introduces only a descriptor type + constructor + reconciliation source and changes no universal component.

---

### Edge Cases

- **A `DescriptorType` whose owning feature is not installed** (the row was produced by a host that had the feature; the constructing host does not). No constructor is registered → the factory raises a domain failure at construction time. Cataloguing and reading the row are unaffected (the design domain never needed the type).
- **A payload that does not deserialize into the declared `TDescriptor`** (corrupt/mismatched data). The owning constructor's deserialize bridge fails with a domain failure attributable to that descriptor type; it is not swallowed.
- **A descriptor type is renamed or moved** (its `FullName` changes). Existing rows carry the old `FullName` and no longer resolve → this is a data migration, by design. Recorded as the accepted cost of type-based discrimination (see Assumptions).
- **An activity declares no inputs/outputs.** Construction returns a valid bare instance; binding is a no-op. Whole-on-return still holds.
- **A CLR activity property name matches an argument but the argument value type is incompatible.** Binding raises a domain failure naming the property — it does not silently skip or coerce.
- **A dynamic (Workflow) activity receives author arguments with no matching typed property.** Expected — they go to the dynamic bag, not to property binding; this is not an error (contrast the CLR kind).
- **Two distinct features both try to own descriptor type `TypeInformation`.** Forbidden by FR-006 (registry throws); `TypeInformation` is permanently owned by `Elsa.Activities.Primitives`.

## Requirements *(mandatory)*

### Functional Requirements

**Discrimination & persistence**

- **FR-001**: A persisted activity-version descriptor MUST be represented by exactly two pieces of data: a `DescriptorType` string (the descriptor type's `FullName` — namespace + type name, **excluding** assembly identity/version) and an opaque serialized payload. The `Kind` concept and the `ImplementationKind` persisted column/term from 005 MUST be removed entirely and replaced by `DescriptorType`.
- **FR-002**: The design domain MUST treat the descriptor as opaque: it serializes the payload on write and MUST NOT deserialize it on read. It MUST NOT reference any concrete descriptor type and MUST NOT maintain any registry of descriptor types/kinds. Materialization of a concrete descriptor object happens only in the runtime feature that owns the descriptor type.
- **FR-003**: The persisted payload MUST be modeled so its serializability is explicit and round-trippable: a `string` shadow/domain property holds the serialized form, and the entity exposes a `[NotMapped]` `JsonElement` view; the saving handler owns the `JsonElement ⇄ string` transform. (A `JsonElement` is chosen over `System.Object` precisely because `object` admits non-`System.Text.Json`-serializable values, e.g. a `JToken`.)
- **FR-004**: The `DescriptorType` value MUST be derivable from `typeof(TDescriptor)` (never hand-authored), so there is no multi-site string-agreement requirement.

**Construction seam (runtime-side, zero design references)**

- **FR-005**: An `IActivityFactory` MUST exist as the single construction entry point with the role of *dispatch + lifecycle orchestration only*. Its `Create` operation MUST accept a `DescriptorType` string, the serialized payload, the author-filled input arguments, and the author-filled output arguments, and MUST delegate to the constructor registered for that descriptor type. It MUST NOT itself perform argument binding or type resolution.
- **FR-006**: An `IActivityConstructorRegistry` MUST map `DescriptorType` → constructor and MUST enforce a one-constructor-per-descriptor-type invariant, throwing a domain exception (identifying the conflicting type) on a duplicate registration. Resolving an unregistered `DescriptorType` MUST surface a domain failure, not a generic infrastructure exception.
- **FR-007**: Construction contributors MUST be expressed as `IActivityConstructor<TDescriptor>` (typed) over a non-generic `IActivityConstructor` (registry-stored). There MUST be **no shared base class**; each implementation provides a minimal explicit-interface bridge that (a) exposes `DescriptorType` derived from `typeof(TDescriptor)` and (b) deserializes the payload into `TDescriptor` before delegating to the typed method.
- **FR-008**: A constructor MUST perform **complete, atomic** construction: deserialize payload → resolve CLR `Type` → activate → apply any descriptor-sourced state → bind the author inputs/outputs. The returned `IActivity` MUST be fully wired. There MUST be no factory-level binding fallback and no deferral of binding to an external event/handler.

**The CLR kind (default / primitive)**

- **FR-009**: `Elsa.Primitives.Models.TypeInformation` MUST serve as the CLR activity descriptor; `ClrImplementationDescriptor` MUST be deleted. The CLR descriptor's `DescriptorType` is `"Elsa.Primitives.Models.TypeInformation"` and its payload is the activity type's own `TypeInformation`.
- **FR-010**: The generic CLR assembly scanner (`Elsa.Activities.Design.Reconciliation.Clr`) MUST emit `TypeInformation.FromType(type)` as the descriptor, referencing only `Elsa.Primitives` for it, and MUST drop any dependency on the deleted `ClrImplementationDescriptor`. No feature → feature project reference may be introduced.
- **FR-011**: The `Elsa.Activities.Primitives` feature MUST contribute the constructor for descriptor type `TypeInformation`, which loads the type (`TypeInformation.LoadType()`) and activates it via the host activation utility. The standard argument-binding helper (match by property name + argument type → invoke set-method) MUST live **inside** `Elsa.Activities.Primitives` (its only consumer), MUST NOT be promoted to a `*.Core` library, and MUST be an interface only if a second consumer justifies one. The binding helper MUST correct the known defects from the experimental code: use the property's declared type (not the reflection object's type) and assignability (not reference inequality) for the type check. `Elsa.Activities.Primitives` is a **runtime** feature and MUST NOT reference any `Elsa.*.Design.*` project — the CLR descriptor `TypeInformation` lives in `Elsa.Primitives`, so no `Design.Core` reference is needed.

**The Workflow kind (carried over from 005)**

- **FR-012**: The Workflow kind's descriptor MUST be `WorkflowIdentity` — a lightweight, dependency-free model in `Elsa.Workflows.Primitives` (a zero-dep building-block library, **not** a feature; structurally a sibling of `Elsa.Primitives`), carrying `DefinitionId`, `VersionId`, and `Version` (the SemVer 2.0.0 string for the workflow definition version, per Unit 4's `n → "n.0.0"` mapping) — and nothing runtime-live. Its persisted `DescriptorType` is `"Elsa.Workflows.Primitives.Models.WorkflowIdentity"`. A workflow definition version marked usable-as-activity is persisted as a catalog row carrying this descriptor. Its constructor (`IActivityConstructor<WorkflowIdentity>`) MUST produce a configured `WorkflowDefinitionActivity` instance for **every** workflow-backed row — applying the `WorkflowIdentity` as typed state and placing the author inputs/outputs (pre-set) in the activity's dynamic bag. Because `WorkflowIdentity` lives in a shared building-block library, neither the producer (the Workflow reconciliation source) nor the consumer (the constructor) introduces a feature → feature reference.
- **FR-013**: `WorkflowDefinitionActivity` is itself an **ordinary CLR `IActivity`** — it is brought into the catalog like any CLR activity, under `DescriptorType = "Elsa.Primitives.Models.TypeInformation"`, and built by the CLR constructor. **The only thing distinguishing a workflow-as-activity row from a primitive activity is its descriptor type plus which constructor builds it**: a `WorkflowIdentity` row is built by `WorkflowActivityConstructor` (producing a `WorkflowDefinitionActivity` configured from the identity); a `TypeInformation` row is built by the CLR constructor. `WorkflowDefinitionActivity` and `WorkflowActivityConstructor` MUST be **runtime-side and Design-free** (§E2.2) and therefore live in a runtime project — `Elsa.Activities.Composition.Runtime` — that references **no** `Elsa.*.Design.*` project. The design-side `WorkflowActivityReconciliationSource` lives in a separate `Elsa.Activities.Composition.Design` project (which may reference `Design.Core`); the two share `WorkflowIdentity` via `Elsa.Workflows.Primitives` (no feature → feature reference). In this unit `WorkflowDefinitionActivity` is **construct-only**; its execution body (load-and-run the referenced workflow version) is deferred to the consumer/pinning unit.

**Reconciliation model**

- **FR-014**: The reconciliation model (`ActivityVersionReconciliationModel`) already carries `string` + `object` for the descriptor and MUST require no shape change; the producer sets the `DescriptorType` (derived from the descriptor object's type) and the descriptor object whose serialization becomes the persisted payload. The universal reconciling handler MUST contain no per-descriptor-type branch.

### Key Entities

- **DescriptorType**: The `FullName` of a descriptor's CLR type; the single discriminator and registry key for construction. Persisted; derived, never authored.
- **Descriptor payload**: The opaque serialized state of a descriptor (e.g. a `TypeInformation`, or a workflow version id). Persisted as a string with a `JsonElement` view; deserialized only by the owning runtime constructor.
- **`IActivityConstructor` / `IActivityConstructor<TDescriptor>`**: The per-feature construction contributor; owns deserialization, type resolution, activation, and argument binding for one descriptor type.
- **`IActivityConstructorRegistry`**: The runtime map `DescriptorType → constructor`, enforcing uniqueness.
- **`IActivityFactory`**: The single dispatch/orchestration entry point for construction.
- **`TypeInformation`**: The CLR activity descriptor (pre-existing, in `Elsa.Primitives`).
- **`WorkflowIdentity`**: The Workflow kind's descriptor — a lightweight `(DefinitionId, VersionId, Version)` model in `Elsa.Workflows.Primitives` (shared building-block lib). `Version` is the SemVer 2.0.0 string for the workflow definition version; `VersionId` is the durable row id used to load it; `DefinitionId` is the stable definition identity.
- **`WorkflowDefinitionActivity`**: The single runtime backing activity type for all workflow-backed activities (construct-only in this unit; execution body deferred).

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: `Elsa.Activities.Runtime.Core` and the runtime construction seam build and pass their tests with **zero** project references to any `Elsa.*.Design.*` assembly (verifiable by reference inspection and by building without the design assemblies).
- **SC-002**: A repository-wide search for `IImplementationDescriptor`, `ClrImplementationDescriptor`, the design-side descriptor registry/source, and the runtime resolver registry/resolver returns **zero** production-code references after the change.
- **SC-003**: Both the CLR and Workflow kinds complete the persist → reload → construct round-trip producing a fully-wired `IActivity`, with **no** `Kind` string consulted at any step.
- **SC-004**: Adding a new descriptor kind requires changes to **only** new per-feature types (descriptor + constructor + reconciliation source) and **zero** lines changed in the factory, the registry, the design domain, or the reconciling handler (demonstrated by the seam-walk and a structural no-branch check).
- **SC-005**: A duplicate constructor registration for one descriptor type fails at registry build time with a domain exception 100% of the time (no silent override path exists).
- **SC-006**: No feature project references another feature project (verifiable by reference inspection across `Primitives`, `Composition`, and `Reconciliation.Clr`).

## Assumptions

- **Type-based discrimination is an accepted persistence coupling.** Persisting `DescriptorType` as a `FullName` couples rows to the descriptor type's namespace+type name; renaming/moving a descriptor type is therefore a deliberate data migration. This is accepted because `TypeInformation` already pins type identity in persisted data for activities — it is an extension of an existing accepted coupling, not a new category. `FullName` (not assembly-qualified) is used so assembly version bumps do not break rows.
- **`Elsa.Activities.Primitives` is universally present.** CLR construction is treated as the "primitive" default that every workflow author uses; there is no separate CLR-only joining module. A host that genuinely wants no primitive activities must supply its own constructor for the `TypeInformation` descriptor type.
- **The host provides a DI activation utility** for activating a resolved CLR type with constructor injection.
- **The execution/graph-materialization path supplies the `(DescriptorType, payload, inputs, outputs)` inputs** to the factory; producing that materialized form from stored nodes is existing/adjacent machinery, not introduced here.
- **The state→argument translation** (turning a stored input/output state into an `InputArgument`/`OutputArgument`) is kind-agnostic and upstream of the constructor; this spec assumes the factory receives already-translated argument dictionaries.
- **No data migration; the catalog is rebuilt from reconciliation.** This is a pre-release refactor: existing persisted catalog rows are not migrated. The existing EF initial migration is deleted and re-created as a fresh initial migration reflecting the `(DescriptorType, payload)` shape (drop `ImplementationKind`); rows regenerate from the reconciliation sources.

## Out of Scope

- **No `ActivityCreated` (or any post-construction lifecycle) event** is introduced — there is no consumer (YAGNI). Construction is atomic. A contributor seam, if ever needed, is a separate, constitutionally-gated change.
- **No `IConditionalEventHandler` / event-pipeline pattern** is introduced (it was considered and dropped as redundant with type-based dispatch and as an ungated framework-level pattern).
- **Wiring the factory into the live execution / graph-materialization path** is out of scope. Unit 006 delivers the construction seam (`IActivityFactory`, `IActivityConstructorRegistry`, the CLR + Workflow constructors) and its own unit/integration tests; integrating it into the running executor is a later unit.
- **`WorkflowDefinitionActivity`'s execution body** (actually loading and running the referenced workflow version) is out of scope — 006 requires only correct *construction* (FR-013).
- **The consumer/pinning side of workflow-as-activity** (binding one catalog row to another, cycle/self-reference runtime guards) remains out of scope, as in 005.
- **Cross-feature duplicate-implementation policy** (whether two enabled features registering the same service should error or silently replace) is noted as a future framework concern, not decided here.

## Constitutional Compliance

This spec is implemented against the two-layer constitution at `.specify/memory/constitution.md` (Elsa) and `.specify/memory/constitution-framework.md` (framework). Constitutional compliance is enforced at the plan stage via the *Constitution Check* gates in `plan-template.md` — not duplicated here.

Originating constitutional notes for this unit:

- **New framework rule proposed (record in-unit per the constitution's amendment-in-unit convention):** *A `*.Core` library MUST NOT be treated as a bucket for every interface produced while refactoring or creating features.* A core library is mainly for **contributor interfaces** and **replacement interfaces** (the §2.6/§2.6.1 contribution surface and the inheritance-replacement seams) — plus shared models and value objects. An interface that is **neither** a contributor nor a replacement contract — i.e. used only within a single feature's own implementation — MUST stay in that feature, so core does not accumulate interfaces that don't belong there. Such an interface graduates to core only when a genuine second cross-feature consumer (a real contributor/replacement need) appears. This governs the placement of the argument-binding helper (FR-011) and should be added to the framework constitution's §2 module-decomposition rules. (Corollary observed, not yet ruled on: such interfaces are frequently "replaced" by inheritance and implemented per-feature.)
- **§E2.2 (Runtime.* MUST NOT depend on Design.*)** is the central invariant this unit restores; FR-002, FR-005–FR-008, FR-013, and SC-001/SC-002 are its enforcement.
- **§2.20 (provider module decomposition / no premature umbrella):** the CLR kind deliberately does **not** get its own joining module; it lives in `Elsa.Activities.Primitives`. Confirm this is consistent with §2.20 at plan stage.
- **No new sanctioned pattern (§2.24.2/§2.24.3) is introduced** — the previously-considered event-handler pattern was dropped, so no §2.24.3 gate is required for this unit.

### Plan-stage risks to investigate (not resolved in this spec)

- The persisted `ActivityDefinitionVersion` entity is the likely last holder of `IImplementationDescriptor`; confirm and re-shape it to `(DescriptorType string + JsonElement)` per FR-001/FR-003.
- Run a full cross-project sweep (including the workflows and API projects) for stray references to every deleted type before declaring SC-002 met.
