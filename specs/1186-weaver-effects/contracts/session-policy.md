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

## Selected candidate (T007, not frozen)

Causal input: the `39a1affef` baseline on both actual adapters had 4 Passed controls and 4
`autonomy.mode_mismatch` failures. Selecting a mode on an established conversation sent zero backend
requests, and session detail exposed no effective policy. The gap is a missing acknowledged update
path, not a rejected update, so the candidate adds the smallest owned path. It does not rework admission.

**State owner.** The default `IAgentSessionService` row owns the session's current policy, a policy
revision and its outstanding operation claims, under the same per-session monitor that already
orders message insertion. No second registry, coordinator or engine is added.

**Claims, not a status scan.** Each operation that reads policy for work acquires a claim on that
row. The claim returns the policy snapshot and revision governing that work. Policy is read *after*
acquisition, never from an earlier session lookup. The claims are internal, server-only tokens: they
are never accepted from or emitted to HTTP, SSE or module payloads.

| Claim | Acquired | Released |
| --- | --- | --- |
| Prompt | Atomically with insertion of a Pending user message; stamps that message's accepted policy and revision | When that exact message becomes terminal, including a never-started Pending message terminalized directly |
| Foreground | By the orchestrator before it reads policy for a turn or continuation | After actual registry Unregister completes. If Register throws and a write cannot be ruled out, it stays unresolved; the owner never infers that nothing was written |
| Preparation | By the invoker before module preparation and authority awaits, also for direct callers with no turn | After a proposal claim covers the work, or on refusal |
| Proposal | Before store insertion, including direct `AddAsync` callers | When the proposal settles as Executed with a validated receipt, Denied, pre-dispatch Expired or refused. AwaitingClient and Uncertain keep it |
| Pause | Before the orchestrator releases foreground for a verified paused turn | After the continuation's foreground claim is held and the pause is removed |

Claims may overlap during handoff, so there is no idle gap. Releasing an old token never releases a
successor's claim. The call-key → per-proposal semaphore → short session-transition order is kept.
No monitor is held across I/O, callbacks or semaphore waits.

**No reservation before collection.** The only field an update may change is `AutonomyMode`.
Availability, capability, context visibility and sensitivity checks do not read it. A prompt is
therefore attributed to the policy current at its atomic insertion. "Policy first" means the new
prompt carries the new revision; "prompt first" means the update loses as busy. A future update of
collection-relevant fields would need a reservation and is outside this leaf.

**Conditional update.** `ChangeAutonomy(session, requestedMode, expectedRevision)` runs under the
row monitor in this order:

1. Refuse undefined modes.
2. Refuse a closed or expired session.
3. Report a stale `expectedRevision` as a conflict, before any no-op comparison.
4. Report busy if any claim is outstanding.
5. Clamp to the current `MaxAutonomyMode`.
6. Replace only `AutonomyMode`; every other policy field survives.
7. Increment the revision only on a real change. An unchanged idle request acknowledges at the current revision.

A lost response is resolved by readback plus a conditional retry. Reading an unchanged old revision
does not acknowledge an in-flight request.

**Readback.** Create and detail responses carry effective autonomy, ceiling, allowed modes and
revision. Accepted messages carry their accepted revision, so later selector values never relabel
earlier results.

**Participation and replacements.** The update path is supported only when the resolved session
service, orchestrator, invoker and proposal service all participate with the same owner instance;
the default registrations do. Otherwise the update returns an explicit unsupported outcome and
creates no default shadow state. Policy cannot change without the owner, so admission and turns keep
today's behavior under replacements. Registry and pause store replacements need no new members,
because the orchestrator holds the claims around their calls. Supported, unsupported and forwarding
replacements are qualified by T019.

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
