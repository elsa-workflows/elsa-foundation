# Native terminal receipt and retention probe

**Status:** proposed M0/#3820 prototype work unit, 13 November 2026; review before implementation.
The [admission probe](native-admission-plan.md) established an active-frame boundary. This work tests its terminal
lifetime. Neither the kernel design nor production StateMachine/API/schema placement is approved by this plan.

## Chosen direction to qualify

Keep the canonical receipt metadata in the same native workflow actor, keyed by exact invocation occurrence.
Use its existing fenced state/consumed-message/reply commit. Add no receipt actor, receipt store, ingress worker,
second scheduler or independently acknowledged queue. A separate receipt actor would need another causal
acknowledgement protocol and more commits; ordinary node-keyed results alone cannot preserve distinct visits.

Propose an optional **model-owned metadata contract** in the workflow actor layer, rather than StateMachine
tests in the durable kernel. Its responsibilities are narrowly:

1. Handle declared receipt/query and admission-retry controls against ended model state, without starting
   authored work. Existing models that do not implement the contract keep their current ended-message behavior.
2. Normalize owned receipt metadata when the workflow is forcibly terminated, without invoking authored
   lifecycle callbacks. Ordinary native frame completion/cancellation closes its own invocation metadata.
3. Report the latest outstanding receipt promise so normal deletion can refuse while it remains protected.

The workflow actor owns dispatch, status, state-version compatibility, fenced commit and parent notification.
The native model owns parsing and changing its private metadata; the durable layer does not parse native state.
No provider schema change or capability flag is presumed. Actual API names remain engineering choices.

## Identity and terminal outcomes

Every response identifies the workflow actor, machine frame occurrence and sender input ID. A definition/node
ID cannot substitute for that occurrence. Metadata queries and retries must work after the native frame is
removed, and after root workflow completion/termination. They must not require the authored definition or an
activity factory merely to read retained receipts.

Also settle the kernel's workflow-actor delete/recreate rule: local frame counters alone cannot distinguish
two creations of a reused actor ID. Exercise an actual normal delete/recreate and a late old invocation address.
Require the shared execution-incarnation identity or an enforced non-reuse contract; do not add a private
StateMachine nonce or count within-row frame uniqueness as this proof. If that shared rule is not yet available,
preserve the counterexample and keep the corresponding M0 gate open while coordinating with #4229.

Use the original accepted digest/version and order. A same-ID/same-contents retry returns the current receipt;
changed contents conflict, including after completion/cancellation. A new input to an ended invocation is
refused, never appended. A missing or expired receipt is distinct from a previously accepted final result.
No query reactivates the machine, re-evaluates a guard or sends a second workflow-finished notice to its parent.

Close outcomes in the terminal commit: queued inputs become not-processed with completion/cancellation reason;
selected but unfinished work is distinguishable as interrupted. Already completed outcomes remain unchanged.
Clear the queued business payloads from the receipt-owned area; retain only identity/digest/order/outcome and
the declared retention metadata. Do not erase ordinary activity state, values or history owned elsewhere.

Forced termination currently bypasses the native interpreter. A generic model-metadata hook must therefore
participate in that workflow-owned terminal commit; a later query must not be the only operation that makes
terminal dispositions truthful. If a newer model state cannot be interpreted, preserve it without guessing or
rewriting it through an older shape, and keep deletion protected until a compatible model can decide.

## Explicit promise and cleanup

The fixture registers a positive receipt window along with capacity/receipt bounds at normal invocation entry.
It is captured policy, not caller input or a binding re-evaluated on query. New accepted receipts record a
versioned digest, decision-time clock value and an explicit absolute valid-until value from the kernel's trusted
clock. The reply reports that deadline and committed identity. Queries/retries cannot extend or shorten it.
The policy is an absolute deadline anchored to the admission decision, not a promised full interval after the
caller finally receives its reply; delayed commit/lost reply cases must make that distinction observable.

Never evict an unexpired accepted receipt to make room. Pending/selected IDs remain protected from reuse while
their evaluation is unsettled, even if their original receipt deadline passes. Once final and expired, metadata
cleanup may release that receipt budget; it cannot reset an invocation's order counter or rearm ended work.
Define the boundary explicitly: `now >= validUntil` is expired. An expired closed occurrence cannot become live.

Normal workflow delete is already refused while the workflow runs. For an ended workflow, the workflow-owned
delete command asks the compatible model for its receipt protection deadline and refuses before it. The read,
refusal/delete decision and consumed command use the same actor/fence authority. A stale pre-admission state
cannot authorize deletion of a newer receipt promise. No API-side precheck or unrelated store read suffices.

An older model must not delete state whose newer version may carry an unreadable promise. Add the generic
state-version check before a model retention decision. This qualifies mixed model versions on the tested kernel
protocol only; it is not proof that old kernel/provider binaries already enforce a newly introduced rule.
Product mixed-version rollout and actual provider/retention qualification remain explicit later gates.

Advance the native metadata state version and define supported source upgrades. Prototype v2 receipts had no
qualified expiry promise; do not invent one while upcasting them. Preserve their identity/digest/outcome and
report/refuse unsupported new admissions where no valid captured policy exists. The fixture uses an explicit
window; no new product default or persisted product ABI is selected.

## Bounded fixture and adverse cases

Extend the non-packable probe or add one small runner on the actual native model/workflow actor/in-memory
store. Reuse request-correlated post-commit replies. A metadata query is not an ordinary workflow output stream.
Use real ordinary child completion and the normal terminate/delete command; never mutate a store row to make
an accepted receipt appear. Explicit old-version fixture seeding, if needed, is labelled setup, not writer proof.

Cover root completion, nested completion while the parent continues, and forced cancellation in Balanced and
Durable modes. Re-enter the same authored node through a normal loop to produce a genuinely new frame; query
and retry the old occurrence while the new one remains active. Also distinguish a live but unregistered child
frame and two workflow actors with equal local frame names. Preserve normal scopes and caller continuation.

| Case | Required committed observation |
|---|---|
| Complete with queued work | Final not-processed/completed reason, unchanged earlier outcomes, no receipt-owned business payload canary; caller continues once. |
| Cancel queued/selected work | Not-processed and interrupted remain distinct; query/retry after termination returns the original identities and deadline. |
| Definition unavailable / factory canary | Terminal query and retained duplicate/conflict succeed without constructing authored work or evaluating bindings. |
| Last slot and retained budget | Completed-but-unexpired receipts still occupy their budget; duplicates/conflicts resolve before capacity checks. Expired final data can free budget. |
| Deadline and policy change | Before/equal/after deadline, clock skew, changed authored policy and reload do not reinterpret an accepted promise. |
| Normal deletion | Refuse before the latest promise, retain state, permit after expiry, and never fabricate an accepted receipt after deletion. |
| Older model / stale writer | Refuse deletion when the model cannot interpret the state; execute an actually staged old-owner delete after takeover and prove its fence refuses. |
| Commit uncertainty | Cut completion, cancellation and receipt-query commit before apply and after apply/lost acknowledgement; retry/query resolves committed facts without duplicate parent continuation. |

Isolated negative controls must fail named assertions when terminal metadata dispatch is dropped, deletion
protection is removed, a closed occurrence is retargeted, pending payload cleanup is omitted or the captured
deadline is incorrectly changed. A generic exception is not a successful control. Keep failed/unrun results.

Review the exact source and executable inventory before one bounded execution packet. Use focused projects,
the normal build-slot wrapper, one worker/no shared compiler, and explicit command/time/process limits as in
the preceding probe. No product solution build, host listener, provider/Docker/cloud service or old Azure job
is implied. Relevant existing admission/Statechart/native regression checks remain required after shared edits.

## Handoff and limits

This plan can qualify the native metadata lifetime and its smallest shared workflow-owner change. It cannot
settle ordinary-trigger causal cohorts, normal eligible activity/guard recovery, full statechart phase/visit
semantics, value/secret boundaries, actual public sender authorization/API, provider/process restart, Studio or
native-compensation acceptance. The StateMachine admitted-input adapter also needs its own integration proof;
the existing signalled-composite spike is not that adapter. Record the result on #3820/#4229, revise the technical
plan from actual evidence, and keep Q1–Q3/M0 and kernel/product approval open until their full gates pass.
