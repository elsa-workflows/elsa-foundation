# Phase 1 Data Model: Single Diff-Based Draft Update Command

> Supersession note (2026-06-11): workflow-level activity/connection tables are superseded by
> [070-workflow-root-activity-contract](../070-workflow-root-activity-contract/spec.md). The
> corrected model uses `WorkflowDefinitionState.RootActivity`.

> **Supersession note (2026-07-05):** model entries for the `WorkflowDefinitionDraftValidation` sibling are superseded — the entity is deleted; validation errors are derived state, recomputed in-lock, not persisted (spec 002 FR-021). Entries describing the semantic diff producing/publishing per-diff events are likewise moot on the mutation path — publication is retired (diff engine remains the tested contract but is unregistered from DI). Reinstatable when a consumer exists.

**Feature**: `003-single-update-command` (Unit 2) · **Date**: 2026-06-03
**Inputs**: [research.md](./research.md) (R2/R3/R5), [spec.md](./spec.md) FR-001/001a/019/020/022/023.

This feature introduces **no new persisted entities and no change to any persisted shape**. The State model, the layout sibling, the validation sibling, and the `WorkflowDefinitionDraft` entity are all reused verbatim. The only *new* type is a transient input DTO (`UpdateDraftRequest`) and an internal, non-persisted diff model. This document records the shapes the command reads, writes, and matches on.

---

## 1. New transient type — `UpdateDraftRequest`

The single coarse command's input. Not persisted; constructed by the caller (designer/API) and passed to `IUpdateDraftCommand.Execute`.

| Field | Type | Notes |
|---|---|---|
| `DraftId` | `string` | Identifies the Draft to update; keys the distributed lock `workflow-draft:{DraftId}`. |
| `State` | `WorkflowDefinitionState` | The **complete desired** content state (reused record, §3). Full-state-always — no patch mode (FR-001, R5). |
| `Layout` | `IReadOnlyCollection<DesignMetadataRecord>` | The **complete desired** designer layout records (FR-001a). Carried *alongside* State, never inside it (honours §E2.9.2 — layout is not reachable through `WorkflowDefinitionState`). |

**Validation rules** (request-shape, pre-lock):
- `DraftId` non-empty. Empty/unknown DraftId → load fails → command surfaces a not-found error (no diff, no events).
- `State` non-null. A caller wanting "empty workflow" passes an empty-but-non-null `WorkflowDefinitionState`.
- `Layout` non-null (may be empty). Layout records whose `NodeId` has no matching activity in `State` are tolerated (orphan layout is a designer concern; diff still computes against stored layout).

**Lifecycle**: constructed → passed to `Execute` → consumed for wholesale assignment + diff → discarded. Stateless.

---

## 2. New internal type — diff model (`DraftStateDiffer` output)

Internal to `Elsa.Workflows.Design.Persistence.EFCore` (provisional `DraftStateDiffer`, R7). **Not** a public contract, **not** persisted. Produces the ordered list of `IEvent`s to publish post-commit.

Conceptual shape (final form decided at implementation):

```
DraftStateDiffer.Diff(stored: (WorkflowDefinitionState, IReadOnlyCollection<DesignMetadataRecord>),
                      desired: (WorkflowDefinitionState, IReadOnlyCollection<DesignMetadataRecord>))
    → IReadOnlyList<IEvent>   // the existing 20 mutation event types, in a deterministic order
```

The differ emits the **existing** event types (§3.4) — it constructs no new event shape. Each `Update*` event carries `OldValue` (from `stored`) + `NewValue` (from `desired`); the differ supplies both.

**Match semantics** (per dimension, R2): same match key + changed payload → UPDATE; key only in stored → REMOVE; key only in desired → ADD. See §4 for keys.

---

## 3. Reused persisted / domain types (UNCHANGED)

### 3.1 `WorkflowDefinitionState` (`Elsa.Workflows.Design.Core/Models/WorkflowDefinitionState.cs`)

Reused record, no shape change.

| Field | Type |
|---|---|
| `Variables` | `IEnumerable<VariableDefinition>` |
| `ActivityConnections` | `IEnumerable<ActivityConnection>` |
| `Activities` | `IEnumerable<ActivityNode>` |
| `Inputs` | `IEnumerable<InputDefinition>` |
| `Outputs` | `IEnumerable<OutputDefinition>` |
| `ActivityOptions` | `WorkflowActivityOptions?` |
| `StrategyOptions` | `WorkflowStrategyOptions?` |

Activity I/O lives on `ActivityNode` (`Inputs`/`Outputs` collections of `ArgumentState`).

### 3.2 `WorkflowDefinitionDraft` (`…Persistence.Core/Entities/WorkflowDefinitionDraft.cs`)

`TenantEntity` (Id, TenantId, CreatedAt, LastModifiedAt) + `WorkflowDefinitionId`, `[NotMapped] State`, `string? StateSource` (shadow JSON). **No rowversion / concurrency token — and none is added** (FR-022, last-writer-wins, R6).

### 3.3 Layout + validation siblings

- `WorkflowDefinitionDraftLayout` — holds `Records` (`DesignMetadataRecord` set). `DesignMetadataRecord(NodeId, X, Y, Width?, Height?, AdditionalProperties?)`. Written wholesale by the command.
- Validation sibling — upserted wholesale with the post-diff validation outcome (unchanged behaviour, R8).

### 3.4 The 20 mutation event types (`Elsa.Workflows.Design.Core/Events/`)

Re-homed, **not redefined**. All `sealed : IEvent`. Their *producer* changes from the deleted per-action commands to `IUpdateDraftCommand`; their identity, payload, and names are preserved (R7). The 3 lifecycle events (`DraftCreated`/`DraftClonedFromVersion`/`DraftDiscarded`) and the validation pair (`DraftValidating`/`DraftValidated`) are untouched.

---

## 4. Match keys (identity per dimension) — the diff's backbone (R2, FR-023)

| Dimension | Element type | Match key | Update event? |
|---|---|---|---|
| Variables | `VariableDefinition` | `ReferenceKey` | yes |
| Workflow inputs | `InputDefinition` | `ReferenceKey` | yes |
| Workflow outputs | `OutputDefinition` | `ReferenceKey` | yes |
| Activities | `ActivityNode` | `NodeId` | no (Add/Remove/Move) |
| Activity inputs | `ArgumentState` | (`NodeId`, `ReferenceKey`) | yes |
| Activity outputs | `ArgumentState` | (`NodeId`, `ReferenceKey`) | yes |
| Connections | `ActivityConnection` | `(Source.ActivityNodeId, Source.Port) → (Target.ActivityNodeId, Target.Port)` tuple | **no** (Add/Remove) |
| Layout | `DesignMetadataRecord` | `NodeId` | move |

**Consequences**:
- Rename of a keyed element (e.g. `VariableDefinition.Name` changes, `ReferenceKey` stable) → single UPDATE (FR-023, SC-015), not remove+add.
- Connections have no synthetic id and no update event; any change diffs as REMOVE(old tuple)+ADD(new tuple). **No id field is added to any element type** — the State model is untouched.
- Activity display-name rename keeps `NodeId` stable → does not cascade to connections (connections key on `ActivityNodeId`). Only deleting/replacing an activity (new `NodeId`) cascades to connection prune — matching today's `RemoveActivityFromDraftCommand`.

---

## 5. State transitions (the command's write sequence)

Inside the per-Draft lock (`workflow-draft:{DraftId}`), absorbing the `DraftMutationPipeline.ExecuteMutation` ordering (R8):

1. **Acquire** distributed lock.
2. **Load + hydrate** stored Draft (State + layout siblings) — `stored` snapshot for diffing.
3. **Apply wholesale**: `draft.State = request.State`; `layout.Records = request.Layout`. Mark Modified.
4. **Diff**: `DraftStateDiffer.Diff(stored, desired)` → ordered `IReadOnlyList<IEvent>`.
5. **Sequential gate**: publish `DraftValidating` (sync, awaited) against post-apply state; collect `Errors`.
6. **Upsert** validation sibling wholesale with the outcome.
7. **SaveChanges** (transactional flush).
8. **Release** lock.
9. **Background-publish** the per-diff events (step 4), then `DraftValidated` (cause-before-effect).

No optimistic-concurrency check at any step (FR-022). A no-op request (desired == stored) yields an empty diff → no mutation events; the validation pair still runs (matches today's pipeline).

---

## 6. What is explicitly NOT modelled here

- **No version/concurrency column** (FR-022).
- **No new id field** on any State element (FR-023 satisfied by existing match keys).
- **No change to `WorkflowDefinitionState`, `DesignMetadataRecord`, the entities, or the event payloads.**
- **Lifecycle commands** (`ICreate/IClone/IDiscard/IPromote…`) and their events — out of scope (FR-003); promote → Unit D; create/clone/discard topology → [follow-up](../../../elsa-foundation-project-management/epic1-elsa-refactor-constitution/follow-up-items/2026-06-03_followup_lifecycle_command_shells.md).
