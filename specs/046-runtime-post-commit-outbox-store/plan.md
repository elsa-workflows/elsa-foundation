# Implementation Plan: Runtime Post-Commit Outbox Store

**Branch**: `codex/runtime-post-commit-outbox-store` | **Date**: 2026-06-11 | **Spec**: [spec.md](./spec.md)

## Summary

Add the default in-memory implementation for the post-commit outbox store boundary that was defined by the operational recovery/outbox contract slice. Keep this slice focused on storage semantics: pending records, deliverable queries, and delivery-result updates.

## Technical Context

- `IRuntimePostCommitOutboxStore` already defines the provider boundary.
- `RuntimePostCommitOutboxItem`, `RuntimePostCommitOutboxQuery`, and `RuntimePostCommitOutboxDeliveryResult` already define the state model.
- `RuntimeCheckpointCommitter` still performs immediate post-commit dispatch; a full durable processor remains out of scope.

## Constitution Check

| Gate | Status | Notes |
| --- | --- | --- |
| Runtime must not depend on Design | PASS | Store uses Runtime.Core models only. |
| Runtime state remains split | PASS | Outbox delivery state stays operational/infrastructure state, not workflow continuation state. |
| Operational recovery is not domain retry | PASS | Store records delivery retries only; no activity/domain retry behavior is introduced. |
| Scope control | PASS | No processor, claiming, distributed lease, or scheduler dispatch replacement. |

## Implementation Steps

1. Add `InMemoryRuntimePostCommitOutboxStore`.
2. Register it in `WorkflowsRuntimeApiFeature`.
3. Update extension-point docs from contract-only to default implementation.
4. Add focused store and DI tests.
5. Update active Speckit pointers and previous slice completion marker.
6. Run validation and self-review.

## Risks

- This default store is single-node in-memory only. Durable providers still need atomic commit and delivery ownership semantics.
- Query owner filtering remains a processor/provider concern because this slice does not implement delivery claiming.
