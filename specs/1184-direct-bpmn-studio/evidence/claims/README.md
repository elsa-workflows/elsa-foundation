# Exact human claims — backend (T028–T031)

Claim-required human work is an explicit, fenced profile. Unclaimed material, state and completion requests are unchanged.

| Layer | Change | Compatibility |
|---|---|---|
| Material | `BpmnModelDefinition` format 6 with a non-empty `ClaimBindings` subset of its human bindings, optional duration timers, and no native service or event work. Profile `foundation-direct-claims-v1`. | Formats 1–5 keep their JSON; `claimBindings` is omitted for them. An older host refuses format 6 at its definition constructor and refuses the profile. |
| State | Private document frame 15 adds a retained `Claims` inventory beside timed human work. It has no native work, parent, cancellation or failure inventory. | Frames 1–14 are byte-identical. Older drivers reject frame 15 unread. A claim on a frame without the inventory, or a frame 15 without it, is refused. |
| Inputs | `claim-human` (invocation, attempt, server-captured claimant) and `complete-claimed-human` (invocation, attempt, claimId, claimant). The legacy `complete-human` on claim-required work receives a retained `claim-required` rejection. | Unclaimed bindings, including unclaimed work inside format 6, keep the exact legacy completion payload and fingerprint. Authority is never rewritten; the claimant is not Authority. |
| Ownership | The existing mailbox and revision CAS commit one owner. No lease, table, team or reassignment. Interruption (timer, cancellation) removes the work; its claim stays as obsolete history and cannot complete or resurrect it. | No new writer outside the model checkpoint and its input receipt. |
| API | `POST bpmn/executions/{id}/work/{invocation}/claim` and `.../complete-claimed`, both reauthorizing `bpmn:complete-human` on the exact execution. Advertised separately as `elsa.api.bpmn-claims` v1. Work listings and inspection add `claimRequired` and the current `claim` only when present. | `elsa.api.bpmn-runtime` and its relations are unchanged. Accepted claim work stays operable with new admission off. |
| Authoring | An owned human binding with `claimRequired: true` publishes format 6 with the exact claim inventory; `false` or absent keeps format 1. | The strict binding shape still refuses non-boolean values and unknown members. |

Rejection codes: `already-claimed`, `claim-required`, `not-claimed`, `claim-not-current`, `foreign-claim`, `claim-not-required`, plus the existing `obsolete-human-work`, `not-human-work` and `execution-terminal`.

## Focused results

- `BpmnDirectExecutionTests.HumanClaims.cs`, 13 cases: format/profile validation; first claim wins and only its exact claimant completes; stale claim, stale attempt and foreign claimant refused without progression; exact replay is a no-op and changed content under the same request id conflicts; restart plus admission-off keeps the owner; timer and cancellation interruption in both arrival orders; unclaimed work inside claim material; old frames refuse claim inputs; the runtime captures the authenticated claimant; a concurrent two-caller race yields exactly one owner (three repeated runs).
- The initial claim tests failed before the protocol registration listed the two new input kinds (the selector refused them); they pass after it. This is the failing control for the new dispatch path.
- Whole BPMN suite 575/575, Interchange 196/196 (claim authoring selects format 6 or keeps format 1), claims capability and route registration, Architecture 609/609, full `Elsa.Server.slnx` Release build.
- The T027 actual pre-claim API and Studio P1 captures pass unchanged on the new writer: both wait stores complete and both terminal stores replay with their original commands.

Not yet proved: Studio claim UI (T032), actual claimed-state restart and old-binary reader refusal (T033), actual interruption through Studio (T034), ApiCompat/version magnitude (T036) and independent review.
