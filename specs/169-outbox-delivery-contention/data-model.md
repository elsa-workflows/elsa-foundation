# Phase 1 Data Model: Outbox Delivery Contention

**Feature**: 169-outbox-delivery-contention · **Date**: 2026-09-17

**No database schema change.** No column is added, removed, or retyped; no migration is generated. The fix reads state that is already persisted (`Status`, `DeliveryFencingToken`) and changes only in-memory contract shapes.

---

## Entities

### RuntimePostCommitOutboxItem *(existing — unchanged)*

The durable delivery record for one post-commit intent. Fields relevant to this feature:

| Field | Role in this feature |
|---|---|
| `OutboxItemId` | Identity presented on every recording |
| `Status` | `Pending` / `Delivering` / `Delivered` / `FailedRetryable` / `FailedFinal` |
| `DeliveringOwnerId` | Set while claimed; `null` otherwise |
| `DeliveryFencingToken` | **Monotonic.** Incremented on each claim, **never reset** — the source of the deterministic trigger |
| `DeliveryVisibleAfter` | Claim expiry; governs reclaim |
| `DeliveryAttemptCount` | Not incremented by a superseded recording (FR-003) |

**Invariant preserved**: the fencing token only ever increases. Stale-claim rejection depends on it, so the fix must not reset it — it excludes fenced rows from the claim-less candidate set instead (FR-010).

---

### RuntimePostCommitOutboxQuery *(modified)*

| Field | Change |
|---|---|
| `Now`, `Limit`, `WorkflowExecutionId`, `IntentKind` | Unchanged |
| `OwnerId` | **REMOVED** (FR-011) |

`OwnerId` has no production caller and every store rejects it with an identical `NotSupportedException`. It is removed together with both refusals. The blank-string validation guard for it is removed with it.

---

### RuntimePostCommitOutboxClaimCompletionOutcome *(extended)*

| Value | Status | Meaning |
|---|---|---|
| `Persisted` | existing | The delivery result was persisted as presented. |
| `DeliveredOnChildEvidence` | existing | A final child-start failure found durable evidence the child exists; the start was persisted as delivered and the projection discarded. |
| `SupersededByOtherOwner` | **NEW** | Another deliverer owns this item. Nothing was written. The owning deliverer's completion governs. |

Extending this enum rather than inventing a parallel result type keeps the subsystem on one vocabulary — see [research.md R6](./research.md#r6--precedent-for-returning-an-outcome-from-this-contract-family).

---

## Contract state transitions

### Claim-less recording — before

```
read deliverable  →  dispatch  →  record
                                    │
                                    ├── row Pending, fence == 0  →  write terminal state
                                    └── row Delivering OR fence > 0  →  THROW  →  drain fails  →  HTTP 500
```

### Claim-less recording — after

```
read deliverable (now also excludes fence > 0)  →  dispatch  →  record
                                                                  │
                                                                  ├── row Pending, fence == 0  →  write terminal state  →  Persisted
                                                                  └── row Delivering OR fence > 0  →  write NOTHING  →  SupersededByOtherOwner
```

**Two independent changes, deliberately both applied.** The read-side exclusion (FR-010) eliminates the *deterministic* fence trigger. The write-side outcome (FR-001) handles the *race*, which no read-side change can close because the competing claim lands between the read and the write. Neither alone is sufficient; see [research.md R3](./research.md#r3--the-issues-top-ranked-candidate-is-already-implemented).

---

## Validation rules

| Rule | Requirement |
|---|---|
| A superseded recording writes nothing — not status, owner, fence, attempt count, failure message, or availability | FR-003 |
| A superseded recording leaves the item recoverable by claim expiry and the sweep | FR-006 |
| A superseded item is counted as neither delivered nor failed by the process result | FR-004, FR-005 |
| A terminal item still raises on recording — supersession does not mask double-completion | existing behaviour, preserved |
| A missing item still raises not-found | existing behaviour, preserved |

**The terminal case is deliberately left throwing.** An already-terminal item means a genuine double-completion bug, not contention, and must stay loud.

---

## Candidate selection

| Selection | Predicate | Change |
|---|---|---|
| `Deliverable` | has a deliverable time **and** carries no fencing token | **fence condition added** (FR-010) |
| `Claimable` | eligible and claimable at `now` | unchanged — the claim path *must* still see fenced rows, or expired claims could never be reclaimed |

The two selections already exist separately, which is what makes this change safe: tightening `Deliverable` cannot affect reclaim.
