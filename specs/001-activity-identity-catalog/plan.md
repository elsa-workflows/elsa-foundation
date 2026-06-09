# Implementation Plan: Activity Identity & Catalog as Source-of-Truth

**Branch**: `001-activity-identity-catalog` | **Date**: 2026-05-27 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/001-activity-identity-catalog/spec.md`

## Summary

Reshape the activity catalog around a stable logical identity (`ActivityTypeKey`) and a polymorphic `IImplementationDescriptor` that decouples *what the activity is* from *how the runtime constructs it*. Split provenance into immutable creation provenance (on `ActivityDefinition`) and operational reconciliation state (on a new sibling entity). Introduce a kind-typed `IActivityImplementationResolver` + a kind-agnostic `IActivityFactory` for construction dispatch, plus a parallel `IImplementationDescriptorRegistry` for kind-→-CLR-type resolution at persistence-load time (both registries follow the canonical §2.6.1 Registry + StartUp Task sub-pattern). Pin the catalog as the single source-of-truth for picker visibility (no live-provider lookup, no `IsBrowsable` flag). Rename the existing `Provisioning` modules to `Reconciliation` to align with Sipke item 6's idempotent-lifecycle framing. Fold in (a) Unit A's `OnEntitySaving` migration for the activity-catalog saving handlers (model-creating stays on the existing `IEntityModelCreatingHandler` sync-side-effect mechanism), (b) the `TenantEntity` base-class hygiene refactor across activity-catalog and workflow-side entities, and (c) the seed JSON-file reconciliation source as end-to-end proof.

## Technical Context

**Language/Version**: C# / .NET 10 (`net10.0` target across the solution).

**Primary Dependencies**:
- Entity Framework Core (Microsoft.EntityFrameworkCore.*) — persistence; SQLite provider for the in-repo default.
- `Elsa.Mediator` / `Elsa.Mediator.Core` — `IDomainEventSender` for §2.6.1 dispatch and the Registry + StartUp Task sub-pattern.
- `Elsa.Api.FastEndpoints` — internal FastEndpoints wrapping for the activity-catalog REST API.
- `System.Text.Json` — shadow-JSON columns for `*Source` projections; polymorphic descriptor (de)serialisation driven by the `ImplementationKind` discriminator.

**Storage**: SQLite via EF Core in the foundation repo; fresh initial migrations regenerated for both `ActivitiesDesignDbContext` and `WorkflowsDesignDbContext` (no production data to preserve).

**Testing**: xUnit (matches existing `Test.Activities.Import` and the test-project conventions in the solution). Framework §2.23.1 registration tests + §2.23.2 branch-covered unit tests are mandatory per spec FR-019 / FR-020.

**Target Platform**: ASP.NET Core via `Elsa.Server` (the foundation-repo host). Solution = `Elsa.Server.slnx`.

**Project Type**: Modular ASP.NET Core application — the Elsa workflow engine; library-style modular composition with feature classes (`IFeature`).

**Performance Goals**: No SLA change vs current state — this is a structural refactor, not a perf-critical feature.

**Constraints**: No production data migration path required (fresh migrations replace existing initial migrations). The golden rule of refactoring (§2.21.1) applies: existing tests on refactored implementations MUST pass without modification of test cases themselves.

**Scale/Scope**: Activity catalog supports large catalogs (thousands of workflows exposed as activities) — the catalog-as-source-of-truth rule (Sipke item 7) is the precondition for this scale, not Unit B's deliverable directly.

## Constitution Check

*GATE: Walked before Phase 0 research; re-walked after Phase 1 design (this file is the second walk).*

| # | Status | Note |
|---|---|---|
| G1 — three-layer per feature | **PASS** | Activity catalog: `Activities.Design.Core` (interfaces, records) → `Activities.Design.Persistence.Core` (entities + store contracts) → `Activities.Design.Persistence.EFCore` (EF mapping + handlers) → `Activities.Design.Persistence.EFCore.Sqlite` (provider). Reconciliation: `Reconciliation.Core` → `Reconciliation` (generic feature) → `Reconciliation.Json` (seed source). Runtime: `Activities.Runtime.Core` (factory + resolver contracts) → `Activities` or kind-specific feature (CLR resolver). No global "Core". |
| G2 — domain-language naming only | **PASS** | No `Features.*` / `Modules.*` / `Implementations.*` / `Providers.*` / `Adapters.*` / `.Contracts` / `.Abstractions` segments introduced. The `Reconciliation` rename replaces `Provisioning` with another domain-language term. |
| G3 — no heavy dep in `.Core` | **PASS** | `Activities.Design.Core` adds only domain types (records, smart-enums, descriptor interface). `Activities.Runtime.Core` adds the factory + resolver interfaces. No heavy NuGet enters either. `Reconciliation.Core` carries only `IActivityDefinitionHasher` + event types. |
| G4 — no peer impl-to-impl across unrelated sub-domains | **PASS** | The `Reconciliation.Json` feature references `Reconciliation` (provider-family inheritance) — permitted. No cross-domain impl references. |
| G5 — contract kind declared | **PASS** | New contribution contracts (`OnActivityImplementationResolversInitializing`, `OnImplementationDescriptorsInitializing`, `OnActivityVersionsReconciling`, `OnEntitySaving`) are domain events per §2.6.1 — contribution kind. Replacement contracts: `IActivityFactory` (one factory per host), `IActivityDefinitionHasher` (default impl, replaceable per provider), `IImplementationDescriptorRegistry` + `IActivityImplementationResolverRegistry` (one of each per host). `IEntityModelCreatingHandler` is NOT migrated to a domain event — sync side-effect chain pattern; documented exception per G21 + clarifications session 3. |
| G6 — §2.7.1 decision rule applied | **PASS** | Inheritance: `ActivityDefinition`/`ActivityDefinitionVersion` → `TenantEntity` → `Entity` (specialization). Adapter: CLR resolver wraps `Type` activation behind `IActivityImplementationResolver<ClrImplementationDescriptor>`. Provider/contributor: resolvers contributed via domain event. Each role explicit. |
| G7 — no `DependsOn` | **PASS** | No static feature-dependency declarations introduced. |
| G8 — `TDbContext : DbContext` only | **PASS** | The `TenantId` index registration in `ElsaDbContextBase.OnModelCreating` scans for `TenantEntity` descendants generically — no `where TDbContext : ElsaDbContextBase` constraint. `ElsaDbContextBase` remains opt-in per §E2.5. |
| G9 — helper libs domain-owned | N/A | No new helper libs. |
| G10 — refactor-cost test (NuGet identity) | **VIOLATION → JUSTIFIED** | `Elsa.Activities.Design.Provisioning.*` → `Elsa.Activities.Design.Reconciliation.*` is a NuGet identity rename. Justified: ratified at clarify session 2 (Sipke item 6 framing); no production consumers; greenfield-equivalent state; the new name names the lifecycle (reconciliation) more accurately than the activity (provisioning). Captured under *Complexity Tracking*. |
| G11 — duplication beats dependency | **PASS** | No new shared utilities introduced beyond the contract layer. Sealed records (input/output/port/argument definitions, argument-state hierarchy) are duplicated structural shapes — intentionally — per the FR-030 derivation rule (signature clarity). |
| G12 — provider module decomposition | **PASS** | `Reconciliation.Json` ships as the dedicated source module (the only concrete source initially); no empty umbrella. The CLR resolver lives in the activities runtime feature that exists today; no new umbrella created. |
| G13 — feature `name` stable | **VIOLATION → JUSTIFIED** | Feature-class rename: `ActivitiesDesignProvisioningFeature` → `ActivitiesDesignReconciliationFeature`. Per §2.19 this counts as major-version-class change. Justified: pre-ratification reshape, ratified at clarify; same provenance as the module rename. Same Complexity Tracking entry. |
| G14 — SemVer for `.Core` | **PASS** (deferred) | SemVer for `Activities.Design.Core` will land at version-bump time. Spec is pre-ratification. |
| G15 — Workflows.Runtime not depending on Workflows.Design | **PASS** | Unit B does not introduce any such dependency. The `TenantEntity` migration touches workflow-side entities (Workflows.Design.Persistence.*) but does not change their relationship to Workflows.Runtime.* |
| G16 — Elsa worked examples in Elsa constitution | **PASS** | Three new constitutional sections land as part of Unit B: (a) **Elsa §E2.x** (catalog source-of-truth, in `constitution.md`); (b) **framework §2.6.5** (sync contributor pattern — rare exception, in `constitution-framework.md`); (c) **Elsa §E3.9** (canonical worked example of §2.6.5 — `IEntityModelCreatingHandler`, in `constitution.md`). Both layers of the two-layer constitution are amended in this unit. §E2.x and §E3.9 are Elsa-specific (worked examples + Elsa-domain rules). §2.6.5 generalises across applications (rare-exception mechanism that any application built on the framework may invoke under the three documented criteria). G16's "Elsa worked examples in Elsa constitution" rule is satisfied: §E3.9 lives in `constitution.md`; framework §2.6.5 carries only the synthetic rule, with the concrete example deliberately living in the Elsa layer. |
| G17 — extension methods reviewed | N/A | No new extension methods introduced beyond mechanical EF Core configuration helpers. |
| G18 — CQS at persistence boundary | **PASS** | Existing CQS shape preserved; `IAddActivityDefinitionCommand`, `IQueries<ActivityDefinition>`, etc. follow the split. |
| G19 — dual-integration smell | N/A | No new cross-system integration introduced. |
| G20 — refactor work preserves existing tests | **PASS** | The plan honours §2.21.1; existing handler/provisioner tests will be migrated (test setup may change; subject + objective preserved). Any test that genuinely no longer applies (e.g. tests asserting `IsBrowsable=false` behaviour) requires explicit architect approval per the rule. |
| G21 — domain events for contribution | **PASS** | Cross-feature contributions go through domain events: `OnActivityVersionsReconciling` (source contributions), `OnActivityImplementationResolversInitializing` (resolver contributions), `OnImplementationDescriptorsInitializing` (descriptor type registry contributions), `OnEntitySaving` (entity-saving hook). `IEntityModelCreatingHandler` is retained under the **§2.6.5 sync contributor pattern — rare exception** (newly codified in the constitution): all three §2.6.5 criteria hold (intrinsically sync dispatch site, behaviour-not-data contribution, Registry + StartUp Task structurally inapplicable). Canonical worked example: Elsa §E3.9. |
| G22 — no tight logic coupling | **PASS** | All cross-feature dependencies are contract-level (domain events with declared payload shapes). The factory/resolver split itself enforces this: resolver returns `Type`; factory does activation. No reliance on side effects between concrete classes. |
| G23 — generic dispatch is not coupling | **PASS** | Domain events used throughout; no `IMediator.Send` for handler-expected-to-run scenarios. |
| G24 — design-time vs runtime contract split | **PASS** | `ArgumentDefinition` (design-time canvas) is distinct from `ArgumentState` (filled-in canvas, design-time) which is distinct from runtime evaluation outputs. The factory bridges design → runtime at construction time; descriptor + states live design-side; runtime contracts (`IActivity`, factory, resolver) live runtime-side. |
| G25 — provider-impl deps | **PASS** | `Activities.Design.Api` depends on `Activities.Design.Persistence.Core` (provider-agnostic), not `*.EFCore`. `Reconciliation.Json` depends on `Reconciliation` + `Activities.Design.Core` — not on any specific persistence provider. |
| G26 — feature documentation | **PASS** (plan-stage commitment) | Each touched feature class (`ActivitiesDesignReconciliationFeature`, `ActivitiesDesignReconciliationJsonFeature`, `EFCoreActivitiesPersistenceFeatureBase`-descendants, etc.) will ship a feature README listing registered domain-event handlers + tasks per §2.22. Specific README contents are tasks-stage. |
| G27 — unit test discipline | **PASS** | §2.23.1 registration tests + §2.23.2 branch-covered tests are mandatory per spec FR-019/FR-020. Visibility rules (feature classes `public` non-sealed; logic-bearing impls `public sealed`) applied to new code. |
| G28 — persistence invariants provider-agnostic | **PASS** | `[Immutable]` enforcement remains in `Elsa.Persistence.EFCore.ElsaDbContextBase` (provider-specific mechanism). The invariants themselves (which fields are immutable) are declared by the entity attributes in `Activities.Design.Persistence.Core` (provider-agnostic). |
| G29 — Elsa runtime executable-always-runs | **PASS** | Unknown-`ImplementationKind` lookup fails through a runtime/domain path (FR-028), not a system failure. Domain gates may deny execution; resolved kinds always produce an `IActivity`. |
| G30 — Elsa 3 import-only | N/A | No Elsa 3 ↔ Elsa 4 changes in Unit B. Existing `Elsa3.Activities.Design.Import` continues to consume the new shape via mapping. |

**Two justified violations** (G10 + G13) — same root cause: the `Provisioning` → `Reconciliation` rename. Recorded in *Complexity Tracking* below.

## Project Structure

### Documentation (this feature)

```text
specs/001-activity-identity-catalog/
├── plan.md                       # This file
├── research.md                   # Phase 0 — code-discovery + best-practice consolidation
├── data-model.md                 # Phase 1 — entity shape, relationships, constraints, immutability
├── quickstart.md                 # Phase 1 — end-to-end seed flow walkthrough
├── contracts/                    # Phase 1 — one file per new contract
│   ├── IImplementationDescriptor.md
│   ├── IImplementationDescriptorRegistry.md
│   ├── IActivityFactory.md
│   ├── IActivityImplementationResolver.md
│   ├── IActivityDefinitionHasher.md
│   ├── OnActivityVersionsReconciling.md
│   ├── OnActivityImplementationResolversInitializing.md
│   ├── OnEntitySaving.md
│   └── read-contracts.md
├── checklists/
│   └── requirements.md           # Already created at /speckit.specify
└── tasks.md                      # /speckit.tasks output (NOT created here)
```

### Source code (touched projects, repository root)

```text
src/
# --- Primitives ---
├── Elsa.Primitives/
│   ├── Entities/Entity.cs                              # TenantId REMOVED
│   ├── Entities/TenantEntity.cs                        # NEW — TenantId moved here
│   ├── Attributes/ImmutableAttribute.cs                # unchanged
│   └── Models/TypeInformation.cs                       # unchanged (kept pure)

# --- Persistence ---
├── Elsa.Persistence.EFCore/
│   ├── ElsaDbContextBase.cs                            # TenantEntity-aware index registration; dispatches OnEntitySaving (model-creating stays on IEntityModelCreatingHandler)
│   ├── Events/OnEntitySaving.cs                        # NEW — domain event (carries DbContext + EntityEntry)
│   ├── Contracts/IEntityModelCreatingHandler.cs        # UNCHANGED — sync side-effect chain pattern; not migrated
│   └── Contracts/IGlobalEntitySavingHandler.cs         # DEPRECATED for activity-catalog (kept for the legacy migration tail elsewhere)

# --- Activities Design (the heart of Unit B) ---
├── Elsa.Activities.Design.Core/
│   ├── Contracts/IActivityDefinition.cs                # RESHAPED — identity + creation provenance + display
│   ├── Contracts/IActivityDefinitionVersion.cs         # RESHAPED — version + kind + descriptor + inputs/outputs/ports
│   ├── Contracts/IActivityDefinitionReconciliationState.cs  # NEW — read contract for reconciliation sibling
│   ├── Contracts/IImplementationDescriptor.cs          # NEW — marker interface
│   ├── Models/ImplementationKind.cs                    # NEW — smart-enum value-record
│   ├── Models/SourceKind.cs                            # NEW — smart-enum value-record
│   ├── Models/ClrImplementationDescriptor.cs           # NEW — wraps TypeInformation
│   ├── Contracts/IImplementationDescriptorRegistry.cs  # NEW — registry contract per §2.6.1 sub-pattern
│   ├── Models/ImplementationDescriptorRegistration.cs  # NEW — sealed record (Kind, Type)
│   ├── Models/ImplementationDescriptorRegistry.cs      # NEW — thin default impl
│   ├── Events/OnImplementationDescriptorsInitializing.cs  # NEW — contribution event
│   ├── Models/ArgumentDefinition.cs                    # SEALED RECORD
│   ├── Models/InputDefinition.cs                       # SEALED RECORD
│   ├── Models/OutputDefinition.cs                      # SEALED RECORD
│   ├── Models/ActivityPortDefinition.cs                # SEALED RECORD
│   ├── Models/ArgumentValue.cs                         # NEW — sealed record { object? Value, ExpressionType ExpressionType }
│   ├── Models/ExpressionType.cs                        # NEW — smart-enum value-record
│   ├── Models/ArgumentState.cs                         # NEW — base record (ReferenceKey + Value)
│   ├── Models/InputState.cs                            # NEW — sealed record : ArgumentState
│   ├── Models/OutputState.cs                           # NEW — sealed record : ArgumentState
│   └── Contracts/IArgumentDefinition.cs                # REMOVED

├── Elsa.Activities.Design.Persistence.Core/
│   ├── Entities/ActivityDefinition.cs                  # RESHAPED — ActivityTypeKey, SourceKind, SourceId, ProvisionedAt, ProvisionedBy; no IsBrowsable
│   ├── Entities/ActivityDefinitionVersion.cs           # RESHAPED — ImplementationKind column + [NotMapped] IImplementationDescriptor ImplementationDescriptor (shadow column declared in EF config)
│   ├── Entities/ActivityDefinitionReconciliationState.cs  # NEW — sibling entity, 1:0..1
│   ├── Contracts/IAddActivityDefinitionCommand.cs      # SIGNATURE UPDATED
│   ├── Extensions/                                     # query extensions updated
│   └── Filters/                                        # filters updated

├── Elsa.Activities.Design.Persistence.EFCore/
│   ├── DbContext/ActivitiesDesignDbContext.cs          # adds ActivityDefinitionReconciliationStates DbSet
│   ├── Configurations/ActivityDefinitionConfiguration.cs        # unique (SourceKind, SourceId, ActivityTypeKey) + lookup (SourceKind, SourceId)
│   ├── Configurations/ActivityDefinitionVersionConfiguration.cs # ImplementationDescriptor shadow column (immutable via PropertySaveBehavior.Throw); (DefinitionId, Version) unique
│   ├── Configurations/ActivityDefinitionReconciliationStateConfiguration.cs  # NEW — FK + IsStale index
│   ├── EntityHandlers/*SavingHandler.cs                # MIGRATED — registered as IDomainEventHandler<OnEntitySaving>
│   ├── EntityHandlers/*ModelCreatingHandler.cs         # UNCHANGED — stays on IEntityModelCreatingHandler (sync side-effect chain)
│   └── Services/AddActivityDefinitionCommand.cs        # updated

├── Elsa.Activities.Design.Persistence.EFCore.Sqlite/
│   └── Migrations/<timestamp>_Initial.cs               # REGENERATED — replaces 20260525083434_Initial

├── Elsa.Activities.Design.Api/
│   ├── Models/ActivityDefinitionView.cs                # ActivityTypeKey, SourceKind, ProvisionedAt, ProvisionedBy; no IsBrowsable
│   ├── Models/ActivityDefinitionVersionDetailsView.cs  # ImplementationKind + descriptor payload
│   ├── Endpoints/Definitions/*.cs                      # DTO + mapping updates
│   └── Endpoints/Versions/*.cs

├── Elsa.Activities.Design.Commands.Core/
│   └── AddDefinitionCommand.cs                         # SIGNATURE UPDATED

├── Elsa.Activities.Design.Api.Handlers/
│   └── (handler updates for new command shape)

# --- Reconciliation (renamed from Provisioning) ---
├── Elsa.Activities.Design.Reconciliation.Core/         # RENAMED from .Provisioning.Core
│   ├── IActivityVersionReconciler.cs                   # RENAMED from IActivityVersionProvisioner
│   ├── OnActivityVersionsReconciling.cs                # RENAMED from OnActivityVersionsProvisioning
│   └── IActivityDefinitionHasher.cs                    # NEW

├── Elsa.Activities.Design.Reconciliation/              # RENAMED from .Provisioning
│   ├── ActivitiesDesignReconciliationFeature.cs        # RENAMED feature class
│   ├── Services/ActivityVersionReconciler.cs           # RENAMED from ActivityVersionProvisioner; writes reconciliation-state row; invokes hasher
│   ├── Services/ActivityVersionReconcilerStartupTask.cs
│   ├── Services/DefaultActivityDefinitionHasher.cs     # NEW — default impl
│   └── Options/ActivityVersionReconcilerOptions.cs

├── Elsa.Activities.Design.Reconciliation.Json/         # NEW — seed JSON-file source
│   ├── ActivitiesDesignReconciliationJsonFeature.cs
│   ├── Handlers/JsonActivityVersionsReconcilingHandler.cs  # handles OnActivityVersionsReconciling
│   ├── Services/JsonActivityCatalogReader.cs
│   ├── Options/JsonReconciliationOptions.cs
│   └── Models/JsonCatalogEntry.cs                      # mirrors elsa-core-activities.json

# --- Activities Runtime (factory + CLR resolver) ---
├── Elsa.Activities.Runtime.Core/
│   ├── Contracts/IActivityFactory.cs                   # NEW
│   ├── Contracts/IActivityImplementationResolver.cs    # NEW — kind-typed
│   └── Events/OnActivityImplementationResolversInitializing.cs  # NEW

├── Elsa.Activities/                                    # the activities runtime feature
│   ├── Services/ActivityFactory.cs                     # NEW — implements IActivityFactory
│   ├── Resolvers/ClrActivityImplementationResolver.cs  # NEW — CLR resolver
│   ├── Services/ActivityImplementationResolverRegistry.cs  # populated via OnActivityImplementationResolversInitializing
│   ├── Services/ActivityImplementationResolverRegistryStartupTask.cs
│   └── Services/ImplementationDescriptorRegistryStartupTask.cs  # NEW — publishes OnImplementationDescriptorsInitializing; flushes contributions

# --- Workflows Design (TenantEntity migration only) ---
├── Elsa.Workflows.Design.Persistence.Core/
│   └── Entities/                                       # WorkflowDefinition, WorkflowDefinitionVersion, WorkflowDefinitionDraft → inherit TenantEntity

├── Elsa.Workflows.Design.Persistence.EFCore.Sqlite/
│   └── Migrations/<timestamp>_Initial.cs               # REGENERATED for the inheritance switch

# --- Tests ---
tests/
├── Elsa.Activities.Design.Tests/                       # NEW or expanded
│   ├── Registration/                                   # §2.23.1 registration tests
│   ├── Unit/                                           # §2.23.2 per-implementation branch-covered tests
│   └── Integration/                                    # JSON reconciler → catalog round-trip → factory → IActivity
```

**Structure Decision.** Per-feature three-layer split (G1) applied consistently; the renamed `Reconciliation` modules replace `Provisioning` 1:1 at the project level. The new `Reconciliation.Json` ships as a dedicated source feature (§2.20 — one concrete source, single provider module, no empty umbrella). The runtime-side factory + resolver registry lives in `Elsa.Activities.Runtime.Core` (contract) + the existing `Elsa.Activities` runtime feature (implementation). Workflow-side touches are limited to the `TenantEntity` inheritance switch and the regenerated initial migration — no entity shape changes (those belong to Units C/D/E).

## Complexity Tracking

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| G10 + G13 — NuGet identity rename + feature-class rename: `Elsa.Activities.Design.Provisioning.*` → `Elsa.Activities.Design.Reconciliation.*`; `IActivityVersionProvisioner` → `IActivityVersionReconciler`; `OnActivityVersionsProvisioning` → `OnActivityVersionsReconciling`; `ActivitiesDesignProvisioningFeature` → `ActivitiesDesignReconciliationFeature`. | Sipke item 6 (2026-05-26) reframes provisioning as one trigger of a broader idempotent reconciliation lifecycle. The new name names the lifecycle accurately; the old name names a sub-step. Ratified at clarify session 2 (2026-05-27). | Keeping `Provisioning` would leave the module name misaligned with the conceptual framing every architect now uses in conversation; downstream Unit F's reconciliation behaviour would land in a module called `Provisioning`, which would confuse readers. The rename is cheap NOW (pre-ratification, no production consumers); deferred it gets more expensive. |

---

# Phase 0 — Research

Saved as [research.md](./research.md). The clarify pass closed the architectural ambiguity, so Phase 0 is primarily code-discovery + best-practice consolidation.

# Phase 1 — Design & Contracts

Saved as:
- [data-model.md](./data-model.md) — entity shape, relationships, indexes, immutability.
- [contracts/](./contracts/) — one file per new contract.
- [quickstart.md](./quickstart.md) — end-to-end seed flow walkthrough.

The agent context file (`CLAUDE.md`) is updated to reference this plan between the SPECKIT markers.
