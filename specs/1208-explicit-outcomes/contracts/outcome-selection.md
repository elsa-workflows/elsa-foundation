# Contract: Outcome selection, ports and diagnostics

## Script outcome API

The form is open (spec Q1, reconciliation row 14). Whatever form is chosen, it MUST:
- select one name, several names, or an explicitly empty selection;
- leave the selection unspecified when the script selects nothing;
- never read a selection from the content of ordinary returned data;
- take effect only on successful completion.

Per the #3389 draft at `18cccb00b`: `setOutcome(name)` and `setOutcomes(names)`; each call replaces the pending
selection, and `setOutcomes([])` is explicitly empty. If row 14 adds an explicitly recognized returned completion
value, this section adds its shape and the precedence rule.

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
| `VF-ACT-006` | A selected name is outside the pinned set and no valid fallback applies; or several names on a node that does not allow several; or an undeclared result projection | run time, fault code |

Removed: `ELS3-BEHAVIOUR-SCRIPT-OUTCOME-ROUTING`, and the `VF-IMP-005` refusals of `setOutcome` / `setOutcomes`.
Narrowed: `ELS3-ACTIVITY-UNSUPPORTED-MODE` no longer covers Switch `MatchAny` or Complete with several literal
outcomes.
