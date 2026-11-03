# Human review of agent output

**Status**: M4 product contract, FR-007–FR-008/US2, Program A4/A5/A14. Delivered by T027 (`Elsa.Agent.Specialists.Review`)
and T028 (Studio page, BPMN journeys). Specialists are user-defined agents (plan D10). The
[editorial process](editorial.md) is a sample built from this contract.

## The wait

`HumanReview` is a native `StatefulActivity`, also usable as a BPMN bound child. It pins exact content, typically a
user-defined agent's output: any JSON value up to 64 KiB in its canonical compact UTF-8 encoding. Optional `Title` and
`Notes` give the reviewer context. It suspends through the existing bookmark and checkpoint machinery. The persisted
wait records the exact tenant, owner partition, execution, activity occurrence, content hash and wait identity.
Outcomes are `Approved` and `Rejected`, and the activity performs no effect. Loops, rewrites and routing around it
stay ordinary workflow control flow. This contract implies no general task inbox, user-task platform or second
approval database.

Content identity is the SHA-256 of the canonical encoding: compact, default encoder. That identity is stable under
whitespace and escaping. It is not normalized for key order or number spelling. A runtime store must therefore keep
private state and results as written; a store that normalizes JSON (for example a `jsonb` column) would leave a
pending review unreadable, though never approved.

## Reading and deciding

One module-owned Studio page, `/agents/reviews/{workflowExecutionId}/{activityExecutionId}`, addresses one exact
wait. In M4 it is reachable by URL only; the link from a pending occurrence in run inspection is T034 (M5). Two endpoints sit under `agent/reviews/{workflowExecutionId}/{activityExecutionId}`:
`agent-reviews:read` gates the read and `agent-reviews:decide` gates the decision, and deciding never implies
reading. The page shows the pinned content, title, notes and status. A reviewer chooses Approve or Deny and may
add a comment; the page offers both on every pending review, and the server's permission check decides.

The caller is the canonical Elsa user (`elsa.identity.user_id`, falling back to `NameIdentifier`/`sub`). A bearer
minted from an external sign-in currently names the provider subject, so it is refused rather than misattributed;
aligning external subjects is an M7 follow-up recorded in `tasks.md`.

The decision endpoint rechecks two things:
- the exact pending wait and content hash;
- the subject's current permission, through `ICurrentSubjectPermissionAuthorizer`.

Caller-supplied actor IDs, `requestedBy` metadata or arbitrary event payloads confer no authority. The closed body
schema refuses them.

The service creates an authenticated encrypted envelope through the existing `ISecretValueProtector`. The envelope
binds:
- the purpose and codec discriminator;
- the tenant and partition;
- the execution, occurrence, node and wait;
- the content hash;
- the server-derived actor;
- the decision and comment;
- the request ID and expiry.

Envelope validation checks every bound field after decryption. A protected value with another purpose is refused.

Dispatch targets the exact saved bookmark through `IBookmarkResumeDispatcher` and the native idempotency receipts,
always with an explicit `PayloadType` and `ProviderId`.

`HumanReviewResumeGuard` is an inline `IEventHandler<ActivityResumeValidating>`. It runs before the initial claim
and verifies, before the wait can be consumed:
- the envelope's purpose, scope and content;
- expiry;
- the subject's current permission.

How the guard classifies outcomes:
- **Transient dependency failure:** the resume stays owed.
- **Deterministic or invalid refusal:** the work item is poisoned and the valid pending wait stays intact.
- **Durably claimed replay of the same delivery:** historical. The guard checks the attempt claim metadata, the
  sole open attempt's delivery ID, the consumed delivery's provider, type and deduplication key, and the payload
  bytes. It then finishes without a new authorization; later expiry or revocation cannot reinterpret it.

## Receipts

Receipts are reconciled only from native records: the consumed delivery while admission is in flight, then the
terminal typed result, plus a bounded per-workflow queued-work scan. Receipts never carry content.

| Status | Meaning |
|---|---|
| `Accepted` | The same request ID and decision replays the admitted receipt. |
| `Conflict` | Different content under one request ID, or a changed content hash. |
| `AlreadyResolved` | A different later decision arrived after consumption. |
| `Pending` | The decision is provably queued. |
| `Refused` | Admission refused the decision. Submit a new request ID; this status is not durable once the runtime forgets the request. |
| `Closed` | The wait ended without a decision, for example through cancellation. |
| `Forbidden`, `NotFound`, `Unavailable` | Current permission is missing; no visible review matches; a dependency is temporarily down. |

A schema-write refusal surfaces as the framework's 409 problem. A concurrent workflow cancellation is settled by
native order and checkpoint fencing, never resurrected.

This is content approval, separate from #2912's production tool and effect authorization. No review decision grants
a tool permission or proves an external effect's idempotency.

## Required implementation receipts

Prove each of the following:
- Restart, then approve; and, in a separate run, restart, then deny.
- Acknowledgement loss.
- Duplicate, conflicting and late decisions.
- Changed content.
- Foreign tenant and foreign wait.
- Expired, tampered and foreign-purpose envelopes.
- Revocation before admission.
- A guard-time outage, and a deterministic guard failure.
- Cancellation races.
- Claim-before-crash recovery.
- Text and structured content across a restart, and the byte-bound edges.

Assert one logical parent continuation, and no approved terminal result on denial or refusal. Browser evidence:
exact content, keyboard decisions, pending, conflict and resolved states, refresh, and a narrow viewport.
