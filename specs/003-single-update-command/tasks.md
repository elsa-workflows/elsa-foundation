---
description: "Task list for Single Diff-Based Draft Update Command (Unit 2)"
---

# Tasks: Single Diff-Based Draft Update Command

> **Supersession note (2026-07-05):** this is a point-in-time execution log. Tasks that wire the semantic diff engine into the mutation path, compute/Background-publish per-diff mutation events, or rewrite the `WorkflowDefinitionDraftValidation` sibling are superseded — per-diff publication is retired (diff engine remains the tested contract but is unregistered from DI) and the validation entity is deleted (errors are derived state; spec 002 FR-021 / this spec's top note). Tasks for the coarse `IUpdateDraftCommand`, the per-Draft lock, the `DraftValidating`/`DraftValidated` pair, and State persistence stand. Reinstatable when a consumer exists.

**Input**: Design documents from `/specs/003-single-update-command/`
**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/](./contracts/), [quickstart.md](./quickstart.md)

**Tests**: INCLUDED. This feature explicitly requires them — FR-013 mandates that every per-concept event mapping keeps a (moved) test and that seven net-new diff-engine behaviours each get a dedicated test; SC-002/003/006/009/012/013/014/015 are test-defined outcomes.

**Organization**: Tasks are grouped by the three user stories in priority order (US1=P1, US2=P2, US3=P2). Note (refactor reality): US1 delivers the load-bearing collapse and the command shell; US3 removes the now-orphaned pipeline mutation path and proves absorption; US2 preserves the event surface + catalog. US3 and US2 both build on the US1 command.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: US1 / US2 / US3 (Setup/Foundational/Polish carry no story label)
- Exact file paths are included in each task.

## Path Conventions

Modular class-library domain (`Elsa.Workflows.Design.*`); build via `dotnet build Elsa.Server.slnx`. No new project/module is created (plan Structure Decision).

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Establish the before-state baseline the §2.21.1 refactor is measured against.

- [X] T001 Establish green baseline: run `dotnet build Elsa.Server.slnx` then the `tests/Elsa.Workflows.Design.Tests` suite; record the current passing set (the granular `*CommandTests` and validation tests) so test-subject/objective preservation (G20/§2.21.1) can be verified after the collapse.
- [X] T002 [P] Confirm the Unit 1 event substrate is present and consumable in `src/Elsa.Events.Core` and `src/Elsa.Events`: `IEvent`, `IEventHandler<in T>`, `IEventPublisher.Publish(IEvent, IEventPublishingStrategy?, CancellationToken)`, and the Sequential + Background strategies (read-only verification — Unit 2 introduces no new substrate, FR-004/FR-006).

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The public contract surface and the diff-identity ground truth that ALL three stories depend on.

**⚠️ CRITICAL**: No user-story work can begin until this phase is complete.

- [X] T003 Verify per-dimension stable match keys against source (FR-023, research.md R2) across the element types in `src/Elsa.Workflows.Design.Core/Models/WorkflowDefinitionState.cs` and its element models: Variables/Inputs/Outputs by `ReferenceKey`, Activities by `NodeId`, activity I/O by (`NodeId`,`ReferenceKey`), connections by endpoint tuple, layout `DesignMetadataRecord` by `NodeId`. Confirm no element type lacks an intrinsic key; **flag in the follow-up if any does** (the one place FR-023 could touch the State model — expected outcome: it does not).
- [X] T004 [P] Create the `UpdateDraftRequest` record in `src/Elsa.Workflows.Design.Persistence.Core/Contracts/UpdateDraftRequest.cs`: `sealed record UpdateDraftRequest(string DraftId, WorkflowDefinitionState State, IReadOnlyCollection<DesignMetadataRecord> Layout)` (reuses existing types; layout carried beside State, never inside it — §E2.9.2, FR-001a).
- [X] T005 Create the `IUpdateDraftCommand` contract in `src/Elsa.Workflows.Design.Persistence.Core/Contracts/IUpdateDraftCommand.cs`: `Task Execute(UpdateDraftRequest request, CancellationToken cancellationToken = default)` (command/query split — `Task`, no queryable return, G18). Depends on T004.

**Checkpoint**: Contract surface exists and compiles; story implementation can begin.

---

## Phase 3: User Story 1 — Collapse the granular command surface into one diff-based `IUpdateDraftCommand` (Priority: P1) 🎯 MVP

**Goal**: Replace the 20 granular mutation commands with a single `IUpdateDraftCommand` that diffs the complete desired state against stored state under the per-Draft lock, emits one event per difference, and persists. The 4 lifecycle commands and the validation pair are untouched.

**Independent Test**: One `Execute` call with a desired state differing in several dimensions (add activity, remove connection, rename variable) publishes exactly the matching per-diff events, the persisted `State` equals the desired state, all under one lock acquisition — and the 20 granular contracts no longer exist on the public surface.

### Tests for User Story 1 (write first; must FAIL before implementation) ⚠️

> All test files live under `tests/Elsa.Workflows.Design.Tests/`. These are the **moved** former `*CommandTests` (FR-013, SC-010) plus the net-new diff-engine cases (FR-013 a–g).

- [X] T006 [P] [US1] Migrated **activity** diff tests in `tests/Elsa.Workflows.Design.Tests/Unit/UpdateDraftCommand/ActivityDiffTests.cs` — driving `IUpdateDraftCommand`: add → `ActivityAddedToDraft`; remove → `ActivityRemovedFromDraft`; assert resulting State.
- [X] T007 [P] [US1] Migrated **activity input/output** diff tests in `tests/Elsa.Workflows.Design.Tests/Unit/UpdateDraftCommand/ActivityIoDiffTests.cs` — add/update/remove by (`NodeId`,`ReferenceKey`) → `OnActivityInput{Added,Updated,Removed}…` and `OnActivityOutput{Added,Updated,Removed}…`.
- [X] T008 [P] [US1] Migrated **connection** diff tests in `tests/Elsa.Workflows.Design.Tests/Unit/UpdateDraftCommand/ConnectionDiffTests.cs` — add → `OnConnectionAddedToDraft`; remove → `OnConnectionRemovedFromDraft` (endpoint-tuple identity).
- [X] T009 [P] [US1] Migrated **variable** diff tests in `tests/Elsa.Workflows.Design.Tests/Unit/UpdateDraftCommand/VariableDiffTests.cs` — declare/update/remove by `ReferenceKey` → `OnVariable{Declared,Updated,Removed}…`.
- [X] T010 [P] [US1] Migrated **workflow input/output** diff tests in `tests/Elsa.Workflows.Design.Tests/Unit/UpdateDraftCommand/WorkflowIoDiffTests.cs` — add/update/remove by `ReferenceKey` → `OnWorkflowInput…` / `OnWorkflowOutput…`.
- [X] T011 [P] [US1] **Layout** diff test in `tests/Elsa.Workflows.Design.Tests/Unit/UpdateDraftCommand/LayoutDiffTests.cs` — changed `DesignMetadataRecord` (X/Y/W/H) for a `NodeId` → `ActivityMovedInDraft` (FR-001a); confirms layout is diffed from the sibling, not from `WorkflowDefinitionState`.
- [X] T012 [P] [US1] **No-op** test (SC-009, FR-013a) in `tests/Elsa.Workflows.Design.Tests/Unit/UpdateDraftCommand/NoOpDiffTests.cs` — desired == stored → zero mutation events, validation pair still runs, State semantically unchanged.
- [X] T013 [P] [US1] **Multi-dimension** test (Scenario 1, SC-002, FR-013b) in `tests/Elsa.Workflows.Design.Tests/Unit/UpdateDraftCommand/MultiDimensionDiffTests.cs` — add activity + remove connection + update variable → exactly 3 events of correct types, persisted State == desired, deterministic event order.
- [X] T014 [P] [US1] **Last-writer-wins** test (SC-014, FR-013c) in `tests/Elsa.Workflows.Design.Tests/Unit/UpdateDraftCommand/LastWriterWinsTests.cs` — writer B's desired state built from a pre-A read overwrites A wholesale (emits REMOVE/UPDATE diffs), completes with no conflict/version error; entity has no version column.
- [X] T015 [P] [US1] **Rename vs id-change** test (SC-015, FR-013d) in `tests/Elsa.Workflows.Design.Tests/Unit/UpdateDraftCommand/IdentityMatchTests.cs` — same id + changed payload → single UPDATE (e.g. `VariableUpdatedInDraft`), zero REMOVE/ADD; differing id → REMOVE+ADD pair.
- [X] T016 [P] [US1] **Connection change = REMOVE+ADD** test (FR-013e) in `tests/Elsa.Workflows.Design.Tests/Unit/UpdateDraftCommand/ConnectionChangeDiffTests.cs` — any endpoint-tuple change diffs as remove(old)+add(new), since connections have no update event.
- [X] T017 [P] [US1] **Activity-removal cascade** test (FR-013f) in `tests/Elsa.Workflows.Design.Tests/Unit/UpdateDraftCommand/ActivityRemovalCascadeTests.cs` — removing an activity (NodeId absent from desired) prunes its connections, emitting `ActivityRemovedFromDraft` + the connection removals.
- [X] T018 [P] [US1] **Lock-once + per-Draft serialisation** test (SC-003) in `tests/Elsa.Workflows.Design.Tests/Unit/UpdateDraftCommand/LockingTests.cs` — one `workflow-draft:{DraftId}` acquisition per call; two concurrent calls on the same Draft serialise; two on different Drafts proceed in parallel.

### Implementation for User Story 1

- [X] T019 [US1] Implement `DraftStateDiffer` in `src/Elsa.Workflows.Design.Persistence.EFCore/Commands/DraftStateDiffer.cs` — internal, `public sealed` (G27); the per-concept diff→event derivation migrated from the 20 deleted command bodies (research R3/R4); matches on the R2 keys; returns an ordered `IReadOnlyList<IEvent>` of the existing 20 event types with `OldValue` from stored, `NewValue` from desired. Not a public contract (G2/G25).
- [X] T020 [US1] Implement `UpdateDraftCommand` in `src/Elsa.Workflows.Design.Persistence.EFCore/Commands/UpdateDraftCommand.cs` — `public sealed`; the absorbed mutation shell (data-model §5 / FR-007): lock → load+hydrate stored → wholesale assign `draft.State = request.State` and `layout.Records = request.Layout` → `DraftStateDiffer.Diff` → Sequential `DraftValidating` gate → upsert validation sibling → transactional SaveChanges → release lock → Background-publish per-diff events then `DraftValidated`. No optimistic-concurrency check (FR-022).
- [X] T021 [US1] Delete the 20 granular mutation command **contracts** from `src/Elsa.Workflows.Design.Persistence.Core/Contracts/` (FR-002 full list: `IAddActivityToDraftCommand` … `IRemoveWorkflowOutputFromDraftCommand`). Keep the 4 lifecycle contracts.
- [X] T022 [US1] Delete the 20 granular mutation command **implementations** from `src/Elsa.Workflows.Design.Persistence.EFCore/Commands/` (apply logic already migrated into `DraftStateDiffer` in T019). Keep `CreateDraftCommand`/`CloneDraftFromVersionCommand`/`DiscardDraftCommand`/`PromoteDraftToVersionCommand`.
- [X] T023 [US1] Update DI in `src/Elsa.Workflows.Design.Persistence.EFCore/EFCoreWorkflowsPersistenceFeatureBase.cs`: remove the 20 granular command registrations; add `AddScoped<IUpdateDraftCommand, UpdateDraftCommand>()` (+ `DraftStateDiffer` if separately resolved). Leave lifecycle command + `DraftMutationPipeline` registrations for now (US3 trims the mutation path).
- [X] T024 [US1] Registration + surface test in `tests/Elsa.Workflows.Design.Tests/Unit/UpdateDraftCommand/RegistrationTests.cs` (SC-001, G27): `IUpdateDraftCommand` resolves; the 20 granular mutation contracts are absent from `Elsa.Workflows.Design.Persistence.Core.Contracts`; the 4 lifecycle contracts remain.

**Checkpoint**: US1 is the MVP — the collapse is functional and independently testable. STOP and validate before US2/US3.

---

## Phase 4: User Story 3 — Absorb the mutation pipeline into the command shell (Priority: P2)

**Goal**: Remove the now-orphaned `DraftMutationPipeline.ExecuteMutation` indirection and prove the command itself is the mutation shell, with the cause-before-effect ordering and single-transaction guarantees intact.

**Independent Test**: An `Execute` producing N per-diff events runs with one lock acquisition, one Sequential `DraftValidating` gate against post-diff state, one transactional flush of State + validation sibling, and Background publication of N events + one `DraftValidated` in cause-before-effect order — with no `DraftMutationPipeline` type remaining as a separate collaborator of the mutation path.

### Implementation for User Story 3

- [X] T025 [US3] Remove the **mutation path** (`ExecuteMutation` + its `mutateDelegate` shape) from `src/Elsa.Workflows.Design.Persistence.EFCore/.../DraftMutationPipeline.cs` now that `UpdateDraftCommand` (T020) embodies the shell. **Retain `ExecuteCreation`** so `CreateDraftCommand`/`CloneDraftFromVersionCommand` stay green (research R11, FR-003); record that the residual creation path is owned by the lifecycle follow-up. Do NOT resurrect a named "pipeline" indirection the command delegates to (FR-007).

### Tests for User Story 3

- [X] T026 [P] [US3] Shell-ordering test (SC-006) in `tests/Elsa.Workflows.Design.Tests/Unit/UpdateDraftCommand/ShellOrderingTests.cs` — N events: one lock acquisition, `DraftValidating` published exactly once (Sequential) against post-diff state, validation sibling rewritten wholesale, State + sibling flushed in one transaction inside the lock, N mutation events + one `DraftValidated` published Background after release in cause-before-effect order.
- [X] T027 [P] [US3] Absorption + lifecycle-intact test (SC-006/SC-008) in `tests/Elsa.Workflows.Design.Tests/Unit/UpdateDraftCommand/PipelineAbsorptionTests.cs` — no `DraftMutationPipeline` collaborator participates in the mutation path; the existing create/clone/discard lifecycle tests still pass unchanged in subject/objective (§2.21.1).

**Checkpoint**: Mutation pipeline fully absorbed; lifecycle commands green.

---

## Phase 5: User Story 2 — Re-home the granular mutation events + update the catalog (Priority: P2)

**Goal**: Preserve all 20 mutation event types as `IUpdateDraftCommand`'s per-diff emissions, keep event-sourcing open (Unit H), and update the catalog's publication-site prose. The 3 lifecycle events stay bound to their commands.

**Independent Test**: All 20 mutation event types still exist and are `IEvent`; each is reachable as an `IUpdateDraftCommand` per-diff emission; the catalog-parity test passes with every mutation event's publication site naming `IUpdateDraftCommand`; the 3 lifecycle events are NOT emitted by `IUpdateDraftCommand`.

### Implementation for User Story 2

- [X] T028 [US2] Update the catalog publication-site prose for the 20 mutation events from the deleted command names to `IUpdateDraftCommand` in `src/Elsa.Workflows.Design.Core/EVENTS.md` (FR-011). **Note**: the actual catalog file is `EVENTS.md` (renamed from `DOMAIN_EVENTS.md` 2026-05-29; spec FR-005/FR-011/SC-005 say `DOMAIN_EVENTS.md` — treat as `EVENTS.md`, research R9). Event *types* and headings are unchanged; this is a documentation-only edit. Lifecycle events' publication sites unchanged.

### Tests for User Story 2

- [X] T029 [P] [US2] Event-preservation test (SC-004) in `tests/Elsa.Workflows.Design.Tests/Unit/EventSurfaceTests/EventPreservationTests.cs` (placed under the existing `EventSurfaceTests/` dir to avoid the file/dir name collision) — all 20 mutation event types still exist in `Elsa.Workflows.Design.Core/Events`, each is an `IEvent`, none deleted; the 3 lifecycle events are not emitted by `IUpdateDraftCommand` (SC-008 partial).
- [X] T030 [P] [US2] Confirm `tests/Elsa.Workflows.Design.Tests/Unit/CatalogParityTests.cs` passes unchanged after the T028 prose edit (FR-012/SC-005) — heading↔IEvent-type parity holds because types are unchanged.
- [X] T031 [P] [US2] Open/closed-mode subscriber test (SC-012) in `tests/Elsa.Workflows.Design.Tests/Unit/EventSourcingContractTests.cs` — a stub `IEventHandler<T>` receives the mutation event when an `Execute` produces that diff (open mode); with no subscriber registered, `Execute` still completes and persists (closed mode).
- [X] T032 [P] [US2] Background-shielding test (SC-013) in `tests/Elsa.Workflows.Design.Tests/Unit/EventSourcingContractTests.cs` — a throwing stub subscriber on a Background-published mutation event does not break `Execute`; the command completes and the Draft persists.

**Checkpoint**: Event surface preserved, catalog accurate, event-sourcing door open for Unit H.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Constitution restatement, meta-repo bookkeeping, and full-suite validation.

- [X] T033 Add the provisional sub-section **§E2.9.7 "Draft-mutation command surface"** to `.specify/memory/constitution.md` (FR-014/FR-016, SC-011) — canonical Draft-mutation surface is the single diff-based `IUpdateDraftCommand`; the 4 lifecycle commands remain distinct; the per-diff event surface is preserved for event-sourcing. Mark provisional with the same "pending architecture-review ratification" status as the rest of §E2.9 (§E2.9.6). Lands in-unit.
- [X] T034 Record the FR-015 audit finding (SC-011): no pre-existing "granular CQS commands" §-pin exists to correct; **verify the generic `Elsa.Persistence` CQS row at `constitution.md` line 427 is unaltered**.
- [X] T035 [P] Update the Unit 2 follow-up `../elsa-foundation-project-management/epic1-elsa-refactor-constitution/follow-up-items/2026-06-02_unit_single_update_command.md` (FR-018, SC-010/SC-011) — record per-test-file disposition (all command tests moved, none deleted), the T003 stable-id finding, the FR-015 audit finding, and the residual `ExecuteCreation` hand-off to the lifecycle follow-up.
- [X] T036 [P] Update `../elsa-foundation-project-management/epic1-elsa-refactor-constitution/PERSONAL_TODO.md` to reflect Unit 2 status (FR-018).
- [X] T037 Full green gate: `dotnet build Elsa.Server.slnx` + run the full `tests/Elsa.Workflows.Design.Tests` suite — migrated diff tests, the seven net-new diff tests, US3 ordering/absorption tests, US2 event/catalog tests, and the **unchanged** validation tests (SC-007) and lifecycle tests (SC-008) all pass.
- [X] T038 Walk `quickstart.md` end-to-end against the landed code to confirm the documented flow, file locations, and gotchas are accurate.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: no dependencies.
- **Foundational (Phase 2)**: depends on Setup; **blocks all stories** (the contract + diff-key ground truth).
- **US1 (Phase 3)**: depends on Foundational. The MVP and the substrate US2/US3 build on.
- **US3 (Phase 4)**: depends on US1 (the command must embody the shell before the pipeline mutation path can be removed).
- **US2 (Phase 5)**: depends on US1 (events must be produced by the command before the catalog/event-sourcing contracts can be asserted). Independent of US3 — US2 and US3 can proceed in parallel after US1.
- **Polish (Phase 6)**: depends on US1–US3; T037/T038 are the final gates.

### User Story Dependencies

- **US1 (P1)**: foundational-only; independently testable; the MVP.
- **US3 (P2)**: builds on US1's command shell.
- **US2 (P2)**: builds on US1's event production; parallel to US3.

### Within Each User Story

- Tests are written first and must FAIL before implementation (US1 tests T006–T018 before impl T019–T024).
- `DraftStateDiffer` (T019) before `UpdateDraftCommand` (T020); both before the deletions (T021–T022) and DI (T023).
- US3: pipeline trim (T025) before its absorption tests (T026–T027).
- US2: catalog edit (T028) before parity confirmation (T030).

### Parallel Opportunities

- T002 ∥ T001 (read-only verification alongside baseline).
- T004 ∥ other Foundational reads; T005 after T004.
- **All US1 test tasks T006–T018 are [P]** — distinct test files, no inter-dependencies — write them together, watch them fail, then implement.
- US3 (T025–T027) and US2 (T028–T032) run in parallel once US1 is done.
- Polish T035 ∥ T036 (different meta-repo files).

---

## Parallel Example: User Story 1 tests

```bash
# Launch the full US1 test wave together (all distinct files, all expected to fail pre-impl):
Task: "Activity diff tests in tests/.../UpdateDraftCommand/ActivityDiffTests.cs"
Task: "Activity I/O diff tests in tests/.../UpdateDraftCommand/ActivityIoDiffTests.cs"
Task: "Connection diff tests in tests/.../UpdateDraftCommand/ConnectionDiffTests.cs"
Task: "Variable diff tests in tests/.../UpdateDraftCommand/VariableDiffTests.cs"
Task: "Workflow I/O diff tests in tests/.../UpdateDraftCommand/WorkflowIoDiffTests.cs"
Task: "Layout diff test in tests/.../UpdateDraftCommand/LayoutDiffTests.cs"
Task: "No-op, multi-dimension, LWW, identity-match, connection-change, cascade, locking tests"
```

---

## Implementation Strategy

### MVP First (User Story 1 only)

1. Phase 1: Setup (baseline + substrate check).
2. Phase 2: Foundational (contract + diff-key verification) — **blocks everything**.
3. Phase 3: US1 — write the test wave, watch it fail, implement the differ + command, delete the 20 contracts/impls, re-wire DI.
4. **STOP and VALIDATE**: the collapse works end-to-end; the granular surface is gone. This is a shippable increment.

### Incremental Delivery

1. Setup + Foundational → contract ready.
2. US1 → the collapse (MVP).
3. US3 → trim the pipeline mutation path (parallel with US2).
4. US2 → event re-homing + catalog + event-sourcing contracts (parallel with US3).
5. Polish → constitution §E2.9.7, follow-up/PERSONAL_TODO, full-suite + quickstart gates.

---

## Notes

- **No FluentAssertions** — xUnit only (constitutionally pinned).
- **No command test is deleted** — every former `*CommandTests` is *moved* to drive `IUpdateDraftCommand`; coverage preserved one-for-one (FR-013, SC-010). No §2.21.1 deletion-approval gate arises.
- Constitution Check (plan.md) is all PASS/N/A with an empty Complexity Tracking table — no SemVer bump (not versioning yet), no architect-approval-gated deletions.
- `[P]` = different files, no incomplete dependencies. `[Story]` maps each task to its user story for traceability.
- Commit after each task or logical group; the `after_tasks` git hook offers a commit at the end.
