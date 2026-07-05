# Phase 1 Data Model — Unit C

> Supersession note (2026-06-11): workflow-level `Activities` + `ActivityConnections`
> data-model entries are superseded by
> [070-workflow-root-activity-contract](../070-workflow-root-activity-contract/spec.md).

> **Supersession note (2026-07-05):** all `WorkflowDefinitionDraftValidation` entries below (entity §2.4, read contract §2.7, the Draft's validation-sibling relationship, the FR-023 rebuild step) are superseded — the entity, its EF config, and `IWorkflowDefinitionDraftValidation` are deleted; validation errors are derived state, recomputed in-lock, not persisted (spec.md FR-021/FR-023). The Draft's only surviving sibling is `WorkflowDefinitionDraftLayout`. Likewise, per-diff mutation-event *publication* is retired (declarations stand); the diff engine is unregistered from DI. Reinstatable when an event-sourcing / cached-error consumer exists.

Entity inventory + relationships + lifecycle for the Workflow Design substrate landed by Unit C. Cross-references spec.md FRs at the entity level; the spec is the authoritative source for behavioural detail.

---

## 1. Entity inventory

### 1.1 Existing entities (Unit C changes summarised)

| Entity | Project | Unit C change |
|---|---|---|
| `WorkflowDefinition` | `Elsa.Workflows.Design.Persistence.Core/Entities` | `MetaData` reference deleted (FR-015); no other field changes (field allocation Unit D's territory per FR-016a). |
| `WorkflowDefinitionVersion` | `Elsa.Workflows.Design.Persistence.Core/Entities` | No field changes; gains a 1:0..1 sibling relationship to `WorkflowDefinitionVersionLayout`. |
| `WorkflowDefinitionDraft` | `Elsa.Workflows.Design.Persistence.Core/Entities` | Gains a 1:0..1 sibling relationship to `WorkflowDefinitionDraftLayout` AND to `WorkflowDefinitionDraftValidation`. May gain a `ClonedFromVersionId` FK (provisional; Unit D's allocation per FR-028). |
| `WorkflowDefinitionState` | `Elsa.Workflows.Design.Core/Models` (record) | XML doc header added quoting scope policy + §E2.X (FR-003). Members unchanged unless FR-005 audit extracts creep — current members (`Variables`, `ActivityConnections`, `Activities`, `Inputs`, `Outputs`, `WorkflowActivityOptions`, `StrategyOptions`) are clean against the policy. |
| `ActivityNode` | `Elsa.Workflows.Design.Core/Models` (record) | `ReferenceKey` renamed to `NodeId` (FR-009). `(activityDefinitionId : string, version : int)` pair collapsed to single `ActivityVersionId : string` (FR-011). `IsStart : bool` (existing) unchanged. |
| `ActivityPortConnection` | `Elsa.Workflows.Design.Core/Models` (record) | `ActivityReferenceKey` renamed to `ActivityNodeId` (FR-009 + R1). |
| `InputDefinition` | `Elsa.Activities.Design.Core/Models` (record) | Gains `bool IsRequired { get; init; } = false;` constructor parameter (FR-036). |
| `OutputDefinition` | `Elsa.Activities.Design.Core/Models` (record) | Gains `bool IsRequired { get; init; } = false;` constructor parameter (FR-036). |
| `WorkflowMetadata` | `Elsa.Workflows.Design.Core/Models` (value object) | **DELETED** (FR-015). |

### 1.2 New entities

| Entity | Project | Type | FR |
|---|---|---|---|
| `WorkflowDefinitionVersionLayout` | `Elsa.Workflows.Design.Persistence.Core/Entities` | sealed class entity | FR-006 |
| `WorkflowDefinitionDraftLayout` | `Elsa.Workflows.Design.Persistence.Core/Entities` | sealed class entity | FR-006 |
| `WorkflowDefinitionDraftValidation` | `Elsa.Workflows.Design.Persistence.Core/Entities` | sealed class entity | FR-021 |
| `DesignMetadataRecord` | `Elsa.Workflows.Design.Persistence.Core/Entities` (or `Models/`) | value object — one entry per placed activity node | FR-006 (sub-shape) |
| `ValidationError` | `Elsa.Workflows.Design.Validations.Core/Models` | sealed record value object | FR-022 |

### 1.3 New read contracts (Tier 1 — `.Core` interfaces)

| Contract | Project | Reads | FR |
|---|---|---|---|
| `IWorkflowDefinitionLayout` | `Elsa.Workflows.Design.Core/Contracts` | unified read surface over both `*Layout` entities | FR-007 |
| `IWorkflowDefinitionDraftValidation` | `Elsa.Workflows.Design.Validations.Core/Contracts` | read surface over `WorkflowDefinitionDraftValidation` entity | FR-021 (read side) |

### 1.4 New domain events

19 events in total. Full list with payload shapes lives in `contracts/events.md`. Inventory:

| Event | Project | Kind | FR |
|---|---|---|---|
| `OnDraftCreated` | Workflows.Design.Core | lifecycle | FR-018 |
| `OnActivityAddedToDraft` | Workflows.Design.Core | mutation (graph) | FR-018 |
| `OnActivityRemovedFromDraft` | Workflows.Design.Core | mutation (graph) | FR-018 |
| `OnActivityPropertyChangedInDraft` | Workflows.Design.Core | mutation (graph) | FR-018 |
| `OnActivityMovedInDraft` | Workflows.Design.Core | mutation (layout) | FR-018 |
| `OnConnectionAddedToDraft` | Workflows.Design.Core | mutation (graph) | FR-018 |
| `OnConnectionRemovedFromDraft` | Workflows.Design.Core | mutation (graph) | FR-018 |
| `OnVariableDeclaredInDraft` | Workflows.Design.Core | mutation (variables) | FR-018 |
| `OnVariableUpdatedInDraft` | Workflows.Design.Core | mutation (variables) | FR-018 |
| `OnVariableRemovedFromDraft` | Workflows.Design.Core | mutation (variables) | FR-018 |
| `OnWorkflowInputAddedToDraft` | Workflows.Design.Core | mutation (workflow inputs) | FR-018 |
| `OnWorkflowInputUpdatedInDraft` | Workflows.Design.Core | mutation (workflow inputs) | FR-018 |
| `OnWorkflowInputRemovedFromDraft` | Workflows.Design.Core | mutation (workflow inputs) | FR-018 |
| `OnWorkflowOutputAddedToDraft` | Workflows.Design.Core | mutation (workflow outputs) | FR-018 |
| `OnWorkflowOutputUpdatedInDraft` | Workflows.Design.Core | mutation (workflow outputs) | FR-018 |
| `OnWorkflowOutputRemovedFromDraft` | Workflows.Design.Core | mutation (workflow outputs) | FR-018 |
| `OnDraftClonedFromVersion` | Workflows.Design.Core | lifecycle | FR-018a |
| `OnDraftDiscarded` | Workflows.Design.Core | lifecycle | FR-018a |
| `OnDraftValidating` | Workflows.Design.Validations.Core | coarse validation | FR-025 |

### 1.5 New commands

16 commands in total. Full list with payload shapes lives in `contracts/commands.md`.

---

## 2. Entity detail

### 2.1 `WorkflowDefinitionVersionLayout` (new — FR-006)

```csharp
[Immutable]
public sealed class WorkflowDefinitionVersionLayout
{
    public string Id { get; init; } = default!;
    public string WorkflowDefinitionVersionId { get; init; } = default!;
    public WorkflowDefinitionVersion WorkflowDefinitionVersion { get; init; } = default!;
    public List<DesignMetadataRecord> Records { get; init; } = new();
    // ... immutability invariants enforced by Workflows.Design.Persistence.EFCore via
    //     PropertySaveBehavior.Throw + SaveChangesAsync guard
}
```

**FK:** `WorkflowDefinitionVersionId` → `WorkflowDefinitionVersion.Id` (1:0..1; Restrict on delete per R5).

**Immutability:** `[Immutable]` attribute drives the scanner; once persisted, no field mutates. Re-laying out an already-promoted Version requires minting a new Version (FR-006a).

**Cardinality:** zero-or-one row per Version (the layout sibling may not yet exist for a Version that hasn't had its layout authored, although in practice promotion copies the Draft's layout into a new Version-layout row — see FR-009a + FR-028).

### 2.2 `WorkflowDefinitionDraftLayout` (new — FR-006)

```csharp
public sealed class WorkflowDefinitionDraftLayout
{
    public string Id { get; set; } = default!;
    public string WorkflowDefinitionDraftId { get; set; } = default!;
    public WorkflowDefinitionDraft WorkflowDefinitionDraft { get; set; } = default!;
    public List<DesignMetadataRecord> Records { get; set; } = new();
    // mutable; mirrors the Draft's mutability per FR-006a
}
```

**FK:** `WorkflowDefinitionDraftId` → `WorkflowDefinitionDraft.Id` (1:0..1; Cascade on delete per R5 — FR-029 atomicity).

**Mutability:** mirrors parent Draft; mutates whenever an `OnActivityMovedInDraft` event fires (FR-018 — layout event folds into the Draft event stream).

### 2.3 `DesignMetadataRecord` (new — FR-006 sub-shape)

```csharp
public sealed record DesignMetadataRecord(
    string NodeId,              // FK-ish — references ActivityNode.NodeId in the parent's State
    double X,
    double Y,
    double? Width = null,
    double? Height = null,
    IDictionary<string, object?>? AdditionalProperties = null   // forward-compat extensibility
);
```

**Belongs to:** `WorkflowDefinitionVersionLayout.Records` or `WorkflowDefinitionDraftLayout.Records`.

**Concrete schema:** one record per placed activity node (per `ActivityNode.NodeId`). The `NodeId` field is the join key into the parent's `WorkflowDefinitionState.Activities[*].NodeId`. Workflow-level canvas state (zoom, pan, etc.) — if added later — lives as a separate row OR as a singleton on the entity (plan-stage detail; current shape is per-node only).

**Orphaning:** transient on Draft-side (per Edge Cases section — tolerated transiently during edits; cleaned on save). Doesn't arise on Version-side (immutable post-promotion).

### 2.4 `WorkflowDefinitionDraftValidation` (new — FR-021)

```csharp
public sealed class WorkflowDefinitionDraftValidation
{
    public string Id { get; set; } = default!;
    public string WorkflowDefinitionDraftId { get; set; } = default!;
    public WorkflowDefinitionDraft WorkflowDefinitionDraft { get; set; } = default!;
    public List<ValidationError> Errors { get; set; } = new();
    // mutable; rewritten wholesale by FR-023 lifecycle
}
```

**FK:** `WorkflowDefinitionDraftId` → `WorkflowDefinitionDraft.Id` (1:0..1; Cascade on delete per R5).

**Lifecycle:** delete-and-re-add per FR-023. After every Draft mutation, the publishing command:
1. Acquires per-Draft lock (FR-027).
2. Updates `WorkflowDefinitionState` snapshot.
3. Publishes the granular FR-018 event.
4. Publishes `OnDraftValidating`; validators run; collect errors via `AddValidationError`.
5. Replaces this entity's `Errors` list wholesale with the collected set.
6. Flushes both to persistence.
7. Releases lock.

No Version-side counterpart — FR-024 promotion gate prevents Versions with non-empty errors.

### 2.5 `ValidationError` (new — FR-022)

```csharp
public sealed record ValidationError(
    string Path,        // R2 format: "{NodeId}/inputs/{InputReferenceKey}", "$workflow", etc.
    string Type,        // R3 format: "RootActivity/Missing", "Graph/UnknownActivityVersion", etc.
    string Message      // human-readable
);
```

**Grouping key:** `(Path, Type)`; multiple errors may share the same key (e.g. two missing required inputs on the same activity).

**Lives in:** `WorkflowDefinitionDraftValidation.Errors`. NEVER directly persisted as standalone; always inside the validation entity's collection.

### 2.6 `IWorkflowDefinitionLayout` (new — FR-007)

```csharp
public interface IWorkflowDefinitionLayout
{
    string Id { get; }
    IReadOnlyList<IDesignMetadataRecord> Records { get; }
}

public interface IDesignMetadataRecord
{
    string NodeId { get; }
    double X { get; }
    double Y { get; }
    double? Width { get; }
    double? Height { get; }
    IReadOnlyDictionary<string, object?>? AdditionalProperties { get; }
}
```

Tier-1 read contract; implemented by both `WorkflowDefinitionVersionLayout` and `WorkflowDefinitionDraftLayout` (each implementing `IWorkflowDefinitionLayout`). Lets design-time consumers (UI, tooling) load layout without depending on `*.Persistence.Core`.

### 2.7 `IWorkflowDefinitionDraftValidation` (new — FR-021 read side)

```csharp
public interface IWorkflowDefinitionDraftValidation
{
    string Id { get; }
    string WorkflowDefinitionDraftId { get; }
    IReadOnlyList<ValidationError> Errors { get; }
}
```

Tier-1 read contract; implemented by the `WorkflowDefinitionDraftValidation` entity. Lets the UI + the promotion gate (FR-024) read the current error set without `*.Persistence.Core` dependency.

---

## 3. Relationships

```
WorkflowDefinition (1) ───┬─── (0..N) WorkflowDefinitionVersion (1) ─── (0..1) WorkflowDefinitionVersionLayout
                          │
                          └─── (0..N) WorkflowDefinitionDraft (1) ──────┬─── (0..1) WorkflowDefinitionDraftLayout
                                                                       │
                                                                       └─── (0..1) WorkflowDefinitionDraftValidation
                                                                                                              ↓
                                                                                              (List<ValidationError>)
```

**Multi-draft cardinality** (how many Drafts can exist per Definition): explicitly deferred to Unit D per FR-028 + Unit C follow-up's *Out of scope*. Unit C's command surface supports the operations; the cardinality constraint is enforced elsewhere.

**Cross-references inside `WorkflowDefinitionState`:**
- `Activities[*].ActivityVersionId : string` references the activity catalog (`Elsa.Activities.Design.Core`); the value follows Unit B's emerging format per FR-011a. No structural FK at the C# level; string handshake only.
- `ActivityConnections[*].ActivityNodeId : string` references `Activities[*].NodeId` within the same State graph (per R1 naming).
- `Activities[*].Inputs[*]` / `Activities[*].Outputs[*]` use `ArgumentState` to bind values to `ArgumentDefinition.ReferenceKey` (unchanged per FR-010).
- `Inputs[*]` (workflow-level) + `Outputs[*]` use the same `InputDefinition` / `OutputDefinition` records as activity inputs/outputs (per Q1 finding in session 3); `IsRequired` flag applies uniformly.

---

## 4. Lifecycle diagrams

### 4.1 Draft mutation pipeline (FR-027 + FR-027a + FR-027c)

```
Client → IAddActivityToDraftCommand.Execute(args)
         │
         ▼
    acquire lock workflow-draft:{DraftId}   ← FR-027 step 1; provider per FR-027a
         │
         ▼
    load Draft + apply mutation in memory    ← FR-027 step 2 (snapshot update)
         │
         ▼
    IDomainEventSender.Send(OnActivityAddedToDraft)  ← FR-027 step 3 (granular event;
         │                                              event-sourcing seam observes here)
         │   (dispatcher: Iterator → ExceptionShielding → Invoker — handler exceptions
         │    caught + logged + swallowed per FR-027c + framework §2.6.1 default)
         ▼
    IDomainEventSender.Send(OnDraftValidating)        ← FR-027 step 4 (validators run;
         │                                              contribute errors via AddValidationError)
         │   (same dispatcher shield; same swallow semantics)
         ▼
    rebuild WorkflowDefinitionDraftValidation.Errors  ← FR-027 step 5 (delete-and-re-add per FR-023)
         │   from event.Errors
         ▼
    transactional flush (DbContext.SaveChangesAsync)  ← FR-027 step 6
         │
         ▼
    release lock                                       ← FR-027 step 7
         │
         ▼
    Client ← command returns
```

Handler exceptions from any step never propagate to the client per FR-027c. The shielding middleware logs with diagnostic context.

### 4.2 Draft promotion to Version (FR-024 + FR-027b)

```
Client → IPromoteDraftToVersionCommand.Execute(draftId)   ← provisional name per R8; Unit D allocates
         │
         ▼
    acquire lock workflow-draft:{DraftId}   ← FR-027b (same lock as mutation commands)
         │
         ▼
    load Draft + load Draft's validation sibling
         │
         ▼
    gate-check: validation.Errors.IsEmpty?
         │           ┌─── if NOT empty ───→ throw DraftHasValidationErrorsException
         │                                          (includes Draft ref + error count)
         │   if empty
         ▼
    create new WorkflowDefinitionVersion from Draft's State (deep copy)
         │
         ▼
    create new WorkflowDefinitionVersionLayout from Draft's Layout (deep copy)
         │
         ▼
    (optional) update Draft lifecycle state — Unit D's territory
         │
         ▼
    transactional flush
         │
         ▼
    release lock
```

Mutations arriving *after* the lock is released but before another mutation acquires it are not part of the promoted Version — they exist on the Draft only. This is the architectural consequence Joey pinned in clarify s2 Q for the promotion lock.

### 4.3 Clone Draft from Version (FR-028)

```
Client → ICloneDraftFromVersionCommand.Execute(sourceVersionId, targetDefinitionId)
         │
         ▼
    create new Draft (generate new DraftId)
         │
         ▼
    acquire lock workflow-draft:{NEW DraftId}   ← FR-027 on the new Draft
         │
         ▼
    deep-copy WorkflowDefinitionState from source Version → new Draft
         │   (NodeIds carry per FR-009a)
         │
         ▼
    create WorkflowDefinitionDraftLayout, deep-copy from source Version's Layout
         │
         ▼
    set new Draft's ClonedFromVersionId = sourceVersionId   ← provisional FK per FR-028
         │
         ▼
    IDomainEventSender.Send(OnDraftClonedFromVersion)
         │
         ▼
    transactional flush
         │
         ▼
    release lock
```

Cardinality interaction (new Clone-from-Version vs pre-existing Draft of the same Definition) — explicitly Unit D's call per FR-028.

### 4.4 Discard Draft (FR-029)

```
Client → IDiscardDraftCommand.Execute(draftId)
         │
         ▼
    acquire lock workflow-draft:{DraftId}   ← FR-027 on the Draft being discarded
         │
         ▼
    load Draft (load returns null if already gone — idempotent path exits cleanly here)
         │
         ▼
    delete Draft + cascade siblings (Layout + Validation) per R5 cascade rules
         │
         ▼
    IDomainEventSender.Send(OnDraftDiscarded)
         │
         ▼
    transactional flush
         │
         ▼
    release lock
```

Idempotent — second Discard on same DraftId is a no-op.

### 4.5 Validation delete-and-re-add (FR-023)

After every mutation pipeline (4.1), the validation entity's `Errors` list is reset to the validators' current pass output:

```
prior Errors = [ ValidationError("$workflow", "RootActivity/Missing", "Workflow has no root activity.") ]

mutation: set a root activity
    ↓
validators run
    ↓
new Errors = [ ]   (the offending condition is gone)

mutation: clear the root activity again
    ↓
validators run
    ↓
new Errors = [ ValidationError("$workflow", "RootActivity/Missing", "Workflow has no root activity.") ]
```

Errors are simple data — never tracked as immutable individuals with `IsSolved` flags.

---

## 5. Persistence invariants

| Invariant | Defined in | Provider enforcement |
|---|---|---|
| `WorkflowDefinitionVersion` is immutable | `[Immutable]` on the entity (Persistence.Core) | EF Core: `PropertySaveBehavior.Throw` + `SaveChangesAsync` guard |
| `WorkflowDefinitionVersionLayout` is immutable | `[Immutable]` on the entity (Persistence.Core) | Same |
| `WorkflowDefinitionDraft` is mutable | (no attribute) | EF Core: standard mutable tracking |
| `WorkflowDefinitionDraftLayout` is mutable | (no attribute) | Same |
| `WorkflowDefinitionDraftValidation` is mutable | (no attribute) | Same; rewritten wholesale per FR-023 |
| Draft delete cascades both siblings | `OnDelete(Cascade)` on FK relationships | EF Core configuration per R5 |
| Version delete is forbidden | `OnDelete(Restrict)` on Version FK | EF Core configuration; backstop against out-of-band deletes |

Per framework §2.9, invariants are defined in `*.Persistence.Core` and enforced by the provider in `*.Persistence.EFCore`. A future non-EF provider would enforce the same invariants through its native mechanism.

---

## 6. Migration impact

Per R10: fresh init migration for both `ActivitiesDesignDbContext` (`IsRequired` column added) and `WorkflowsDesignDbContext` (`MetaData` column removed, three new entity tables added). SQLite default provider; migration is regenerated at /speckit.tasks execution time.

---

## Cross-references

- Spec: [spec.md](./spec.md) — authoritative FR/SC source.
- Research: [research.md](./research.md) — plan-stage decisions referenced as R1..R10.
- Contracts: [contracts/commands.md](./contracts/commands.md), [contracts/events.md](./contracts/events.md), [contracts/read-surfaces.md](./contracts/read-surfaces.md).
- Quickstart: [quickstart.md](./quickstart.md).
