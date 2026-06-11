# Feature Specification: Workflow-as-Activity (Generalized Specialized-Activity Kind)

> **⚠️ SUPERSEDED by [`006-activity-construction-seam`](../006-activity-construction-seam/spec.md) (2026-06-05). Historical record only — do not implement as written.**
>
> This spec's *construction mechanism* was rejected on attempted implementation and re-designed in 006. Specifically rejected: (a) the `IImplementationDescriptor` interface in `Elsa.Activities.Design.Core`, which forced `Elsa.Activities.Runtime.Core → Elsa.Activities.Design.Core` (violates Elsa §E2.2); (b) the `Kind` string discriminator (an invented 3-place coupling — replaced by descriptor-**type** discrimination); (c) the two registries (the design-side descriptor-type registry is deleted; only a runtime constructor registry remains); (d) `ClrImplementationDescriptor` (deleted — `Elsa.Primitives.Models.TypeInformation` *is* the CLR descriptor).
>
> The **producer-side goals** of this spec (US1–US4: marked workflows become version-distinct catalog rows with mirrored I/O; one well-known backing type; generalizable to future kinds) are **retained** and re-expressed against the new seam in 006. Read 006 for the design that is actually implemented; read this for the workflow-as-activity *intent* and producer requirements.
>
> **2026-06-11 design-domain cleanup:** catalog `Ports` were replaced by module-owned `DesignFacets`. This historical spec must not be read as requiring workflow-as-activity to publish outcome ports into the core catalog. Flowchart/outcome visualization belongs to the owning activity/designer module as a facet payload.

**Feature Branch**: `005-workflow-as-activity` *(spec authored on `main`; no feature branch cut — consistent with units 001–004)*
**Created**: 2026-06-04
**Status**: Superseded by 006
**Input**: Unit 4 of the Elsa entity-design refactor. Let an author mark a workflow definition "usable as an activity" so it surfaces in the activity-version catalog as real `ActivityDefinitionVersion` records that, when executed, run the referenced workflow. **Producer side only.** Adds the `Workflow` implementation kind as a second instance of the *specialized activity* shape (an activity whose behaviour derives from a design-time artifact, not a hand-written CLR type), and is designed so a future `Dynamic`/`OpenApi` kind plugs into the very same four seams with zero changes to the universal reconciling handler, the registries, or the factory. Extends Unit B (`specs/001-activity-identity-catalog`); builds on the implementation-kind/descriptor mechanism and string semver (`specs/004-activity-semantic-versioning`). Works against constitution v3.0.0 (draft).

## Clarifications

### Session 2026-06-04

- Q: Where does the "usable as activity" marking live — a new flag/column on the workflow definition version, or existing authored content? → A: **Resolved — it already exists.** `WorkflowDefinitionState.WorkflowActivityOptions.UsableAsActivity` (a `bool?` on the `WorkflowActivityOptions` record) is the marking, and it is authored content inside State (§E2.9). Co-resident author-authored UI metadata also already lives there / on the definition: `WorkflowActivityOptions.ActivityCategory` (→ `Category`), `WorkflowDefinition.Name` (→ `DisplayName`), `WorkflowDefinition.Description` (→ `Description`), and `WorkflowDefinitionState.Inputs` / `.Outputs` (→ the activity's inputs/outputs). **No new flag/column is introduced on `WorkflowDefinitionVersion`.** The migration concern in the input ("no data migration for a new column") is therefore moot — there is no new column. (See FR-013, FR-014.)
- Q: Workflow versions are `int` (`WorkflowDefinitionVersion.Version`); the activity catalog version is a SemVer 2.0.0 **string** (Unit 3). How is the catalog row's `Version` derived? → A: **Resolved — map `int n` → `"n.0.0"`**, reusing the exact convention Unit 3 adopted for the Elsa-3 import adapter (`specs/004` FR-007). The workflow's `int` version is the source of truth; the mapping is deterministic and idempotent. A per-workflow author-supplied semver string for workflow versions is **out of scope** (it would be a workflow-versioning change, not a workflow-as-activity change). (See FR-005, Edge Cases.)
- Q: What is the descriptor payload — a synthetic "workflow version id", or the existing per-version row identity? → A: **Resolved — the workflow definition version's durable row `Id`** (`WorkflowDefinitionVersion` inherits `Entity.Id`). That single string is the design-time artifact reference the descriptor carries; the runtime backing activity loads-and-executes the workflow version by that id. No new identifier is invented. (See FR-001, FR-008.)
- Q: Does the reconciler need cross-source ordering guarantees because workflow-backed activities may reference other (possibly workflow-backed) activities? → A: **Resolved — no.** `IActivityReconciliationSource` has no `Order` member; the JSON source's ascending `Order` is *intra-source* file staging, not a cross-source contract. Reconciliation is per-`(SourceKind, SourceId, ActivityTypeKey, Version)` idempotent (Model X find-or-create/append/hash-mismatch). A workflow-backed row referencing another catalog row is a *consumer-side binding* concern (the later pinning unit), not a producer-side reconciliation-ordering concern. Catalog rows are independent records; producing one does not require the referenced one to pre-exist. (See FR-010, Edge Cases.)
- Q: Is cycle / self-reference detection (a usable-as-activity workflow that transitively references itself) in scope? → A: **Deferred — flagged, not built.** Producing catalog rows for a self-referential workflow does not break reconciliation (each version is an independent row; no row materializes another at reconcile time). The infinite-recursion hazard is a *runtime execution* concern that manifests only when a consumer pins and runs such an activity — i.e. in the consumer/pinning unit and the workflow engine, both out of scope here. Unit 4 records the hazard as a known validation gap for the pinning unit. (See FR-016, Edge Cases.)

## User Scenarios & Testing *(mandatory)*

### User Story 1 — An author exposes a workflow as a catalog activity (Priority: P1)

A workflow author builds a workflow (`Approve Expense`), declares its inputs and outputs, and toggles "usable as activity" on the definition. When the host reconciles its activity catalog, the workflow appears in the activity-version catalog as a real `ActivityDefinitionVersion` row — picker-visible, with the author's display name, category, description, and the workflow's inputs/outputs surfaced as the activity's inputs/outputs — exactly as a CLR activity or a JSON-sourced activity would. The author did nothing activity-specific beyond the marking and the I/O they already declared.

**Why this priority**: This is the load-bearing reason Unit 4 exists. Unit B (US3) proved the catalog *schema* can hold a non-CLR `ImplementationKind = Workflow` row; Unit 4 ships the producer that actually creates those rows from real workflow definitions. Without it the `Workflow` kind is theoretical. Every other story is downstream of "a marked workflow becomes a catalog row".

**Independent Test**: Persist a workflow definition version whose `State.WorkflowActivityOptions.UsableAsActivity = true`, with declared `State.Inputs`/`State.Outputs`; run reconciliation; assert a catalog `ActivityDefinitionVersion` exists with `ImplementationKind = "Workflow"`, a `WorkflowImplementationDescriptor` carrying the version's row id, `DisplayName`/`Category`/`Description` populated from the authored data, and the workflow's inputs/outputs mirrored onto the activity row.

**Acceptance Scenarios**:

1. **Given** a workflow definition version with `UsableAsActivity = true`, **When** reconciliation runs, **Then** a catalog row is created with `SourceKind = "Workflow"`, `ImplementationKind = "Workflow"`, and `ActivityTypeKey` derived from the workflow definition identity.
2. **Given** the same workflow's authored UI metadata (definition name, description, `ActivityCategory`), **When** the row is produced, **Then** `DisplayName`, `Description`, and `Category` are populated from that authored data (contrast the CLR source, which leaves UI metadata null).
3. **Given** the workflow declares inputs `{amount, approver}` and outputs `{decision}`, **When** the row is produced, **Then** the activity row's `Inputs` and `Outputs` mirror them so a future consumer can bind to the shape. Workflow outcomes are not core catalog ports; any visual outcome representation belongs to an owning activity/designer module facet.
4. **Given** a workflow definition version whose `UsableAsActivity` is `false`, `null`, or absent, **When** reconciliation runs, **Then** **no** catalog row is produced for it.

---

### User Story 2 — Each workflow version is its own pinnable catalog row (Priority: P1)

A workflow author publishes `Approve Expense` v1, later v2, later v3 — each still marked usable-as-activity. The catalog holds three version-distinct `ActivityDefinitionVersion` rows under one `ActivityDefinition`, each independently resolvable by `(DefinitionId, Version)`. This is the exact-lookup seam Unit 3 (US4) guaranteed; Unit 4 proves a workflow-backed definition actually populates multiple rows so the later pinning unit has real, pinnable versions to select among.

**Why this priority**: The producer/consumer boundary is the whole reason Unit 4 is "producer side only". Unit 4's contract to the downstream pinning unit is precisely *"the catalog holds multiple, version-distinct, queryable-by-exact-version rows per workflow-backed definition, and that shape needs no further model change to be pinnable."* If a workflow collapsed to a single mutable row, pinning would be impossible.

**Independent Test**: Persist three versions of one usable-as-activity workflow; reconcile; assert three `ActivityDefinitionVersion` rows exist under one `ActivityDefinition`; resolve each by `(DefinitionId, exact Version string)` and get exactly that record; resolve a non-existent version and get none.

**Acceptance Scenarios**:

1. **Given** workflow versions `1`, `2`, `3` (all usable-as-activity), **When** reconciliation runs, **Then** catalog rows `"1.0.0"`, `"2.0.0"`, `"3.0.0"` exist under one `ActivityDefinition` (one row per workflow version, record identity preserved).
2. **Given** those rows, **When** resolved by `(DefinitionId, "2.0.0")`, **Then** the v2 record returns; **When** resolved by `(DefinitionId, "9.0.0")`, **Then** no record matches (exact, not nearest).
3. **Given** an unchanged workflow, **When** reconciliation re-runs, **Then** no new rows are appended (idempotent — existing `(DefinitionId, Version)` observed and skipped).
4. **Given** the multi-version rows produced here, **When** the later pinning unit selects a pinned version, **Then** no additional catalog-model change is required (the shape is already pinnable).

---

### User Story 3 — A workflow-backed activity executes through one well-known backing type (Priority: P1)

When a workflow-backed activity is instantiated at runtime, the activity factory activates a **single, well-known CLR type** (`WorkflowDefinitionActivity`) for *every* workflow-backed activity — the specific workflow to run is selected by the workflow definition version id carried in the descriptor and applied as pre-filled activity state. The factory does not need a distinct CLR type per workflow; the descriptor's payload selects the workflow.

**Why this priority**: This is the runtime half of the producer side. Cataloguing a workflow as an activity is worthless if the runtime cannot turn a `WorkflowImplementationDescriptor` into a runnable activity. P1 because it closes the descriptor → resolver → `IActivity` round trip (the analogue of Unit B's SC for the CLR seed), and it is where the §E2.2 Runtime/Design boundary is most at risk.

**Independent Test**: Construct a `WorkflowImplementationDescriptor` carrying a workflow version id; route it through `IActivityFactory.Create`; assert the produced `IActivity` is a `WorkflowDefinitionActivity` whose state carries the workflow version id (the factory's "activate type + apply state" split is honoured — the resolver only returned the `Type`).

**Acceptance Scenarios**:

1. **Given** a `WorkflowImplementationDescriptor` with version id `W`, **When** the factory creates the activity, **Then** the resolver returns `typeof(WorkflowDefinitionActivity)` (Kind `"Workflow"`) and the factory activates it with `W` applied as pre-filled state.
2. **Given** two descriptors with different version ids, **When** each is created, **Then** both produce a `WorkflowDefinitionActivity` instance, differing only by the applied version id (one backing type, many workflows).
3. **Given** the runtime resolver + backing activity type, **When** their project references are inspected, **Then** neither references any `Elsa.*.Design.*` project (§E2.2 holds).

---

### User Story 4 — The abstraction generalizes to a future Dynamic/OpenApi kind (Priority: P2)

An architect reviewing Unit 4 must be able to walk the four seams — descriptor type, design-side descriptor-registry source, runtime resolver + resolver source, design-side reconciliation source — and confirm that adding a hypothetical `Dynamic`/`OpenApi` kind (descriptor payload = an OpenAPI operation reference) requires only *new* per-kind types plugged into those seams, with **zero** edits to the universal `ActivityVersionsReconcilingHandler`, the `IImplementationDescriptorRegistry`, the `IActivityImplementationResolverRegistry`, or `IActivityFactory`.

**Why this priority**: The framing claim of Unit 4 is that workflow-as-activity is one instance of a *generalized* specialized-activity shape. If adding the `Workflow` kind quietly required a special-case in any universal component, the generalization is false and that special-case is a defect to fix now, before a second kind inherits it. P2 because it is a design-validation guarantee rather than a shipped runtime capability.

**Independent Test**: A documented seam-walk (in the spec / plan) plus a structural test: after the `Workflow` kind is added, the universal handler, both registries, and the factory contain no `if (kind == "Workflow")`-style branch — every kind flows through the same registration/resolution path. The same walk is repeated on paper for `Dynamic` and shown to touch only new types.

**Acceptance Scenarios**:

1. **Given** the `Workflow` kind fully wired, **When** the universal reconciling handler is inspected, **Then** it contains no kind-specific branch — it resolves the descriptor type via the registry and contributes rows uniformly.
2. **Given** a hypothetical `Dynamic` kind, **When** the four-seam walk is applied, **Then** it introduces only a `DynamicImplementationDescriptor`, a descriptor-registry source, a runtime resolver + resolver source, and a reconciliation source — and changes none of the universal components.
3. **Given** any seam that *would* require a `Workflow`/`Dynamic` special-case, **When** it is found, **Then** it is recorded as a flagged risk to resolve in this unit (not deferred).

---

### Edge Cases

- **A workflow is marked usable-as-activity but declares no inputs/outputs.** A valid catalog row is still produced with empty `Inputs`/`Outputs`/`DesignFacets`. The marking, not the I/O, gates row production.
- **The marking is toggled off after rows already exist.** The Workflow source stops contributing rows for that definition; stale-row removal is the reconciler's existing operational concern (Unit B `ActivityDefinitionReconciliationState.RemovedAt` / stale sweep), not a new mechanism here.
- **A workflow's `int` version is `0` or negative.** The `int n → "n.0.0"` mapping is applied verbatim (`0 → "0.0.0"`). Workflow versions are system-assigned positive integers in practice; no invented normalization beyond the documented mapping.
- **Two workflow versions map to the same catalog version string.** Cannot occur: distinct `int` workflow versions map to distinct `"n.0.0"` strings, and `(DefinitionId, Version)` identity is preserved per row (Unit B/Unit 3 Model X).
- **A workflow content change without a version bump.** Inherits Unit 3's Model X safety net: same `(DefinitionId, Version)` with a different content hash throws `ActivityVersionHashMismatchException`. In practice workflow publication assigns a new `int` version, so this is the rare author-error guard, re-pointed at workflow-sourced rows.
- **An unknown `ImplementationKind` is loaded** (e.g. a `Workflow` row read by a process that did not register the Workflow descriptor type / resolver). Per Unit B: the row still catalogues and reads; only *execution* fails through a runtime/domain path. Unit 4 introduces no new failure mode here.
- **A usable-as-activity workflow that (transitively) references itself.** Producing catalog rows is safe (independent records; no reconcile-time materialization of the referenced workflow). The infinite-recursion hazard is a *runtime execution* concern realized only when a consumer pins-and-runs it — deferred to the consumer/pinning unit + the workflow engine. Recorded as a known validation gap (FR-016).
- **A workflow-backed activity references another (workflow-backed) activity.** No reconciliation-ordering requirement: each catalog row is independent; producing one does not require the referenced row to pre-exist. Binding to the referenced row is the consumer/pinning unit's concern.

## Requirements *(mandatory)*

### Functional Requirements

**The `Workflow` implementation kind & its descriptor**

- **FR-001**: A `WorkflowImplementationDescriptor` MUST be defined implementing `IImplementationDescriptor` (mirroring `ClrImplementationDescriptor`), with `Kind => "Workflow"` (a `const KindValue = "Workflow"`). Its payload MUST be the referenced **workflow definition version's durable row id** (`WorkflowDefinitionVersion.Id`) — the single design-time artifact reference, analogous to how `ClrImplementationDescriptor` wraps `TypeInformation`. It MUST round-trip through the catalog store's polymorphic descriptor column unchanged (the Unit B serialize/deserialize path).
- **FR-002**: The `WorkflowImplementationDescriptor` MUST live **Design-side** (the descriptor is design-time artifact data, like `ClrImplementationDescriptor` which lives in `Elsa.Activities.Design.Core`). It MUST NOT carry a live workflow object or any runtime handle — only the durable id string.
- **FR-003**: The `(Kind = "Workflow" → typeof(WorkflowImplementationDescriptor))` mapping MUST be contributed to the design-side `IImplementationDescriptorRegistry` via a new `IImplementationDescriptorSource` implementation (e.g. `WorkflowImplementationDescriptorSource`), registered in DI, exactly like `ClrImplementationDescriptorSource`. The universal `RegisterImplementationDescriptors` handler and the registry MUST NOT change.
- **FR-004**: The kind string `"Workflow"` MUST be owned by this module and agree across the three places the CLR seed established the convention: the descriptor's `KindValue`, the resolver's `Kind`, and the descriptor-source registration. No central enum edit is required (smart-enum string value per Unit B FR-006).

**The Workflow reconciliation source (producer)**

- **FR-005**: A new `IActivityReconciliationSource` implementation (`WorkflowActivityReconciliationSource`) with `SourceKind => "Workflow"` MUST be introduced. Its `Read()` MUST enumerate workflow definition versions whose `State.WorkflowActivityOptions.UsableAsActivity == true` and contribute **one `ActivityVersionReconciliationModel` per workflow definition version** (record-level identity preserved). The model's `Version` MUST be the workflow `int` version mapped to a SemVer 2.0.0 string via `n → "n.0.0"` (FR-013); its `ImplementationKind` MUST be `"Workflow"`; its `ImplementationDescriptor` MUST be a `WorkflowImplementationDescriptor` carrying that version's row id.
- **FR-006**: The Workflow source MUST populate the row's author-authored UI metadata from the authored data — `DisplayName` from `WorkflowDefinition.Name`, `Description` from `WorkflowDefinition.Description`, `Category` from `WorkflowActivityOptions.ActivityCategory` — i.e. **no null-UI-metadata restriction** (contrast the CLR source). It MUST surface the workflow's `State.Inputs` as the activity row's `Inputs` and `State.Outputs` as `Outputs`. It MUST NOT publish workflow outcomes as core catalog ports; any future outcome/port visualization belongs to the owning activity/designer module as a `DesignFacets` payload.
- **FR-007**: The Workflow source MUST plug into the existing universal `ActivityVersionsReconcilingHandler` via DI (`IEnumerable<IActivityReconciliationSource>`) exactly like the CLR and JSON sources. The handler MUST resolve the polymorphic descriptor via the registry **unchanged** — no `Workflow`-specific branch in the handler, reconciler, or hasher. `SourceId` MUST be a stable identifier for the workflow-source instance.
- **FR-008**: The `ActivityTypeKey` for a workflow-backed row MUST be derived from the **workflow definition identity** (`WorkflowDefinitionVersion.DefinitionId`), stable across the workflow's versions (so all versions of one workflow share one `ActivityDefinition`, mirroring how a CLR type's full name is the stable key across its semver rows). The exact key form (e.g. a `Workflow:{DefinitionId}` qualified key) is a plan-stage detail; it MUST exclude the per-version id (which lives in the descriptor).

**The runtime resolver & backing activity (execution)**

- **FR-009**: A runtime-side `IActivityImplementationResolver<WorkflowImplementationDescriptor>` (`Kind => "Workflow"`) MUST be introduced (mirroring `ClrActivityImplementationResolver`) whose `Resolve(descriptor)` returns the single well-known CLR type `typeof(WorkflowDefinitionActivity)` for **every** workflow-backed activity. It MUST be contributed to the runtime-side registry via a new `IActivityImplementationResolverSource` (e.g. `WorkflowActivityImplementationResolverSource`), registered in DI exactly like `ClrActivityImplementationResolverSource`. The universal `RegisterActivityImplementationResolvers` handler, the resolver registry, and `IActivityFactory` MUST NOT change.
- **FR-010**: A new CLR activity type `WorkflowDefinitionActivity` MUST be introduced that, when executed, loads and runs the referenced workflow definition version. It MUST live **Runtime-side** and MUST NOT introduce a `Runtime → Design` dependency (§E2.2). It bridges the activity-runtime and the workflow-runtime; both are Runtime-side, so the bridge is within the allowed direction. The exact host project MUST be named in the plan (working direction: `Elsa.Activities.Runtime` for the resolver + source, with `WorkflowDefinitionActivity` sited so it can reference the workflow-runtime execution surface without pulling any `Design` project — see Constitutional Compliance).
- **FR-011**: The workflow definition version id MUST be injected into the activated `WorkflowDefinitionActivity` as **pre-filled activity state applied by the factory** — NOT via a constructor argument and NOT as a descriptor-carried default input expression. Justification: the existing `IActivityFactory` contract is "resolver returns a `Type`; factory activates the type and applies argument state" (Unit B FR-028). The version id is exactly such applied state — it is the activity's configuration, set the same way every other activity's inputs are set — so it rides the existing "activate type + apply state" split with no new factory capability. The resolver remains pure (returns only the `Type`).
- **FR-012**: One CLR backing type MUST serve all workflow-backed activities (the descriptor's version-id payload, not the CLR type, selects which workflow runs). No per-workflow CLR type is generated.

**Marking, model & migration**

- **FR-013**: The "usable as activity" marking MUST be read from the **existing** `WorkflowDefinitionState.WorkflowActivityOptions.UsableAsActivity` (`bool?`) — authored content already present in State (§E2.9). **No new flag, column, or property** MUST be added to `WorkflowDefinitionVersion` or `WorkflowDefinition` for the marking. The `int → "n.0.0"` version mapping MUST be a deterministic, idempotent function reused from Unit 3's Elsa-3 import convention (`specs/004` FR-007); it MUST NOT invent, increment, or reinterpret the workflow's version beyond that mapping.
- **FR-014**: Because no new column is introduced (FR-013), **no data migration** is required for the marking. If any plan-stage detail nonetheless touches the activities-design schema, a fresh SQLite migration MUST replace the initial migration (Unit B "no preserved production data" convention) — but the expectation is **zero** schema change on the workflow side and at most reuse of Unit B/Unit 3's already-regenerated activities-design schema on the catalog side.

**Producer/consumer boundary & deferrals**

- **FR-015**: This unit MUST guarantee the catalog holds multiple version-distinct, exact-lookup-by-`(DefinitionId, Version)` rows per workflow-backed definition (Unit 3 US4 path serving a real pin) and MUST confirm in the plan that this shape needs **no further catalog-model change** to be pinnable. It MUST NOT build version **selection**, pin **storage** on the consuming side, or **"empty ⇒ latest"** resolution — all belong to the dedicated activity-version-pinning unit. It MUST NOT build the workflow-execution semantics beyond resolving + activating the backing activity (the workflow engine already executes).
- **FR-016**: Cycle / self-reference detection (a usable-as-activity workflow that transitively references itself) MUST be **flagged as a known validation gap**, not built — it is a runtime-execution hazard realized only by the consumer/pinning unit + the workflow engine, both out of scope. The flag MUST be recorded in the Unit 4 follow-up file (FR-019) for the pinning unit to pick up.
- **FR-017**: This unit MUST NOT build the `Dynamic`/`OpenApi` provider; only the abstraction MUST be **shown** to accommodate it (FR-018). It MUST NOT build CLR multi-version assembly-load-context loading.

**Generalization proof, tests, docs & constitution (in-unit)**

- **FR-018**: The spec/plan MUST contain a **four-seam generalization walk** for a hypothetical `Dynamic`/`OpenApi` kind (descriptor payload = an OpenAPI operation reference) demonstrating that adding it touches only new per-kind types at (1) descriptor type, (2) design-side descriptor-registry source, (3) runtime resolver + resolver source, (4) design-side reconciliation source — and changes none of: the universal `ActivityVersionsReconcilingHandler`, `IImplementationDescriptorRegistry`, `IActivityImplementationResolverRegistry`, `IActivityFactory`, the reconciler, or the hasher. Any seam requiring a kind-specific special-case MUST be surfaced as a flagged risk and resolved in this unit, not deferred.
- **FR-019**: Every new/reshaped feature class MUST carry a framework §2.23.1 registration test; every logic-bearing new class (the Workflow reconciliation source, the Workflow resolver, the descriptor source, the `WorkflowDefinitionActivity`) MUST carry §2.23.2 branch-covered unit tests; a test MUST round-trip a `WorkflowImplementationDescriptor` through the catalog store (extends Unit B FR-021); a structural test MUST assert the universal handler/registries/factory contain no `Workflow`-specific branch (FR-018). Existing tests on refactored code MUST pass unchanged (framework §2.21.1 golden rule). A Unit 4 follow-up file (`epic1-elsa-refactor-constitution/follow-up-items/2026-06-04_unit_workflow_as_activity.md`) MUST be created/updated, and `PERSONAL_TODO.md` updated to reflect Unit 4 status.
- **FR-020**: The constitution MUST be updated **in-unit**: Elsa **§E2.8** (and the activity / implementation-kind wording) MUST add the `"Workflow"` kind alongside `"Clr"` and MUST state the **specialized-activity generalization** — an implementation kind whose descriptor references a design-time artifact (workflow definition version today; an OpenAPI operation tomorrow), resolved at runtime to a backing CLR type with the artifact reference applied as state. Any reconciliation-source wording that enumerates the known `SourceKind`s MUST add `"Workflow"`. **§E2.2** MUST be reaffirmed for the Runtime-side backing activity + resolver (descriptor + reconciliation source are Design-side; resolver + backing activity are Runtime-side; no Runtime→Design edge).

### Key Entities

- **`WorkflowImplementationDescriptor`** *(new, Design-side — `Elsa.Activities.Design.Core`)* — `IImplementationDescriptor` with `Kind = "Workflow"`, payload = the referenced `WorkflowDefinitionVersion.Id`. The `Workflow`-kind analogue of `ClrImplementationDescriptor`.
- **`WorkflowImplementationDescriptorSource`** *(new, `IImplementationDescriptorSource`)* — contributes `(Kind "Workflow" → typeof(WorkflowImplementationDescriptor))` to the design-side descriptor registry via DI; analogue of `ClrImplementationDescriptorSource`.
- **`WorkflowActivityReconciliationSource`** *(new, `IActivityReconciliationSource`, `SourceKind = "Workflow"`)* — reads usable-as-activity workflow definition versions and contributes one `ActivityVersionReconciliationModel` per version, with full author-authored UI metadata and the workflow's I/O surfaced as the activity's inputs/outputs. The producer.
- **`WorkflowActivityImplementationResolver`** *(new, Runtime-side, `IActivityImplementationResolver<WorkflowImplementationDescriptor>`, `Kind "Workflow"`)* — returns `typeof(WorkflowDefinitionActivity)` for every workflow-backed descriptor; analogue of `ClrActivityImplementationResolver`.
- **`WorkflowActivityImplementationResolverSource`** *(new, `IActivityImplementationResolverSource`)* — contributes the resolver to the runtime resolver registry via DI; analogue of `ClrActivityImplementationResolverSource`.
- **`WorkflowDefinitionActivity`** *(new, Runtime-side, CLR `IActivity`)* — the single well-known backing type for **all** workflow-backed activities; loads + runs the referenced workflow definition version (version id applied as state by the factory). Bridges activity-runtime ↔ workflow-runtime, Runtime-side only.
- **`WorkflowActivityOptions.UsableAsActivity`** *(existing, reused — `Elsa.Workflows.Design.Core.Models`)* — the `bool?` marking inside `WorkflowDefinitionState`; co-resident `ActivityCategory` feeds the row's `Category`. Outcomes no longer feed a core catalog port model. No model change.
- **`ActivityVersionReconciliationModel`** *(existing, reused — `Elsa.Activities.Design.Reconciliation.Core.Models`)* — the contribution model; its `ImplementationDescriptor` is `object`, so a `WorkflowImplementationDescriptor` slots in with `ImplementationKind = "Workflow"` and no model change.
- **`WorkflowDefinitionVersion` / `WorkflowDefinition`** *(existing, read-only here)* — the artifact the Workflow source reads: `int Version` (→ `"n.0.0"`), `Id` (→ descriptor payload), `DefinitionId` (→ `ActivityTypeKey`), `Name`/`Description`/`State` (→ UI metadata + I/O). No new column.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of workflow definition versions marked `UsableAsActivity = true` produce exactly one picker-visible catalog `ActivityDefinitionVersion` row per workflow version on reconciliation; 100% of unmarked versions produce none — verified by a test covering both branches.
- **SC-002**: A workflow with N marked versions yields N version-distinct catalog rows under one `ActivityDefinition`, each resolvable by `(DefinitionId, exact version string)` and a non-existent version returning none — the pinnable shape the downstream unit depends on (no further model change required).
- **SC-003**: Re-running reconciliation against unchanged workflows appends zero new rows (idempotent), proving the workflow-sourced `(DefinitionId, Version)` identity is stable.
- **SC-004**: A `WorkflowImplementationDescriptor` routed through `IActivityFactory.Create` produces a `WorkflowDefinitionActivity` whose state carries the workflow version id, for any number of distinct version ids, using one backing CLR type — verified by a test (the descriptor → resolver → `IActivity` round trip).
- **SC-005**: The universal `ActivityVersionsReconcilingHandler`, `IImplementationDescriptorRegistry`, `IActivityImplementationResolverRegistry`, and `IActivityFactory` contain **zero** `Workflow`-specific branches after the kind is added — verified by a structural test and the documented four-seam generalization walk, which also shows a `Dynamic` kind would touch only new types.
- **SC-006**: No project in the Runtime composition (resolver, resolver source, `WorkflowDefinitionActivity`) references any `Elsa.*.Design.*` project — §E2.2 verified by a reference-direction check.
- **SC-007**: All pre-existing activity-catalog, reconciliation, and runtime tests pass unchanged in subject and objective (framework §2.21.1); the build is green with no new column added to the workflow-side schema.

## Assumptions

- **The marking already exists.** `WorkflowDefinitionState.WorkflowActivityOptions.UsableAsActivity` is the opt-in; `ActivityCategory` and `Outcomes` are co-resident authored metadata. Unit 4 reads them; it does not add them. (FR-013.)
- **Workflow version is `int`; activity catalog version is string semver.** The Workflow source maps `int n → "n.0.0"`, reusing Unit 3's Elsa-3 import convention (`specs/004` FR-007). A per-workflow author-supplied semver for *workflow* versions is out of scope (a separate workflow-versioning concern).
- **Descriptor payload = the workflow definition version's durable row `Id`.** No new identifier is invented; the runtime backing activity loads-and-runs the workflow version by that id.
- **No preserved production data.** Consistent with Unit B/Unit 3; any incidental activities-design schema touch regenerates the SQLite migration fresh. Expectation: zero workflow-side schema change.
- **Pattern reuse only.** The Workflow kind plugs into the four established seams (descriptor + `IImplementationDescriptorSource`; reconciliation source; runtime resolver + `IActivityImplementationResolverSource`) exactly as the CLR seed and JSON source do. No new contribution pattern is introduced (framework §2.6.1; §2.24.2 sanctioned patterns).
- **Producer side only.** Version selection, pin storage on the consuming side, and "empty ⇒ latest" resolution are the dedicated pinning unit. Cycle/self-reference detection and the `Dynamic`/OpenAPI provider are out of scope (flagged, not built).
- **The workflow engine already executes.** `WorkflowDefinitionActivity` resolves + activates + delegates to the existing workflow-runtime execution surface; Unit 4 does not implement workflow-execution semantics.

## Constitutional Compliance

This spec is implemented against the two-layer constitution at `.specify/memory/constitution.md` (Elsa) and `.specify/memory/constitution-framework.md` (framework). Compliance is enforced at the plan stage via the *Constitution Check* gates — not duplicated here. Spec-level constitutional notes / flags:

- **§E2.8 amended in-unit** (FR-020): adds the `"Workflow"` implementation kind alongside `"Clr"` and codifies the **specialized-activity generalization** (an implementation kind whose descriptor references a design-time artifact). This is an extension of Unit B's polymorphic-descriptor model and Unit 3's catalog-versioning model, not a new structural pattern.
- **§E2.2 reaffirmed in-unit** (FR-010, FR-020) — **load-bearing flag.** `WorkflowDefinitionActivity` + the resolver + the resolver source are **Runtime-side**; the descriptor + descriptor source + reconciliation source are **Design-side**. The backing activity bridges activity-runtime ↔ workflow-runtime (both Runtime), which is *within* the allowed direction. **The plan MUST name the exact host project for `WorkflowDefinitionActivity` and confirm by reference-direction check (SC-006) that it pulls no `Design` project.** Note the resolver legitimately references the Design-side descriptor type (`ClrActivityImplementationResolver` already references `ClrImplementationDescriptor` from `Elsa.Activities.Design.Core` — Design-consumed-by-Runtime is the allowed §E2.2 direction). If the only natural home for the backing activity would instead force a Runtime→Design edge, that is a flagged risk to resolve in the plan.
- **Sanctioned-patterns check (framework §2.24.2 / §2.24.3 gate).** All four seams reuse the sanctioned §2.6.1 DI-source/contributor pattern already used by the CLR seed and JSON source. The `int → "n.0.0"` mapping is a deterministic function, not a pattern. **No §2.24.3 gate is triggered.**
- **Generalization is a design constraint, not just prose** (FR-018, SC-005): the abstraction's validity is asserted by a structural test (no kind-specific branch in universal components) plus a paper walk for `Dynamic`. A failed walk is an architecture-meeting escalation trigger (Definition of Done point 2), to be resolved in-unit.
