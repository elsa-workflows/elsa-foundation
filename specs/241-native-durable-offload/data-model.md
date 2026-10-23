# Background activity records and race semantics

## Identity and state

A `BackgroundActivityWorkRequest` contains WorkId, WorkflowExecutionId, ActivityExecutionId, AttemptId/ordinal, pinned executable identity, executable node ID, contract fingerprint, immutable input snapshot and deterministic coordinator result work item. Persistence scope is resolved by the existing provider scope mechanism and participates in every key/query. WorkId is deterministic from workflow + activity invocation + logical attempt. Worker delivery attempts do not change it.

A `BackgroundActivityWorkItem` carries the immutable request plus Pending/Claimed/ResultReady/Accepted/Cancelled status, claim owner, monotonic fence, visibility deadline, revision, recorded/updated timestamps and optional immutable result/receipt digest. No public API accepts a worker-selected CLR type or workflow-state replacement.

A terminal result is Complete (declared outcome and JSON typed result), Fault (safe normalized fault) or Cancel (safe reason). Unsupported Suspend becomes a diagnostic fault. Receipt identity includes logical work identity and a canonical payload digest; the current claim authorizes its first commit. Exact retries of the accepted receipt return idempotent success; conflicts never overwrite it.

Checkpoint participants carry admissions, acceptance requests (WorkId + expected receipt/digest) and cancellation requests (workflow or explicit activity scope). Admission is create-only. Acceptance is ResultReady → Accepted only, with native terminal state and continuation in the same transaction. Cancellation retires Pending/Claimed/ResultReady but leaves Accepted history intact. Providers preserve requests through all state-set copy/coalescing/fingerprint operations.

## Lifecycle race table

| Situation | Durable rule | Required observation |
|---|---|---|
| Admission checkpoint fails | Roll back activity admission and work row together. | No runnable orphan and no parked activity without owed work. |
| Worker claims pending/expired item | CAS revision; increment fence; set owner/deadline. | At most one currently authorized owner. |
| Deadline passes without successor | Renewal/completion reject at `now >= visibleUntil`. | An expired owner cannot commit. |
| Worker dies | No business transition; row becomes reclaimable at expiry. | Successor may physically repeat invocation. |
| Result commit response lost | Same current/recorded claim and identical receipt is replay success. | No conflicting overwrite or rerun after ResultReady. |
| Worker result vs cancellation | Worker may create ResultReady, but coordinator cancellation can still win before application. | Ready receipt alone is not native success. |
| Result application vs cancellation | Serialized coordinator checkpoint/fence decides first accepted terminal transition. | Cancel-first rejects late apply; apply-first preserves completed history. |
| Subtree cancellation | Resolve only explicit descendants of that scope. | Other branches/workflows remain live. |
| Receipt commit then wake failure | Sweep ResultReady and enqueue stable wake again. | Same receipt eventually reaches coordinator. |
| Application commit fails | Receipt remains ResultReady and native state unchanged. | Retry applies once. |
| Application response lost | Immutable commit/receipt marker handles replay. | No second output/fault/parent callback. |
| Downstream completion replay | Native propagation marker/stable checkpoint handles independently of receipt. | No second continuation or poison incident. |
| Host shutdown | Abandon/expire claim; stop renewal; never return Cancel automatically. | Work remains owed. |

## Payload and retention boundaries

Use existing snapshot/value policies and secret masking. V1 supports terminal, registry-resolvable JSON-compatible result types and persistable input snapshots. Reject transient, untyped object, encryption-required or unsupported value policies before body execution. Retain accepted/cancelled identity evidence with workflow history. A later retention service must not drop replay protection while delayed delivery remains possible; this slice does not add an independent destructive sweeper.
