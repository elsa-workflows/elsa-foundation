# Implementation Plan: Runtime Activity Execution State Projection

**Branch**: `codex/runtime-activity-state-projection` | **Date**: 2026-06-11 | **Spec**: [spec.md](./spec.md)

## Summary

Extend the checkpoint-state projection introduced for workflow execution state to activity execution state. The existing `IActivityExecutionStateStore` remains the store boundary; the in-memory checkpoint writer now projects accepted activity execution upserts from checkpoint commits into that store under the same serialized write/projection gate.

## Technical Context

- `IActivityExecutionStateStore` and `InMemoryActivityExecutionStateStore` already exist.
- `RuntimeCheckpointCommit.StateChanges.ActivityExecutions` already carries activity execution state changes.
- `InMemoryRuntimeCheckpointWriter` already serializes workflow execution projection and commit recording with an async gate.
- `WorkflowsRuntimeApiFeature` already registers `IActivityExecutionStateStore` before `IRuntimeCheckpointWriter`.

## Constitution Check

| Gate | Status | Notes |
| --- | --- | --- |
| Runtime must not depend on Design | PASS | Uses Runtime.Core activity state contracts only. |
| Runtime state remains split | PASS | Activity state stays in its own store boundary. |
| Checkpoint-driven state | PASS | Projection happens from accepted checkpoint commits, not direct handler writes in this slice. |
| Scope control | PASS | No durable provider, recovery scanner, outbox, or scheduler lifecycle rewrite. |

## Implementation Steps

1. Extend the in-memory checkpoint writer to accept an optional `IActivityExecutionStateStore`.
2. Validate activity state projection operations and state identity before recording writes.
3. Project activity execution state changes under the writer gate.
4. Add focused writer tests for activity projection and invalid operations.
5. Update active Speckit pointers and run validation.

## Risks

- This slice does not yet remove direct activity state writes from scheduler handlers. That behavior remains a later checkpoint-boundary refactor.
- The in-memory writer is a default/test implementation, not the final durable transaction provider.
