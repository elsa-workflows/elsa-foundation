# Specialist human review extension points

Owner project: `Elsa.Agent.Specialists.Review`
Domain: `Elsa.Agent`

Agents are user-defined: authors create and publish them through Studio or configuration, and compose them with
ordinary workflow control flow. This package supplies one generic building block. `HumanReview` is a native wait
that pins exact content, such as an agent's output, for a permitted person's decision. The Copywriter/Proofreader
revision process in [spec1179's editorial contract](../../../../../../specs/1179-specialist-agents/contracts/editorial.md)
is a sample built from these blocks. It is not product code.

The package adds no general task inbox, user-task platform, approval store or generic checkpoint API.

## Consumed contracts

| Contract | Use |
|---|---|
| `IBookmarkResumeDispatcher` | Dispatches one decision to the exact saved review bookmark with explicit `PayloadType` and `ProviderId`. |
| `IEventHandler<ActivityResumeValidating>` | `HumanReviewResumeGuard` validates a fresh decision before native admission claims the wait, following the DispatchWorkflow guard. A transient dependency failure throws `RuntimeSchedulerWorkUnavailableException`, so the resume stays owed. Any other refusal leaves the pending wait intact and is recorded as a blocking poisoned-work incident. |
| `ICurrentSubjectPermissionAuthorizer` | Rechecks `agent-reviews:decide` for the server-derived subject when a decision is issued and again before admission. Hosts without a current-subject provider refuse every decision. |
| `ISecretValueProtector` | Protects the decision envelope. The protector takes no purpose, so the `elsa.agent.human-review-decision/1` purpose and its codec live inside the authenticated plaintext and are checked after decryption. |
| `IDistributedLockProvider` | Serializes submits for one wait. Native admission alone decides which decision consumes the wait. |
| `IWorkflowSchedulerWorkQueue` | Bounded per-workflow scan for a queued decision that carries the same request ID. |

## Exposed contracts

| Contract | Lifetime | Notes |
|---|---|---|
| `IHumanReviews` | Scoped | Reads and decides one exact wait for a tenant and actor that the host derives and the caller supplies. Receipts reconcile from native records: the consumed trigger delivery while admission is in flight, then the terminal typed result. |
| `HumanReview` | Activity | `StatefulActivity` with outcomes `Approved` and `Rejected`. `Content` is any JSON value, up to 64 KiB in its canonical compact UTF-8 encoding. Optional `Title` and `Notes` give the reviewer context. |

A durably claimed replay of the same delivery is historical. To match it, the guard checks:
- the attempt claim metadata;
- the trigger delivery ID of the sole open attempt;
- the consumed delivery's provider, type and deduplication key;
- the stored payload bytes.

Expiry and revocation apply only before admission. Content approval grants no tool or effect permission (see #2912).
