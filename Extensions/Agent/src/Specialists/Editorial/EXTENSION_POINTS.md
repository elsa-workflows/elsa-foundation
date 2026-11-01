# Specialist editorial extension points

Owner project: `Elsa.Agent.Specialists.Editorial`
Domain: `Elsa.Agent`

The finite editorial example from [spec1179 editorial contract](../../../../../specs/1179-specialist-agents/contracts/editorial.md).
It adds no general task inbox, user-task platform, approval store or generic checkpoint API.

## Consumed contracts

| Contract | Use |
|---|---|
| `IBookmarkResumeDispatcher` | Dispatches one decision to the exact saved review bookmark with explicit `PayloadType` and `ProviderId`. |
| `IEventHandler<ActivityResumeValidating>` | `EditorialResumeGuard` validates a fresh decision before native admission claims the wait, following the DispatchWorkflow guard. A temporary dependency failure throws `RuntimeSchedulerWorkUnavailableException`, so the resume stays owed. Any other refusal leaves the pending wait intact, recorded as a blocking poisoned-work incident. |
| `ICurrentSubjectPermissionAuthorizer` | Rechecks `editorial-reviews:decide` for the server-derived subject when a decision is issued and again before admission. Hosts without a current-subject provider refuse every decision. |
| `ISecretValueProtector` | Protects the decision envelope. The protector has no purpose parameter, so the `elsa.agent.editorial-decision/1` purpose and codec live inside the authenticated plaintext and are checked after decryption. |
| `IDistributedLockProvider` | Serializes submits for one wait. Native admission alone decides which decision consumes it. |
| `IWorkflowSchedulerWorkQueue` | Runs a bounded per-workflow scan for a queued decision that carries the same request ID. |

## Exposed contracts

| Contract | Lifetime | Notes |
|---|---|---|
| `IEditorialReviews` | Scoped | Reads and decides one exact wait for a caller-supplied, host-derived tenant and actor. Receipts reconcile from native records: the consumed trigger delivery while admission is in flight, then the terminal typed result. |
| `EditorialRevisionGate` | Activity | Deterministic routing: `Write`, `Review`, `Approved`, `Exhausted`. |
| `ReviewEditorialContent` | Activity | `StatefulActivity` with outcomes `Approved` and `Rejected`. |

A durably claimed replay of the same delivery is historical. The guard matches four things:
- the attempt claim metadata;
- the sole open attempt's trigger delivery ID;
- the consumed delivery's provider, type and deduplication key;
- the stored payload bytes.

Expiry and revocation apply only before admission. Content approval grants no tool or effect permission
(see #2912).
