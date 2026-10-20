# Data model: nested, repeated and confirmed compensation (M3)

Extends [spec 251 data-model.md](../251-compensation-uncertainty/data-model.md). The authored step structure is unchanged.

## Scope input

| Input | Type | Default | Rule |
|---|---|---|---|
| `confirmOnComplete` | boolean | `false` | When `true`, completing `Done` confirms the scope's entries instead of handing them over (FR-309). Pinned at publication. |

## New activities

| Activity | Inputs | Outcomes | Notes |
|---|---|---|---|
| `Confirm` | `scope` (string, optional: blank = nearest enclosing scope, else an authored scope node id) | `Done` | Structural leaf: its first evaluation sends `compensation.confirm` (R-1) and completes. |
| `Compensate` | `scope` (string, optional, as above), `reason` (string, optional) | `Done` | Structural leaf: sends `compensation.compensate` (R-1) and completes. The target scope stops the running step, which usually cancels the Compensate activity's own ancestors; its completion is then acked as late. |

## Messages (ancestor notifications, R-1)

| Code | From | Payload |
|---|---|---|
| `compensation.register` | a scope completing `Done` (R-2) | `{ originScope, batch, batchCount, entries: [HandedOverEntry] }` (≤ 16 entries per message) |
| `compensation.confirm` | `Confirm` | `{ confirmingActivityExecutionId }` |
| `compensation.compensate` | `Compensate` | `{ requestingActivityExecutionId, reason?, target? }` |

`HandedOverEntry`: `{ originEntryId, step, operationKey, forwardActivityExecutionId, forwardIterationId?, forwardOutcome, status, handlerExecutableNodeId?, origin? }`. `origin` is present when the entry was itself handed over (transitive hand-over keeps the first origin).

## Runtime metadata keys (new)

| Key | Set by | Read by |
|---|---|---|
| `compensation.originScopeActivityExecutionId`, `compensation.originEntryId` | the scope, on a handed-over handler's scheduling metadata **and provenance** (FR-304) | the scope, Studio |
| `compensation.cause` = `explicit` | the scope, on absorbed or cancelled step metadata after Compensate | Studio |

Fault codes: `compensation.target-not-found`, `compensation.confirm-during-recovery`, `compensation.compensate-in-handler`, `compensation.scope.compensate-requested`, `compensation.scope.uncertain-unhandled`.

## Private state — type `Elsa.CompensationScope.State`, version `3`

```json
{
  "phase": "Compensating",
  "nextStepIndex": 2,
  "nextOrdinal": 5,
  "cause": { "kind": "explicit", "step": "Charge card", "activityExecutionId": "ae-9", "requestedBy": "ae-12" },
  "entries": [
    {
      "id": "ae-scope/entry/3",
      "step": "Reserve warehouse B",
      "ordinal": 3,
      "status": "Registered",
      "forwardActivityExecutionId": "ae-5",
      "forwardIterationId": "it-2",
      "operationKey": "ae-inner/step/1",
      "forwardOutcome": "Completed",
      "handlerExecutableNodeId": "release-b",
      "origin": { "scopeActivityExecutionId": "ae-inner", "entryId": "ae-inner/entry/2" },
      "handlerAttempts": []
    }
  ],
  "confirmations": [ { "activityExecutionId": "ae-11", "entryIds": ["ae-scope/entry/1"] } ],
  "receivedHandOvers": [ "ae-inner" ]
}
```

| Field | Change from v2 |
|---|---|
| entry `status` | adds `Confirmed`. |
| entry `handlerExecutableNodeId` | new, optional. Set for handed-over entries, whose step is not in this scope's structure; own entries resolve the handler from the step as before. |
| entry `origin` | new, optional. |
| `cause.kind` | adds `explicit` (Compensate) and `requested` (cascade fault from an inner scope). `requestedBy` is new and optional. |
| `confirmations` | new. Every Confirm (and `confirmOnComplete`) with the entries it confirmed. |
| `receivedHandOvers` | new. Origin scope occurrences whose hand-over was fully journaled; de-duplicates repeats (FR-306). |

## Upcast v2 → v3

Every v2 field is kept. `confirmations` and `receivedHandOvers` become `[]`; entries get no `origin` and no `handlerExecutableNodeId`. No v2 status changes meaning. Version 3 is never downgraded.

## Entry status transitions (additions)

| From | Event | To |
|---|---|---|
| `Registered`, `NoHandler`, `Uncertain` | Confirm, or `confirmOnComplete` on `Done` | `Confirmed` |
| (none) | hand-over received | the handed-over status, with `origin` |
| running step (started) | Compensate stops it | `Uncertain` (with or without handler) |
