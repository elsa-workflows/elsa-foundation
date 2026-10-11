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
