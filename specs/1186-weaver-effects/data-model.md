# Data model

These are the existing aggregates and the minimum candidate additions for causal qualification.
Public member names/signatures follow the demonstrated journey; this document does not freeze them.

## Conversation and accepted policy

`AgentSession` remains the conversation owner: opaque ID, actor/tenant, provider/conversation identity,
status/lifetime, current policy, messages and admitted context. An autonomy-only update changes the
current policy's autonomy value and its concurrency token; all unrelated restrictions survive.
The client receives effective mode, allowed modes/ceiling and an opaque policy revision, without
receiving internal denied-capability lists or unrelated private policy data.

An accepted user delivery retains its request identity/fingerprint, source, prompt mode, client and
complete context/provenance. Add accepted-policy attribution sufficient to identify the effective
mode/revision governing that delivery. Generated results retain that attribution even after a later
idle change. It grants no resource authority; execution/disclosure still recheck current authority.

Autonomy update intent contains the desired supported mode and an expected policy revision. A
correlation/delivery identity, if required by demonstrated lost-response behavior, is retained with
its original intent and acknowledged outcome. Conflicting reuse never changes the original intent.
Authoritative readback resolves a lost response; unresolved delivery remains visible to the client.

## Lifecycle ownership

The existing session owner orders policy updates with collection reservations, pending messages,
foreground leases, direct preparation/invocation, pause/review, approved dispatch, pending client
completion and receipt continuation. A reservation names its session and exact owner/operation;
cleanup by another operation cannot release it. Process-local short state transitions do not retain
scoped services or hold a monitor during asynchronous work.

```text
idle -> collecting -> accepted pending -> foreground
foreground -> retained review/pause -> dispatch -> pending client -> receipt continuation
foreground or continuation -> terminal message -> foreground ownership released -> idle
dispatched/unconfirmed -> explicit uncertain ownership; no automatic mutation replay
```

The arrows are ownership handoffs, not a new universal status enum. Original registry cancellation,
paused transcript integrity and per-call/per-proposal serialization remain authoritative. A terminal
display label alone is insufficient for idle. Every supported owner replacement must provide the
same boundary or refuse operations requiring it explicitly.

## Prepared operations and observed results

Existing immutable preparation binds module/tool/version, session/actor/tenant, turn/call/invocation,
target/revision, arguments, preview, risk, execution location, expiry and original client. Module
review and current host/session policy determine the actual approval requirement. Automatic approval
does not manufacture a later human approval.

Existing `AgentToolResult`/receipt still carries the module's typed evidence, exact invocation and
before/after targets. Server-persisted effects require independent owner readback. Client working
state requires native atomic validation/application, a whole-batch Undo and accepted acknowledgement;
Save/Publish remain separate. A result/status without the receipt cannot establish application.

## Correlated audit query

Audit records need immutable authoritative audience ownership (actor and tenant), independent of the
actor who performs a particular lifecycle action. Session/proposal/invocation/turn/call correlation,
status and before-versus-after-dispatch meaning are preserved. No model/caller metadata grants
ownership. Legacy records without authoritative ownership are ineligible for user audit queries.

The owning reader accepts authenticated actor/tenant, optional authorized session and bounded limit;
it filters and orders eligible records before the limit. The API never fetches a global unbounded
list then resolves one session per event. A replacement lacking scoped-query support refuses it.
Keep emission/query separate and avoid a session-service -> audit-sink -> session-service DI cycle.

Pre-dispatch expiry retains zero dispatch/effect and one correlated transition under serialized
repeated decisions. Expiry after client dispatch records uncertainty. Accepted late truthful receipts
can reconcile uncertainty without rerunning the operation or rewriting earlier evidence.
