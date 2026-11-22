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

## #3281 follow-up diagnostics

Delivered bare-null codes retain their meaning. Deliberate literal-null refusals may use the existing null family;
missing/blank/empty-list/invalid numeric refusals use the appropriate existing contract/no-counterpart family and
truthful shape-specific wording. Do not call a missing/empty member null.

Every refusal names the original member path (canonical source name when absent), the legacy/target mismatch and an
edit in Elsa 3 terms. Refused nodes get no duplicate required-input refusal. Refused selections write no imported
definition/version/publication and make no HTTP call.

Required reasons include legacy no-body versus target body for missing ContentType; Done-on-every-response versus
non-2xx Failed for cleared status lists; unsupported nonpositive Timer recurrence; and raw HTTP 0/negative versus
target 200. Guidance asks for explicit compatible members, including a nonempty status list with reviewed
Done/status paths. Blank Script/Cron guidance names the converted authoring constraint without inventing a missing
runtime capability. Faithful conversions add no warning. No null policy/default/suppression is added.
