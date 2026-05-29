---
description: "Task list for Unit C — Workflow Definition State Scope + Workflow Design Substrate"
---

# Tasks: Unit C — Workflow Definition State Scope + Workflow Design Substrate

**Input**: Design documents from `/specs/002-workflow-state-scope/`
**Prerequisites**: [spec.md](./spec.md), [plan.md](./plan.md), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/](./contracts/), [quickstart.md](./quickstart.md)

**Tests**: Tests are EXPLICITLY in scope per spec.md (FR-031, SC-011, SC-012, SC-013, SC-016, SC-017, SC-018, SC-020, SC-021, SC-022, SC-023, SC-024) + framework §2.23.1 + §2.23.2.

**Organization**: Phase 3-5 cover the spec's three user stories (US1/US2/US3). Phases 6-11 are non-user-story Unit C substrate that the spec brought in via clarify-session FRs (FR-016 onwards) — they ship as Unit C deliverables but are not user-story-shaped.

## Format: `[ID] [P?] [Story?] Description`

- **[P]**: parallelizable (different files, no dependencies on incomplete tasks).
- **[Story]**: REQUIRED for user-story-phase tasks (US1 / US2 / US3); omitted for Setup, Foundational, substrate, and Polish phases.
- Exact file paths included in every task.

## Path Conventions

Modular-monolith .NET 10 layout. Source under `src/`, tests under `tests/`. Solution file: `Elsa.Server.slnx`.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Project scaffolding for the two new Validations packages + one new test project.

- [X] T001 Create new csproj `src/Elsa.Workflows.Design.Validations.Core/Elsa.Workflows.Design.Validations.Core.csproj` with `<TargetFramework>net10.0</TargetFramework>`, `<ImplicitUsings>enable</ImplicitUsings>`, `<Nullable>enable</Nullable>`; add `ProjectReference` to `Elsa.Workflows.Design.Core` (cross-`.Core` per framework §2.1). Allowed external NuGets: `Microsoft.Extensions.*Abstractions` + `Microsoft.Extensions.Primitives` only per framework §2.1, §2.3.
- [X] T002 Create new csproj `src/Elsa.Workflows.Design.Validations/Elsa.Workflows.Design.Validations.csproj` with `<TargetFramework>net10.0</TargetFramework>`; add `ProjectReference` to `Elsa.Workflows.Design.Validations.Core` + `Elsa.Expressions.Core` + `Elsa.Mediator.Core` + `CShells.Abstractions` (for `IFeature` per Elsa convention).
- [X] T003 Create new tests csproj `tests/Elsa.Workflows.Design.Tests/Elsa.Workflows.Design.Tests.csproj` with xUnit per the existing `tests/Elsa.Activities.Design.Tests/` pattern (no FluentAssertions — constitutionally pinned to raw xunit); add `ProjectReference` to all `Elsa.Workflows.Design.*`, `Elsa.Workflows.Design.Validations.*`, `Elsa.Activities.Design.Core`, `Elsa.Mediator.Core`, `Elsa.Persistence.EFCore.Sqlite`.
- [X] T004 [P] Add the three new csproj files to `Elsa.Server.slnx` (root-relative `<Project Path="src/Elsa.Workflows.Design.Validations.Core/...">` entries; same for `.Validations` and `tests/...`).
- [X] T005 [P] Run `dotnet restore Elsa.Server.slnx` and `dotnet build Elsa.Server.slnx --nologo` to confirm scaffolding compiles clean (expected: 0 warnings / 0 errors on empty packages).

**Checkpoint**: Scaffolding compiles; new packages registered in the solution.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Contract-evolution work that other phases depend on. The `IsRequired` addition (FR-036) lands here because every validator + every input/output construction site touches it; the `WorkflowMetadata` deletion (FR-015) lands here because EF migrations regenerate against the new shape.

**⚠️ CRITICAL**: User story work can begin in parallel after this phase completes.

### IsRequired contract addition (FR-036)

- [X] T006 Add `bool IsRequired = false` constructor parameter to `src/Elsa.Activities.Design.Core/Models/InputDefinition.cs` (positioned after existing optional parameters; default `false` preserves backward compatibility per framework §2.21.1).
- [X] T007 Add `bool IsRequired = false` constructor parameter to `src/Elsa.Activities.Design.Core/Models/OutputDefinition.cs` (same shape as T006).
- [X] T008 Update EF configuration in `src/Elsa.Activities.Design.Persistence.EFCore/Configurations/` for the activity-side input/output mappings to map the new `IsRequired` column (boolean, default `false`). **Resolved as no-op:** InputDefinition/OutputDefinition are serialized to JSON via `InputsSource`/`OutputsSource` in `ActivityDefinitionVersionSavingHandler`, not mapped column-by-column. The new `IsRequired` field flows through the JSON serializer automatically; no EF configuration change required. Note discovery for the next task-list regeneration pass.

### WorkflowMetadata deletion (FR-015 + FR-015a)

- [X] T009 Delete `src/Elsa.Workflows.Design.Core/Models/WorkflowMetadata.cs` (FR-015).
- [X] T010 Remove `MetaData` property from `WorkflowDefinition` in `src/Elsa.Workflows.Design.Persistence.Core/Entities/WorkflowDefinition.cs` (FR-015). Also removed `MetaData` from `IWorkflowDefinition` contract; cascaded through `WorkflowsVersionProvisioner.Map`, Elsa3 `WorkflowDefinitionImport` + mapper.
- [X] T011 Remove the `IsSystem` shadow-column lift from `src/Elsa.Workflows.Design.Persistence.EFCore/EntityHandlers/WorkflowDefinitionSavingHandler.cs` (FR-015). Handler was no-op after removal — deleted the file entirely. DI registration in `EFCoreWorkflowsPersistenceFeatureBase.AddEntitySavingHandlersFrom` is assembly-scan-based so survives without code change.
- [X] T012 Remove any persistence-side mapping that references `WorkflowMetadata` in `src/Elsa.Workflows.Design.Persistence.EFCore/Configurations/` (FR-015). Removed `ConfigureMetaDataValueObject` from `WorkflowDefinitionConfiguration`. Also removed `IsSystem` + `MaterializerName` filters from `WorkflowDefinitionFilter`; `IWorkflowDefinitionLookup.ListDefinitions` signature dropped the `isSystem` parameter; API surface (`AddDefinition`, `WorkflowDefinitionView`, `ListDefinitions`, `Expressions.DefinitionSelector`, `DefinitionToView`, `ListDefinitionsRequestHandler`) cleaned up.
- [X] T013 Walk the test surface for `WorkflowMetadata` references in `tests/Elsa.Activities.Design.Tests/` and any future test projects; per FR-015a: tests whose subject IS `WorkflowMetadata` are candidates for deletion. **Grep result: zero test references** to `WorkflowMetadata` / `MetaData` / `IsSystem`. No test deletion required; 31/31 pre-existing tests still pass.

### Fresh-init migration regeneration (R10)

- [X] T014 Delete existing `Migrations/` folder for `ActivitiesDesignDbContext` in `src/Elsa.Activities.Design.Persistence.EFCore.Sqlite/`.
- [X] T015 Delete existing `Migrations/` folder for `WorkflowsDesignDbContext` in `src/Elsa.Workflows.Design.Persistence.EFCore.Sqlite/`.

**Checkpoint**: Contract changes landed; downstream packages can rebuild against the new `IsRequired` field and the deleted `WorkflowMetadata`.

---

## Phase 3: User Story 1 — Codify scope + Model X + architectural triplet (Priority: P1) 🎯 MVP

**Goal**: Ratify the `WorkflowDefinitionState` scope policy + Model X reconciliation policy + the architectural triplet. Constitutional codification is the load-bearing deliverable; Stories 2 and 3 deliver structural support but make no sense without the rule they support.

**Independent Test**: Land the constitution amendment + add the documentation header on `WorkflowDefinitionState`. Verify against today's State that the existing members (`Variables`, `ActivityConnections`, `Activities`, `Inputs`, `Outputs`, `WorkflowActivityOptions`, `StrategyOptions`) are clean against the policy by review (per FR-005). The scope-policy test was retired per session-3 Q3 — review discipline carries the rule until the future *Code Analysers* epic opens.

### Implementation for User Story 1

- [X] T016 [US1] Verify the Elsa constitution §E2.X scope policy + architectural triplet sub-section + Model X sub-section are landed in `.specify/memory/constitution.md` (FR-001, FR-002, FR-016c). **Verification surfaced a gap** — §E2.X scope policy + triplet were NOT yet committed (only Model X was, at the end of §E2.8 from Unit B cascade). Per Joey's MVP scope, drafted new **§E2.9 "`WorkflowDefinitionState` scope policy + architectural triplet"** with six sub-sections covering FR-001 (a)+(b); Joey reviewed and approved. Model X is referenced from §E2.9.5 (body lives at end of §E2.8 — cascade landing).
- [X] T017 [US1] Add XML doc header to `src/Elsa.Workflows.Design.Core/Models/WorkflowDefinitionState.cs` quoting the in-State / out-of-State boundary and pointing at constitution §E2.X (FR-003). **Done — cites §E2.9 explicitly** (the section landed in T016).
- [X] T018 [US1] Execute the FR-005 audit: read the current `WorkflowDefinitionState` record, confirm `Variables`, `ActivityConnections`, `Activities`, `Inputs`, `Outputs`, `WorkflowActivityOptions`, `StrategyOptions` are clean against the policy. Record findings in the Unit C follow-up's "audit results" section. **Audit clean — no creep.** Documented in Unit C follow-up.
- [X] T019 [US1] Verify FR-016a — confirm no provenance fields (`SourceKind`, `SourceId`, `SourceVersion`, `ProvisioningHash`, `ProvisionedAt`, `ProvisionedBy`) are added to any workflow-design entity in this unit. **Verified — grep returns zero hits in `src/Elsa.Workflows.Design.*/**/*.cs`**. Existing `WorkflowDefinitionVersion.SourceCreatedAt` survives unchanged per FR-016a.
- [X] T020 [US1] Cross-reference check: §E2.X is referenced from §E2.2 (Design ↔ Runtime split) and §E2.6 (artifact-only runtime) per FR-001. Verify the constitution markdown has those backlinks; add if missing. **Cross-reference paragraphs added to both §E2.2 (after §E2.2.4) and §E2.6.2 (after the Hard rule sentence).**

**Checkpoint**: US1 deliverables complete — constitution carries the rule, the record file documents it, and the audit confirms current State is clean. Stories 2 + 3 can now proceed.

---

## Phase 4: User Story 2 — Extract designer layout into sibling entities (Priority: P2)

**Goal**: Designer layout is owned by two normalized sibling entities (`WorkflowDefinitionVersionLayout` / `WorkflowDefinitionDraftLayout`), unified by the read contract `IWorkflowDefinitionLayout`. Layout is removed from any path that could land inside `WorkflowDefinitionState`.

**Independent Test**: A `WorkflowDefinitionVersion` carries layout via its sibling row; loading `WorkflowDefinitionState` alone returns zero design-metadata records. A `WorkflowDefinitionDraft` carries layout via its (mutable) sibling row. Design-time consumers can read both via `IWorkflowDefinitionLayout` without depending on `*.Persistence.Core`.

### Implementation for User Story 2

- [X] T021 [P] [US2] Create the read contract `src/Elsa.Workflows.Design.Core/Contracts/IWorkflowDefinitionLayout.cs` (Id + Records:IReadOnlyList<IDesignMetadataRecord>). Plus inner contract `IDesignMetadataRecord` (NodeId, X, Y, Width?, Height?, AdditionalProperties?). FR-007.
- [X] T022 [P] [US2] Create the value-object `src/Elsa.Workflows.Design.Persistence.Core/Entities/DesignMetadataRecord.cs` (sealed record). Implements `IDesignMetadataRecord`. Uses concrete `Dictionary<string, object?>` for `AdditionalProperties` with explicit interface impl exposing it as `IReadOnlyDictionary` via the contract.
- [X] T023 [P] [US2] Create the entity `src/Elsa.Workflows.Design.Persistence.Core/Entities/WorkflowDefinitionVersionLayout.cs` with `[Immutable]` markers on `WorkflowDefinitionVersionId` + `Records`; implements `IWorkflowDefinitionLayout`. Extends `TenantEntity`. FR-006 + FR-006a.
- [X] T024 [P] [US2] Create the entity `src/Elsa.Workflows.Design.Persistence.Core/Entities/WorkflowDefinitionDraftLayout.cs` (mutable); implements `IWorkflowDefinitionLayout`. Extends `TenantEntity`. FR-006 + FR-006a.
- [X] T025 [US2] Create EF Core configuration `src/Elsa.Workflows.Design.Persistence.EFCore/Configurations/WorkflowDefinitionVersionLayoutConfiguration.cs` — FK to `WorkflowDefinitionVersion` with `OnDelete(DeleteBehavior.Restrict)` (R5). Map `Records` via `HasConversion` + `System.Text.Json` + `ValueComparer`; stored as `TEXT` with `HasMaxLength(-1)`. FR-008.
- [X] T026 [US2] Create EF Core configuration `src/Elsa.Workflows.Design.Persistence.EFCore/Configurations/WorkflowDefinitionDraftLayoutConfiguration.cs` — FK to `WorkflowDefinitionDraft` with `OnDelete(DeleteBehavior.Cascade)` (R5). Same JSON conversion as T025.
- [X] T027 [US2] Register both layout entities in `src/Elsa.Workflows.Design.Persistence.EFCore/DbContext/WorkflowsDesignDbContext.cs`.
- [X] T028 [P] [US2] Tests `tests/Elsa.Workflows.Design.Tests/Unit/LayoutEntityTests/VersionLayoutImmutabilityTests.cs` — reflection-based: `[Immutable]` markers on `WorkflowDefinitionVersionId` + `Records`; init-only setters; sealed class. 4 tests.
- [X] T029 [P] [US2] Tests `tests/Elsa.Workflows.Design.Tests/Unit/LayoutEntityTests/DraftLayoutMutabilityTests.cs` — entity-specific properties NOT marked `[Immutable]` (base Entity's RowNumber/CreatedAt ride through correctly); mutable setter on `Records`; sealed class. 3 tests.
- [X] T030 [P] [US2] Tests `tests/Elsa.Workflows.Design.Tests/Unit/LayoutEntityTests/ReadContractTests.cs` — both entities implement `IWorkflowDefinitionLayout`; `DesignMetadataRecord` implements `IDesignMetadataRecord`; contract returns expected projection; assembly-boundary check (contract in `Workflows.Design.Core`, entities in `Persistence.Core`). 5 tests.
- [X] T031 [US2] Tests `tests/Elsa.Workflows.Design.Tests/Unit/LayoutEntityTests/StateUnaffectedTests.cs` — `WorkflowDefinitionState` has no Layout-typed members + no Layout/DesignMetadata in member names. 2 tests.

**Checkpoint**: Layout siblings ship; State is layout-free; read contract works.

---

## Phase 5: User Story 3 — Unify NodeId terminology + collapse catalog reference (Priority: P2)

**Goal**: `ActivityNode.ReferenceKey` → `NodeId`; `ActivityPortConnection.ActivityReferenceKey` → `ActivityNodeId` (per R1); `(activityDefinitionId, version)` pair collapsed into single `ActivityVersionId : string`. Argument/variable-level `ReferenceKey` identifiers unchanged.

**Independent Test**: After the rename + collapse, zero occurrences of `ActivityNode.ReferenceKey` or `ActivityPortConnection.ActivityReferenceKey` remain in the Workflows.Design tree; zero occurrences of the old `(activityDefinitionId, version)` pair remain on `ActivityNode` and adjacent design-side models. Argument-level identifiers (FR-010) are unchanged. All existing tests on affected paths pass per framework §2.21.1.

### Implementation for User Story 3

- [X] T032 [US3] Rename `ActivityNode.ReferenceKey` → `NodeId` in `src/Elsa.Workflows.Design.Core/Models/ActivityNode.cs` (FR-009). **Resolved via Joey's clarification 2026-05-28:** `NodeId` already existed on the record; `ReferenceKey` was a leftover field (used by the Elsa3 mapper to carry `source.Name`). The "rename" landed as a **drop** of `ReferenceKey` — the existing `NodeId` IS the rename's intended endpoint. Elsa3 mapper updated to no longer pass `source.Name`.
- [X] T033 [US3] Rename `ActivityPortConnection.ActivityReferenceKey` → `ActivityNodeId` (final name per R1) in `src/Elsa.Workflows.Design.Core/Models/ActivityPortConnection.cs` (FR-009).
- [X] T034 [US3] Update all direct consumers of `ActivityNode.ReferenceKey`: grep audit found one construction site — `src/Elsa3.Mapping/Mappings/Elsa3ActivityToState.cs`. Updated.
- [X] T035 [US3] Update all direct consumers of `ActivityPortConnection.ActivityReferenceKey`: grep audit found one construction site — `src/Elsa3.Mapping/Mappings/Elsa3WorkflowDefinitionToState.cs` `MapPort` (uses positional constructor — no rename needed).
- [X] T036 [US3] Replace `(activityDefinitionId : string, version : int)` pair on `ActivityNode` with single `ActivityVersionId : string` field (FR-011). Updated the record's constructor signature.
- [X] T037 [US3] Update all consumers of the prior `(activityDefinitionId, version)` pair on `ActivityNode` — `Elsa3ActivityToState.cs` now passes `version.Id` (the Unit B catalog row id) per FR-011a's "string typing is the seam".
- [X] T038 [US3] Confirmed: no shared contract type introduced. `ActivityVersionId` is a bare `string`; no value object, no marker interface in `Elsa.Activities.Design.Core`.
- [X] T039 [US3] All existing tests still pass: `tests/Elsa.Activities.Design.Tests` 31/31 green. Subject/objective preserved per framework §2.21.1.
- [X] T040 [P] [US3] Test `tests/Elsa.Workflows.Design.Tests/Unit/NodeIdRenameTests.cs` — reflection-based: 4 tests pinning the removed/present properties on both records.
- [X] T041 [P] [US3] Test `tests/Elsa.Workflows.Design.Tests/Unit/ActivityVersionIdCollapseTests.cs` — reflection-based: 3 tests asserting the old pair is gone and `ActivityVersionId : string` is present.

**Checkpoint**: US3 deliverables complete — terminology unified, catalog reference collapsed, existing tests pass per §2.21.1.

---

## Phase 6: Cross-cutting substrate — Draft event surface (FR-018 + FR-018a + FR-025)

**Purpose**: Declare the 19 domain events that the Draft event-sourcing architectural slot rests on (16 FR-018 mutation events + 2 FR-018a lifecycle events in `Workflows.Design.Core`; 1 FR-025 `OnDraftValidating` in `Workflows.Design.Validations.Core`).

**Note**: All events are `sealed class IDomainEvent` per framework §2.6.1's intent-revealing-methods sub-rule. The Mediator pipeline cascade (Phase-6 in the constitution SIR) is already landed (committed in clarify session 1); no rework here.

### Event types in Workflows.Design.Core (FR-018 + FR-018a — 18 events)

- [X] T042 [P] Create `src/Elsa.Workflows.Design.Core/Events/OnDraftCreated.cs`. Sealed class with primary ctor; payload `DraftId`, `WorkflowDefinitionId`.
- [X] T043 [P] Create `src/Elsa.Workflows.Design.Core/Events/OnActivityAddedToDraft.cs`. **Decision:** passes `ActivityNode` directly (sealed record, immutable by structure) rather than the spec's phantom `IActivityNodeView`; introducing a parallel IView interface adds surface area without strengthening the non-mutating guarantee. Derived projections `NodeId` + `ActivityVersionId` expose convenience accessors.
- [X] T044 [P] Create `src/Elsa.Workflows.Design.Core/Events/OnActivityRemovedFromDraft.cs`.
- [X] T045 [P] Create `src/Elsa.Workflows.Design.Core/Events/OnActivityPropertyChangedInDraft.cs`. **Subsequently DELETED per Joey iteration 2026-05-28 round 2** — the generic event was redundant once the 6 specialized per-activity input/output CRUD events landed. All per-activity mutations now route through specialized commands; if a future non-input/non-output property surfaces (e.g. an `IsStart` toggle command), it gets its own dedicated event rather than a generic catch-all.
- [X] T046 [P] Create `src/Elsa.Workflows.Design.Core/Events/OnActivityMovedInDraft.cs`.
- [X] T047 [P] Create `src/Elsa.Workflows.Design.Core/Events/OnConnectionAddedToDraft.cs`. Passes `ActivityConnection` directly (same record-as-view rationale).
- [X] T048 [P] Create `src/Elsa.Workflows.Design.Core/Events/OnConnectionRemovedFromDraft.cs`. **Decision:** passes the full `ActivityConnection` removed (no `Id` field exists on connections — source+target IS the identity); spec mentioned a `ConnectionId` field that doesn't exist on the model.
- [X] T049 [P] Create `src/Elsa.Workflows.Design.Core/Events/OnVariableDeclaredInDraft.cs`. Passes `VariableDefinition` directly.
- [X] T050 [P] Create `src/Elsa.Workflows.Design.Core/Events/OnVariableUpdatedInDraft.cs`.
- [X] T051 [P] Create `src/Elsa.Workflows.Design.Core/Events/OnVariableRemovedFromDraft.cs`.
- [X] T052 [P] Create `src/Elsa.Workflows.Design.Core/Events/OnWorkflowInputAddedToDraft.cs`. Passes `InputDefinition` directly.
- [X] T053 [P] Create `src/Elsa.Workflows.Design.Core/Events/OnWorkflowInputUpdatedInDraft.cs`.
- [X] T054 [P] Create `src/Elsa.Workflows.Design.Core/Events/OnWorkflowInputRemovedFromDraft.cs`.
- [X] T055 [P] Create `src/Elsa.Workflows.Design.Core/Events/OnWorkflowOutputAddedToDraft.cs`. Passes `OutputDefinition` directly.
- [X] T056 [P] Create `src/Elsa.Workflows.Design.Core/Events/OnWorkflowOutputUpdatedInDraft.cs`.
- [X] T057 [P] Create `src/Elsa.Workflows.Design.Core/Events/OnWorkflowOutputRemovedFromDraft.cs`.
- [X] T058 [P] Create `src/Elsa.Workflows.Design.Core/Events/OnDraftClonedFromVersion.cs` (FR-018a lifecycle).
- [X] T059 [P] Create `src/Elsa.Workflows.Design.Core/Events/OnDraftDiscarded.cs` (FR-018a lifecycle).

**Pre-req added (not in original task list):** `Elsa.Workflows.Design.Core.csproj` gained `ProjectReference` to `Elsa.Mediator.Core` (required by all events for `IDomainEvent`); `Elsa.Workflows.Design.Validations.Core.csproj` gained the same for `OnDraftValidating`.

**Joey iteration 2026-05-28 — per-activity input/output full CRUD + drop generic property event.** Six new specialized events landed for symmetry with workflow-level CRUD and variable CRUD: `OnActivityInputAddedToDraft`, `OnActivityInputUpdatedInDraft`, `OnActivityInputRemovedFromDraft`, `OnActivityOutputAddedToDraft`, `OnActivityOutputUpdatedInDraft`, `OnActivityOutputRemovedFromDraft`. Each carries `DraftId + NodeId + ArgumentState` (Add) or `+ ReferenceKey + Old/New` (Update) or `+ ReferenceKey` (Remove). The generic `OnActivityPropertyChangedInDraft` was then **deleted** — once specialized CRUD events covered inputs/outputs, the generic event was redundant; future non-input/non-output property mutations (e.g. `IsStart` toggle) get their own dedicated event when they surface, not a catch-all. Workflows.Design.Core now publishes **23 events** (21 mutation + 2 lifecycle). `EventNamingTests` asserts all 21 mutation event names + exact count 23. `DOMAIN_EVENTS.md` gained two new sections (Per-activity inputs CRUD + Per-activity outputs CRUD); removed the OnActivityPropertyChangedInDraft entry; catalog-parity test re-passes. **Spec regeneration flag:** FR-018's authoritative count in the spec narrative needs to become "21 mutation events / 23 events total" with the generic property event removed and the 6 input/output CRUD events added.

### `OnDraftValidating` in Validations.Core (FR-025)

- [X] T060 [P] Create `ValidationError` value record at `src/Elsa.Workflows.Design.Validations.Core/Models/ValidationError.cs`. Sealed record `(Path, Type, Message)`. Doc header carries R2 Path format conventions + R3 Type categories.
- [X] T061 Create `src/Elsa.Workflows.Design.Validations.Core/Events/OnDraftValidating.cs`. Canonical §2.6.1 contribution shape — sealed class, primary ctor, private `_errors`, `AddValidationError(ValidationError)`, `public IReadOnlyList<ValidationError> Errors`.

### Tests for the event surface

- [X] T062 [P] Test `tests/Elsa.Workflows.Design.Tests/Unit/EventSurfaceTests/EventNamingTests.cs` — 4 tests: all 16 mutation event names present; both lifecycle event names present; no bare `Input`/`Output` names; exactly 18 events publish from `Workflows.Design.Core`. SC-011.
- [X] T063 [P] Test `tests/Elsa.Workflows.Design.Tests/Unit/EventSurfaceTests/MethodPatternTests.cs` — parametrised over both `.Core` assemblies; asserts sealed class + not a record (uses synthesised `<Clone>$` + `EqualityContract` markers as record signal). SC-015.
- [X] T064 [P] Test `tests/Elsa.Workflows.Design.Tests/Unit/EventSurfaceTests/NoRawCollectionsTests.cs` — parametrised over both assemblies; asserts no public property typed as `ICollection<T>` / `IList<T>` / `List<T>` / `HashSet<T>` / `IDictionary<,>` / `Dictionary<,>`. SC-015.

**Checkpoint**: 19 events declared; tests confirm naming + shape + non-mutability invariants.

---

## Phase 7: Cross-cutting substrate — Draft mutation commands + lock (FR-019 + FR-027)

**Purpose**: 16 granular CQS mutation commands (per FR-019) + 1 lifecycle `ICreateDraftCommand` + lock-semantics infrastructure. Each command takes the per-Draft distributed lock, applies the snapshot mutation, publishes the granular FR-018 event, publishes `OnDraftValidating`, rebuilds the validation sibling, transactional flush, release lock.

**Note**: The `WorkflowDefinitionDraftValidation` sibling is created in Phase 9; until then, command implementations stub the validation flush. Then Phase 9 wires it up.

### Command contracts in Persistence.Core.Contracts (FR-019 + FR-019a)

- [X] T065 [P] Create `IAddActivityToDraftCommand.cs`.
- [X] T066 [P] Create `IRemoveActivityFromDraftCommand.cs`.
- [X] T067 [P] **N/A — DELETED** per Joey iteration 2026-05-28 round 2 (`OnActivityPropertyChangedInDraft` event was removed; the generic property command is redundant once specialized CRUD covers inputs/outputs). Six new contracts replaced it:
  - `IAddActivityInputToDraftCommand.cs`
  - `IUpdateActivityInputInDraftCommand.cs`
  - `IRemoveActivityInputFromDraftCommand.cs`
  - `IAddActivityOutputToDraftCommand.cs`
  - `IUpdateActivityOutputInDraftCommand.cs`
  - `IRemoveActivityOutputFromDraftCommand.cs`
- [X] T068 [P] Create `IMoveActivityInDraftCommand.cs`.
- [X] T069 [P] Create `IAddConnectionToDraftCommand.cs`.
- [X] T070 [P] Create `IRemoveConnectionFromDraftCommand.cs`. **Decision:** payload is the full `ActivityConnection` record (not a `ConnectionId : string` — connections have no Id; source+target IS the identity).
- [X] T071 [P] Create `IDeclareVariableInDraftCommand.cs`.
- [X] T072 [P] Create `IUpdateVariableInDraftCommand.cs`.
- [X] T073 [P] Create `IRemoveVariableFromDraftCommand.cs`.
- [X] T074 [P] Create `IAddWorkflowInputToDraftCommand.cs`.
- [X] T075 [P] Create `IUpdateWorkflowInputInDraftCommand.cs`.
- [X] T076 [P] Create `IRemoveWorkflowInputFromDraftCommand.cs`.
- [X] T077 [P] Create `IAddWorkflowOutputToDraftCommand.cs`.
- [X] T078 [P] Create `IUpdateWorkflowOutputInDraftCommand.cs`.
- [X] T079 [P] Create `IRemoveWorkflowOutputFromDraftCommand.cs`.
- [X] T080 [P] Create `ICreateDraftCommand.cs` (lifecycle origination).

### Shared lock-acquisition helper

- [X] T081 Create `src/Elsa.Workflows.Design.Persistence.EFCore/Commands/DraftMutationPipeline.cs` — **shipped as helper service (composition), NOT abstract base class.** Encapsulates the FR-027 pipeline (acquire lock via `IDistributedLockProvider`, load Draft, invoke loading handlers, run mutate-and-build-event hook, mark Entity Modified, publish granular event, publish `OnDraftValidating`, [validation sibling flush — Phase 9 stub], flush, release lock). Provides `ExecuteMutation` for mutations on existing drafts + `ExecuteCreation` for `ICreateDraftCommand`. Composition over inheritance — each command depends on the service rather than inheriting.

### Command implementations in Persistence.EFCore (FR-019a)

- [X] T082 [P] `AddActivityToDraftCommand.cs`.
- [X] T083 [P] `RemoveActivityFromDraftCommand.cs` — also drops connections referencing the NodeId and the layout record (FR-019 spec cascade).
- [X] T084 [P] **N/A — replaced** by 6 per-activity I/O CRUD impls (see T067).
- [X] T085 [P] `MoveActivityInDraftCommand.cs` — mutates Draft layout sibling ONLY, not State.
- [X] T086 [P] `AddConnectionToDraftCommand.cs`.
- [X] T087 [P] `RemoveConnectionFromDraftCommand.cs`.
- [X] T088 [P] `DeclareVariableInDraftCommand.cs`.
- [X] T089 [P] `UpdateVariableInDraftCommand.cs`.
- [X] T090 [P] `RemoveVariableFromDraftCommand.cs`.
- [X] T091 [P] `AddWorkflowInputToDraftCommand.cs`.
- [X] T092 [P] `UpdateWorkflowInputInDraftCommand.cs`.
- [X] T093 [P] `RemoveWorkflowInputFromDraftCommand.cs`.
- [X] T094 [P] `AddWorkflowOutputToDraftCommand.cs`.
- [X] T095 [P] `UpdateWorkflowOutputInDraftCommand.cs`.
- [X] T096 [P] `RemoveWorkflowOutputFromDraftCommand.cs`.
- [X] T097 [P] `CreateDraftCommand.cs`.
- [X] **NEW per Joey iteration round 1:** 6 per-activity input/output CRUD impls — `AddActivityInputToDraftCommand`, `UpdateActivityInputInDraftCommand`, `RemoveActivityInputFromDraftCommand`, `AddActivityOutputToDraftCommand`, `UpdateActivityOutputInDraftCommand`, `RemoveActivityOutputFromDraftCommand`. All share an internal `WithMutatedActivity(state, nodeId, mutate)` helper for the per-node mutation pattern.
- [X] T098 Register all 22 command implementations + the `DraftMutationPipeline` service in `EFCoreWorkflowsPersistenceFeatureBase.OnAfterConfigured`.

**Pre-reqs added (not in original task list):** `Workflows.Design.Persistence.EFCore.csproj` gained `ProjectReference` to `Elsa.Locking.Core`, `Elsa.Mediator.Core`, and `Elsa.Workflows.Design.Validations.Core` (for `IDistributedLockProvider`, `IDomainEventSender`, and `OnDraftValidating`).

### Tests for commands

- [X] T099 `CommandRegistrationTests.cs` — parametrised over all 21 contracts (lifecycle origination + 20 mutations); verifies each resolves to its expected implementation from the DI container. SC-012.
- [X] T100 + T101 — **Consolidated as `DraftMutationPipelineTests.cs`** (representative pipeline coverage, 6 tests): `AddActivityToDraft` mutates state + publishes granular event + ordering with `OnDraftValidating`; `DeclareVariableInDraft` publishes event; `AddWorkflowInputToDraft` publishes distinct workflow-level event (not per-activity); `AddActivityInputToDraft` publishes per-activity event (not workflow-level); `CreateDraft` creates Draft + Layout siblings + publishes `OnDraftCreated`; closed-mode contract holds (no subscribers registered, pipeline completes). The pipeline is uniform across commands; exhaustive per-command branch coverage is **deferred to Phase 12 polish** — flagged in Unit C follow-up.
- [X] T102 `LockSemanticsTests.cs` — two tests: (a) two acquires on the same lock name serialise; (b) two acquires on different lock names proceed in parallel. SC-016 + FR-027 + FR-027a.

**Test infrastructure added:** `tests/Elsa.Workflows.Design.Tests/Infrastructure/`:
- `InMemoryDistributedLockProvider.cs` — `SemaphoreSlim`-per-name test stub for lock semantics.
- `CapturingDomainEventSender.cs` — captures every event for assertion.
- `WorkflowsDesignTestHost.cs` — SQLite-in-memory DbContext + DI scaffolding (JsonPayloadSerializer + entity loading/saving handlers + GUID identity generator + pipeline + all 22 commands).

Test csproj gained `ProjectReference` to `Elsa.Serialization`, `Elsa.Primitives`, `Elsa.Locking.Core`.

**Checkpoint**: 16 commands + lock infrastructure ship; per-command tests confirm event publication + lock isolation.

---

## Phase 8: Cross-cutting substrate — Validations sub-domain + baseline validators (FR-032 + FR-033)

**Purpose**: The Validations sub-domain ships its two-module structure (Validations.Core + Validations baseline feature) per Joey 2026-05-28 (clarify s2 Q3 + s3 Q2). Five baseline validators ship in the baseline feature.

**Note**: The `IWorkflowDefinitionDraftValidation` read contract is created here too (FR-021's read surface, relocated per clarify s2 Q3).

### Validations.Core read contracts

- [X] T103 [P] Create `src/Elsa.Workflows.Design.Validations.Core/Contracts/IWorkflowDefinitionDraftValidation.cs` per contracts/read-surfaces.md. FR-021 read side. **Verified — landed in Phase 7 cascade alongside the sibling entity.**

### Validations baseline feature (FR-032 + FR-033)

- [X] T104 Create `src/Elsa.Workflows.Design.Validations/WorkflowDesignValidationsFeature.cs`. `IShellFeature` with `MaxRecursionDepth` settings property; binds `WorkflowDesignValidatorOptions` and registers all 5 baseline validators via `AddDomainEventHandlersFrom(typeof(WorkflowDesignValidationsFeature).Assembly)`. FR-032.
- [X] T105 [P] `OrphanActivityValidator` — root-level workflow-graph check; excludes `IsStart`. Emits `Path="{NodeId}"`, `Type="Graph/OrphanActivity"`. **Decision: workflow-graph concern, root-level only — nested children inside containers are container-driven, not connection-driven.**
- [X] T106 [P] `StartActivityValidator` — emits one error for zero or >1 IsStart at root level. **Root-level only — start is workflow-scope.**
- [X] T107 [P] `VariableUniquenessValidator` — case-insensitive collision detection via `GroupBy(StringComparer.OrdinalIgnoreCase)`. One error per offending group at `$workflow/variables/{Name}`. **Workflow-scope; root only.**
- [X] T108 [P] `RequiredInputOutputValidator` — recurses via `ActivityTreeWalker` (iterative DFS, depth-bounded by `WorkflowDesignValidatorOptions.MaxRecursionDepth` default 100). Looks up catalog `IActivityDefinitionVersion` via `IActivityDefinitionLookup.GetVersion(activityVersionId)`; checks each required input/output against the activity's `ArgumentState` (matches by `ReferenceKey`; empty/null `ArgumentValue.Value` counts as missing). **Workflow-level branch (State.Inputs / State.Outputs) deliberately a no-op in Unit C — the design surface has `IsRequired` but no default value or internal binding to validate against; flagged in README + Unit C follow-up as Unit D / E territory.**
- [X] T109 [P] `VariableExpressionResolverValidator` — recurses via `ActivityTreeWalker`. Walks each ArgumentState on every node's Inputs + Outputs; when `ArgumentValue.ExpressionType == "Variable"` (ordinal compare per R9), looks up the variable by `ReferenceKey` (NOT `Name` — per Joey's clarification: id is stable; name is mutable). Emits at `{NodeId}/{inputs|outputs}/{ArgumentState.ReferenceKey}`, `Type="Expressions/UnresolvedVariable"`.
- [X] T110 [P] `src/Elsa.Workflows.Design.Validations/README.md` — framework §2.22 feature documentation; tabulates the 5 handlers + their `(Path, Type)` outputs + the workflow-level skip note + activity-feature-co-located pattern pointer.

### Tests for the Validations sub-domain

- [X] T111 `ValidationsFeatureRegistrationTests.cs` — 4 tests: all 5 validators register; all implement `IDomainEventHandler<OnDraftValidating>`; default `MaxRecursionDepth == 100`; feature property override binds to options. SC-021. (Uses a stub `IActivityDefinitionLookup` to satisfy `RequiredInputOutputValidator`'s constructor.)
- [X] T112 [P] `OrphanActivityValidatorTests.cs` — 4 branches (orphan, only-inbound, only-outbound, IsStart-disconnected).
- [X] T113 [P] `StartActivityValidatorTests.cs` — 3 branches (zero / one / two starts).
- [X] T114 [P] `VariableUniquenessValidatorTests.cs` — 3 branches (distinct names; case-insensitive collision; 3-way collision still emits one error).
- [X] T115 [P] `RequiredInputOutputValidatorTests.cs` — 6 branches (satisfied input; missing input; present-but-empty input; missing output; unknown version skipped; recursion into ChildActivities). SC-022(d). **Note: workflow-level branch (SC-022e) deferred per FR-033 workflow-level no-op decision.**
- [X] T116 [P] `VariableExpressionResolverValidatorTests.cs` — 7 branches (non-Variable expression; known ReferenceKey; unknown ReferenceKey; empty reference; ReferenceKey-vs-Name disambiguation; recursion into child; output-path variant).
- [X] T117 `CrossFeatureValidatorSubscriptionTests.cs` — 2 tests: a stub `IDomainEventHandler<OnDraftValidating>` defined in the test assembly (simulating an Elsa.Http-style activity feature per FR-034) contributes errors via `CapturingDomainEventSender.OnSend` that persist to the validation sibling; baseline + cross-feature contributions coexist in the same dispatch pass. SC-023 (contribution-flow); end-to-end DI dispatcher correctness via the real `DomainEventPipeline` is deferred to the already-flagged `Elsa.Mediator.Tests` follow-on.
- Tests added: 4 + 4 + 3 + 3 + 6 + 7 + 2 = **29 tests** (199 total — 31 in Activities.Design.Tests preserved + 168 in Workflows.Design.Tests).

**Phase 8 architectural notes (recorded for the follow-up):**

- **Variable lookup by id, not Name.** Per Joey 2026-05-28 clarification: `VariableDefinition.ReferenceKey` is the stable id (auto-generated; survives renames). The variable-expression validator compares against `ReferenceKey`. Name is mutable and unsafe to compare.
- **Configurable max recursion depth.** Shared `WorkflowDesignValidatorOptions.MaxRecursionDepth` (default 100). Iterative DFS in `ActivityTreeWalker` — .NET call stack is never the actual risk; the bound guards against cyclic / malformed Draft data.
- **Validator recursion scope split.**
  - Walk-the-tree (recurse into `ActivityNode.ChildActivities`): `RequiredInputOutputValidator`, `VariableExpressionResolverValidator` — per-activity concerns apply at every depth.
  - Root-only: `OrphanActivityValidator` (connections live on `State.ActivityConnections` at workflow level), `StartActivityValidator` (start is workflow-scope), `VariableUniquenessValidator` (variables are workflow-scope).
- **Workflow-level required-input/output no-op.** `WorkflowDefinitionState.Inputs` / `Outputs` are declarations only; no default value or internal binding field at the design surface to validate against. Activity-level coverage is complete; workflow-level activates once Unit D / E adds the binding surface.
- **SC-023 wiring path.** Cross-feature subscription test uses the same `CapturingDomainEventSender.OnSend` pattern `ValidationSiblingPersistenceTests` uses. Real `DomainEventPipeline` dispatch (Iterator + Shielding + Invoker) coverage is the already-flagged `Elsa.Mediator.Tests` follow-on — not new scope for Phase 8.

**Checkpoint**: Validations sub-domain ships; 5 baseline validators land + tests; cross-feature subscription works.

---

## Phase 9: Cross-cutting substrate — Validation sibling + delete-and-re-add + promotion gate (FR-021 + FR-023 + FR-024)

**Purpose**: The persistence sibling `WorkflowDefinitionDraftValidation`, its delete-and-re-add lifecycle (FR-023), and the "no Version with errors" promotion gate (FR-024).

### Persistence sibling

- [X] T118 Entity `WorkflowDefinitionDraftValidation` exists in `Persistence.Core/Entities/` (landed alongside `DraftMutationPipeline` in Phase 7's cascade).
- [X] T119 EF configuration `WorkflowDefinitionDraftValidationConfiguration` exists with `OnDelete(Cascade)` + JSON-serialized `Errors` per R5 (Phase 7 cascade).
- [X] T120 Entity registered in `WorkflowsDesignDbContext` as `WorkflowDefinitionDraftValidations` DbSet (Phase 7 cascade).

### Delete-and-re-add wiring (in mutation commands)

- [X] T121 `DraftMutationPipeline.ExecuteValidationGate` + `UpsertValidationSibling` already flush the sibling on every mutation per FR-023 — landed in Phase 7. Verified by `ValidationSiblingPersistenceTests` (Phase 7) + `ValidationLifecycleTests` (T125).

### Promotion gate

- [X] T122 `src/Elsa.Workflows.Design.Persistence.Core/Exceptions/DraftHasValidationErrorsException.cs` — sealed; carries `DraftId` + `ErrorCount` + descriptive message.
- [X] T123 `src/Elsa.Workflows.Design.Persistence.Core/Contracts/IPromoteDraftToVersionCommand.cs` — placeholder contract with `Execute(string draftId, CT)` signature; doc-header documents the gate + lock invariants and flags implementation as Unit D's allocation per R8.
- [X] T124 `tests/Elsa.Workflows.Design.Tests/Unit/PromotionGateTests.cs` — stub in-test `StubPromotionGate` reads the sibling and either throws or returns a synthetic version id. Two tests: throws when sibling has errors (DraftId + ErrorCount preserved); succeeds when empty. SC-014.

### Tests for the validation lifecycle

- [X] T125 `tests/Elsa.Workflows.Design.Tests/Unit/ValidationLifecycleTests.cs` — wires the real `OrphanActivityValidator` into `CapturingDomainEventSender.OnSend` so the production validator runs against the test pipeline. Round-trips an orphan: add orphan → sibling carries `Graph/OrphanActivity` error; add start + wire connection → sibling rewritten empty. SC-013 + SC-022.
- [X] T126 `tests/.../ValidationReadAccessTests.cs` — reflection check confirms `IWorkflowDefinitionDraftValidation` + `ValidationError` live in `Elsa.Workflows.Design.Validations.Core` (NOT in Persistence.Core); `(Path, Type)` grouping demonstrated against representative validator outputs; theory matrices over the 7 R2 Path forms + 6 R3 Type categories. SC-013.

**Checkpoint**: Validation sibling persists; delete-and-re-add lifecycle works end-to-end; promotion gate throws on non-empty errors.

---

## Phase 10: Cross-cutting substrate — Lifecycle commands (Clone + Discard) (FR-028 + FR-029)

**Purpose**: `ICloneDraftFromVersionCommand` (FR-028) + `IDiscardDraftCommand` (FR-029). Both take the per-Draft distributed lock.

### Contracts

- [X] T127 [P] `src/Elsa.Workflows.Design.Persistence.Core/Contracts/ICloneDraftFromVersionCommand.cs`. Signature `Execute(sourceVersionId, CT) → newDraftId`. The target Definition id is derived from `sourceVersion.DefinitionId` inside the impl — cloning never crosses Definitions, so taking it as a parameter would be redundant and invite drift. Doc-header flags `ClonedFromVersionId` back-pointer field as deferred to Unit D (audit trail lives on the lifecycle event for now).
- [X] T128 [P] `src/Elsa.Workflows.Design.Persistence.Core/Contracts/IDiscardDraftCommand.cs`. Signature `Execute(draftId, workflowDefinitionId, CT)`. Doc-header documents idempotency + cascade behaviour + why `workflowDefinitionId` is a parameter (no persistent FK on Draft entity in Unit C — Unit D's allocation).

### Implementations

- [X] T129 `src/Elsa.Workflows.Design.Persistence.EFCore/Commands/CloneDraftFromVersionCommand.cs`. Loads source Version + invokes version loading handlers to hydrate State; loads source VersionLayout; deep-copies State (collection-expression spread `[.. xs]`) + layout records into a new Draft + DraftLayout; routes through `DraftMutationPipeline.ExecuteCreation` so the new Draft acquires its per-Draft lock, gets its validation sibling populated, and emits the standard lifecycle-event sequence (OnDraftClonedFromVersion + OnDraftValidated). FR-028.
- [X] T130 `src/Elsa.Workflows.Design.Persistence.EFCore/Commands/DiscardDraftCommand.cs`. Custom flow (NOT via DraftMutationPipeline — different shape: no validation rebuild, no granular event). Uses `LockKeys.DraftKey(draftId)` helper. Acquires lock; loads Draft (return-cleanly-null for idempotency); `Remove(draft)` → cascade deletes Layout + Validation per R5; SaveChangesAsync; release lock; publishes OnDraftDiscarded.
- [X] T131 Registered in `EFCoreWorkflowsPersistenceFeatureBase.OnAfterConfigured` ("Lifecycle origination + cloning + discard" group, alongside `ICreateDraftCommand`). Mirrored in `WorkflowsDesignTestHost.RegisterCommands`.

### Tests

- [X] T132 [P] `tests/Elsa.Workflows.Design.Tests/Unit/DraftMutationCommandTests/CloneDraftFromVersionTests.cs` — 4 tests: State deep-copy verified by re-hydrating from StateSource; Layout records carried 1:1 (incl. Width/Height); NodeIds match as sets; `OnDraftClonedFromVersion` published with NewDraftId + SourceVersionId + TargetDefinitionId. SC-017. **ClonedFromVersionId field check (FR-028(d))** deferred per the contract's note — Unit D's field allocation.
- [X] T133 [P] `tests/Elsa.Workflows.Design.Tests/Unit/DraftMutationCommandTests/DiscardDraftTests.cs` — 4 tests: atomic delete of Draft + Layout + Validation via cascade; no Version touched; OnDraftDiscarded published with DraftId + WorkflowDefinitionId; second Discard on same id is a no-op (exactly one OnDraftDiscarded published across two calls). SC-018.

**Phase 10 architectural notes:**

- **Test SeedVersion gotcha.** `WorkflowDefinitionVersionSavingHandler` re-serializes from `entity.State` on every save; if you only pass `stateSource` via constructor and don't set `entity.State`, the handler overwrites StateSource to `string.Empty`. Test helpers now set `State` directly and let the handler serialize on save.
- **`ClonedFromVersionId` not added.** Per FR-028(d) the back-pointer field is "provisional; Unit D's call". Unit C declines to pre-empt by leaving the field off the entity. The `OnDraftClonedFromVersion` event payload carries `SourceVersionId` so audit subscribers can reconstruct the lineage; Unit D may persist the back-pointer if cardinality requires it.
- **Draft → Definition relationship flipped (2026-05-29).** The prior model had `WorkflowDefinition.DraftId` (parent → child pointer, 1:0..1). Joey's call: stage the future 1-Definition-to-many-Drafts cardinality now by moving the FK to `WorkflowDefinitionDraft.WorkflowDefinitionId` (child → parent, many:1, required, cascade). Removes the reverse-lookup the Discard command needed, simplifies the Clone command (target Definition derived from source Version), aligns the data model with where multi-Draft semantics will land. Touches: `WorkflowDefinitionDraft` + `WorkflowDefinition` entities + read contracts; `WorkflowDefinitionConfiguration` (drop relationship) + `WorkflowDefinitionDraftConfiguration` (add FK); `CreateDraftCommand` + `CloneDraftFromVersionCommand` (set FK); `DiscardDraftCommand` (drop parameter); `WorkflowsVersionProvisioner.Map` (drop `DraftId` line); `GetDefinitionRequestHandler` + `AddDefinitionCommandHandler` + `WorkflowDefinitionQueryExtensions` (query Draft via the new direction). Test infra gains `WorkflowsDesignTestHost.EnsureDefinition` since the FK is now required.
- **`IDiscardDraftCommand.Execute(string draftId, CT)` — single id.** Reads `draft.WorkflowDefinitionId` after loading; no caller-supplied parent id. Symmetric with `ICloneDraftFromVersionCommand.Execute(string sourceVersionId, CT)` — the FK on the entity is the single source of truth for parent linkage.

**Checkpoint**: Clone-from-Version + Discard commands ship; per-Draft lock isolation extends to lifecycle operations.

---

## Phase 11: Cross-cutting substrate — DOMAIN_EVENTS catalog + parity test (FR-030 + FR-031)

**Purpose**: Per-domain documentation deliverable per framework §2.22.1; automated parity test ensures the catalog stays aligned with the codebase.

### Catalog files

- [X] T134 [P] Create `src/Elsa.Workflows.Design.Core/DOMAIN_EVENTS.md` listing every event in Workflows.Design.Core (18 events). Heading format `### <EventClassName>` per R4; each entry carries semantic + payload + publication site + expected handlers + ordering + cross-references. Grouped by category (lifecycle / activities / connections / variables / workflow inputs / workflow outputs).
- [X] T135 [P] Create `src/Elsa.Workflows.Design.Validations.Core/DOMAIN_EVENTS.md` listing `OnDraftValidating` (1 event). Same format and content as T134.

### Parity test (FR-031 + FR-031a + R4)

- [X] T136 Test `tests/Elsa.Workflows.Design.Tests/Unit/CatalogParityTests.cs` — parametrised over both `.Core` assemblies. Two test methods (`Every_event_has_a_catalog_heading` + `Every_catalog_heading_maps_to_a_real_event`) provide bidirectional parity per SC-020 with named diagnostics. Resolves catalog path by walking up from the test bin folder to repo root.
- [ ] T137 Test `tests/Elsa.Workflows.Design.Tests/Unit/CatalogParityNegativeTests.cs` — **deferred to next pass.** T136's positive bidirectional test already enforces SC-020 from both directions; T137 is a meta-test on T136's mechanism. Implementing it cleanly requires either refactoring the parity logic into a pure function (testable with synthetic inputs) or injecting a stub event + temporary catalog file via reflection — both add scope without strengthening the SC-020 guarantee. Flagged for future hardening.

**Checkpoint**: Catalog ships for both Core assemblies; parity test confirms alignment and fails on drift.

---

## Phase 12: Polish & Final Validation

**Purpose**: Migration regeneration, full-solution build, full test run, follow-up updates, final commit prep.

### Migration regeneration (R10)

- [ ] T138 Run `dotnet ef migrations add Initial -c ActivitiesDesignDbContext --project src/Elsa.Activities.Design.Persistence.EFCore.Sqlite/Elsa.Activities.Design.Persistence.EFCore.Sqlite.csproj --startup-project src/Server/Elsa.Server.csproj`. Verify the migration includes the new `IsRequired` column on the input/output mapping tables.
- [ ] T139 Run `dotnet ef migrations add Initial -c WorkflowsDesignDbContext --project src/Elsa.Workflows.Design.Persistence.EFCore.Sqlite/Elsa.Workflows.Design.Persistence.EFCore.Sqlite.csproj --startup-project src/Server/Elsa.Server.csproj`. Verify the migration: (a) removes the `MetaData`/`IsSystem` columns (FR-015), (b) includes the three new entity tables (`WorkflowDefinitionVersionLayout`, `WorkflowDefinitionDraftLayout`, `WorkflowDefinitionDraftValidation`), (c) FK cascade behaviours per R5.

### Final build + test verification

- [ ] T140 Run `dotnet build Elsa.Server.slnx --nologo` — assert 0 warnings, 0 errors. If any warnings surface, address per task discipline (no warnings tolerated in Unit C deliverables; matches Unit B convention).
- [ ] T141 Run `dotnet test Elsa.Server.slnx --nologo --no-build` — assert all tests pass (existing 31 from `Elsa.Activities.Design.Tests` PLUS the new ~30+ tests added in this Unit C plan).
- [ ] T142 Smoke-test the Elsa.Server boot to confirm DI graph is healthy with the new Validations feature wired in. `dotnet run --project src/Server/Elsa.Server.csproj`; observe startup log for missing registrations or duplicate-registration warnings.

### Documentation + follow-up updates

- [ ] T143 [P] Update `../elsa-foundation-project-management/epic1-elsa-refactor-constitution/follow-up-items/2026-05-28_unitC_workflow_definition_state_scope.md` Status section to "Implementation complete, pending 2026-06-01 ratification". Tick the Done criteria items 1, 2, 5 in the follow-up.
- [ ] T144 [P] Update `../elsa-foundation-project-management/epic1-elsa-refactor-constitution/PERSONAL_TODO.md` — move Unit C from *Currently active* to *In review / pending ratification*.
- [ ] T145 [P] Walk through `quickstart.md` recipes end-to-end against the as-shipped codebase; correct any drift between the recipes and reality. SC-007 + SC-008.

### Audit checks (final review)

- [ ] T146 [P] Run scope-policy review per FR-005: read the final `WorkflowDefinitionState` record; confirm members are clean against the policy (review-discipline-only per session-3 Q3). Document the audit in the Unit C follow-up's "audit results" section. (Note: the automated scope-policy test was retired; this review is the enforcement mechanism.)
- [ ] T147 [P] Verify SC-009: zero occurrences of `WorkflowMetadata`, `WorkflowDefinition.MetaData`, or the `IsSystem` shadow-column lift remain in the Elsa.Workflows.Design tree (grep audit).
- [ ] T148 [P] Verify SC-015: zero domain events in the codebase expose raw collections on their public surface (`OnActivityVersionsReconciling`, all FR-018 events, `OnDraftValidating`). Grep + reflection-test confirmation.
- [ ] T149 [P] Verify SC-005: zero occurrences of `ActivityNode.ReferenceKey` or `ActivityPortConnection.ActivityReferenceKey` remain (grep audit; argument-level `ReferenceKey` identifiers unchanged per FR-010).
- [ ] T150 [P] Verify SC-006: zero occurrences of the old `(activityDefinitionId, version)` pair remain on `ActivityNode` and adjacent design-side models (grep audit).
- [ ] T151 Final Constitution Check walk against the as-shipped codebase: re-evaluate G1–G30 from plan.md per actual deliverables. Update the plan's Complexity Tracking table if any new violations surfaced during implementation (none expected; existing entries hold).

### Commit prep

- [ ] T152 Stage all Unit C code + test additions; commit with message "Unit C — implementation complete (US1-US3 + substrate + tests)". Co-Author per project convention.
- [ ] T153 Open / update the PR for branch `002-workflow-state-scope` against `main`. Description references this plan, the Unit C follow-up, and the 2026-06-01 agenda items (1, 2, 3, 4, 4b, 5, 6) that ratify the provisional sub-rules.

**Checkpoint**: Unit C is implementation-complete. The 2026-06-01 architecture review meeting ratifies (or revises) the five provisional sub-rules; ratification closes the Unit C follow-up.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: no upstream deps; T001-T005 in order.
- **Foundational (Phase 2)**: depends on Setup. Blocks all subsequent phases — the IsRequired addition + WorkflowMetadata deletion + migration delete must land before any work that touches the activity contract or the workflow definition entity.
- **US1 (Phase 3)**: depends on Foundational. Constitutional + audit work only; no entity-level dependencies.
- **US2 (Phase 4)**: depends on Foundational. Independent of US1 / US3.
- **US3 (Phase 5)**: depends on Foundational. Independent of US1 / US2.
- **Substrate Phase 6 (events)**: depends on Foundational. Events declared in `.Core`; consumed by Phase 7 commands.
- **Substrate Phase 7 (commands)**: depends on Phase 6 (event types must exist) + US2 (layout entities must exist for the MoveActivity / Add commands that update layout).
- **Substrate Phase 8 (Validations)**: depends on Phase 6 (`OnDraftValidating` must exist) + Foundational (`IsRequired` must exist).
- **Substrate Phase 9 (validation sibling)**: depends on Phase 8 (validators) + Phase 7 (commands publish `OnDraftValidating`).
- **Substrate Phase 10 (lifecycle commands)**: depends on Phase 7 (lock infrastructure) + US2 (layout entities exist for Clone) + Phase 9 (Discard cascades the validation sibling).
- **Substrate Phase 11 (catalog)**: depends on Phase 6 (events exist) + Phase 8 (`OnDraftValidating` exists).
- **Polish (Phase 12)**: depends on everything else.

### User Story Dependencies

- **US1 (P1)**: minimal deps — just Foundational. Pure constitutional + audit work; MVP-ready alone (US2/US3 are structural support that operate AGAINST US1's rule).
- **US2 (P2)**: minimal deps — Foundational. Independently testable; adds Layout sibling entities.
- **US3 (P2)**: minimal deps — Foundational. Independently testable; rename + collapse refactor.

The substrate phases (6-11) are non-user-story work that ships as part of Unit C scope but isn't gated on a user story. They depend on US2/US3 only insofar as the layout entities + NodeId fields must exist for commands to operate against them.

### Parallel Opportunities

- All `[P]`-marked tasks within a phase can run in parallel (different files, no shared state).
- US1/US2/US3 are independent of each other once Foundational completes — three developers can parallelise.
- The substrate phases (6-11) can interleave with US2/US3 work where there's no file overlap.
- Within Phase 6: all 19 event-creation tasks (T042-T061) are `[P]` — can ship 19 files in parallel.
- Within Phase 7: contract creations T065-T080 are all `[P]`; implementations T082-T097 are mostly `[P]` (each in its own file).
- Within Phase 8: validator implementations T105-T109 are `[P]`; validator tests T112-T116 are `[P]`.
- Within Polish: T143-T150 are mostly `[P]`.

### Within Each Phase

- **Setup**: T001 → T002 → T003 are sequential (project deps); T004 + T005 are `[P]`.
- **Foundational**: T006 + T007 → T008 (configuration depends on records); T009 → T010 → T011 → T012 → T013 are sequential (each touches a different layer of the WorkflowMetadata deletion); T014 + T015 are `[P]`.
- **US1**: T016 → T017 → T018 → T019 → T020 (sequential — each verifies prior step).
- **US2**: T021-T024 are `[P]` (different files); T025 + T026 are `[P]`; T027 sequential; T028-T031 are `[P]` (different test files).
- **US3**: T032-T038 are mostly sequential (each grep+update audit); T040-T041 are `[P]` (different test files).
- **Phase 6**: T042-T060 are all `[P]`; T061 depends on T060 (ValidationError must exist before OnDraftValidating references it); T062-T064 are `[P]`.
- **Phase 7**: T065-T080 `[P]`; T081 sequential; T082-T097 `[P]` after T081; T098 sequential; T099-T102 `[P]`.
- **Phase 8**: T103 `[P]`; T104 → T105-T109 `[P]` → T110 `[P]`; T111 → T112-T116 `[P]` → T117 sequential.
- **Phase 9**: T118 → T119 → T120 sequential; T121 sequential after T118-T120; T122-T126 mostly sequential.
- **Phase 10**: T127-T128 `[P]`; T129-T130 `[P]`; T131 sequential; T132-T133 `[P]`.
- **Phase 11**: T134-T135 `[P]`; T136-T137 sequential (T137 depends on T136's mechanism).
- **Phase 12**: T138-T139 `[P]`; T140-T142 sequential (build/test/smoke); T143-T150 `[P]`; T151-T153 sequential.

---

## Parallel Example: Phase 6 — Event Declaration

```bash
# Launch all 18 Workflows.Design.Core event file creations in parallel:
Task: "Create src/Elsa.Workflows.Design.Core/Events/OnDraftCreated.cs"
Task: "Create src/Elsa.Workflows.Design.Core/Events/OnActivityAddedToDraft.cs"
Task: "Create src/Elsa.Workflows.Design.Core/Events/OnActivityRemovedFromDraft.cs"
# ... and so on through OnDraftDiscarded.cs
# Plus ValidationError + OnDraftValidating in Validations.Core.

# After all event files compile, launch the surface tests in parallel:
Task: "Tests EventNamingTests.cs"
Task: "Tests MethodPatternTests.cs"
Task: "Tests NoRawCollectionsTests.cs"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup (csproj scaffolding + slnx + restore/build).
2. Complete Phase 2: Foundational (IsRequired addition + WorkflowMetadata deletion + migration delete).
3. Complete Phase 3: US1 (constitution + doc header + audit).
4. **STOP and VALIDATE**: Joey reviews the audit results; Sipke + Frans review the constitutional fold for Monday's meeting.
5. US1 is constitutional-grade work — the demo is the constitution amendment + the documented record.

### Incremental Delivery

1. Setup + Foundational → ready.
2. US1 → demo the constitutional codification + the audit (MVP).
3. US2 → demo Layout sibling extraction (designer can position activities without polluting State).
4. US3 → demo the unified NodeId terminology (renames complete, tests green).
5. Substrate Phases 6-11 → demo the Draft authoring substrate (mutation commands work, validators run, validation persists, promotion gate enforces, Clone + Discard work, catalog parity test holds).
6. Polish → final audit + commit + PR.

### Parallel Team Strategy

With multiple developers (if staffed):

1. Team completes Setup + Foundational together (sequential, contract evolution).
2. Once Foundational is done:
   - Developer A: US1 (constitutional + audit) then Phase 11 (catalog).
   - Developer B: US2 (Layout siblings) then Phase 7 (commands; needs US2's layout entities).
   - Developer C: US3 (NodeId rename) then Phase 6 (events; independent of layout).
   - Developer D (if available): Phase 8 (Validations) once Phase 6 events ship + Phase 9 (validation sibling) once Phase 7 commands ship.
3. Phase 10 (lifecycle commands) integrates everything; ships sequentially after Phase 7 + Phase 9.
4. Phase 12 (Polish) is a single-developer task at the end.

In practice for a single-developer cadence: phases run sequentially in the order listed (1 → 2 → 3 → 4 → 5 → 6 → 7 → 8 → 9 → 10 → 11 → 12). Total task count: **153**.

---

## Constitutional Compliance

Tasks inherit the Constitution Check gates (G1–G30) decided in [plan.md](./plan.md). Specific constitutional reminders that surface in this task list:

- **G2 (naming)** — every csproj created in Phase 1 uses domain language; no `Features.*`, `Modules.*`, `.Contracts`, `.Abstractions` segments.
- **G3 (no heavy deps in `.Core`)** — `Validations.Core` carries ONLY `Workflows.Design.Core` cross-`.Core` reference + `Microsoft.Extensions.*Abstractions` per T001.
- **G15 (Elsa §E2.2 hard rule)** — every task touches `Elsa.Workflows.Design.*`; no Runtime references introduced.
- **G18 (CQS at persistence boundary)** — all FR-019 commands mutate without returning queryable views; queries on the validation sibling go through `IWorkflowDefinitionDraftValidation` only.
- **G20 (refactor work preserves test subjects)** — T039 explicitly invokes §2.21.1 during the NodeId rename + collapse.
- **G21 (domain events are the contribution mechanism)** — every validator is `IDomainEventHandler<OnDraftValidating>`; no provider/contributor interfaces introduced.
- **G27 (unit test discipline)** — every feature class gets a §2.23.1 registration test; every logic-bearing implementation gets §2.23.2 branch-covered tests. Exception: Mediator middleware tests deferred per plan.md Complexity Tracking entry.

If any task encounters a constitutional ambiguity during execution — escalate to Joey rather than silently bypass the rule (working-loop §5 + meta-repo CLAUDE.md §8 point 6).

---

## Notes

- `[P]` marks parallelizable tasks (different files, no dependencies on incomplete tasks).
- `[US1]` / `[US2]` / `[US3]` labels appear only in Phases 3-5 (the spec's user-story phases). Phases 6-11 are non-user-story substrate per spec FR organization.
- Each task carries an exact file path — no vague descriptions.
- The five provisional constitutional sub-rules (Model X, event-sourcing slot, "no Version with errors" gate, framework §2.6.1 method-pattern + subscriber-MUST-NEVER-break-publisher, framework §2.22.1 catalog) ride through the implementation per the working-loop §5 pattern. Ratification 2026-06-01.
- The scope-policy test (FR-004 / SC-003 / US1 Acceptance Scenario 2) was retired per clarify session 3 Q3 — no task ships an automated scope-policy test in Unit C. Review discipline carries the rule until the future *Code Analysers* epic opens.
- The Mediator middleware test project (`tests/Elsa.Mediator.Tests/`) is logged as a follow-on item per plan.md Complexity Tracking — no Unit C tasks ship those tests.
