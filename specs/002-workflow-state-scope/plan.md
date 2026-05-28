# Implementation Plan: Unit C — Workflow Definition State Scope + Workflow Design Substrate

**Branch**: `002-workflow-state-scope` | **Date**: 2026-05-28 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/002-workflow-state-scope/spec.md`

## Summary

Unit C pins the scope of `WorkflowDefinitionState` as the canonical authored document of a workflow definition, ratifies the architectural triplet (`WorkflowDefinitionState` ↔ read models/projections ↔ `WorkflowExecutable`), and lands the supporting workflow-design substrate: layout sibling entities (FR-006..FR-008), NodeId rename + `ActivityVersionId` collapse (FR-009..FR-011a), legacy `WorkflowMetadata` deletion (FR-015..FR-015a), Model X reconciliation policy (FR-016..FR-016c), Draft event-sourcing architectural slot with 16 granular mutation events + 2 lifecycle events + 16 CQS commands + 1 coarse `OnDraftValidating` event (FR-017..FR-020), validation sibling + delete-and-re-add lifecycle + "no Version with errors" promotion gate (FR-021..FR-024a), per-Draft distributed lock with mutation/promotion serialisation (FR-027..FR-027c), Clone-from-Version + Discard-Draft commands (FR-028..FR-029), DOMAIN_EVENTS catalog + reflection-based parity test (FR-030..FR-031a), and the Validations sub-domain with five baseline validators (FR-032..FR-036).

Technical approach is conservative — the spec leans on existing constitutional rules (framework §2.6.1, §2.10, §2.20, §E2.2) and only introduces new modules where they are genuinely needed. Three new packages ship: `Elsa.Workflows.Design.Validations.Core`, `Elsa.Workflows.Design.Validations`, and (via the relocated entities in FR-006..FR-008/FR-021) the existing `Elsa.Workflows.Design.Persistence.Core` + `Elsa.Workflows.Design.Persistence.EFCore` gain new entities and EF configurations. The new `Elsa.Workflows.Design.Core/DOMAIN_EVENTS.md` and `Elsa.Workflows.Design.Validations.Core/DOMAIN_EVENTS.md` are documentation deliverables policed by an automated parity test. Five provisional constitutional sub-rules (Model X reconciliation policy, Draft event-sourcing architectural slot, "no Version with errors" gate, framework §2.6.1 method-pattern + subscriber-must-never-break-publisher, framework §2.22.1 domain-events catalog) ride through the implementation as gate-flagged items pending 2026-06-01 architecture-review ratification; the implementation provisional-adopts them so the codebase stays in sync.

## Technical Context

**Language/Version**: C# 13 / .NET 10 (per existing `<TargetFramework>net10.0</TargetFramework>` in all csproj files).
**Primary Dependencies**: EF Core 10 (SQLite default provider), System.Text.Json, JetBrains.Annotations, Microsoft.Extensions.{DependencyInjection,Logging,Options}.*Abstractions, CShells.Abstractions, Medallion.Threading (via `Elsa.Locking.FileSystem` adapter), Nuplane (for hot-reload host composition per framework §3 Strategy B).
**Storage**: SQLite (in-foundation default) via `Elsa.Workflows.Design.Persistence.EFCore.Sqlite`. Persistence invariants (immutability of Version entities, `[Immutable]` attribute scanner driving `PropertySaveBehavior.Throw` + `SaveChangesAsync` guard) are provider-agnostic per framework §2.9; the SQLite migration is regenerated fresh per Unit B's "no preserved production data" convention.
**Testing**: xUnit (per existing `tests/Elsa.Activities.Design.Tests/`). New tests project planned: `tests/Elsa.Workflows.Design.Tests/` (covers scope-policy assertion, catalog parity, validator branch coverage, lock semantics, clone/discard, layout cascade). Existing `tests/Elsa.Activities.Design.Tests/` extends with `IsRequired` field assertions on `InputDefinition` / `OutputDefinition`.
**Target Platform**: cross-platform .NET runtime (Windows / Linux / macOS); `Elsa.Server` ASP.NET Core host is the application target.
**Project Type**: modular monolith framework library (multiple packages composed via Nuplane Strategy B per framework §3). NOT a single-project app; Unit C ships several new csproj packages.
**Performance Goals**: design-time path — not a runtime hot path. The mutation pipeline (lock acquisition + snapshot mutation + event dispatch + validators + flush) targets sub-second latency for typical Draft sizes (< 100 activities, < 50 variables). No throughput target; design-time interactions are operator-driven.
**Constraints**: per-Draft lock serialises mutations on the same Draft; mutations on *different* Drafts run in parallel without contention (FR-027a). EF Core migration must remain clean against SQLite. No new heavy NuGet dependencies admitted to any `.Core` package (framework §2.1 / §2.3). Backward compatibility within Unit C scope is governed by framework §2.21.1 (golden rule of refactoring) — existing tests' subjects/objectives preserved.
**Scale/Scope**: Unit C deliverable footprint — 3 new csproj packages, ~20 new entity/contract/event types, ~16 new commands, 5 baseline validators, 1 new test project. The wider Elsa solution carries ~64 csproj packages today; Unit C grows it by 3.

## Constitution Check

*GATE: Walked before Phase 0 (initial) and re-checked after Phase 1 (final). Each gate marked PASS / VIOLATION / N/A. Provisional constitutional sub-rules from clarify sessions 1-3 are gate-flagged with `(prov.)` — they cascade into the codebase under provisional adoption pending 2026-06-01 ratification; this is the working-loop §5 pattern (code temporarily ahead of formally-ratified constitution).*

This plan is governed by a **two-layer constitution**:
- **`.specify/memory/constitution.md`** — Elsa Workflow Engine Constitution v2.0.0 (draft).
- **`.specify/memory/constitution-framework.md`** — Modular Software Design Framework Constitution v2.0.0 (draft).

| # | Gate | Status | Notes |
|---|---|---|---|
| G1 | Three-layer separation applied per feature; no global "Core" library. | **PASS** | Validations follows it (Validations.Core + Validations baseline feature + EF Core mappings co-located in existing `*.Design.Persistence.EFCore`). Layout entities follow it (read contract `IWorkflowDefinitionLayout` in `Design.Core`; entities in `Design.Persistence.Core`; mappings in `Design.Persistence.EFCore`). |
| G2 | Naming uses domain language only. | **PASS** | No `Features.*`, `Modules.*`, `Implementations.*`, `Providers.*`, `Adapters.*`, `.Contracts`, `.Abstractions` segments introduced. `Validations.Core` / `Validations` is sub-domain naming per §2.2. |
| G3 | No heavy dependency in any `.Core` library. | **PASS** | `Validations.Core` declares only Workflows.Design.Core cross-`.Core` reference (for `IWorkflowDefinitionDraft` in `OnDraftValidating`'s payload); no heavy NuGets. |
| G4 | No peer references between Layer-3 impls across unrelated sub-domains. | **PASS** | `Validations` (Layer-3) subscribes to events from `Validations.Core`; does not reference any other Layer-3 implementation. Activity-feature-co-located validators (FR-034) subscribe to `OnDraftValidating` via the same Core-only seam. |
| G5 | Any new contract declares its kind: replacement or contribution. | **PASS** | All new commands are CQS contracts (replacement-style: one handler per command per framework §2.10); all new events are contribution-style domain events (§2.6.1). No ambiguous-kind contracts introduced. |
| G6 | §2.7.1 decision rule applied. | **PASS** | New validators = independent additive contribution → domain-event handler (§2.6.1 / §2.7.1 row 3). No new inheritance chains or adapters introduced in Unit C. |
| G7 | No `DependsOn`-style static feature declarations. | **PASS** | All new feature classes use DI-only registration; fail-fast at construction time per framework §2.11. |
| G8 | Persistence generic constraint is `where TDbContext : DbContext`. | **PASS** | New EF configurations + entity handlers follow the existing pattern; no Elsa-specific base constraint introduced. `ElsaDbContextBase` opt-in regime preserved per §E2.5. |
| G9 | Helper libraries are domain-owned, never referenced from `.Core`, never activatable. | **N/A** | Unit C introduces no helper libraries (§2.4). |
| G10 | Refactor-cost test — preserve NuGet identity wherever possible. | **PASS** | No existing NuGet renames. New packages are net-new (`Elsa.Workflows.Design.Validations.Core` + `Elsa.Workflows.Design.Validations`). `ValidationError` / `OnDraftValidating` / `IWorkflowDefinitionDraftValidation` are net-new types in net-new packages — no relocation of an existing public surface. |
| G11 | Duplication beats dependency; 3-repetition rule for shared utilities only. | **PASS** | Baseline validators ship as small independent handlers; no shared utility helpers extracted prematurely. |
| G12 | Provider module decomposition: no empty umbrella; replace meta NuGet packages with specific sub-package. | **PASS** | `Elsa.Workflows.Design.Validations` ships a single baseline feature; no empty `Validations` umbrella under it. |
| G13 | Feature `name` stable across refactors. | **PASS** | New feature class names are net-new; no in-place rename of existing feature names. |
| G14 | SemVer for `.Core`. | **PASS** | `Validations.Core` is net-new (v1.0.0 starting point); `Workflows.Design.Core` gains new optional types (FR-018 events, FR-018a lifecycle events, `IWorkflowDefinitionLayout`) — additive expansion (MINOR per §4.2). `Activities.Design.Core` gains an additive constructor parameter on two records (FR-036 `IsRequired` defaulted to `false`) — additive (MINOR), backward-compatible call sites preserved. |
| G15 | **Elsa-specific:** `Workflows.Runtime.*` MUST NOT depend on `Workflows.Design.*`. | **PASS** | All Unit C deliverables live under `Workflows.Design.*`; no runtime references introduced. The §E2.2 hard rule remains intact. |
| G16 | **Elsa-specific:** Elsa worked examples live in Elsa constitution §E3. | **PASS** | New §E3.10 worked example landed in `constitution.md` (not `constitution-framework.md`). |
| G17 | Extension methods §2.8 four-question framework. | **N/A** | Unit C introduces no extension methods. |
| G18 | Persistence CQS at contract boundary. | **PASS** | All new persistence contracts are commands (`IAdd*`, `IRemove*`, `IUpdate*`, `ICreate*`, `ICloneDraftFromVersionCommand`, `IDiscardDraftCommand`) that mutate state; queries on the validation sibling go through `IWorkflowDefinitionDraftValidation` read contract — no combined command-query methods. |
| G19 | No dual-integration smell. | **N/A** | Unit C introduces no dual-external-system modules. |
| G20 | **Refactor work:** existing tests preserved across reorganization per framework §2.21.1. | **PASS** | The NodeId rename + `ActivityVersionId` collapse (FR-009..FR-011) are refactors; FR-012 explicitly invokes §2.21.1. The `IsRequired` addition (FR-036) defaults to `false` — backward-compatible. The `WorkflowMetadata` deletion (FR-015..FR-015a) explicitly walks the test surface. |
| G21 | **Domain events are the contribution mechanism.** | **PASS** *(prov.)* | All 19 new domain events in Workflows.Design.Core + Validations.Core are `IDomainEvent`; validators subscribe via `IDomainEventHandler<OnDraftValidating>`. **Provisional:** framework §2.6.1 method-pattern (intent-revealing methods + `sealed class` + `IReadOnlyList<T>` read accessor) — provisionally adopted; ratification 2026-06-01 Item 4. |
| G22 | **No tight logic coupling between concrete implementations.** | **PASS** *(prov.)* | Validators are independent handlers; the §2.6.1 subscriber-MUST-NEVER-break-publisher rule (clarify s2 Q1) enforces this at the dispatcher level. **Provisional:** ratification 2026-06-01 Item 4b. |
| G23 | Generic dispatch is not a coupling mechanism. | **PASS** | All event publication goes through `IDomainEventSender.Send`; no `IMediator` smuggling. The dispatcher's exception-shielding middleware reinforces this isolation. |
| G24 | Design-time vs runtime contract split. | **PASS** | `OnDraftValidating` is a design-time event in `Validations.Core` — Workflows.Runtime never subscribes. The §E2.2 hard rule is preserved at the package boundary. |
| G25 | Provider-implementation dependencies. | **PASS** | New feature modules depend on Core/Validations.Core/Persistence.Core; never directly on `Persistence.EFCore` or `Persistence.EFCore.Sqlite`. EF mappings live in the provider-specific layer per §2.20 Rule 3. |
| G26 | Feature documentation. | **PASS** | New `DOMAIN_EVENTS.md` catalogs (FR-030) document every event + handler audiences for both `Workflows.Design.Core` and `Workflows.Design.Validations.Core`. Feature-class documentation (handlers registered, tasks registered per framework §2.22) ships alongside the Validations feature. **Provisional:** §2.22.1 catalog sub-rule — ratification 2026-06-01 Item 5. |
| G27 | **Unit test discipline §2.23.** | **PASS with one Complexity Tracking entry** | Validations feature satisfies §2.23.1 (registration test) + §2.23.2 (per-validator branch coverage). The three new `Elsa.Mediator` middleware classes (`DomainEventHandlerIteratorMiddleware`, `DomainEventExceptionShieldingMiddleware`, refactored `DomainEventHandlerInvokerMiddleware`) — already landed in this branch — require branch-covered tests per §2.23.2; deferred to a follow-on item per Complexity Tracking below (no existing `tests/Elsa.Mediator.Tests` project; creating one is scope outside Unit C). |
| G28 | Persistence invariants provider-agnostic. | **PASS** | `WorkflowDefinitionVersionLayout` immutability (FR-006a) is declared in `Workflows.Design.Persistence.Core` via the `[Immutable]` attribute; the EFCore provider honours it through the existing `PropertySaveBehavior.Throw` + `SaveChangesAsync` guard mechanism. Provider-portable. |
| G29 | **Elsa-specific — runtime contract.** | **PASS** | Unit C is design-only; no runtime contract surfaces touched. The `WorkflowExecutable` named in the architectural triplet (FR-001) is downstream (Units E/G); Unit C only names it. |
| G30 | **Elsa-specific — Elsa 3 import-only.** | **N/A** | Unit C introduces no Elsa-3-compat surface. |

**Initial Constitution Check verdict: PASS** with one Complexity Tracking entry (G27 — Mediator middleware tests deferred). Five provisional sub-rules are gate-flagged but not violations — they cascade per the working-loop §5 pattern.

## Project Structure

### Documentation (this feature)

```text
specs/002-workflow-state-scope/
├── spec.md                                  # Feature spec (complete)
├── plan.md                                  # This file
├── research.md                              # Phase 0 output — design decisions for plan-stage items
├── data-model.md                            # Phase 1 output — entity inventory + relationships + lifecycle
├── quickstart.md                            # Phase 1 output — developer onboarding for Unit C deliverables
├── contracts/                               # Phase 1 output — interface contracts (commands + events + read surfaces)
│   ├── commands.md                          # 16 FR-019 mutation commands + ICreateDraftCommand + ICloneDraftFromVersionCommand + IDiscardDraftCommand
│   ├── events.md                            # 16 FR-018 mutation events + 2 FR-018a lifecycle events + OnDraftValidating
│   └── read-surfaces.md                     # IWorkflowDefinitionLayout + IWorkflowDefinitionDraftValidation + IsRequired contract
└── tasks.md                                 # Phase 2 output — produced by /speckit.tasks (NOT created here)
```

### Source Code (repository root)

```text
src/
├── Elsa.Workflows.Design.Core/                                  # EXISTING — Unit C additions:
│   ├── Contracts/
│   │   └── IWorkflowDefinitionLayout.cs                         # NEW (FR-007) — Tier-1 read contract over both layout entities
│   ├── Events/                                                  # NEW directory
│   │   ├── OnDraftCreated.cs                                    # NEW (FR-018 lifecycle origination)
│   │   ├── OnActivityAddedToDraft.cs                            # NEW (FR-018)
│   │   ├── OnActivityRemovedFromDraft.cs                        # NEW (FR-018)
│   │   ├── OnActivityPropertyChangedInDraft.cs                  # NEW (FR-018)
│   │   ├── OnActivityMovedInDraft.cs                            # NEW (FR-018 layout event)
│   │   ├── OnConnectionAddedToDraft.cs                          # NEW (FR-018)
│   │   ├── OnConnectionRemovedFromDraft.cs                      # NEW (FR-018)
│   │   ├── OnVariableDeclaredInDraft.cs                         # NEW (FR-018)
│   │   ├── OnVariableUpdatedInDraft.cs                          # NEW (FR-018)
│   │   ├── OnVariableRemovedFromDraft.cs                        # NEW (FR-018)
│   │   ├── OnWorkflowInputAddedToDraft.cs                       # NEW (FR-018)
│   │   ├── OnWorkflowInputUpdatedInDraft.cs                     # NEW (FR-018)
│   │   ├── OnWorkflowInputRemovedFromDraft.cs                   # NEW (FR-018)
│   │   ├── OnWorkflowOutputAddedToDraft.cs                      # NEW (FR-018)
│   │   ├── OnWorkflowOutputUpdatedInDraft.cs                    # NEW (FR-018)
│   │   ├── OnWorkflowOutputRemovedFromDraft.cs                  # NEW (FR-018)
│   │   ├── OnDraftClonedFromVersion.cs                          # NEW (FR-018a lifecycle)
│   │   └── OnDraftDiscarded.cs                                  # NEW (FR-018a lifecycle)
│   ├── Models/
│   │   ├── WorkflowDefinitionState.cs                           # EXISTING — XML doc header added per FR-003
│   │   ├── ActivityNode.cs                                      # EXISTING — ReferenceKey → NodeId rename (FR-009); (activityDefinitionId, version) → ActivityVersionId (FR-011)
│   │   ├── ActivityPortConnection.cs                            # EXISTING — ActivityReferenceKey → NodeId-named property (FR-009; final name decided in research.md)
│   │   └── WorkflowMetadata.cs                                  # DELETED (FR-015)
│   └── DOMAIN_EVENTS.md                                         # NEW (FR-030) — catalog of all events in this domain
│
├── Elsa.Workflows.Design.Validations.Core/                      # NEW PROJECT (FR-032)
│   ├── Elsa.Workflows.Design.Validations.Core.csproj            # NEW — references Workflows.Design.Core
│   ├── Contracts/
│   │   └── IWorkflowDefinitionDraftValidation.cs                # NEW (FR-021 read surface) — relocated from Workflows.Design.Core
│   ├── Events/
│   │   └── OnDraftValidating.cs                                 # NEW (FR-025) — relocated from Workflows.Design.Core
│   ├── Models/
│   │   └── ValidationError.cs                                   # NEW (FR-022) — relocated from Workflows.Design.Core
│   └── DOMAIN_EVENTS.md                                         # NEW (FR-030) — catalog for Validations.Core
│
├── Elsa.Workflows.Design.Validations/                           # NEW PROJECT (FR-032 + FR-033)
│   ├── Elsa.Workflows.Design.Validations.csproj                 # NEW — references Validations.Core + Expressions.Core
│   ├── WorkflowDesignValidationsFeature.cs                      # NEW — IFeature for activation; registers all baseline validators
│   ├── Validators/
│   │   ├── OrphanActivityValidator.cs                           # NEW (FR-033 — graph integrity)
│   │   ├── StartActivityValidator.cs                            # NEW (FR-033 — missing/duplicate start)
│   │   ├── VariableUniquenessValidator.cs                       # NEW (FR-033 — case-insensitive uniqueness)
│   │   ├── RequiredInputOutputValidator.cs                      # NEW (FR-033 — required-vs-optional)
│   │   └── VariableExpressionResolverValidator.cs               # NEW (FR-033 — expression.Type == Variable → must resolve)
│   └── README.md                                                # Feature doc per framework §2.22 (handlers + tasks)
│
├── Elsa.Workflows.Design.Persistence.Core/                      # EXISTING — Unit C additions:
│   ├── Entities/
│   │   ├── WorkflowDefinitionVersionLayout.cs                   # NEW (FR-006) — sibling of Version, immutable
│   │   ├── WorkflowDefinitionDraftLayout.cs                     # NEW (FR-006) — sibling of Draft, mutable
│   │   └── WorkflowDefinitionDraftValidation.cs                 # NEW (FR-021) — sibling of Draft, mutable, holds ValidationError list
│   └── Contracts/
│       └── (FR-019 mutation command contracts land here per FR-019a)
│           ├── IAddActivityToDraftCommand.cs                    # NEW (FR-019)
│           ├── IRemoveActivityFromDraftCommand.cs               # NEW (FR-019)
│           ├── IUpdateActivityPropertyInDraftCommand.cs         # NEW (FR-019)
│           ├── IMoveActivityInDraftCommand.cs                   # NEW (FR-019)
│           ├── IAddConnectionToDraftCommand.cs                  # NEW (FR-019)
│           ├── IRemoveConnectionFromDraftCommand.cs             # NEW (FR-019)
│           ├── IDeclareVariableInDraftCommand.cs                # NEW (FR-019)
│           ├── IUpdateVariableInDraftCommand.cs                 # NEW (FR-019)
│           ├── IRemoveVariableFromDraftCommand.cs               # NEW (FR-019)
│           ├── IAddWorkflowInputToDraftCommand.cs               # NEW (FR-019)
│           ├── IUpdateWorkflowInputInDraftCommand.cs            # NEW (FR-019)
│           ├── IRemoveWorkflowInputFromDraftCommand.cs          # NEW (FR-019)
│           ├── IAddWorkflowOutputToDraftCommand.cs              # NEW (FR-019)
│           ├── IUpdateWorkflowOutputInDraftCommand.cs           # NEW (FR-019)
│           ├── IRemoveWorkflowOutputFromDraftCommand.cs         # NEW (FR-019)
│           ├── ICreateDraftCommand.cs                           # NEW (FR-019 lifecycle)
│           ├── ICloneDraftFromVersionCommand.cs                 # NEW (FR-028)
│           └── IDiscardDraftCommand.cs                          # NEW (FR-029)
│
├── Elsa.Workflows.Design.Persistence.EFCore/                    # EXISTING — Unit C additions:
│   ├── Configurations/
│   │   ├── WorkflowDefinitionVersionLayoutConfiguration.cs      # NEW (FR-008)
│   │   ├── WorkflowDefinitionDraftLayoutConfiguration.cs        # NEW (FR-008)
│   │   └── WorkflowDefinitionDraftValidationConfiguration.cs    # NEW (FR-008 — implied; same pattern)
│   ├── Commands/                                                # NEW directory — command implementations per FR-019a
│   │   └── (one impl per FR-019 contract above; each takes the per-Draft lock per FR-027)
│   └── EntityHandlers/                                          # EXISTING — IsSystem shadow-column lift removed (FR-015)
│
├── Elsa.Activities.Design.Core/                                 # EXISTING — Unit C addition (Unit B contract evolution):
│   └── Models/
│       ├── InputDefinition.cs                                   # EXISTING — adds `bool IsRequired { get; init; } = false;` (FR-036)
│       └── OutputDefinition.cs                                  # EXISTING — adds `bool IsRequired { get; init; } = false;` (FR-036)
│
└── Elsa.Activities.Design.Persistence.EFCore/                   # EXISTING — Unit C addition:
    └── Configurations/
        └── (existing InputDefinition / OutputDefinition mappings gain IsRequired column per FR-036; SQLite migration regenerated fresh)

tests/
├── Elsa.Activities.Design.Tests/                                # EXISTING — Unit C additions:
│   └── Unit/
│       └── InputOutputDefinitionTests.cs                        # NEW — IsRequired field assertions (SC-024)
│
└── Elsa.Workflows.Design.Tests/                                 # NEW PROJECT — Unit C primary test surface
    ├── Elsa.Workflows.Design.Tests.csproj                       # NEW
    ├── Unit/
    │   ├── CatalogParityTests.cs                                # SC-020 — DOMAIN_EVENTS.md ↔ assembly parity
    │   ├── EventNamingTests.cs                                  # SC-011 — no bare Input/Output names; WorkflowInput/Output prefix mandatory
    │   ├── MethodPatternTests.cs                                # SC-015 — no raw collections on domain events
    │   ├── BaselineValidatorTests/                              # SC-021 — per-validator branch coverage per §2.23.2
    │   │   ├── OrphanActivityValidatorTests.cs                  # SC-022 (a)
    │   │   ├── StartActivityValidatorTests.cs                   # SC-022 (b)
    │   │   ├── VariableUniquenessValidatorTests.cs              # SC-022 (c)
    │   │   ├── RequiredInputOutputValidatorTests.cs             # SC-022 (d, e)
    │   │   └── VariableExpressionResolverValidatorTests.cs      # SC-022 (f)
    │   ├── ValidationsFeatureRegistrationTests.cs               # SC-021 — §2.23.1 registration test
    │   ├── CrossFeatureValidatorSubscriptionTests.cs            # SC-023 — separate-assembly stub validator works
    │   ├── DraftMutationCommandTests/                           # SC-012 — per-command behaviour + event publication
    │   │   └── (one test class per FR-019 command)
    │   ├── DraftLockSemanticsTests.cs                           # SC-016 — concurrent-mutation serialisation; per-Draft isolation
    │   ├── CloneDraftFromVersionTests.cs                        # SC-017 — deep-equality State + Layout; NodeIds 1:1
    │   ├── DiscardDraftTests.cs                                 # SC-018 — atomic deletion; idempotent; no Version touched
    │   └── PromotionGateTests.cs                                # SC-014 — promotion throws when validation row non-empty
    └── Integration/
        └── (per Constitution Check G27, integration testing is deferred per framework §2.23.6)
```

**Structure Decision**: Modular-monolith library layout per existing `Elsa.Server.slnx` convention. Unit C ships 2 new csproj packages under `src/Elsa.Workflows.Design.Validations.*` plus 1 new test project at `tests/Elsa.Workflows.Design.Tests/`. The new `DOMAIN_EVENTS.md` deliverable lives co-located with each `.Core` project's project root (recommended location per framework §2.22.1). All other changes are additions to existing packages.

## Complexity Tracking

> Items requiring justification because they deviate from a default or defer a constitutional obligation.

| Item | Why deferred / accepted | Simpler alternative rejected because |
|---|---|---|
| **G27 — Mediator middleware tests deferred to follow-on unit.** | The three new middleware classes (`DomainEventHandlerIteratorMiddleware`, `DomainEventExceptionShieldingMiddleware`, refactored `DomainEventHandlerInvokerMiddleware`) require §2.23.2 branch-covered tests. No `tests/Elsa.Mediator.Tests` project exists in the codebase today. The Mediator code change is a framework-constitution cascade applied in this branch; the test project + branch coverage is a sibling deliverable that logically belongs in a *Mediator hygiene* follow-on, not in Unit C. Build is 0w/0e and the 31 existing tests confirm no contract regression at the Mediator boundary. | Adding `tests/Elsa.Mediator.Tests` to Unit C would expand scope by an entire test project, all the registration scaffolding, and ~10–15 unit tests across the three middleware classes — disproportionate against the small middleware delta. Logged in the Unit C follow-up as a follow-on item; not blocking Unit C ratification per Joey 2026-05-28 (Phase-6 cascade documentation). |
| **5 provisional constitutional sub-rules adopted in code before ratification.** | Working-loop §5: code may be temporarily ahead of the formally-ratified constitution. The five sub-rules (Model X, Draft event-sourcing slot, "no Version with errors" gate, framework §2.6.1 method-pattern + subscriber-must-never-break-publisher, framework §2.22.1 catalog) are agenda items 1, 2, 3, 4, 4b, 5, 6 for the 2026-06-01 architecture review. Implementing under provisional adoption ensures the codebase stays in sync as ratification lands; if any sub-rule is revised on Monday, the cascade is small (refactor inside the same branch). | Waiting for formal ratification before implementing would block all of Unit C for 4 days and reintroduce code/constitution drift the working loop is designed to avoid. The agenda explicitly names the provisional adoptions; reviewers know what's pending. |
| **`OnDraftValidating` exception semantics rely on the new exception-shielding middleware.** | FR-027c requires the mutation pipeline to be robust against handler exceptions. The default `Elsa.Mediator` pipeline (Phase-6 cascade) provides exception shielding by default. If an operator composes a custom pipeline that swaps the shielding middleware for fail-fast semantics, FR-027c's contract is broken at the operator's deliberate choice — that is the "default + escape hatch" framing of framework §2.6.1's new sub-rule. The plan accepts this as a feature, not a violation. | Mandating shielding at the framework level (no escape hatch) would over-constrain operators with legitimate fail-fast or aggregate-throw use cases. The default protects the common path; the escape hatch is named and surfaced. |

---

## Phase 0: Outline & Research

**Output**: [research.md](./research.md)

Phase 0 resolves the plan-stage detail decisions listed in the Unit C follow-up's *Open questions → Plan-stage detail decisions* section. None of these are NEEDS CLARIFICATION items blocking the plan — they are concrete plan-stage choices that need pinning before implementation. The research.md captures each with **Decision / Rationale / Alternatives considered** in a single pass.

Items resolved by research.md:
1. `ActivityPortConnection` NodeId-named join key — final name decision (`NodeId` vs `ActivityNodeId`).
2. `ValidationError.Path` format convention.
3. `ValidationError.Type` extensibility convention.
4. Catalog-parity test mechanism (reflection-scan strategy + markdown heading convention).
5. EF cascade rules for `*Layout` and validation siblings.
6. ~~Forbidden-types mechanism for the scope-policy test.~~ **Item retired** per clarify session 3 — scope-policy enforcement is review-discipline-only; future *Code Analysers* epic owns compile-/build-time enforcement.
7. Test-project allocation (single new `tests/Elsa.Workflows.Design.Tests` vs sibling split).
8. Provisional name for `IPromoteDraftToVersionCommand` (Unit D allocates final).
9. `"Variable"` expression-type kind string convention (already used per `InputArgument.cs`).
10. Migration strategy for the `WorkflowMetadata` deletion + `IsRequired` column addition (fresh init vs incremental).

## Phase 1: Design & Contracts

**Outputs**: [data-model.md](./data-model.md), [contracts/](./contracts/), [quickstart.md](./quickstart.md)

Phase 1 produces three artefacts:

**`data-model.md`** — entity inventory + relationships + lifecycle:
- All Unit C entity additions (`WorkflowDefinitionVersionLayout`, `WorkflowDefinitionDraftLayout`, `WorkflowDefinitionDraftValidation`).
- `ValidationError` value object shape (`Path`, `Type`, `Message`).
- Cross-references to existing entities (`WorkflowDefinitionVersion`, `WorkflowDefinitionDraft`, `ActivityNode`, `ActivityPortConnection`).
- Field-level evolution: `ActivityNode.NodeId` (rename), `ActivityNode.ActivityVersionId` (collapse), `InputDefinition.IsRequired` / `OutputDefinition.IsRequired` (additive).
- Lifecycle diagrams: Draft mutation pipeline (FR-027 ordering); Clone-from-Version (FR-028); Discard (FR-029); promotion gate (FR-024); validator delete-and-re-add (FR-023).
- Deletion: `WorkflowMetadata` and its `MetaData` reference on `WorkflowDefinition`; `IsSystem` shadow-column lift.

**`contracts/`** — interface contracts grouped by surface:
- `contracts/commands.md` — full FR-019 command list + ICreateDraft + IClone + IDiscard; one command per file's worth of contract metadata.
- `contracts/events.md` — full FR-018 + FR-018a + FR-025 event list with payload shape (intent-revealing methods + read accessor), publication site, expected handler audiences, ordering guarantees.
- `contracts/read-surfaces.md` — `IWorkflowDefinitionLayout`, `IWorkflowDefinitionDraftValidation`, `IsRequired` contract addition; cross-references to the read-contract Tier 1 pattern (`2026-05-24_ENTITY_DESIGN_SUMMARY_JOEY.md` §3.5).

**`quickstart.md`** — developer onboarding for Unit C deliverables:
- "I want to add a custom validator" — walks the cross-feature subscription pattern.
- "I want to add a new mutation command" — walks the command + event + lock pattern.
- "How does the validation lifecycle work?" — explains delete-and-re-add + the (Path, Type) grouping.
- "Where do activity-specific validators live?" — points at FR-034 + Elsa §E3.10.
- "How does the catalog parity test work?" — points at FR-030 + FR-031 + the parity test.

**Agent context update**: The `CLAUDE.md` in this repo carries a SPECKIT-marked block that points at the active plan. Phase 1 updates that block to reference this `plan.md`.

---

## Phase 2: Task generation (NOT done here)

`/speckit.tasks` produces `tasks.md` from this plan in a separate invocation.

## Cross-references

- Spec: [spec.md](./spec.md)
- Elsa Workflow Engine Constitution: [`.specify/memory/constitution.md`](../../.specify/memory/constitution.md) v2.0.0 (draft) — §E3.10 worked example codifies the activity-domain naming used by FR-034.
- Modular Software Design Framework Constitution: [`.specify/memory/constitution-framework.md`](../../.specify/memory/constitution-framework.md) v2.0.0 (draft) — §2.6.1 carries the method-pattern + subscriber-MUST-NEVER-break-publisher rules under provisional ratification.
- Monday architecture-review agenda: [`../../../elsa-foundation-project-management/epic1-elsa-refactor-constitution/2026-06-01_AGENDA_review_meeting.md`](../../../elsa-foundation-project-management/epic1-elsa-refactor-constitution/2026-06-01_AGENDA_review_meeting.md) — Items 1, 2, 3, 4, 4b, 5, 6 cover Unit C's provisional sub-rules.
- Unit C follow-up: [`../../../elsa-foundation-project-management/epic1-elsa-refactor-constitution/follow-up-items/2026-05-28_unitC_workflow_definition_state_scope.md`](../../../elsa-foundation-project-management/epic1-elsa-refactor-constitution/follow-up-items/2026-05-28_unitC_workflow_definition_state_scope.md) — canonical status document.
- Unit B follow-up: [`../../../elsa-foundation-project-management/epic1-elsa-refactor-constitution/follow-up-items/2026-05-28_unitB_activity_identity_catalog.md`](../../../elsa-foundation-project-management/epic1-elsa-refactor-constitution/follow-up-items/2026-05-28_unitB_activity_identity_catalog.md) — Unit B contract surface that Unit C consumes (Activity catalog, `IActivityDefinitionVersion`).
