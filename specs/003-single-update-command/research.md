# Phase 0 Research: Single Diff-Based Draft Update Command

**Feature**: `003-single-update-command` (Unit 2) · **Date**: 2026-06-03
**Inputs**: [spec.md](./spec.md), constitution `.specify/memory/constitution.md` §E2.2/§E2.5/§E2.6/§E2.9, framework constitution §2.6/§2.6.1/§2.10/§2.21.1/§2.23, three codebase-mapping sweeps (command surface, State model, events/validation/catalog).

This document resolves every open decision the spec left for plan and records the verified codebase facts the design rests on. All spec `[NEEDS CLARIFICATION]` markers are resolved here (the last one, FR-021 naming, is decided in R7).

---

## R1 — Verified current surface (ground truth)

| Element | Count | Location |
|---|---|---|
| Granular mutation command contracts | 20 | `Elsa.Workflows.Design.Persistence.Core/Contracts/I*DraftCommand.cs` |
| Granular mutation command impls | 20 | `Elsa.Workflows.Design.Persistence.EFCore/Commands/*.cs` |
| Granular mutation **event types** | 20 | `Elsa.Workflows.Design.Core/Events/` |
| Lifecycle event types | 3 | same (`OnDraftCreated`, `OnDraftClonedFromVersion`, `OnDraftDiscarded`) |
| Lifecycle command contracts | 4 | `…Persistence.Core/Contracts/` (`ICreate/IClone/IDiscard/IPromote…`) |
| Validation event pair | 2 | `Elsa.Workflows.Design.Validations.Core/Events/` (`OnDraftValidating`, `OnDraftValidated`) |

**Decision**: spec counts (20/20/3, 23 Design.Core events total) are confirmed against source. The follow-up's "22/23" framing is approximate; the plan uses the verified counts.

**DI registration**: all 24 commands + the `DraftMutationPipeline` are registered `AddScoped` in `Elsa.Workflows.Design.Persistence.EFCore/EFCoreWorkflowsPersistenceFeatureBase.cs` (pipeline at the `.AddScoped<DraftMutationPipeline>()` line). The 5 baseline `IDraftValidator`s register in `Elsa.Workflows.Design.Validations/WorkflowDesignValidationsFeature`.

---

## R2 — Diff identity keys per dimension (resolves FR-023)

The State model carries stable ids on every dimension **except connections**, which use an endpoint tuple. Verified shapes (`Elsa.Workflows.Design.Core/Models/WorkflowDefinitionState.cs` + element types):

| Dimension | Element type | Match key (identity) | Update event exists? |
|---|---|---|---|
| Variables | `VariableDefinition` | `ReferenceKey` (string) | yes (`OnVariableUpdatedInDraft`) |
| Workflow inputs | `InputDefinition` | `ReferenceKey` | yes |
| Workflow outputs | `OutputDefinition` | `ReferenceKey` | yes |
| Activities | `ActivityNode` | `NodeId` (string) | no (only Add/Remove/Move) |
| Activity inputs | `ArgumentState` (in `ActivityNode.Inputs`) | (`NodeId`, `ReferenceKey`) | yes |
| Activity outputs | `ArgumentState` (in `ActivityNode.Outputs`) | (`NodeId`, `ReferenceKey`) | yes |
| Connections | `ActivityConnection` | `(Source.ActivityNodeId, Source.Port) → (Target.ActivityNodeId, Target.Port)` value tuple | **no** (only Add/Remove) |
| Layout | `DesignMetadataRecord` (in `WorkflowDefinitionDraftLayout.Records`) | `NodeId` | move (`OnActivityMovedInDraft`) |

**Decision**: the semantic diff matches on these per-dimension keys. Same-key + changed-payload → UPDATE; key-absent-from-desired → REMOVE; key-absent-from-stored → ADD. A rename of a keyed element (e.g. `VariableDefinition.Name` changes while `ReferenceKey` is stable) is a single UPDATE — confirming FR-023 and SC-015.

**Connection refinement (important)**: connections have *no* synthetic id and *no* update event. Their value-tuple identity is their key; any change to a connection is a different tuple, so it diffs as REMOVE(old tuple)+ADD(new tuple). This is consistent with the existing event surface (only `OnConnectionAddedToDraft` / `OnConnectionRemovedFromDraft`). FR-023's "stable id per dimension" is therefore read as "stable *match key* per dimension" — for connections that key is the endpoint tuple, not an id. **No id field needs to be added to any element type.** This retires the open item flagged in the spec's Assumptions ("the one place FR-023 could touch the State model"): it does not — the State model is untouched.

**Activity rename cascade**: an activity's identity is `NodeId` (stable across rename). Because connections key on `ActivityNodeId`, renaming an activity's *display name* does not touch connections (the NodeId is unchanged). Only deleting/replacing an activity (new NodeId) cascades to connection remove+add — matching today's `RemoveActivityFromDraftCommand`, which already prunes connections referencing the removed NodeId.

---

## R3 — Diff granularity & event mapping (confirms FR-019, semantic)

**Decision**: semantic, per domain concept — the diff engine is concept-aware (not a generic JSON-tree differ). Each detected concept-level change maps 1:1 to one of the 20 event types. The mapping is exhaustively derivable from the deleted commands' bodies (each command already encodes one concept→event mapping):

| Detected change | Event |
|---|---|
| Activity present in desired, absent in stored | `OnActivityAddedToDraft` |
| Activity absent in desired, present in stored | `OnActivityRemovedFromDraft` (+ prune its connections) |
| Layout record (X/Y/W/H) changed for a NodeId | `OnActivityMovedInDraft` |
| Activity-input added / changed / removed (by `ReferenceKey`) | `OnActivityInput{Added,Updated,Removed}…` |
| Activity-output added / changed / removed | `OnActivityOutput{Added,Updated,Removed}…` |
| Connection tuple added / removed | `OnConnection{Added,Removed}…` |
| Variable declared / changed / removed (by `ReferenceKey`) | `OnVariable{Declared,Updated,Removed}…` |
| Workflow input added / changed / removed | `OnWorkflowInput{Added,Updated,Removed}…` |
| Workflow output added / changed / removed | `OnWorkflowOutput{Added,Updated,Removed}…` |

The `Update*` events carry `OldValue` + `NewValue`; the diff supplies both (stored = old, desired = new). This is exactly what the deleted update-commands constructed.

---

## R4 — Apply-step topology (confirms FR-020, survive as private apply-steps)

The 20 command bodies are tiny and uniform: each does a `State = State with { … }` (or `State.WithMutatedActivity(nodeId, …)`, or a layout-record edit) and constructs one event. Verified examples:

- `AddActivityToDraftCommand`: `State = State with { Activities = State.Activities.Append(activity) }` → `OnActivityAddedToDraft`.
- `UpdateVariableInDraftCommand`: find old by `ReferenceKey`, replace, → `OnVariableUpdatedInDraft(…, old, new)`.
- `MoveActivityInDraftCommand`: edits the `WorkflowDefinitionDraftLayout.Records` (layout sibling, not State) → `OnActivityMovedInDraft`.

**Decision**: each command's apply logic is demoted to a **private apply-step** invoked by the diff engine. Two viable internal shapes:

- **(chosen) Apply-step = "produce the event from a detected diff"**, and a single State-rebuild applies all desired state at once. Because `IUpdate` receives the *complete desired state* (R5), the engine can set `draft.State = desiredState` wholesale (and `layout.Records = desiredLayout`) in one shot, then *separately* compute the event list by diffing. This cleanly separates "apply" (trivial: assign desired) from "emit" (diff → events). The per-concept logic that survives is the **diff-and-emit** mapping (R3), not a 20-fold mutation sequence.
- (rejected) Replaying 20 kinds of incremental `State with{…}` mutations one diff at a time — needlessly reconstructs the end state the caller already supplied, and risks intermediate-state ordering bugs.

So FR-020's "apply logic survives as private apply-steps" is realised as: the **event-derivation** per concept survives (migrated from each command body); the **state mutation** collapses to a single wholesale assignment of the supplied desired state. This preserves every command's *objective* (the test "change X yields event Y") under §2.21.1 while removing the per-action State-juggling that the full-state input makes redundant. Apply-steps live as `private`/`internal` methods (or small per-dimension differ types) inside the `IUpdate` implementation assembly — never as public contracts.

**Test migration (FR-013, SC-010)**: each `*CommandTests` whose objective is "operation produces event E and resulting state S" is **moved/migrated** (not deleted) to drive `IUpdateDraftCommand` with a desired state expressing that one change and asserting E + S. Coverage is preserved one-for-one — **every diff dimension keeps a test** so that each event is validated to publish correctly (Joey, 2026-06-03). No test deletion arises, so no §2.21.1 architect-approval gate is triggered.

---

## R5 — Input shape (confirms FR-001/FR-001a full-state + layout)

**Decision**: `IUpdate` takes the **complete desired Draft state** = desired `WorkflowDefinitionState` **plus** the desired layout records (the `DesignMetadataRecord` set). No partial/patch mode. Concrete carrier (plan choice): a small DTO

```
UpdateDraftRequest(string DraftId, WorkflowDefinitionState State, IReadOnlyCollection<DesignMetadataRecord> Layout)
```

- Reuses the existing `WorkflowDefinitionState` record (no new State shape) and the existing `DesignMetadataRecord` (the layout sibling's element). Both already carry the R2 match keys.
- Keeps layout *out of* `WorkflowDefinitionState` (honours §E2.9.2 "designer layout metadata… never reachable through WorkflowDefinitionState"); the request object simply carries both siblings side by side.
- The command writes `draft.State = request.State` and `layout.Records = request.Layout` inside the lock, then diffs both for events.

`OldValue` for update events comes from the *stored* state loaded under the lock; `NewValue` from the request. So the request need not carry old values — the engine reads them.

---

## R6 — Concurrency (confirms FR-022, last-writer-wins whole-draft)

**Verified**: `WorkflowDefinitionDraft` (`…Persistence.Core/Entities/WorkflowDefinitionDraft.cs`) has **no rowversion / concurrency token** — it inherits `TenantEntity` (Id, TenantId, CreatedAt, LastModifiedAt). The per-Draft distributed lock (`workflow-draft:{DraftId}`, via `IDistributedLockProvider`) serialises writers but does not detect a stale-based payload.

**Decision**: last-writer-wins, whole-draft. `IUpdate` adds **no** version column and performs **no** optimistic-concurrency check. A desired state computed from a stale read overwrites a concurrently-committed edit; the resulting diff legitimately emits REMOVE/UPDATE events for the clobbered work. Conflict *avoidance* (e.g. single-editor-per-Draft UX) is a workflow-designer concern, out of Unit 2 scope. This keeps the `WorkflowDefinitionDraft` entity unchanged (Assumption preserved) and matches SC-014.

---

## R7 — Command + apply-step naming (resolves FR-021, the last open marker)

The 4 retained lifecycle commands are named `I{Verb}…Command` (`ICreateDraftCommand`, `ICloneDraftFromVersionCommand`, `IDiscardDraftCommand`, `IPromoteDraftToVersionCommand`).

**Decision**:
- **Command contract**: `IUpdateDraftCommand` (impl `UpdateDraftCommand`), method `Task Execute(UpdateDraftRequest request, CancellationToken cancellationToken = default)`. Chosen over the bare provisional `IUpdate` for consistency with the lifecycle command naming family and to keep the contract self-describing in `…Persistence.Core/Contracts/`.
- **Per-diff events**: keep their existing names verbatim (`OnActivityAddedToDraft`, …). They are already `IEvent`s on the Unit 1 substrate; re-homing changes only their *producer*, not their identity. Renaming them would break the catalog-parity test and event-sourcing consumers for no benefit.
- **Diff engine / apply-steps**: internal; provisional `DraftStateDiffer` (computes the event list from stored-vs-desired) living in `Elsa.Workflows.Design.Persistence.EFCore` alongside `UpdateDraftCommand`. Not a public contract (G2/G25 — no `.Contracts`-style indirection, no public surface).

This fully resolves the spec's only remaining `[NEEDS CLARIFICATION]` (FR-021).

---

## R8 — Validation pair reuse (confirms FR-008)

**Verified**: `OnDraftValidating(IWorkflowDefinitionDraft draft)` exposes `ICollection<ValidationError> Errors`; the single `ExecuteValidations : IEventHandler<OnDraftValidating>` (`Elsa.Workflows.Design.Validations/Handlers/ExecuteValidations.cs`) injects `IEnumerable<IDraftValidator>`, calls `Validate(draft, ct)` on each, and `Errors.Add`s the results. `OnDraftValidated(draft, IReadOnlyList<ValidationError> errors)` carries the persisted outcome + `HasErrors`.

**Decision**: unchanged. `IUpdate` runs the identical Sequential gate once against the post-diff state, upserts the validation sibling wholesale, then Background-publishes the per-diff events followed by `OnDraftValidated` (cause-before-effect). This is the exact `DraftMutationPipeline.ExecuteMutation` ordering, lifted into the command. No validator, no `IDraftValidator` contract, no validation event changes. (Confirms SC-007.)

---

## R9 — Catalog file name + parity test (corrects FR-011/SC-005 wording; confirms FR-012)

**Verified**: the catalog is `src/Elsa.Workflows.Design.Core/EVENTS.md` (renamed from `DOMAIN_EVENTS.md` on 2026-05-29; the spec's `DOMAIN_EVENTS.md` references are stale — treat as `EVENTS.md`). Each entry records **Semantic / Payload / Publication site / Expected handlers**. The parity test `tests/Elsa.Workflows.Design.Tests/Unit/CatalogParityTests.cs` asserts **bidirectional heading↔IEvent-type parity only** (regex `^### (On[A-Za-z0-9]+)\s*$`); it does **not** assert publication-site text.

**Decision**: FR-011's work is editing the **"Publication site"** prose of each mutation-event entry from the deleted command name to `IUpdateDraftCommand` (documentation-only). Because event *types* are unchanged, the parity test (FR-012/SC-005) keeps passing untouched. Both `EVENTS.md` catalogs (Design.Core and Validations.Core) are parametrised in the test; only the Design.Core mutation entries' publication-site lines change.

---

## R10 — Constitution update locus (confirms FR-014/FR-015/FR-016)

**Verified**: §E2.9 (`constitution.md` lines 646–702) codifies the `WorkflowDefinitionState` scope policy + architectural triplet + Model X reconciliation + status — it contains **no** sub-rule pinning "granular CQS commands" as the Draft-mutation surface. The 2026-06-02 audit finding (no pre-existing pin) holds. The only `CQS` mention is the generic `Elsa.Persistence` registry row (line 427) — unrelated to Draft mutation; **MUST NOT** be altered.

**Decision**: add a new draft sub-section **§E2.9.7 "Draft-mutation command surface"** stating the canonical Draft-mutation surface is the single diff-based `IUpdateDraftCommand` (the 4 lifecycle commands remain distinct; the per-diff event surface is preserved for event-sourcing). Mark it provisional with the same "pending architecture-review ratification" status as the rest of §E2.9 (§E2.9.6). Lands in-unit per `[[feedback_constitution_updates_in_unit]]`. Record the "no pre-existing pin to correct" finding in the Unit 2 follow-up (SC-011).

---

## R11 — Lifecycle commands during the absorption (confirms FR-003)

**Verified**: `CreateDraftCommand` + `CloneDraftFromVersionCommand` call `DraftMutationPipeline.ExecuteCreation`; `DiscardDraftCommand` **already** bypasses the pipeline (own lock + direct delete + Background `OnDraftDiscarded`); `PromoteDraftToVersionCommand` is an unimplemented placeholder (Unit D).

**Decision**: Unit 2 absorbs only the **mutation** path (`ExecuteMutation`) into `IUpdateDraftCommand`. The `ExecuteCreation` path **stays** on `DraftMutationPipeline` for now so create/clone remain green (least-invasive, FR-003). After Unit 2, `DraftMutationPipeline` no longer has a *mutation* responsibility; its residual creation path + the create/clone/discard topology alignment is the [`2026-06-03_followup_lifecycle_command_shells.md`](../../../elsa-foundation-project-management/epic1-elsa-refactor-constitution/follow-up-items/2026-06-03_followup_lifecycle_command_shells.md) follow-up; promote is Unit D. So "no standalone `DraftMutationPipeline` on the **mutation** path" (SC-006) is satisfied even though the type lingers for creation until the follow-up retires it.

---

## Summary of resolutions

| Spec marker / open item | Resolution |
|---|---|
| FR-019 diff granularity | Semantic per-concept; 1:1 event map (R3) |
| FR-020 apply-steps | Public contracts deleted; per-concept *emit* logic survives privately; state apply collapses to wholesale assignment (R4) |
| FR-021 naming | `IUpdateDraftCommand` + `UpdateDraftRequest`; events keep names; internal `DraftStateDiffer` (R7) |
| FR-022 concurrency | Last-writer-wins, no version column (R6) |
| FR-023 identity | Per-dimension match keys; connections = endpoint tuple (no id added) (R2) |
| Input shape | Full desired State + layout via `UpdateDraftRequest` (R5) |
| Catalog filename | `EVENTS.md`, not `DOMAIN_EVENTS.md`; publication-site prose edit only (R9) |
| Constitution | New provisional §E2.9.7 (R10) |
| Lifecycle commands | Mutation path absorbed; creation path lingers → follow-up; promote → Unit D (R11) |

No unresolved `[NEEDS CLARIFICATION]` remain. Ready for Phase 1.
