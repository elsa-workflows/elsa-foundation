# Implementation Plan: Descriptor-Type-Driven Activity Construction

**Branch**: `main` *(authored on main; no feature branch — consistent with units 001–005)* | **Date**: 2026-06-05 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `specs/006-activity-construction-seam/spec.md`

## Summary

Replace Unit 4's rejected construction machinery (a `Kind` string + `IImplementationDescriptor` in `Elsa.Activities.Design.Core` + a design-side descriptor-type registry + a runtime resolver registry + a descriptor-carrying `IActivityFactory`) with a single **descriptor-type-driven** runtime construction seam, restoring the §E2.2 invariant that `Elsa.Activities.Runtime.*` does not depend on `Elsa.Activities.Design.*`.

Technical approach: persist a descriptor as `(DescriptorType: string FullName, payload: opaque JSON)`. The design domain serializes on write and never deserializes. At runtime, `IActivityFactory` (pure dispatch) looks up the single `IActivityConstructor` registered for the `DescriptorType` in `IActivityConstructorRegistry`, and the constructor deserializes the payload into its `TDescriptor`, resolves the CLR type, activates it, and binds the author inputs/outputs — returning a whole `IActivity`. The CLR descriptor is `Elsa.Primitives.Models.TypeInformation` (no `ClrImplementationDescriptor`); the Workflow descriptor is a new lightweight `Elsa.Workflows.Primitives.Models.WorkflowIdentity` (`DefinitionId`, `VersionId`, `Version`). The registry is populated via the constitutional Registry + StartUp Task + Domain Event pattern (G21), declared entirely in `Elsa.Activities.Runtime.Core`.

Scope (per clarifications): construction seam + its own tests only. `WorkflowDefinitionActivity` is construct-only; its execution body and live-executor integration are deferred. No data migration — the EF initial migration is reset.

## Technical Context

**Language/Version**: C# / .NET 10 (`net10.0`)
**Primary Dependencies**: CShells (`CShells.Abstractions` 0.0.24) for feature/DI composition; `Microsoft.Extensions.DependencyInjection.Abstractions`; `Elsa.Events.Core` (`IEvent`/`IEventHandler`/`IEventPublisher`); `Elsa.Expressions.Core`; `Elsa.Primitives`; `Elsa.Workflows.Primitives`; `Elsa.Serialization.Core` (`IPayloadSerializer`); EF Core 10 + SQLite (design persistence).
**Storage**: Activity catalog in `Elsa.Activities.Design.Persistence.EFCore(.Sqlite)`. Descriptor persisted as two columns: `DescriptorType` (string) + `DescriptorPayloadSource` (string JSON). No data migration; initial migration regenerated.
**Testing**: xunit only. **No FluentAssertions** (constitutionally pinned). Registration tests per feature (G27); branch-covering unit tests per logic-bearing class; structural/reference tests for §E2.2 (G15) and no-feature-to-feature (SC-006).
**Target Platform**: `Elsa.Server` ASP.NET Core host; library/feature modules under `src/`.
**Project Type**: Modular feature framework (multi-project library set + host).
**Performance Goals**: N/A — construction is per-activity-node, not a hot loop measured here; correctness/architecture is the objective.
**Constraints**: `Elsa.Activities.Runtime.*` MUST NOT reference `Elsa.*.Design.*` (§E2.2/G15). No feature → feature project references (G4/§2.20, SC-006). `.Core` libraries take no heavy NuGets (G3).
**Scale/Scope**: ~9 affected projects (Runtime.Core, Runtime, new Primitives, new Composition, Design.Core, Design.Persistence.{Core,EFCore,EFCore.Sqlite}, Design.Api, Reconciliation{,.Core,.Clr,.Json}, Elsa3.Activities.Design.Import) + their test projects.

## Constitution Check

*GATE: walked before Phase 0 and re-walked after Phase 1 (below). Two-layer constitution: `.specify/memory/constitution.md` (Elsa) + `constitution-framework.md`.*

| # | Gate | Status | Note |
|---|---|---|---|
| G1 | Three-layer separation; no global Core | **PASS** | `Runtime.Core` / `Design.Core` are sub-domain cores, not a global bucket. New code respects per-feature layering. |
| G2 | Domain-language naming; no `.Contracts`/`.Abstractions`/`Features.*` | **PASS** | `IActivityFactory`, `IActivityConstructor`, `IActivityConstructorRegistry`, `WorkflowIdentity`, `Elsa.Activities.Primitives`, `Elsa.Activities.Composition`. No banned segments. |
| G3 | No heavy deps in `.Core` | **PASS** | `Runtime.Core` adds only the construction contracts + an `IEvent`; deps stay `Events.Core`/`Expressions.Core`/`Primitives`. |
| G4 | No peer refs between impl libs across sub-domains | **PASS** | `Primitives` and `Composition` never reference each other; shared types (`TypeInformation`, `WorkflowIdentity`) live in zero-dep building-block libs. `Reconciliation.Clr → Elsa.Primitives` only. (SC-006) |
| G5 | New contract declares kind (replacement/contribution) | **PASS** | `IActivityConstructor` = **contribution**; `IActivityFactory`/`IActivityConstructorRegistry` = **replacement** (single runtime service, swappable). Recorded in contracts/. |
| G7 | No `DependsOn` static feature deps | **PASS** | Fail-fast at DI/registry build (unregistered `DescriptorType` → domain failure). |
| G8 | EF generic constraint is `DbContext` | **PASS / N-A** | No new generic persistence constraints; existing `ActivitiesDesignDbContext` unchanged in this respect. |
| G15 | **Runtime ⊀ Design** (Elsa §E2.2, applied to Activities) | **PASS — central** | Whole unit restores this; enforced by a reference/structural test. The deleted leak (`Runtime.Core → Design.Core` via the descriptor) is removed. |
| G18 | Command/query split at persistence contract | **PASS** | Save/load handlers stay split; no combined command-query method added. |
| G20 | Refactor: existing tests preserved (subject/objective) | **PASS w/ recorded deletions** | Tests of deleted types (design descriptor registry, runtime resolver registry/resolver, `OnImplementationDescriptorsInitializing`) have their *subject removed*; deletions recorded here (architect approval: Joey, this session) per §2.21.1. Reconciliation/persistence behaviour tests are migrated, not deleted. |
| G21 | **Contribution via domain event; Registry + StartUp Task for sync access; no `IEnumerable<TProvider>` for new code** | **PASS — by design** | `IActivityConstructorRegistry` is populated via `OnActivityConstructorsInitializing` (`IEvent`, Sequential) + a single aggregating `RegisterActivityConstructors` handler + a StartUp Task; sync-read afterward. Mirrors §E3.3. Resurrects the pattern the experiment deleted, but **Design-free**, in `Runtime.Core`/`Runtime`. |
| G22 | No tight logic coupling between concretes | **PASS** | Constructors interact only through the registry contract + the descriptor data shape. |
| G23 | Generic dispatch only for fire-and-forget | **PASS** | The startup event is fire-and-forget population; the *expectation* of a specific construction outcome flows through the typed `IActivityConstructor` contract + registry, not a bus. |
| G24 | Design-time vs runtime contract split | **PASS** | Design binds to **opaque** `(DescriptorType, JsonElement)`; runtime binds to the typed `IActivityConstructor<TDescriptor>`. The only shared shape is the serialized payload (a primitive), not a shared contract. |
| G25 | Feature modules don't depend on concrete providers | **PASS** | `Primitives`/`Composition` depend on `Runtime.Core` + building-block libs, not on persistence providers. |
| G27 | Unit-test discipline (registration + branch tests; visibility) | **PLANNED** | Feature classes `public` not sealed; logic-bearing impls (`ActivityFactory`, `ActivityConstructorRegistry`, the constructors, the binder) `public sealed`; registration tests assert resolvability. |
| G26 | Feature documentation (handlers + tasks) | **PLANNED** | `Primitives`/`Composition`/`Runtime` ship docs listing the registered handler (`RegisterActivityConstructors`) + the StartUp task. |
| G28 | Persistence invariants provider-agnostic | **PASS** | Descriptor immutability (`DescriptorType`, payload write-once) defined on the entity / `.Persistence.Core`; EF enforcement (`PropertySaveBehavior.Throw`) in `.Persistence.EFCore`. |
| G29 | Executable-always-runs; runtime self-sufficient | **PASS** | An unregistered `DescriptorType` is a **domain** failure at construction (a missing owning feature), surfaced explicitly — not a swallowed system fault; cataloguing/reading the row is unaffected. |
| G6, G9–G14, G16–G17, G19, G30 | — | **N/A** | No adapters/helpers/dual-integration/Elsa3-runtime changes; naming stable; no extension-method logic added. (G30: the Elsa3 import edit is a shape update to an existing one-way adapter, no new direction.) |

**Originating constitutional item (record in-unit, framework §2 amendment):** *A `*.Core` library MUST NOT be a bucket for every interface.* Core is for **contributor** and **replacement** contracts (+ shared models); feature-internal interfaces stay in the feature. Governs `IActivityArgumentBinder` (stays in `Elsa.Activities.Primitives`). No §2.24.3 new-pattern gate is needed (no new framework pattern is introduced; the event-handler-binding idea was dropped).

**No unjustified violations → Complexity Tracking is empty.**

## Project Structure

### Documentation (this feature)

```text
specs/006-activity-construction-seam/
├── plan.md              # This file
├── research.md          # Phase 0 — decisions + rejected alternatives
├── data-model.md        # Phase 1 — entities + reshaped persistence + contracts
├── quickstart.md        # Phase 1 — the four-seam walk + construction round-trip
├── contracts/           # Phase 1 — construction-seam contracts + reshaped read-contract
│   ├── construction-seam.md
│   └── persistence-shape.md
├── checklists/
│   └── requirements.md   # from /speckit.specify
└── tasks.md             # /speckit.tasks (NOT created here)
```

### Source Code (repository root)

```text
src/
├── Elsa.Activities.Runtime.Core/            # construction-seam CONTRACTS (Design-free)
│   ├── Contracts/IActivity.cs               # (existing)
│   ├── Contracts/IActivityFactory.cs        # RESHAPE → Create(descriptorType, JsonElement, inputs, outputs, ct)
│   ├── Contracts/IActivityConstructor.cs    # NEW (non-generic + IActivityConstructor<TDescriptor>)
│   ├── Contracts/IActivityConstructorRegistry.cs  # NEW
│   ├── Events/OnActivityConstructorsInitializing.cs  # NEW (IEvent; Registry+StartUp pattern)
│   ├── Models/InputArgument.cs, OutputArgument.cs    # (existing)
│   └── Attributes/{Required,Version}Attribute.cs     # (existing, already moved)
├── Elsa.Activities.Runtime/                 # construction-seam IMPL (Design-free)
│   ├── Services/ActivityFactory.cs          # RESHAPE → pure dispatch
│   ├── Services/ActivityConstructorRegistry.cs       # NEW (one-per-DescriptorType; throws on dup)
│   ├── Handlers/RegisterActivityConstructors.cs      # NEW (single aggregating handler)
│   ├── Tasks/ActivityConstructorsStartupTask.cs      # NEW (publishes the event once)
│   └── ActivitiesRuntimeFeature.cs          # register factory + registry + handler + task
├── Elsa.Activities.Primitives/              # NEW runtime feature — CLR kind (NO Design ref, §E2.2)
│   ├── Constructors/ClrActivityConstructor.cs        # IActivityConstructor<TypeInformation>
│   ├── Binding/ActivityArgumentBinder.cs    # feature-internal binder (NOT in Core)
│   ├── Activities/WriteLine.cs              # ported from elsa-core, adapted to InputArgument<string> + IActivity
│   └── ActivitiesPrimitivesFeature.cs
├── Elsa.Activities.Composition.Runtime/     # NEW runtime feature — Workflow kind (Design-free, §E2.2)
│   ├── Constructors/WorkflowActivityConstructor.cs   # IActivityConstructor<WorkflowIdentity>
│   ├── Activities/WorkflowDefinitionActivity.cs      # ordinary CLR IActivity; construct-only this unit
│   └── ActivitiesCompositionRuntimeFeature.cs
├── Elsa.Activities.Composition.Design/      # NEW design feature — Workflow reconciliation source
│   ├── Reconciliation/WorkflowActivityReconciliationSource.cs   # refs Design.Core + Reconciliation.Core
│   └── ActivitiesCompositionDesignFeature.cs
├── Elsa.Workflows.Primitives/               # zero-dep building-block lib
│   └── Models/WorkflowIdentity.cs           # NEW (DefinitionId, VersionId, Version)
├── Elsa.Activities.Design.Core/             # PURGE descriptor knowledge
│   ├── (DELETE) Contracts/IImplementationDescriptor.cs
│   ├── (DELETE) Contracts/IImplementationDescriptorRegistry.cs
│   ├── (DELETE) Contracts/IImplementationDescriptorSource.cs
│   ├── (DELETE) Events/OnImplementationDescriptorsInitializing.cs
│   ├── (DELETE) Models/{ClrImplementationDescriptor,WorkflowImplementationDescriptor,
│   │             ImplementationDescriptorRegistration,ImplementationDescriptorRegistry}.cs
│   └── Contracts/IActivityDefinitionVersion.cs       # RESHAPE → DescriptorType + JsonElement
├── Elsa.Activities.Design.Persistence.Core/
│   ├── Entities/ActivityDefinitionVersion.cs         # RESHAPE descriptor columns
│   └── (DELETE/relocate) Exceptions/ActivityDescriptorDeserialisationException.cs
├── Elsa.Activities.Design.Persistence.EFCore/
│   ├── Configurations/ActivityDefinitionVersionConfiguration.cs   # column rename + immutability
│   └── EntityHandlers/{Saving,Loading}Handler.cs     # no type resolution; JsonElement⇄string
├── Elsa.Activities.Design.Persistence.EFCore.Sqlite/
│   └── Migrations/*                          # DELETE + regenerate fresh initial
├── Elsa.Activities.Design.Reconciliation.Core/
│   └── Models/ActivityVersionReconciliationModel.cs  # ImplementationKind → DescriptorType
├── Elsa.Activities.Design.Reconciliation/
│   └── Services/ActivityVersionReconciler.cs, Handlers/ActivityVersionsReconcilingHandler.cs  # drop type-resolution; persist (DescriptorType, payload)
├── Elsa.Activities.Design.Reconciliation.Clr/
│   └── Services/ClrAssemblyScanner.cs        # emit TypeInformation + DescriptorType
├── Elsa.Activities.Design.Reconciliation.Json/
│   └── catalog format: ImplementationKind → DescriptorType (no behaviour change otherwise)
├── Elsa.Activities.Design.Api/
│   └── Commands/Handlers/Mapping/Views        # (DescriptorType, payload) shape
└── Elsa3.Activities.Design.Import/
    └── Models/ActivityDefinitionVersionImport.cs     # (DescriptorType, payload) shape

tests/
├── Elsa.Activities.Runtime.Tests/           # factory dispatch, registry dup-guard, constructors, binder, round-trip
├── Elsa.Activities.Primitives.Tests/        # CLR constructor + binder branches + registration
├── Elsa.Activities.Composition.Tests/       # Workflow constructor (construct-only) + registration
└── Elsa.Activities.Design.Tests/            # reconciliation/persistence migration; §E2.2 + no-feature-ref structural tests
```

**Structure Decision**: The CLR kind lives in one runtime feature `Elsa.Activities.Primitives` (Design-free). The Workflow kind is **split** across `Elsa.Activities.Composition.Runtime` (Design-free: the backing activity + constructor) and `Elsa.Activities.Composition.Design` (the reconciliation source; references `Design.Core`) — so the runtime activity carries no Design dependency (§E2.2/FR-013). The `Composition` name is retained deliberately (its own activity sub-domain for composing bundles of activities/workflows; §E3.10's `Elsa.Workflows.Activities.*` model-prefix naming is intentionally not adopted). The construction seam lives in the existing `Elsa.Activities.Runtime(.Core)`; the descriptor *types* live in zero-dep building-block libs (`Elsa.Primitives` for `TypeInformation`, `Elsa.Workflows.Primitives` for `WorkflowIdentity`) so no feature references another (SC-006). The design domain is purged of descriptor knowledge.

**Note (symmetry)**: `WorkflowDefinitionActivity` is itself an ordinary CLR `IActivity` — catalogued under a `TypeInformation` descriptor and built by the CLR constructor like any primitive. A workflow-as-activity row differs only by `(DescriptorType = WorkflowIdentity, constructor = WorkflowActivityConstructor)`, where the constructor produces a configured `WorkflowDefinitionActivity`.

## Phased implementation overview (detail → `/speckit.tasks`)

- **Phase A — Runtime seam (Design-free core).** `IActivityConstructor(+<T>)`, `IActivityConstructorRegistry`, `OnActivityConstructorsInitializing`; reshape `IActivityFactory`; impl `ActivityFactory` (dispatch), `ActivityConstructorRegistry` (dup-guard throw), `RegisterActivityConstructors`, startup task; wire `ActivitiesRuntimeFeature`. Tests + §E2.2 reference test.
- **Phase B — CLR kind.** `Elsa.Activities.Primitives` (Design-free) with `ClrActivityConstructor<TypeInformation>`, the feature-internal `ActivityArgumentBinder` (fix the 3 binder bugs), and `WriteLine` ported from elsa-core (adapted to `InputArgument<string>` + `IActivity`); registration tests.
- **Phase C — Workflow kind (split).** `Elsa.Workflows.Primitives.Models.WorkflowIdentity`; `Elsa.Activities.Composition.Runtime` (Design-free) with `WorkflowActivityConstructor` (produces a configured `WorkflowDefinitionActivity`) + construct-only `WorkflowDefinitionActivity` (itself an ordinary CLR activity); `Elsa.Activities.Composition.Design` with `WorkflowActivityReconciliationSource`; tests.
- **Phase D — Design purge + persistence reshape.** Delete the descriptor interface/registry/sources/event + `ClrImplementationDescriptor`/`WorkflowImplementationDescriptor` from `Design.Core`; reshape `ActivityDefinitionVersion` + read contract to `(DescriptorType, payload)`; rewrite save/load handlers (no type resolution); EF config column rename + immutability; regenerate the SQLite initial migration.
- **Phase E — Reconciliation + API + import.** Rename `ImplementationKind → DescriptorType` on the reconciliation model; drop descriptor-type resolution from the reconciling handler/reconciler; update CLR scanner (`TypeInformation`), JSON catalog format, Design.Api commands/handlers/views, and the Elsa3 import shape.
- **Phase F — Sweep + gates.** Repo-wide search proving zero references to all deleted types (SC-002); reference test proving `Runtime.*` ⊀ `Design.*` (SC-001) and no feature→feature refs (SC-006); seam-walk doc for a hypothetical kind (SC-004); full build + tests.

## Complexity Tracking

*No constitutional violations require justification — table intentionally empty.*
