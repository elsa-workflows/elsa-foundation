# Session policy and lifecycle contract

**Status: Frozen for #3440 (T026), preview API.** This records the surface implemented by
T007–T022 and documented by T023. T026 freezes it as the demonstrated minimum after two proofs passed:
- the normal composed Studio gate (T024): the complete browser suite;
- the complete independent package witness (T025): 38 original, 4 current-authority and 26 per-adapter autonomy cases.

Both first passed on `7fc0a3c2e` and passed again on the final reviewed source `904671134`, after the review fixes. Later commits change documentation only.
#3440 records the exact heads, counts and hashes of every run, passing or failing.

The members stay preview (pre-GA) API under ADR 0080 D4, and the source marks them "Preview". A later change needs its own scoped contract decision. It is not a new module-specific orchestration path. The live domain catalog,
[Agent `EXTENSION_POINTS.md`](../../../Extensions/Agent/src/Elsa.Agent/EXTENSION_POINTS.md#session-policy-and-owner-scoped-audit-3440-preview),
owns current extension-point documentation; [Core contracts](../../../Extensions/Agent/src/Core/Contracts/AgentContracts.cs)
and [Core models](../../../Extensions/Agent/src/Core/Models/AgentModels.cs) are the signature authority.

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

### Wire shape (T009/T010, frozen for #3440)

`PUT /_elsa/agent/sessions/{sessionId}/autonomy`, using the existing `Use` permission. Body:
`{ "autonomyMode": "manual" | "auto-read-only" | "full-auto", "expectedRevision": <number> }`.
Mode names are trimmed and case-insensitive. Success returns 200 with `data` set to the session
policy `{ "autonomyMode", "maxAutonomyMode", "allowedAutonomyModes": [...], "revision" }`.
`allowedAutonomyModes` lists every mode up to the ceiling, most restrictive first. An unchanged idle
request returns the current revision.

Failures carry no `data`, even when the owner returned the current state; the client reads back
through session detail. The body is validated before session authorization, so a malformed body
returns 400 for any session.

| Condition | Status | Error code |
| --- | --- | --- |
| Request without a JSON content type, or an unsupported body encoding | 415 | none (empty body) |
| Malformed JSON, missing `expectedRevision`, or a missing/unknown/undefined mode | 400 | `agent.session.autonomy_invalid` |
| Missing session | 404 | `agent.session.not_found` (existing session authorization) |
| Foreign actor or tenant | 403 | `agent.session.forbidden` (existing session authorization) |
| Session gone or no longer owned between authorization and update | 404 | `agent.session.not_found` |
| Stale expected revision | 409 | `agent.session.policy_stale` |
| Outstanding work | 409 | `agent.session.busy` |
| Closed or expired | 409 | `agent.session.closed` |
| No policy service, or a non-participating composition | 409 | `agent.session.autonomy_unsupported` |

Readback and attribution:

- Create-session and session-detail responses gain an additive `policy` object of the same shape.
  It is omitted only when the composition cannot read it back (Unsupported).
- Each session-detail message view gains `acceptedPolicyRevision`. It is always serialized, as
  `null` when the installed store does not own revisions.
- The accepted-prompt response projects the domain `AgentMessage` through `AgentAcceptedMessageResponse`:
  every other message member keeps its name and value, and `message.acceptedPolicy` is the same public
  session-policy shape (`autonomyMode`, `maxAutonomyMode`, `allowedAutonomyModes`, `revision`), or
  `null` when the store does not own revisions. Denied capabilities, admitted context kinds, the
  sensitivity ceiling and the retention label stay on the server-side frozen `AgentMessage.AcceptedPolicy`.
- Prompt admission whose authorized revision moved before insertion returns 409
  `agent.message.policy_changed` and inserts nothing. An identical retry of an already accepted
  delivery (same request ID and fingerprint) returns the original message with its original
  `acceptedPolicy`, even after a policy change, and re-retains only that message's own context.

Client rules:

- Send at most one update per session at a time, with the last acknowledged revision as `expectedRevision`.
- Keep dependent prompt submission blocked while an update is unresolved.
- After a lost response, retry the same conditional request. A `policy_stale` answer means another
  write committed, so read detail back and show that authoritative state, without claiming which
  intent won.
- Ignore responses for another backend, session or client generation.

Use the existing selected-backend HTTP transport and stream URL resolution. Preserve ambient-cookie
omission and existing backend/generation isolation.

### Core signatures (preview, additive)

| Member | Shape |
| --- | --- |
| `IAgentSessionPolicyService` (new, scoped) | `GetAsync(sessionId, actorId, tenantId, ct)` and `ChangeAutonomyAsync(AgentSessionAutonomyChangeRequest, ct)`, both returning `Task<AgentSessionPolicyResult>` |
| `AgentSessionAutonomyChangeRequest` | `(SessionId, ActorId, TenantId, AgentAutonomyMode RequestedMode, long ExpectedRevision)` |
| `AgentSessionPolicyResult` | `(AgentSessionPolicyOutcome Outcome, AgentSessionPolicyState? State)`, `Succeeded` |
| `AgentSessionPolicyOutcome` | `Succeeded`, `StaleRevision`, `Busy`, `InvalidMode`, `SessionClosed`, `NotFound`, `Unsupported`. `StaleRevision` and `Busy` return the current state; the others return none |
| `AgentSessionPolicyState` | `(AgentPolicy Policy, long Revision)`; the default owner starts at revision 1 |
| `AgentSession.PolicyRevision` | `long? { init; }`, null when the store does not own revisions |
| `AgentMessage.AcceptedPolicy` | `AgentSessionPolicyState? { init; }`, stamped at atomic insertion |
| `AgentMessageCreateRequest.ExpectedPolicyRevision` | `long? { init; }`; a moved revision makes the owning store throw `AgentSessionPolicyChangedException` |
| `AgentActionProposal.PolicyRevision` | `long? { init; }`, the approval basis at preparation; `AddAsync` refuses a stamp that is not the current revision. A proposal without a prepared invocation is never claimed or checked, because it can never be dispatched |
| `AgentSessionPolicyChangedException` | `InvalidOperationException` with `Code = "agent.message.policy_changed"`, `SessionId`, `ExpectedRevision`, `CurrentRevision` |
| `IAgentAuditReader.ListOwnedAsync` (new default member) | `ListOwnedAsync(AgentAuditOwnerQuery, ct)` returns `Task<AgentAuditQueryResult>`; the default returns `AgentAuditQueryResult.Unsupported` |
| `IAgentAuditOwnership` (new, singleton) | `AssociateAsync(sessionId, actorId, tenantId, ct)` returns `Task<bool>`; first association wins |
| `AgentAuditOwnerQuery` | `(ActorId, TenantId, string? SessionId, int Take)`, `MaxTake = 1000` |
| `AgentAuditQueryOutcome` / `AgentAuditQueryResult` | `Succeeded`, `Unsupported`; `(Outcome, IReadOnlyList<AgentAuditEvent> Events)`, static `Unsupported`, `Succeeded` |

`IAgentAuditReader.ListAsync` remains a trusted, unscoped in-process read and is not an
authorization boundary. Agent API models add `AgentSessionPolicyResponse`, `AgentSessionAutonomyRequest`,
`AgentCreateSessionResponse.Policy`, `AgentSessionDetailsResponse.Policy` and
`AgentMessageViewModelResponse.AcceptedPolicyRevision`, and `AgentMessageAcceptedResponse.Message`
becomes `AgentAcceptedMessageResponse`, whose `AcceptedPolicy` is an `AgentSessionPolicyResponse?`.

## Session replacement owner

Extend the session service's demonstrated owning boundary rather than adding a parallel admission
registry. The T007 selection below keeps the state in the default session row behind a separate
public policy service. Unsupported compositions refuse explicitly; they never emulate atomic support
with independent checks or a permissive fallback.

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

## Selected contract (T007, implemented by T008, frozen by T026)

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
| Preparation | By the invoker for a Mutating tool, after its static checks and before the call gate, preparation or authority awaits; also for direct callers with no turn | In a `finally` on every exit (refusal, duplicate replay, cancellation, audit failure), after the proposal claim exists when there is one |
| Proposal | Before store insertion, including direct `AddAsync` callers; it records the prepared invocation's deadline. A proposal without a prepared invocation can never be dispatched, so it is never claimed | When the proposal reaches Executed, Denied, Expired, Failed, Cancelled or server-scope Uncertain. AwaitingClient keeps it. Client-scope Uncertain keeps it until the continuation consumes it, while the Pause claim still covers the originating turn. A late acknowledgement does not depend on mode |
| Pause | Before the orchestrator releases foreground for a verified paused turn | After the continuation's foreground claim is held and the pause is removed |

A pre-dispatch proposal claim (AwaitingApproval, or an approved FullAuto proposal that never ran)
whose prepared deadline has passed does not count as busy. That covers orphans left when a turn is
cancelled between proposal insertion and the pause. `ExecuteAsync` marks the claim dispatched under
the row monitor, and refuses if the deadline has passed, before setting Executing. A dispatched
claim never expires into idle.

The default registry's busy refusal is a known pre-write refusal and releases the new Foreground
claim. A Foreground claim left unresolved by an unknown registry failure keeps that session busy for
policy changes. Restart and recovery belong to #3441. That is truthful conservative busy, not a
fabricated idle state.

Claims may overlap during handoff, so there is no idle gap. Releasing an old token never releases a
successor's claim. The call-key → per-proposal semaphore → short session-transition order is kept.
No monitor is held across I/O, callbacks or semaphore waits.

**No reservation before collection.** The only field an update may change is `AutonomyMode`. The
default evaluator and collector do not read it. Replaceable evaluators and collectors still receive
the whole policy, so admission carries the revision read at authorization into insertion. Insertion
refuses with an explicit `policy_changed` conflict if the revision moved meanwhile. "Policy first"
then means a retried prompt carries the new revision; "prompt first" means the update loses as busy.
This protects future policy fields without a reservation.

**Conditional update.** `ChangeAutonomyAsync` runs under the row monitor in this order:

1. Refuse undefined modes.
2. Refuse a missing session or one not owned by the request's actor and tenant (`NotFound`).
3. Refuse a closed or expired session.
4. Report a stale `expectedRevision` as a conflict, before any no-op comparison.
5. Report busy if any claim is outstanding.
6. Clamp to the current `MaxAutonomyMode`.
7. Replace only `AutonomyMode`; every other policy field survives.
8. Increment the revision only on a real change. An unchanged idle request, including one that
   clamps to the current mode, acknowledges at the current revision.

A lost response is resolved by readback plus a conditional retry. Reading an unchanged old revision
does not acknowledge an in-flight request.

**Approval authority.** The claim's snapshot is the approval authority for preparation and turns.
It replaces reads taken before the claim: the orchestrator's early session lookup feeding the turn
request, tool loops and continuation, and the invoker's early lookup. A caller-supplied `policy`
argument can only tighten it. So a proposal's stamped revision always matches its approval basis.

**Readback.** Create and detail responses carry effective autonomy, ceiling, allowed modes and
revision. Accepted messages carry their accepted revision, so later selector values never relabel
earlier results.

**Participation.** `ChangeAutonomyAsync` and readback live on the public scoped
`IAgentSessionPolicyService`. `IAgentSessionService` gains no new members, and claims stay internal.
The [replacement guidance](#replacement-guidance) lists which replacements keep the update supported.
Supported, unsupported and forwarding replacements are qualified by deterministic component tests in an in-process CShells shell built from project references, not packed packages, by
T019 (`AgentCompositionTests`).

## Lifecycle outcomes (T013–T014, T020)

| Situation | Proposal status | Caller outcome | Retained result and audit |
| --- | --- | --- | --- |
| Deny before dispatch | Denied | Success with the denied proposal | Lifecycle envelope `denied`; zero effect |
| Prepared deadline passes before dispatch (AwaitingApproval, Edited or Approved), seen by Approve, Execute or the dispatch mark | Expired, recorded once | 409 `agent.proposal.expired`, repeated unchanged for later Approve and Execute | One `ProposalFailed` record with `dispatch=none`, `effect=none` |
| Cancellation during the current-authority recheck, before module or client dispatch, including turn cancellation before automatic dispatch | Cancelled | 409 `agent.proposal.cancelled`, repeated unchanged | Lifecycle envelope `refused`, zero-effect summary |
| The executor throws, is cancelled after module dispatch, or reports an executed effect without a valid owning-resource receipt | Uncertain | Uncertain result; never replayed | Lifecycle envelope `uncertain` |
| Any other failure the executor returns, including a replacement returning the `agent.proposal.cancelled` code or the current-authority recheck returning `agent.proposal.expired` | Failed | The returned failure | Lifecycle envelope `refused`, zero-effect summary |
| A replacement reports success with no executed effect and an unrecognised status | Failed | The returned value | No lifecycle envelope |
| AwaitingClient reaches its deadline without an admitted receipt | Uncertain, never Expired | Uncertain result; the request is never reissued | `ProposalFailed` with `dispatch=client`, `effect=possible`, `deadline=pending-client` |
| AwaitingClient loses its reviewed contract or authority on a repeated execute | Uncertain | Uncertain result | `ProposalFailed` with `dispatch=client`, `effect=possible` |
| A validated receipt arrives for AwaitingClient or client-scope Uncertain | Executed | Accepted receipt; a duplicate identical acknowledgement returns it, a conflicting one is refused | `ProposalExecuted` |

Every proposal audit record carries `proposalId`, `invocationId`, `status`, `turnId` and
`toolCallId`, with the session on the event. The execute route also returns the retained terminal
data for Expired, Failed, Denied and Cancelled failures.

**Automatic approval is not a review decision.** A proposal prepared without review
(`RequiresApproval == false`) never satisfies a current policy that requires review: execution then
refuses with `agent.proposal.approval_required`, and `ApproveAsync` does not report it as an
idempotent human decision. `AddAsync` refuses a review-required proposal inserted already approved
or past review.

**Lifecycle-outcome disclosure.** The engine's receipt-less lifecycle envelope for a mutating tool
(`agent.operation-outcome` schema `1`, exactly `{ status, invocationId }`, status `denied`,
`refused` or `uncertain`, matching the stamped `Access.InvocationId`) is admitted into the
originating turn through the owning module's current disclosure decision
(`IAgentContextProvider.AdmitDisclosureAsync`), not the tool's mutation authority. Withdrawn write
authority therefore still lets the refusal continue the turn; withdrawn read/disclosure authority
still blocks it with `agent.result.disclosure_denied`. Host capability policy applies to both.
Applied effects, read-tool results and every other shape keep the full tool-authority check.

## Audit boundary (T021–T022)

`GET /_elsa/agent/audit` (existing route and `Audit` permission) now reads only through
`IAgentAuditReader.ListOwnedAsync`, with the authenticated normalized actor and tenant, the optional
`sessionId` and `take` clamped to 1..1000 (default 100). The owning reader filters by ownership and
session and orders newest first (ties: latest append first) before the limit. A record without a
session, or whose session has no ownership association (legacy), is never returned. There is no
global-read or per-event session-lookup fallback.

| Condition | Status | Error code |
| --- | --- | --- |
| Invalid `take` binding | 400 | Existing binding error |
| Explicit `sessionId` missing or foreign | 404 / 403 | `agent.session.not_found` / `agent.session.forbidden` |
| Principal without a resolvable actor or tenant | 403 | `agent.actor.unresolved` |
| Installed reader returns Unsupported | 409 | `agent.audit.query_unsupported` |

The default session service associates each new session with its actor and tenant through
`IAgentAuditOwnership` before the row exists or any record is emitted; a conflicting association
fails creation with `AgentContributionException`. A session with a blank actor or tenant stays
ineligible. The default store orders append, association and query under one monitor, and the
association has no dependency on the session service, so there is no DI cycle.

## Replacement guidance

The update is supported only when the resolved `IAgentSessionService`, `IAgentStreamingService`,
`IAgentToolInvoker` and `IAgentProposalService` are the default participants of one owner
instance. The check is by the resolved instance, so a `services.Replace(...)`, a forwarding
decorator or an additive last-registered service all count as replacements.

| Replacement | Effect |
| --- | --- |
| `IAgentTurnRegistry`, `IAgentTurnStateStore` | Stay supported: the orchestrator holds the claims around their calls, and they need no new members (qualified by T019). Only the default registry's busy refusal is known to precede a write, so a replacement registry's refusal or failure leaves the session conservatively busy for policy changes until restart (#3441) |
| `IAgentSessionService`, `IAgentStreamingService`, `IAgentToolInvoker`, `IAgentProposalService`, including forwarding decorators | Readback and update return Unsupported: 409 `agent.session.autonomy_unsupported`, and create/detail omit `policy`. No default shadow state is created; admission and turns keep their existing behavior |
| `IAgentActionProposalExecutor` | Not a participant; the proposal service holds the claims around it. Only the default executor's engine-private pre-dispatch cancellation is recorded as Cancelled. A replacement cannot signal Cancelled, so any failure it returns, including the `agent.proposal.cancelled` code, is recorded as a Failed refusal with zero effect. Not separately qualified by T019 |
| `IAgentSessionPolicyService` | A replacement cannot join the internal default claims. It must own the same ordering against all admitted work, or return Unsupported. Per-turn wrappers are not registrations and are unaffected |
| `IAgentAuditReader` | A reader that does not implement `ListOwnedAsync` inherits the Unsupported default, and the audit route answers 409 `agent.audit.query_unsupported`. An implementation must filter and order before the limit and throw `ArgumentException` for a blank actor or tenant or a limit outside 1..1000. Replace `IAgentAuditOwnership` with the same store so association and query stay coherent |
| `IAgentAuditOwnership` | Must keep first-association-wins. A replacement session service must associate each session before emitting any record for it; otherwise its sessions stay ineligible (empty results, never a fallback) |

## Constitution and SemVer classification

Rechecked against framework v6.1.0 and Elsa v6.3.0.

- **§2.6.2 replacement contracts.** `IAgentSessionPolicyService` and `IAgentAuditOwnership` are
  single-implementation contracts registered with `TryAdd*`. Unsupported compositions refuse
  explicitly rather than falling back. As for every Agent contract except `IAgentToolAuthorizer`
  (`AgentRegistrationValidator`), the kind is declared only in the catalog and a second additive
  registration is not diagnosed at startup. See [Registration decision](#registration-decision).
- **§2.22.1 catalog.** The Agent catalog is updated in this unit for the new contracts, the new
  reader member and the participation rules.
- **§2.23.3 visibility.** The new Core types are public because host and persistence authors
  implement or call them; `DefaultAgentSessionPolicyService`, the owner, claims and participation
  are `internal sealed`. Agent Core and Agent API are not yet enrolled in PublicApiAnalyzers (#2591
  enrolls domains one at a time). The enrolled `Elsa.Agent` surface is unchanged, and the build
  passes without RS0016/RS0017, so no baseline changes.
- **§2.23.5 exception boundaries.** `AgentSessionPolicyChangedException` is a Core domain exception;
  no infrastructure exception crosses the new members.
- **§4.2.** The members above are additive (Minor): new interfaces, one default interface member,
  new models, enums and exception, init-only properties, a new route and additive response fields.
  There is one exception, a changed public signature (Major under §4.1/§4.2):
  `AgentMessageAcceptedResponse.Message` in `Elsa.Agent.Api` changes type from `AgentMessage` to
  `AgentAcceptedMessageResponse`. This keeps private policy fields off the wire. It breaks C#
  consumers that construct the record or read `.Message` as `AgentMessage`; the JSON is unchanged
  except for the projected `acceptedPolicy`. It is accepted pre-GA under ADR 0080 D4 with computed
  versions under Elsa §E5, like the behaviour changes below.
- **Behavior changes** (Major under §4.2's changed-semantics rule; accepted pre-GA under ADR 0080 D4
  with computed versions under Elsa §E5; this work performs no release):
  1. The audit route requires `ListOwnedAsync`: custom readers now get 409, and legacy sessions without
     an ownership association are no longer listed.
  2. Cancellation during the pre-dispatch authority recheck is Cancelled with zero effect, where it
     used to be stored as Uncertain.
  3. Repeated decisions on an expired proposal return `agent.proposal.expired`, where they used to
     return `invalid_state` or `closed`. Pre-dispatch expiry emits one correlated audit record.
  4. An automatic approval no longer satisfies a policy that requires review. `AddAsync` refuses
     review-required proposals inserted already approved.
  5. For engine lifecycle envelopes of mutating invocations, the owning module's
     `AdmitDisclosureAsync` decision now replaces the tool's mutation-authority check.
  6. `AddMessageAsync` and `AddAsync` on the default services can throw `AgentSessionPolicyChangedException`
     when a caller stamps a revision that is no longer current.
  7. A duplicate accepted prompt delivery re-retains that message's own context.
  8. Only the default executor's engine-private signal records Cancelled. A replacement
     `IAgentActionProposalExecutor` returning `agent.proposal.cancelled` is recorded as a Failed refusal,
     not Cancelled. A module cannot fake a zero-effect cancellation after dispatch.
- **Not touched:** §2.6.1 events (no new event or fan-in), §E2 design/runtime split, §E7 tiers
  (new tests are in the existing `Elsa.Agent.Tests` project), and the draft §2.24.

**Obsolete source.** None removed. The per-event audit lookup was already removed in `95e6d6532`.
`AgentEndpointActor.CanAccess` still has three production callers. `IAgentAuditReader.ListAsync`
remains a public, trusted in-process member; removing it would be a Major change outside this leaf.

## Registration decision

The new replacement contracts follow the Agent domain's existing `TryAdd*` registration. Startup
diagnoses duplicates only for `IAgentToolAuthorizer`. Diagnosing two new contracts alone would leave
the domain inconsistent, so the Agent-wide §2.6.2 gap is tracked in
[#3633](https://github.com/elsa-workflows/elsa-foundation/issues/3633).

## Ceiling

"The administrator's current allowed modes/ceiling" (FR-006) is the session's owning `AgentPolicy`:
`MaxAutonomyMode` and `AllowedAutonomyModes`, supplied by the host when the session is created.
The HTTP create route uses `AgentPolicy.Default`, which has a FullAuto ceiling. The update clamps to
the owning policy's current ceiling, and that clamp is covered by component tests on sessions created
with a restrictive policy. No runtime surface for changing an administrator ceiling exists in this
leaf or before it, and no native case runs with a ceiling below FullAuto.

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
