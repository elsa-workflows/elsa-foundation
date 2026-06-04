# Implementation Plan: Activity Semantic Versioning

**Branch**: `004-activity-semantic-versioning` *(authored on `main`; no feature branch — consistent with units 001–003)* | **Date**: 2026-06-04 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `specs/004-activity-semantic-versioning/spec.md`

## Summary

Replace the `int` activity version with an author-controlled **string SemVer 2.0.0** across the Unit B activity-definition-version model (entity, read contract, projection, reconciliation model, order definition, API surface) and unify the runtime `int Version` on `ActivityBase`/`IActivity` onto the same semver. The version's **source of truth is the declaring assembly's version**; an optional `[Version]` attribute on a CLR activity type overrides it. A new **`Elsa.Activities.Design.Reconciliation.Clr`** feature scans a configured folder of DLLs, discovers `IActivity` implementations, reads their metadata + resolved version, and contributes them through the existing reconciliation-source pattern. As a prerequisite correction, the reconciliation feature is reshaped to the canonical §2.6.1 DI-source pattern (contracts to `.Core`; single non-abstract feature; sources contributed by independent features).

All five spec-deferred items are resolved in this plan (see [research.md](research.md)) **within sanctioned patterns** — no §2.24.3 gate is triggered:

1. **Semver ordering (FR-008)** → persisted normalised sortable key as a **real CLR property hidden from the read interface** (framework §2.9.1 / §2.24.2 row 12), plus a semver **comparator** (§2.24.2 row 9 Strategy). DB-side `OrderBy` on the sortable key.
2. **`Elsa.Activities.Runtime.Core` extraction (FR-009/FR-020)** → validated and **adopted**: Design→Runtime-activities is the **allowed** §E2.2 direction; the package already exists (a *move*, not greenfield). It mirrors the established Workflows Design/Runtime split, applied to the Activities sub-tree — the logical setup. The optional `[Version]` attribute is the deviation mechanism Sipke/Frans asked for (assembly version is the *fallback*, not an inescapable equality); no live dissent on the extraction or the assembly scanner.
3. **CLR `SourceId`** → a configured logical source name (defaulting to the normalised folder path).
4. **Reflection mechanics** → reflection-only inspection via `MetadataLoadContext` (no execution, no default-ALC pollution).
5. **Load-context** → default to reflection-only `MetadataLoadContext`; multi-version ALC loading stays out of scope.

## Technical Context

**Language/Version**: C# / .NET 10 (`net10.0`)
**Primary Dependencies**: `Microsoft.Extensions.*Abstractions`, EF Core (Sqlite default provider), `System.Reflection.MetadataLoadContext` (new — scanner only). No FluentAssertions (constitutionally pinned; xunit only).
**Storage**: EF Core; SQLite default. Activities-design context migration regenerated fresh (no data migration — Unit B convention).
**Testing**: xUnit. §2.23.1 registration tests per feature; §2.23.2 branch-covered unit tests per logic-bearing class; §2.21.1 golden-rule preservation of existing reconciliation/catalog tests; `ReadContractSurfaceTests` updated to pin `Version : string`.
**Target Platform**: Cross-platform .NET host (`Elsa.Server`).
**Project Type**: Modular class-library set + host (single solution `Elsa.Server.slnx`).
**Performance Goals**: Reconciliation is one-shot at startup; the scan is bounded by folder DLL count. No per-request hot path introduced. Ordering must be correct (SC-002) and DB-evaluable (no client-side full materialisation for "latest").
**Constraints**: `.Core` zero-heavy-dep (§2.3); Runtime MUST NOT depend on Design (§E2.2); Model X version-row immutability (§E2.8).
**Scale/Scope**: Catalog scale = number of activity types × versions (low thousands). Folder scan = tens–hundreds of DLLs.

## Constitution Check

*GATE: walked before Phase 0; re-walked after Phase 1 (see end of this section).*

| # | Gate | Verdict | Note |
|---|---|---|---|
| G1 | Three-layer separation; no global Core | **PASS** | New `.Reconciliation.Clr` is a Layer-3 feature; contracts move to `.Reconciliation.Core`; `Elsa.Activities.Runtime.Core` is a sub-domain `.Core`, not a global one. |
| G2 | Domain-language naming; no marker segments | **PASS** | `Elsa.Activities.Design.Reconciliation.Clr` names the source kind (CLR), mirroring runtime `Clr*Source` vocabulary. No `.Contracts`/`.Abstractions`/`Features.*`. |
| G3 | No heavy dep in `.Core` | **PASS** | Abstractions moved into `Elsa.Activities.Runtime.Core` carry no heavy deps. `MetadataLoadContext` lands in the **`.Clr` implementation feature**, never in a `.Core`. |
| G4 | No peer impl-to-impl across sub-domains | **PASS** | `.Clr` references `.Reconciliation.Core` (contracts) + `Elsa.Activities.Runtime.Core` (activity abstractions) — both `.Core`s. It does NOT reference the reconciliation feature impl. |
| G5 | Contract kind declared (replacement/contribution) | **PASS** | `IActivityReconciliationSource` is a **contribution** contract (consumed as `IEnumerable<>`). The default hasher is a **replacement** (§2.6.2). |
| G6 | §2.7.1 composition decision applied | **PASS** | Additive cross-feature contribution → DI source/contributor (§2.6.1). Inheritance explicitly rejected for source contribution (FR-021). Heavy dep (reflection/assembly load) isolated in the `.Clr` feature. |
| G7 | No `DependsOn` static feature deps | **PASS** | Host enables both the reconciliation feature and `.Clr`; fail-fast at DI. No declared dependency. |
| G8 | Persistence generic constraint = `DbContext` | **N/A** | No new generic persistence constraint introduced. |
| G9 | Helper libs domain-owned, not from `.Core` | **N/A** | No new helper library. |
| G10 | Refactor-cost (NuGet identity) justified | **PASS** | Moving `IActivityReconciliationSource` + model into `.Reconciliation.Core` changes their assembly identity. Justified: zero existing implementations of the source (greenfield consumer surface); the model's only consumers are within the reconciliation family. Finer-grained `.Core` split is the preferred outcome. |
| G11 | Duplication beats dependency | **PASS** | No new shared utility introduced; the semver comparator lives where the version type lives. |
| G12 | Provider module decomposition; no premature umbrella | **PASS** | `.Clr` is the first reconciliation **source provider** — a sibling feature, not an umbrella. `Elsa.Activities.Runtime.Core` already exists (no new umbrella). See FR-020 verdict below. |
| G13 | Feature `name` stable across refactors | **VIOLATION (justified)** | `ActivitiesDesignReconciliationFeature` goes abstract→non-abstract and drops `Sources`. The rule's "create-new-feature + retire" applies to *renames*; here the feature **name is unchanged** and the change is a defect correction (it was never shipped/ratified). Recorded in Complexity Tracking. |
| G14 | SemVer for `.Core` | **PASS** | Moving contracts into `.Reconciliation.Core` and re-typing `Version` is a MAJOR change to the affected `.Core`s; versioned accordingly at packaging. |
| G15 | **Elsa: Runtime MUST NOT depend on Design** | **PASS** | The new dependency is `.Reconciliation.Clr` (**Design** side) → `Elsa.Activities.Runtime.Core` (**Runtime-activities** abstractions). §E2.2 forbids only `Workflows.Runtime.* → Workflows.Design.*`. Design→Runtime is allowed. No Runtime→Design edge is introduced. **This is the FR-020 dependency-direction verdict.** |
| G16 | Elsa examples in Elsa constitution | **PASS** | §E2.8 amendment + any extraction wording land in `constitution.md` (FR-016). |
| G17 | Extension methods >3 lines reviewed | **N/A** | No new branching extension methods planned. |
| G18 | Persistence command/query split | **PASS** | Ordering change touches a query path (`ActivityVersionOrderDefinition` / lookup); no command/query merge introduced. |
| G19 | No hidden dual-integration module | **PASS** | `.Clr` integrates one concern (CLR assembly scanning). |
| G20 | **Refactor: existing tests keep passing** | **PASS (must hold)** | FR-021/FR-018 are §2.21.1 golden-rule refactors. Existing reconciliation + catalog tests preserved; only wiring/type-locations/version-type move. `ReadContractSurfaceTests` updated to assert the new `string` type (a pin update, not a subject change). |
| G21 | Domain events are the contribution mechanism | **PASS** | Contribution flows through `OnActivityVersionsReconciling` + `IEnumerable<IActivityReconciliationSource>` (Registry-style sync read at startup). No new `IEnumerable<TProvider>` introduced *for new code* beyond the already-established reconciliation-source surface (legacy-aligned, Unit B). |
| G22 | No tight logic coupling between impls | **PASS** | `.Clr` and the reconciler communicate only through the contribution model + event. |
| G23 | Generic dispatch not a coupling mechanism | **PASS** | `OnActivityVersionsReconciling` is a declared domain event with an expected aggregating handler — not smuggled through a generic bus. |
| G24 | Design-time vs runtime contract split | **PASS** | The `Version` semver is the shared `.Core` data shape; design-time (catalog/picker) and runtime (execution) consume it through their own contracts (FR-018 unifies the *value*, not the consumers). |
| G25 | Provider-impl deps only in provider-suffixed features | **PASS** | `.Clr` carries the provider suffix and is the only project pulling assembly-loading. Generic reconciliation feature depends only on `.Core`. |
| G26 | Feature documentation (handlers + tasks) | **PASS (deliverable)** | `EXTENSION_POINTS.md` for the reconciliation feature updated; `.Clr` ships its own listing (the source it registers, the folder-path option). |
| G27 | Unit test discipline (registration + branch) | **PASS (deliverable)** | Registration test for `.Clr` feature + reshaped reconciliation feature; branch tests for the scanner, comparator, sortable-key normaliser, assembly-version→semver resolver. Feature classes `public` non-sealed; logic impls `public sealed`. |
| G28 | Persistence invariants provider-agnostic | **PASS** | The sortable-key field + `[Immutable]` invariant live on the entity in `.Persistence.Core`; provider enforces via the existing immutable scanner. |
| G29 | Elsa: runtime executable-always-runs | **N/A** | This unit is design/catalog-side. No change to runtime loadability of artifacts. |
| G30 | Elsa: Elsa-3 compat is import-only | **PASS** | FR-007 maps the Elsa-3 `int` version to a semver string one-way in the import adapter. |

**Post-Phase-1 re-walk:** the concrete artefacts (data-model, contracts, project tree below) introduce no additional pattern. The one VIOLATION (G13) is the justified feature-shape correction; the FR-008 sortable key is the catalogued §2.9.1 pattern; the comparator is the catalogued Strategy pattern. **No §2.24.3 gate triggered.** Re-walk verdict: unchanged.

## Project Structure

### Documentation (this feature)

```text
specs/004-activity-semantic-versioning/
├── plan.md              # This file
├── research.md          # Phase 0 — the 5 deferred-item resolutions
├── data-model.md        # Phase 1 — entity/contract/value changes
├── quickstart.md        # Phase 1 — how to verify the unit
├── contracts/
│   └── surfaces.md      # Phase 1 — the changed contract surfaces (interfaces, attribute, source)
├── checklists/
│   └── requirements.md  # (from /speckit.specify)
└── tasks.md             # Phase 2 — created by /speckit.tasks, NOT here
```

### Source Code (repository root)

```text
src/
├── Elsa.Activities.Runtime.Core/                       # EXISTING. RECEIVES the moved abstractions:
│   ├── Contracts/IActivity.cs                          #   moved from Elsa.Workflows.Runtime.Core (Version → semver)
│   ├── ActivityBase.cs                                 #   moved (Version → semver)
│   ├── Contracts/IActivityExecutionContext.cs          #   moved
│   └── VersionAttribute.cs                             #   NEW optional [Version("x.y.z")]
│
├── Elsa.Activities.Design.Core/                        # IActivityDefinitionVersion.Version : int → string
│   └── Models/ActivityDefinitionVersionInfo.cs         #   Version : string
│
├── Elsa.Activities.Design.Persistence.Core/            # ActivityDefinitionVersion entity:
│                                                        #   Version : string (immutable) + SemVerSortKey (real CLR prop, hidden from interface, immutable)
│
├── Elsa.Activities.Design.Persistence.EFCore(.Sqlite)/ # OrderDefinition reworked to order by SemVerSortKey; fresh migration
│
├── Elsa.Activities.Design.Reconciliation.Core/         # RECEIVES (FR-021):
│   ├── IActivityReconciliationSource.cs                #   moved from the feature project
│   ├── Models/ActivityVersionReconciliationModel.cs    #   moved; Version : string
│   └── (existing: OnActivityVersionsReconciling, IActivityVersionReconciler, IActivityDefinitionHasher, ActivityVersionHashMismatchException : string)
│
├── Elsa.Activities.Design.Reconciliation/              # RESHAPED (FR-021): non-abstract, no Sources;
│                                                        #   registers options + default hasher + reconciler + single startup task + universal handler
│
├── Elsa.Activities.Design.Reconciliation.Clr/          # NEW (FR-010): clean IShellFeature
│   ├── ClrActivityReconciliationFeature.cs             #   registers the source; ClrReconciliationOptions { FolderPath, SourceId }
│   ├── ClrActivityReconciliationSource.cs              #   IActivityReconciliationSource, SourceKind="CLR"
│   ├── ClrAssemblyScanner.cs                           #   MetadataLoadContext scan → discovered activity models
│   ├── ActivityVersionResolver.cs                      #   attribute → else assembly-version → semver (FR-020)
│   └── (domain-scoped exceptions per §2.23.5)
│
└── (semver value + comparator)                         # SemVer type + ISemVerComparer (Strategy) — see research.md for home
```

**Structure Decision**: Extends the existing Unit B `Elsa.Activities.Design.*` + `Elsa.Activities.Runtime.Core` tree. Two structural additions: (1) the `.Reconciliation.Clr` provider feature; (2) the abstractions move into the existing `Elsa.Activities.Runtime.Core`. Both validated above (G4, G12, G15). An **`Elsa.Activities.Design`/`Elsa.Activities.Runtime` split** alongside the established `Elsa.Workflows.Design`/`Elsa.Workflows.Runtime` split is the natural, symmetric decomposition — it dissolves the `Workflows.Runtime.Core` catch-all rather than growing it, which is the decomposition intent (§E2.3-style).

## Complexity Tracking

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| **G13** — `ActivitiesDesignReconciliationFeature` changes shape (abstract→non-abstract, drops `Sources`) without a create-new/retire rename | The abstract-base-with-`Sources` shape is a defect (mis-uses inheritance for a DI-contribution concern, and permits duplicate startup tasks). It was never ratified/shipped. The feature **name is unchanged**, so the §2.19 rename machinery does not apply. | Creating a new feature + retiring the old one would churn the feature name for no consumer benefit and contradict FR-021's "correct in place." The §2.21.1 golden rule (existing tests pass) bounds the risk. |

*(The `Elsa.Activities.Runtime.Core` extraction is **not** a gate violation — G3/G12/G15 PASS — so it is not tracked here. It is an adopted structural decision; see Summary item 2 and Structure Decision.)*
