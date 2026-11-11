# Data model: Explicit outcomes

## Outcome selection

| Field | Rule |
|---|---|
| `State` | `Unspecified`, `Named` or `Empty`. |
| `Names` | Only for `Named`: one or more, non-blank, distinct (ordinal), in the order selected. |

Resolution to the names a parent routes by:

| State | Names routed |
|---|---|
| `Unspecified` | `[Done]` |
| `Named` | `Names` |
| `Empty` | `[]` |

### Lifecycle within one attempt

```text
attempt starts ──► Unspecified
  select(names) ──► Named(names) or Empty      (the script outcome API; the form follows Q1)
  completes      ──► the selection is validated (set, flag, fallback) and committed with the completion
  faults/cancels ──► the selection is discarded; nothing is dispatched
  suspends       ──► the selection is discarded; the resuming attempt starts Unspecified
```

## Outcome set (per node, pinned)

| Field | Rule |
|---|---|
| `Outcomes` | Declared names: static outcomes plus provider outcomes over the node's configuration. Contains `Done` exactly when a completion may choose no branch (Q2, model A); `Done` is never selected with a branch. |
| `AllowsMultiple` | `true` when a completion may name more than one outcome (Q10). RunJavaScript: always; Switch: in `MatchAny` mode. |
| `Fallback` | None at node level (Q6). An activity-declared catch-all is an ordinary member of `Outcomes` that the activity or script selects deliberately. |

## Completion record (`ActivityCompletion`)

| Version | Outcome field |
|---|---|
| current | `OutcomeKey: string` |
| new | `Selection: { state, names }` |

A record of the current version reads as `Named([OutcomeKey])` (Q11); no unrecorded empty or unspecified history is
invented, and the backward read has a fixed legacy-record fixture. The `runtime.completionOutcomeNames` metadata
keeps the resolved names, so an `Empty` selection is stored as `[]` with the state on the completion record.
