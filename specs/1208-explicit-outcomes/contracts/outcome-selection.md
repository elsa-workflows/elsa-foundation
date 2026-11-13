# Contract: Outcome selection, ports and diagnostics

## Script outcome API

A script selects by returning the value of the built-in pure constructor `complete(result, outcomes)` (spec FR-007
to FR-007b, Q1). There are no setters.

| Script returns | Result | Selection |
|---|---|---|
| `order` (ordinary data) | `order` | unspecified (`Done`) |
| `complete(order)` | `order` | unspecified (`Done`) |
| `complete(order, 'Approved')` | `order` | `Approved` |
| `complete(order, ['Approved', 'Notify'])` | `order` | `Approved`, `Notify` |
| `complete(order, [])` | `order` | explicitly empty |
| `{ result: order, outcomes: ['Approved'] }` | that object | unspecified: a look-alike is data |

- Calling `complete` changes nothing; only returning its value selects. The selection takes effect only on successful
  completion.
- The value is recognized by a brand only the evaluator holds, and only as the script's own returned value. Nested in
  returned data it faults with `VF-ACT-006`.
- `outcomes` is a string or an array of strings; anything else faults with `VF-ACT-006`. Repeats count once.
- `complete` exists only in script activities, not in input-binding expressions, and is a reserved built-in name.

## Ports

| Activity | Outcome set | Several per completion |
|---|---|---|
| RunJavaScript | `Done` + possible outcomes (no `Unmatched`) | yes |
| Switch, `MatchFirst` | case labels + `Default` | no |
| Switch, `MatchAny` | case labels + `Default` | yes |
| Graph `Complete`, Finish | the literal outcomes authored | when the graph declares it |
| SendHttpRequest, no expected codes | unchanged (`Done`, `Failed`, `Timeout`) | no |
| SendHttpRequest, expected codes | codes, unmatched status code, `Failed`, `Timeout`; **no `Done`** (Q8) | no |
| CreateUserTask | unchanged (actions, `Unmatched`) | no |

## Diagnostics

| Code | When | Where |
|---|---|---|
| `VF-AUTH-010` | A literal outcome name in a script is not in the node's outcome set | publication |
| `VF-AUTH-011` | A Flowchart connection's source port is not in its source node's pinned outcome set | publication |
| `VF-AUTH-012` | Warning: a RunJavaScript script returns a string literal equal to an outcome name connected on that node; return values are data, routing needs the returned completion form (FR-008a) | publication |
| `VF-AUTH-013` | A node whose pinned set has no `Done` and whose activity cannot guarantee a selection on every successful completion (FR-002b) | publication |
| `VF-ACT-006` | A selected name is outside the pinned set (no node-level fallback); or several names on a node that does not allow several; or an unspecified selection without `Done`; or an undeclared result projection | run time, fault code |
| `ELS3-SCRIPT-OUTCOME-CATCHALL` | Info: the converter declared a catch-all outcome with no connection for computed `setOutcome` names outside the known set (FR-027) | conversion |

Removed: `ELS3-BEHAVIOUR-SCRIPT-OUTCOME-ROUTING`, and the `VF-IMP-005` refusals of `setOutcome` / `setOutcomes`.
Narrowed: `ELS3-ACTIVITY-UNSUPPORTED-MODE` no longer covers Switch `MatchAny` or Complete with several literal
outcomes.
