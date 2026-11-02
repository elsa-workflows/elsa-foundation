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

## Studio (T032) — component proof

- **Client:** `elsa.api.bpmn-claims` v1 is a separate capability. `claim-human` and `complete-claimed-human` are retained, replayable operations like completion and retry. Their bodies carry only the request id, attempt and claim id, never a claimant.
- **Run page:** claim-required work shows "Unclaimed", "Claimed by you" or "Claimed by <subject>". Unclaimed work offers only Claim work. Complete work is enabled only for the server-reported owner, and the request carries that exact claim id. Unclaimed work in either human profile keeps the original completion. A host without the claims capability gets a disabled Claim with an explanation.
- **Authoring:** an owned human binding accepts boolean `claimRequired`; a string or unknown member stays unsupported and unchanged. The inspector offers "Require a claim before completion" only when the backend advertises `bpmn-authored-claim-publish`, a new relation on `elsa.api.bpmn-publishing`. A retained `true` on a backend without it is shown disabled with an explanation.
- **Results:** the three affected files pass 89/89, including a lost claim response that is resumed with its original request after a reload. Workflow typecheck passes, and the repo's ESLint passes on all changed files. Interchange passes 196/196 with the new publishing relation.
- **Full workflows suite:** at load averages 242–369 on 15 cores it showed 4 failures in unrelated files (activity browser, editor run canvas, library browse). Each passed when rerun on its own. This is not a low-load whole-suite pass; that run is repeated before final qualification.

This is component proof only. Actual browser claiming, two-subject contention, restart and old-binary refusal are T033–T034.

## Actual host claim restart and old-reader fence (T033)

**Owned production Workbench** (`BpmnHumanClaimUpgradeTests`). Products are at `74b82f11e`; the test, fixture and witness were committed afterwards and change no product. [Receipt](claims-host.json).

1. Raw format 6 publication returns `foundation-direct-claims-v1`. The work listing shows `claimRequired` and no claim.
2. Legacy `complete` is refused as `claim-required`, without a revision.
3. The seeded administrator claims the work (revision 2).
4. The process is killed, and the stopped Runtime and Design stores are captured. The host restarts with a second seeded administrator, a distinct subject.
5. The second subject sees the same view and owner. Its claim is refused as `already-claimed` and its claimed completion as `foreign-claim`, without a revision. Reusing the owner's request id with its different subject conflicts.
6. The owner completes the work (revision 3, Completed). After another real restart, the same request replays the same result with `replay: true` and an unchanged view.
7. Native workflow/activity/bookmark rows stay zero.

**Actual pre-claim binary** (`tools/bpmn/verify_claim_reader_fence.py`). The predecessor Workbench was built from `fea85ce64`, the head immediately before the claim writer. It ran under its own configuration against the captured claimed stores. [Receipt](claim-reader-fence.json).

| Request | Status | Reason |
|---|---|---|
| Execution header | 200 | Header projection only; it discloses no work or claim |
| Work listing | 422 | "The BPMN state and exact work inventory differ" (frame 15 refused) |
| Inspection | 422 | Invalid inspection material (format 6 refused) |
| Legacy completion | 422 | "The BPMN material profile … is unsupported" |
| Claim | 404 | Route absent |

The model execution and input rows are byte-identical before and after.

The physical stores are retained here: [claimed runtime](claimed-runtime.sqlite.gz), [claimed design](claimed-design.sqlite.gz) and [completed runtime](completed-runtime.sqlite.gz). Their hashes are in the receipts.

Limits: two administrators in one tenant prove distinct-subject ownership, not role or team policy (which is deferred). Actual Studio browser claiming and interruption are T034.
