# Contract: `IUpdateDraftCommand`

**Feature**: `003-single-update-command` (Unit 2) · **Date**: 2026-06-03
**Layer**: `Elsa.Workflows.Design.Persistence.Core/Contracts/` (the provider-agnostic persistence command surface; sibling to the 4 retained lifecycle command contracts).
**Resolves**: FR-021 naming (R7).

> **Supersession note (2026-07-05):** the command contract stands as the canonical Draft-mutation surface, but its **per-diff event emission** and **`WorkflowDefinitionDraftValidation` sibling write** are retired — per-diff publication is dropped (no subscribers; `DraftStateDiffer` remains the tested contract but is unregistered from DI) and the validation entity is deleted (errors are derived state; spec 002 FR-021). The command still takes the lock, applies desired state, runs `DraftValidating`, persists State, and Background-publishes `DraftValidated`. Reinstatable when an event-sourcing consumer exists.

This is the **only** new public contract Unit 2 introduces. It replaces the 20 deleted granular mutation command contracts as the canonical Draft-mutation surface. The internal `DraftStateDiffer` and the per-dimension apply/emit logic are **not** contracts (G2/G25 — no public indirection).

---

## Command contract

```csharp
namespace Elsa.Workflows.Design.Persistence.Core.Contracts;

/// <summary>
/// The single coarse Draft-mutation command. Replaces the 20 granular per-action
/// mutation commands. Receives the complete desired Draft state, diffs it against
/// the stored state under the per-Draft lock, persists wholesale, and publishes one
/// event per detected difference. Lifecycle commands (create/clone/discard/promote)
/// are separate and out of scope.
/// </summary>
public interface IUpdateDraftCommand
{
    Task Execute(UpdateDraftRequest request, CancellationToken cancellationToken = default);
}
```

**Command/Query split (G18)**: `Execute` mutates and returns no queryable view (`Task`, not `Task<T>`). Pure command. Callers re-read via the existing query surface if they need the post-update projection.

---

## Request DTO

```csharp
namespace Elsa.Workflows.Design.Persistence.Core.Contracts; // or a Models namespace within the same Core

/// <summary>The complete desired state of a Draft. Full-state-always; no patch mode.</summary>
public sealed record UpdateDraftRequest(
    string DraftId,
    WorkflowDefinitionState State,
    IReadOnlyCollection<DesignMetadataRecord> Layout);
```

- `WorkflowDefinitionState` and `DesignMetadataRecord` are **existing** types reused unchanged (see [data-model.md](../data-model.md) §3).
- `Layout` is carried beside `State`, never inside it (§E2.9.2).

---

## Behavioural contract (the absorbed pipeline ordering)

`Execute` performs, in order (data-model.md §5):

1. Acquire `workflow-draft:{DraftId}` distributed lock.
2. Load + hydrate stored Draft (State + layout) → `stored` snapshot.
3. Assign desired wholesale: `draft.State = request.State`, `layout.Records = request.Layout`; mark Modified.
4. Diff `stored` vs desired → ordered event list (existing 20 event types).
5. **Sequential** publish `DraftValidating` (sync, awaited gate) against post-apply state.
6. Upsert validation sibling wholesale.
7. SaveChanges (transactional).
8. Release lock.
9. **Background** publish the per-diff events, then `DraftValidated` (cause-before-effect).

**Guarantees**:
- **Atomicity**: state + validation outcome commit in one transaction under the lock.
- **Event substrate**: events publish on the Unit 1 unified `IEventPublisher` — Sequential strategy for the validation gate, Background for the per-diff + outcome events.
- **Last-writer-wins** (FR-022): no concurrency token; a stale-derived desired state overwrites concurrent edits, and the diff legitimately emits the resulting REMOVE/UPDATE events.
- **No-op tolerance**: desired == stored → empty mutation-event list; the validation pair still runs.

**Error modes**:
- Unknown/empty `DraftId` → load fails → not-found surfaced; no diff, no events, no commit.
- Lock acquisition failure → surfaced to caller; no partial write.
- A `DraftValidating` handler that throws breaks the caller (Sequential gate semantics, by design).

---

## Events emitted (producer re-homing, not redefinition)

The 20 mutation event types in `Elsa.Workflows.Design.Core/Events/` keep their names, payloads, and identity. Only their **publication site** changes from the deleted command to `IUpdateDraftCommand` — a documentation edit in `EVENTS.md` (R9). The catalog-parity test (`CatalogParityTests`) is unaffected because event *types* are unchanged.

Event-per-difference mapping: see [research.md](../research.md) R3.

---

## What this contract does NOT cover

- Lifecycle operations (create/clone/discard/promote) — separate contracts, out of scope (FR-003).
- Any query/projection of Draft state — existing query surface.
- The internal diff engine shape — implementation detail, not contracted.
