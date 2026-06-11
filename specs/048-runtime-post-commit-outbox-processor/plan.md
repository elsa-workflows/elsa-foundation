# Implementation Plan: Runtime Post-Commit Outbox Processor

**Branch**: `codex/runtime-post-commit-outbox-processor` | **Date**: 2026-06-11 | **Spec**: [spec.md](./spec.md)

## Summary

Add a narrow single-run post-commit outbox processor. It reads deliverable items from `IRuntimePostCommitOutboxStore`, dispatches through the existing post-commit intent dispatcher, and records delivered or retryable-failed outcomes. It deliberately stops before background workers, ownership claiming, and distributed processing.

## Technical Context

- `IRuntimePostCommitOutboxStore` already stores pending and retryable delivery state.
- `IRuntimePostCommitIntentDispatcher` already represents immediate intent delivery.
- `InMemoryRuntimePostCommitOutboxStore` owns retry exhaustion normalization.

## Constitution Check

| Gate | Status | Notes |
| --- | --- | --- |
| Runtime must not depend on Design | PASS | Uses Runtime.Core contracts and models only. |
| Operational retry remains separate from domain retry | PASS | Dispatch failures become outbox delivery failures, not domain retries. |
| Actor/provider neutrality | PASS | No ownership/claiming or actor framework dependency. |
| Scope control | PASS | No background processor, distributed provider, or wait activation behavior. |

## Implementation Steps

1. Add processor request/result contracts.
2. Add `IRuntimePostCommitOutboxProcessor`.
3. Implement default `RuntimePostCommitOutboxProcessor`.
4. Register the processor in `WorkflowsRuntimeApiFeature`.
5. Add focused processor and DI tests.
6. Run validation and self-review.

## Risks

- Without delivery ownership, concurrent processors could race in durable-provider implementations. This slice is limited to a single-run provider-neutral boundary.
- Wait-dependent intents still require later wait-registration activation policy before broad production use.
