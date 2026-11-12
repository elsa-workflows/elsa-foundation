# Witness data model

Existing public contribution envelopes remain canonical in [spec1018](../1018-weaver-module-contributions/contracts/contributions.md). These are fixture-domain meanings, not common schemas.

| Entity | Fields and invariant |
|---|---|
| Personal layout key | Current authenticated subject/tenant, fixed trusted Studio host ID, namespace `home-layout`. Never model-selected; reserved default subject refused. |
| Widget resource | Module ID, own stable widget ID, document revision, visible boolean. Exactly one matching entry in a valid schema-1 personal document. |
| Context snapshot | Resource target, visibility/revision and bounded module guidance; no unrelated widget values/options/secret canaries. Client claims are references; backend reads canonical state. |
| Visibility intent | Exact reviewed widget/revision and desired boolean. Reject foreign scope/widget, malformed/extra arguments, missing widget and no-op. |
| Review payload | Own typed versioned preview with before/desired visibility, widget/host display and reviewed revision; requires approval. |
| Outcome | Own typed read/change payload, real after revision and visibility. Effect receipt correlates invocation and exact before/after resource, ServerPersisted scope. |
| Distribution record | Committed producer source, SDK tgz and NuGet identities/hashes/dependencies, external author source/lock, common baseline hashes, consumer graphs and serving generations. |
| Case record | Input identities, exact observations and before/after owner state; status remains failed for intended negative child, separate expectedFailureObserved with exact reason. |

## Transitions

Missing personal document/widget → explicit unavailable/refused, no fallback write.

Present → authoritative read → validated preparation/review → current authority and revision recheck → CAS → independent reread → observed applied. Read/preparation have no effects. Denial/stale/CAS conflict preserves the current owner's document. Confirmation failure after possible commit → uncertain, no automatic replay. Identical accepted execute returns the retained result under the candidate's stated retention boundary.

Module baseline absent → installed/admitted → removed/unavailable → compatible restored. A valid incompatible Studio package is installed but browser registration is rejected. Package presence, serving membership and contribution availability are separate observations. Consumed shell intent cannot become fresh intent after remount.

Workflow client operations keep their existing exact binding/revision, native validation, whole-change undo and explicit persistence states; a widget's persisted receipt never proves a workflow save.
