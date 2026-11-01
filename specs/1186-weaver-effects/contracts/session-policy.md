# Candidate session policy and lifecycle contract

The package/browser journeys select the smallest contract. This is the behavioral boundary to
implement and qualify, not a frozen API or a new module-specific orchestration path.

## Authenticated transport

- Existing create-session and session-detail readback expose the server's acknowledged effective
  autonomy, current ceiling/allowed modes and policy revision as additive response data.
- An autonomy-only command on the existing session accepts a supported requested mode and expected
  policy revision. Use the existing session permission and authenticated normalized actor/tenant;
  caller actor/tenant metadata cannot select the owner. Malformed or unknown modes are refused.
- Success returns authoritative acknowledged policy. Valid requested modes clamp to current limits.
  All unrelated policy fields survive. No new session/provider conversation/transcript is created.
- A concurrent update or incompatible active ownership returns an explicit correlated conflict/busy
  outcome. A stale acknowledgement cannot relabel a newer update, backend or session.
- A committed change with a lost response is resolved by authoritative readback or idempotent intent
  delivery. No retry may duplicate an effect. A still-unresolved request blocks dependent submission
  and remains visible; elapsed time is not an acknowledgement.
- Accepted messages/turn results identify the policy that admitted them. A later selector value is
  never the authority for attributing an earlier result.

Use the existing selected-backend HTTP transport and stream URL resolution. Preserve ambient-cookie
omission and existing backend/generation isolation. Public wire names and methods are recorded here
after causal selection and before final contract review.

## Session replacement owner

Extend `IAgentSessionService` or its demonstrated owning boundary rather than adding a parallel
admission registry. Unsupported default members may refuse explicitly for legacy replacements;
they must never emulate atomic support with independent checks or a permissive fallback.

The owner must cover:

1. Message collection and insertion, preserving duplicate request/fingerprint/provenance semantics.
2. Foreground acquisition/release, including session lookup before registration and terminal cleanup.
3. Retained pause/review, approved dispatch, unconfirmed application and receipt continuation.
4. Direct invoker consumers: registry/authority/preparation awaits before proposal creation cannot
   allow a policy update to overtake already admitted work.
5. Loss/cancellation/failure cleanup that releases only its own reservation and retains genuine
   accepted receipts or explicitly unresolved work.

No global monitor spans I/O. No owner monitor spans waits for call/proposal semaphores. Preserve their
existing serialization order and use short session transitions/reservations around asynchronous
work. The same operation may hand off ownership without an idle gap; a different operation receives
an explicit losing disposition. Preserve original foreground cancellation tokens and pause storage.

## Audit boundary

An authenticated owner-scoped query has an explicit supported result/refusal and takes actor,
tenant, optional session and bounded limit. Filtering/order precede limit in the owning reader.
Legacy missing ownership is ineligible. The API has no global-read/per-event-lookup fallback.

Expiry records retain session/proposal/invocation/turn/call identities and dispatch meaning. Before
dispatch: Expired, zero effect. After dispatch without confirmation: Uncertain, possible effect.
Duplicate delivery retains the original transition/receipt; success always requires owning evidence.

## Separately packaged module

Acme contributes a new size tool, strict bounded input/preview/result schemas, current personal
authority, native Home CAS/readback, installed guidance/capability, disclosure ownership and result
renderer through existing public seams. Weaver/provider/host code contains no size/Home cases.
The existing visibility operation and workflow client edits keep their review requirements.

Both actual provider loops must demonstrate one established conversation changing mode in both
directions, exact original correlations, real owner outcomes and receipt continuation. Generic native
SSE correlation may recognize an inline automatic receipt; it must not execute again to obtain an
HTTP response for a checker. Missing/truncated/mismatched evidence refuses qualification.

## Mandatory native qualification inventory

The new autonomy/lifecycle evidence section requires each case below exactly once **for each actual
Copilot and Anthropic adapter**, in addition to the unchanged original38 and four current-authority
cases. Its manifest uses the actual selected adapter identity; omissions, duplicates, wrong-cause
outcomes and absent owner/correlation evidence fail the qualifier. The original negative controls
remain their actual single-adapter observations; their presence cannot substitute for this matrix.
The four mode cases form the initial causal baseline. All thirteen cases per adapter are mandatory
in the complete final witness; none is an optional diagnostic.

| Case key | Native observation and owner |
| --- | --- |
| `automatic-size-control` | Established FullAuto conversation: one inline persisted size effect with exact native-SSE/provider/owner receipt. |
| `reviewed-size-control` | Established Manual conversation: owner unchanged until approval; actual effect and receipt return to the originating turn. |
| `automatic-to-manual` | Same conversation acknowledges Manual, then size change requires review; original conversation/history/receipts survive. |
| `manual-to-automatic` | Same conversation acknowledges FullAuto, then eligible size changes once with owner confirmation. |
| `denied-result-continuation` | Deny a prepared size operation; zero effect, correlated denial/refusal delivered into the originating provider turn and truthful completion. |
| `duplicate-effect-delivery` | Repeat execution and receipt/result delivery; original identity/receipt retained, exactly one effect and no second mutation/continuation admission. |
| `cancel-before-effect` | Cancel before effect through supported real lifecycle; no effect or false application, explicit correlated outcome. |
| `interrupted-after-effect` | Interrupt real dispatched/applied work before confirmation; truthful uncertainty/late confirmation, native owner readback and no mutation replay. |
| `stale-target-continuation` | Advance native revision after preparation; refused original operation has no additional effect and actual refusal returns to the originating turn. |
| `authority-refusal-continuation` | Withdraw native mutation authority before application while retaining required read/disclosure authority; exact refusal, zero effect and authorized refusal continuation in the originating turn. Full disclosure withdrawal must still block provider entry, as the separate four current-authority cases require. |
| `visibility-review-control` | FullAuto still requires review for the original visibility operation; native owner stays unchanged until approved application. |
| `workflow-review-control` | FullAuto still requires review for the native client edit; atomic validation/dirty state/whole Undo/explicit Save/reload and Publish boundary remain correct. |
| `busy-policy-lifecycle` | Real retained review/application/receipt-continuation work blocks incompatible policy changes; exact ownership, policy and original receipts stay coherent. |

`autonomy_journeys.py` owns manifest/completeness and actual-adapter orchestration; `browser.mjs`
drives genuine UI/client application; `agent_transport.py`/`case_records.py` own generic HTTP/SSE/
provider correlation; native module/Preferences and workflow editor observations establish effects.
Controlled barriers may coordinate those real paths but cannot replace engine, store or owner logic.
Fine-grained ownership permutations, reader limits and unsupported replacements also have deterministic
behavior tests; do not mislabel those tests as native/provider evidence. Contract freeze requires
successful normal composed Studio and the complete immutable package witness, after this matrix.
