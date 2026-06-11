# Implementation Plan: Runtime Post-Commit Outbox Recording

**Branch**: `codex/runtime-post-commit-outbox-recording` | **Date**: 2026-06-11 | **Spec**: [spec.md](./spec.md)

## Summary

Extend `RuntimeCheckpointCommitter` so successful checkpoint writes record post-commit intents into `IRuntimePostCommitOutboxStore` before immediate in-process dispatch. This preserves the ordering contract while keeping the full durable processor out of scope.

## Technical Context

- `RuntimeCheckpointCommit.PostCommitIntents` already carries the intents produced before the checkpoint boundary.
- `IRuntimePostCommitOutboxStore` and `InMemoryRuntimePostCommitOutboxStore` already model delivery state.
- The existing committer immediately dispatches intents after checkpoint persistence succeeds.

## Constitution Check

| Gate | Status | Notes |
| --- | --- | --- |
| Runtime must not depend on Design | PASS | Uses Runtime.Core contracts and models only. |
| Post-commit effects are after checkpoint commit | PASS | Pending outbox records are written after the checkpoint writer succeeds and before dispatch. |
| Operational retry remains separate from domain retry | PASS | Failed immediate dispatch records delivery failure only. |
| Scope control | PASS | No background processor, claiming, redelivery, or scheduler dispatch replacement. |

## Implementation Steps

1. Extend `RuntimeCheckpointCommitter` with optional `IRuntimePostCommitOutboxStore`.
2. Record deterministic pending outbox items after successful checkpoint write.
3. Record delivered or failed-final delivery results around immediate dispatch.
4. Add focused committer tests for ordering, failure, skip, and write failure.
5. Update Speckit pointers and previous slice completion marker.
6. Run validation and self-review.

## Risks

- Immediate dispatch remains best-effort in-process behavior. Durable delivery processors will need ownership, retry policy, and redelivery semantics in later slices.
- Outbox result recording after an already successful external side effect can still fail in an in-memory/default composition; durable providers need transactional semantics.
