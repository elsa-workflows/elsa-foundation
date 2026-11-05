# 0020. Runtime checkpoint commit records post-commit work without inline delivery

## Status

Accepted

## Context

Runtime checkpoint commit advances durable runtime state and may produce post-commit work such as scheduler enqueue intents. The previous module shape mixed checkpoint persistence, pending outbox recording, and inline delivery, which made the checkpoint commit interface shallow and forced tests to fake writer, dispatcher, outbox, policy, and timing seams for one behavior.

## Decision

Runtime checkpoint commit applies a named checkpoint's state changes and records any post-commit delivery work, but it does not dispatch that work inline. Post-commit delivery is owned by a separate delivery module such as the outbox processor. Provider adapters that persist checkpoint state should persist the associated pending post-commit work through the same commit path where the provider can support atomicity.

The provider-facing adapter is `IRuntimeCheckpointCommitStore`: it stores a full runtime checkpoint commit, including state changes and pending post-commit work. Delivery keeps its separate outbox interface for querying deliverable work and recording delivery outcomes; pending work creation is not part of the delivery-facing interface.

`RuntimeCheckpointPersistenceMode.Skip` means no checkpoint state persistence and no pending post-commit work recording. A skipped commit that contains post-commit intents is an expected policy contradiction and should be reported as a failed commit result rather than silently dropping delivery work.

This change may break current runtime checkpoint adapter contracts. That is acceptable for this work because the goal is a clean runtime checkpoint commit architecture and implementation, not compatibility with the shallow writer/dispatcher split.

## Consequences

- The checkpoint commit interface reports persistence and recorded pending work, not delivery success.
- `IRuntimePostCommitIntentDispatcher` remains a post-commit delivery seam and is not part of the checkpoint commit interface.
- A failure after checkpoint state is persisted but before pending post-commit work is recorded is an explicit inconsistent-durability failure, not a fallback to inline dispatch.
- Tests for checkpoint commit can focus on commit atomicity and recorded delivery work; tests for delivery retries belong with the post-commit delivery module.
- `IRuntimeCheckpointWriter` should be replaced by the narrower `IRuntimeCheckpointCommitStore` rather than preserved under a misleading writer name.
- `IRuntimePostCommitOutboxStore.SavePendingAsync` should be removed from the delivery-facing interface.
- Migration should replace the old seam rather than layer compatibility over it.

## Clarification (2026-10-11)

Confirmed by the owner on [#2584](https://github.com/elsa-workflows/elsa-foundation/issues/2584#issuecomment-6104682664)
for [spec 234](../../specs/234-hop-chain-collapse/spec.md). When the scheduler queue shares the checkpoint store's unit
of work, writing a continuation's scheduler work item inside the checkpoint transaction is *recording* post-commit work,
not *inline dispatch*. Nothing runs until a drain claims the item, and the item exists if and only if the checkpoint
committed. The decision above is unchanged: the commit still dispatches nothing, and every other kind of post-commit work,
and every composition whose queue does not share that unit of work, keeps the outbox.

## Proposed D-2 amendment

**Status: proposed (2026-11-05), part of [#3052](https://github.com/elsa-workflows/elsa-foundation/issues/3052).**
This extends atomic recording to background admissions. The accepted status above does not accept this amendment.
Identity/retry semantics belong to [ADR 0045](0045-workflow-value-flow-uses-role-owned-bindings-and-immutable-invocation-records.md#proposed-d-2-execution-contract);
flush cadence belongs to [ADR 0032](0032-runtime-checkpoint-cadence-is-policy-driven-per-workflow.md#proposed-d-2-amendment).

Stage a background admission with the workflow-state changes and pinned inputs it depends on. Publish them through
one atomic durable checkpoint: before commit a worker cannot claim the work; after commit its preceding state and
admission exist together. A separate state commit followed by a visible enqueue leaves a gap and does not satisfy
this rule.

When the background queue shares the checkpoint unit of work, recording its work item in that transaction is the
recording already allowed by the 11 October clarification. When delivery crosses stores, atomically record an outbox
intent with workflow state and deliver background work after commit. Delivery/claim handling tolerates duplicates
under stable work identity. If the composition cannot provide atomic recording and recoverable delivery, reject
unsupported placement with a clear diagnostic rather than offer best-effort durability. Neither case dispatches
user work inside the checkpoint transaction.

Several admissions and preceding state changes may share a commit under ADR 0032's bounded policy. Flush pending
admissions before waiting for results and within the configured finite dispatch bound. The durable queue or pending
intent is the source of recoverable work; a lost wake-up notification must not lose that work. Recording an identity
before an external call and its result afterwards still cannot atomically include an arbitrary external effect.

Publication order makes preceding committed changes visible to worker reads. It does not freeze every variable at
admission: later reads may observe later eligible commits. Pin activity inputs separately and apply the shared
version/conflict/protection rules on worker reads and coordinator finalization. Apply the same causal and defined
queue-precedence rules across providers without promising the same winner for unordered workers.

Implementing specs must prove pre-commit invisibility, state/admission atomicity, lost-wake-up recovery, duplicate
delivery/claim safety, bounded dispatch and coordinator finalization. This amendment delivers no runtime/store change.
