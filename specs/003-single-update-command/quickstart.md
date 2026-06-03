# Quickstart: Single Diff-Based Draft Update Command

**Feature**: `003-single-update-command` (Unit 2) · **Date**: 2026-06-03

This is the developer-facing orientation for the `IUpdateDraftCommand` collapse. It shows what changes for callers, how to drive the command, and where the moving parts live.

---

## What changed (caller's view)

**Before** (Unit C): 20 granular commands — `AddActivityToDraftCommand`, `UpdateVariableInDraftCommand`, `MoveActivityInDraftCommand`, … Each applied one incremental mutation and emitted one event.

**After** (Unit 2): one command. You compute the **complete desired Draft state** (the same way the designer already holds it) and submit it. The command diffs it against what's stored and emits the same per-concept events — now derived from the diff rather than from a per-action call.

```csharp
await updateDraftCommand.Execute(
    new UpdateDraftRequest(
        DraftId: draftId,
        State:   desiredState,    // complete WorkflowDefinitionState
        Layout:  desiredLayout),  // complete DesignMetadataRecord set
    cancellationToken);
```

There is no patch/partial mode. "Move one activity" and "rewrite the whole graph" use the identical call; the diff decides which events fire.

---

## What stays the same

- **Events**: `OnActivityAddedToDraft`, `OnVariableUpdatedInDraft`, `OnActivityMovedInDraft`, … — same names, same payloads, same `IEvent` substrate. Event-sourcing consumers (Unit H) see no change in the event stream's vocabulary.
- **Validation pair**: `OnDraftValidating` (Sequential gate) → `OnDraftValidated` (Background outcome). Unchanged; runs once per `Execute` against the post-diff state.
- **Lock**: the per-Draft `workflow-draft:{DraftId}` distributed lock.
- **Lifecycle commands**: `ICreateDraftCommand`, `ICloneDraftFromVersionCommand`, `IDiscardDraftCommand`, `IPromoteDraftToVersionCommand` — untouched (FR-003).

---

## End-to-end flow (one `Execute` call)

```
lock(workflow-draft:{DraftId})
  ├─ load + hydrate stored Draft         → stored (State + layout)
  ├─ draft.State   = request.State        (wholesale assign)
  ├─ layout.Records = request.Layout       (wholesale assign)
  ├─ diff(stored, desired)                → ordered IEvent list
  ├─ publish OnDraftValidating  [Sequential, awaited gate]
  ├─ upsert validation sibling (outcome)
  └─ SaveChanges  [transaction]
release lock
publish per-diff events        [Background]   ← cause
publish OnDraftValidated       [Background]   ← effect (after cause)
```

Last-writer-wins: no version check. A desired state built on a stale read overwrites concurrent edits, and the diff emits the resulting events — by design (FR-022).

---

## Where the code lives

| Piece | Location |
|---|---|
| `IUpdateDraftCommand` + `UpdateDraftRequest` | `src/Elsa.Workflows.Design.Persistence.Core/Contracts/` |
| `UpdateDraftCommand` impl + internal `DraftStateDiffer` | `src/Elsa.Workflows.Design.Persistence.EFCore/Commands/` |
| The 20 mutation event types (unchanged) | `src/Elsa.Workflows.Design.Core/Events/` |
| Validation pair + `ExecuteValidations` handler (unchanged) | `src/Elsa.Workflows.Design.Validations.Core/Events/`, `…Validations/Handlers/` |
| Event catalog (publication-site prose edited) | `src/Elsa.Workflows.Design.Core/EVENTS.md` |
| DI registration | `src/Elsa.Workflows.Design.Persistence.EFCore/EFCoreWorkflowsPersistenceFeatureBase.cs` |

---

## Verifying the change

- **Parity test** (`tests/Elsa.Workflows.Design.Tests/Unit/CatalogParityTests.cs`) must stay green — event types are unchanged.
- **Migrated command tests**: each former `*CommandTests` whose objective was "operation X yields event E and state S" is **moved** to drive `IUpdateDraftCommand` with a desired state expressing change X, asserting E + S (FR-013, SC-010). Coverage is preserved one-for-one — every diff dimension keeps a test so each event is validated to publish correctly. No tests are deleted.
- **New diff-engine tests** (net-new behaviours no single granular command exercised — FR-013 a–g): no-op (desired == stored → zero mutation events, validation still runs); multi-dimension diff in one `Execute` → exact event set in deterministic order; last-writer-wins overwrite (SC-014); rename = single UPDATE vs id-change = REMOVE+ADD (SC-015); connection change = REMOVE+ADD (no update event); activity removal cascades to connection prune; cause-before-effect ordering (per-diff events precede `OnDraftValidated`).

---

## Gotchas

- **Connections have no id and no update event** — a changed connection diffs as remove(old tuple)+add(new tuple). Expected, not a bug.
- **Layout is separate from State** — pass it in `UpdateDraftRequest.Layout`, never expect it inside `WorkflowDefinitionState`.
- **`DraftMutationPipeline` still exists** after Unit 2 — only its *mutation* path is absorbed; the *creation* path lingers for create/clone until the [lifecycle follow-up](../../../elsa-foundation-project-management/epic1-elsa-refactor-constitution/follow-up-items/2026-06-03_followup_lifecycle_command_shells.md) retires it.
