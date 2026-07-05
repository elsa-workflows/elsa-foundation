# Implementation Plan: Single Diff-Based Draft Update Command

**Branch**: `main` (spec authored on main; feature dir tracked in `.specify/feature.json`) | **Date**: 2026-06-03 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/003-single-update-command/spec.md`

> **Supersession note (2026-07-05):** the plan's semantic-diff-and-Background-publish stage is retired (no subscribers; event-sourcing slot unbuilt; diff engine remains the tested contract but is unregistered from DI) and the `WorkflowDefinitionDraftValidation` sibling write is gone (entity deleted; errors are derived state — spec 002 FR-021). The coarse `IUpdateDraftCommand`, per-Draft lock, `OnDraftValidating`/`OnDraftValidated` pair, and State persistence stand. Reinstatable when a consumer exists.

## Summary

Unit 2 collapses Unit C's **20 granular Draft-mutation commands** into **one coarse diff-based command**, `IUpdateDraftCommand`. The command receives the *complete desired Draft state* (`UpdateDraftRequest(DraftId, WorkflowDefinitionState State, IReadOnlyCollection<DesignMetadataRecord> Layout)`), and — inside the existing per-Draft distributed lock `workflow-draft:{DraftId}` — loads the stored state, assigns the desired state wholesale, computes a **semantic per-concept diff** (1:1 to the existing 20 mutation event types), runs the unchanged `OnDraftValidating` Sequential gate, persists transactionally, and Background-publishes one event per detected difference followed by `OnDraftValidated`. The command **is** the mutation shell — it absorbs `DraftMutationPipeline.ExecuteMutation`; there is no standalone pipeline collaborator on the mutation path.

Per-concept *event-derivation* logic survives privately (migrated from each deleted command body into an internal `DraftStateDiffer`); the per-action *state mutation* collapses to a single wholesale assignment because the caller supplies the full desired state. Concurrency is **last-writer-wins, whole-draft** (no version column added). Diff identity uses each dimension's existing **stable match key** (connections use their endpoint tuple) — **no id field is added to any State element**, so the State model is untouched. The 4 lifecycle commands stay out of scope. A new provisional constitution sub-section **§E2.9.7** restates the canonical Draft-mutation surface.

Technical approach detail is resolved in [research.md](./research.md) (R1–R11); shapes in [data-model.md](./data-model.md); the public surface in [contracts/update-draft-command.md](./contracts/update-draft-command.md).

## Technical Context

**Language/Version**: C# / .NET (latest LTS per `Elsa.Server.slnx`)
**Primary Dependencies**: EF Core (`Elsa.Workflows.Design.Persistence.EFCore`); the Unit 1 unified event substrate `IEvent`/`IEventHandler<T>`/`IEventPublisher` (`Elsa.Events.Core`/`Elsa.Events`); `IDistributedLockProvider` (existing per-Draft lock)
**Storage**: EF Core-backed Draft persistence (`WorkflowDefinitionDraft` + layout/validation siblings, JSON-shadowed `StateSource`). **No schema change** — no new column, no new entity.
**Testing**: xUnit (no FluentAssertions — constitutionally pinned); existing `tests/Elsa.Workflows.Design.Tests`
**Target Platform**: Server-side workflow engine (Elsa.Server host)
**Project Type**: Modular class-library domain (`Elsa.Workflows.Design.*` family); not web/mobile
**Performance Goals**: No new performance target; one `Execute` does the work previously done by N granular calls under one lock + one transaction (a net reduction in round-trips). Diff is O(elements) per dimension.
**Constraints**: Must preserve event identity (event-sourcing open for Unit H); must preserve the validation pair ordering (cause-before-effect); §E2.2 — `Elsa.Workflows.Runtime.*` MUST NOT depend on `Elsa.Workflows.Design.*` (this feature is entirely Design-side, so direction is trivially respected); §2.21.1 golden rule of refactoring on the migrated command tests.
**Scale/Scope**: ~20 command contracts + 20 impls deleted; 1 contract + 1 impl + 1 internal differ added; ~20 command test files migrated; 1 catalog (`EVENTS.md`) publication-site prose edit; 1 constitution sub-section added.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

This plan is governed by a **two-layer constitution**: `.specify/memory/constitution.md` (Elsa) + `.specify/memory/constitution-framework.md` (framework).

**Pre-Phase-0 walk** (against the spec) and **post-Phase-1 walk** (against data-model/contracts/tree) agree — no design artefact introduced a violation. Single consolidated table:

| # | Gate | Verdict | Justification |
|---|---|---|---|
| G1 | Three-layer separation; no global Core | **PASS** | Reuses the existing `Design.Core` / `Design.Persistence.Core` / `Design.Persistence.EFCore` three-layer split; contract in `.Persistence.Core`, impl + differ in `.Persistence.EFCore`. No new "Core" library. |
| G2 | Domain-language naming; no `.Contracts`/`.Abstractions`/`Features.*` segments | **PASS** | New types `IUpdateDraftCommand`/`UpdateDraftCommand`/`UpdateDraftRequest`/`DraftStateDiffer` use domain language. The `Contracts/` *folder* is the established existing convention in `…Persistence.Core` (folder, not a namespace segment violating §2.2) — consistent with the 4 lifecycle commands already there. |
| G3 | No heavy dep in `.Core` | **PASS** | `IUpdateDraftCommand` + `UpdateDraftRequest` land in `.Persistence.Core` reusing existing types only; EF Core stays in `.Persistence.EFCore`. |
| G4 | No cross-sub-domain impl peer refs | **PASS** | All work is within the `Design` persistence family; the differ is impl-to-impl within the same provider module. No new cross-domain edge. |
| G5 | New contract declares kind (replacement/contribution) | **PASS** | `IUpdateDraftCommand` is a **replacement** persistence command (single registered impl), same kind as the commands it replaces; no contribution semantics. |
| G6 | §2.7.1 composition decision applied | **PASS** | No new feature composition — reuses the existing event substrate (contribution via domain events) and the existing lock adapter. No new inheritance/adapter introduced. |
| G7 | No `DependsOn` static feature deps | **PASS** | Registration is DI fail-fast; no static dependency declaration added. |
| G8 | Persistence generic constraint `where TDbContext : DbContext` | **PASS** | No new generic persistence constraint introduced; reuses existing `EFCoreWorkflowsPersistenceFeatureBase` patterns. |
| G9 | Helper libs domain-owned, not from `.Core` | **PASS** | `DraftStateDiffer` is an internal impl detail of `.Persistence.EFCore`, not a helper library, not referenced from a `.Core`. |
| G10 | Refactor-cost / NuGet-identity test | **PASS** | No NuGet identity change; types are added/removed within existing packages. |
| G11 | Duplication beats dependency | **PASS** | The per-concept diff/emit logic is consolidated into one differ (it was already duplicated structure across 20 commands); no new shared dependency is forced. |
| G12 | Provider module decomposition; no empty umbrella | **PASS** | No new module; EFCore remains the single provider for Design persistence. |
| G13 | Feature name stable | **PASS** | No feature rename; this is a command-surface change within the existing `WorkflowDesignPersistence` feature. |
| G14 | SemVer for `.Core` | **N/A** | The project is not versioning packages yet (per Joey, 2026-06-03). No SemVer bump is computed or asserted; revisit when versioning is introduced. |
| G15 | **Elsa** Runtime MUST NOT depend on Design | **PASS** | Entirely Design-side; introduces no Runtime→Design edge. Seam mechanism untouched. |
| G16 | Elsa examples in Elsa constitution | **PASS** | The new §E2.9.7 worked statement lands in `constitution.md` (Elsa layer), not the framework layer. |
| G17 | Extension methods >3 lines walked vs §2.8 | **PASS / N/A** | No new extension methods; the differ is a class with methods, not extension methods. |
| G18 | Command/query split at contract | **PASS** | `Execute` returns `Task` (no queryable view); pure command. |
| G19 | No hidden dual-external-system integration | **PASS / N/A** | No external system integration added. |
| G20 | **Refactor:** existing tests' subject/objective preserved; deletions need recorded architect approval | **PASS** | No command tests are deleted — they are **moved/migrated** to drive `IUpdateDraftCommand`, because every diff must still be validated to publish the correct event(s) (per Joey, 2026-06-03). Each former `*CommandTests`'s objective ("change X → event E + state S") becomes a diff-driven test asserting the same event + state. Coverage is preserved one-for-one; **every diff dimension keeps a test**. No architect-approval-gated deletion arises. |
| G21 | Domain events are the contribution mechanism | **PASS** | Per-diff events publish on the unified event substrate; the validation contribution uses the Registry-free Sequential gate already in place. No new `IEnumerable<TProvider>` interface introduced. |
| G22 | No tight logic coupling between concrete impls | **PASS** | The command depends on event contracts + the lock provider abstraction, not on another concrete impl's side effects. |
| G23 | Generic dispatch not used as coupling | **PASS** | The validation gate is a declared domain event (`OnDraftValidating`), expecting a specific handler — correctly a domain event, not smuggled through a generic bus. Per-diff events are genuine fire-and-forget Background pub/sub. |
| G24 | Design-time vs runtime contract split | **PASS / N/A** | No contributor surface with dual consumers added; this is a design-side mutation command. |
| G25 | Provider-impl dependencies | **PASS** | The contract sits in provider-agnostic `.Persistence.Core`; the EFCore impl + differ sit in the provider module. Generic consumers depend on `.Persistence.Core`. |
| G26 | Feature documentation (handlers + tasks) | **PASS** | `EVENTS.md` publication-site prose updated to name `IUpdateDraftCommand` as producer; feature doc continues to list the validation handler. No new tasks registered. |
| G27 | Unit test discipline (registration + branch tests; visibility) | **PASS** | `UpdateDraftCommand` is `public sealed` with branch-covering unit tests (add/update/remove/move/no-op/LWW/rename); the feature class stays `public` non-sealed; registration test asserts `IUpdateDraftCommand` resolves. |
| G28 | Persistence invariants provider-agnostic | **PASS** | No invariant change; last-writer-wins is the absence of a token, defined behaviourally in `.Persistence.Core` contract docs, enforced (trivially) in EFCore. |
| G29 | **Elsa** runtime contract — executability preserved | **PASS / N/A** | Design-side only; does not touch runnable-artifact loading. |
| G30 | **Elsa** Elsa-3 compat import-only | **PASS / N/A** | No Elsa-3 import path touched. |

**No violations.** All gates PASS or N/A; Complexity Tracking is empty.

## Project Structure

### Documentation (this feature)

```text
specs/003-single-update-command/
├── plan.md              # This file
├── research.md          # Phase 0 (R1–R11 + resolution table)
├── data-model.md        # Phase 1 (UpdateDraftRequest, diff model, reused shapes, match keys)
├── quickstart.md        # Phase 1 (developer orientation)
├── contracts/
│   └── update-draft-command.md   # Phase 1 (IUpdateDraftCommand + UpdateDraftRequest)
├── checklists/
│   └── requirements.md  # Spec-quality checklist
└── tasks.md             # Phase 2 (/speckit.tasks — NOT created here)
```

### Source Code (repository root)

```text
src/
├── Elsa.Workflows.Design.Core/
│   ├── Models/WorkflowDefinitionState.cs           # REUSED unchanged
│   ├── Events/                                     # 20 mutation events REUSED (producer re-homed)
│   └── EVENTS.md                                   # EDIT: publication-site prose → IUpdateDraftCommand
├── Elsa.Workflows.Design.Persistence.Core/
│   ├── Contracts/
│   │   ├── IUpdateDraftCommand.cs                  # NEW (+ UpdateDraftRequest)
│   │   ├── ICreateDraftCommand.cs                  # KEPT (lifecycle, out of scope)
│   │   ├── ICloneDraftFromVersionCommand.cs        # KEPT
│   │   ├── IDiscardDraftCommand.cs                 # KEPT
│   │   ├── IPromoteDraftToVersionCommand.cs        # KEPT (Unit D)
│   │   └── I*DraftCommand.cs  (×20 mutation)       # DELETED
│   └── Entities/WorkflowDefinitionDraft.cs         # REUSED unchanged (no version column)
├── Elsa.Workflows.Design.Persistence.EFCore/
│   ├── Commands/
│   │   ├── UpdateDraftCommand.cs                   # NEW (absorbs ExecuteMutation)
│   │   ├── DraftStateDiffer.cs                     # NEW (internal; stored-vs-desired → IEvent list)
│   │   ├── CreateDraftCommand.cs / Clone…/Discard… # KEPT (ExecuteCreation lingers → follow-up)
│   │   └── *DraftCommand.cs  (×20 mutation)        # DELETED (apply/emit logic migrated into differ)
│   ├── DraftMutationPipeline.cs                    # ExecuteMutation removed; ExecuteCreation lingers
│   └── EFCoreWorkflowsPersistenceFeatureBase.cs    # DI: drop 20 regs, add IUpdateDraftCommand reg
├── Elsa.Workflows.Design.Validations.Core/Events/  # OnDraftValidating/Validated REUSED unchanged
└── Elsa.Workflows.Design.Validations/Handlers/ExecuteValidations.cs  # REUSED unchanged

tests/
└── Elsa.Workflows.Design.Tests/
    ├── Unit/CatalogParityTests.cs                  # stays green (event types unchanged)
    └── …/*CommandTests.cs                          # MIGRATED to drive IUpdateDraftCommand

.specify/memory/constitution.md                     # ADD provisional §E2.9.7 (Draft-mutation surface)
```

**Structure Decision**: No new project/module. The change is contained to the existing `Elsa.Workflows.Design.*` family: contract in `.Persistence.Core/Contracts/`, impl + internal differ in `.Persistence.EFCore/Commands/`, catalog + constitution edits. This respects G1/G12/G15 (Design-side only, no new umbrella, no Runtime→Design edge).

## Complexity Tracking

> No Constitution Check violations. Nothing to justify.

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| *(none)* | | |
