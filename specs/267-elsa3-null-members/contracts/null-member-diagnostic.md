# Contract: `ELS3-ACTIVITY-NULL-UNCONVERTIBLE`

An error diagnostic of the Elsa 3 conversion (`Elsa3ActivityDiagnosticCodes.NullUnconvertible`). The node is not
converted. The code is stable: never renamed or reused (spec 235 skips and reports items by code).

| Field | Value |
|---|---|
| Severity | Error |
| Code | `ELS3-ACTIVITY-NULL-UNCONVERTIBLE` |
| Path | the node's JSON path plus the member, e.g. `$.root.activities[2].step` |
| Message | `Member '<member>' is null. <reason>` where the reason states what Elsa 3 did, e.g. "Elsa 3 ran the loop with a step of 0, so it never advanced." |
| Guidance | `Set a value for '<member>' in the Elsa 3 workflow, or remove the activity.` For SetVariable / SetOutput values, the reason names #3280 |

Raised for the spec's "refuse" rows only. A converted null member (no binding, typed null, Elsa 3's value) reports
nothing.
