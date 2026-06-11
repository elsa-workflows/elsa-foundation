# Implementation Plan: Runtime Activity Fault Incidents

**Branch**: `codex/runtime-activity-fault-incidents` | **Date**: 2026-06-11 | **Spec**: [spec.md](./spec.md)

## Summary

Change activity invocation fault handling to commit a minimal blocking incident together with the faulted activity state through the existing checkpoint committer.

## Technical Context

- `IncidentState`, `IIncidentStateStore`, and `RuntimeCheckpointNames.IncidentRecorded` already exist.
- `WorkflowInvokeActivitySchedulerWorkHandler` currently saves faulted activity state directly.
- `RuntimeCheckpointCommitter` and `InMemoryRuntimeCheckpointWriter` already project activity and incident state changes.

## Constitution Check

| Gate | Status | Notes |
| --- | --- | --- |
| Runtime state is continuation state | PASS | Only minimal blocking incident state is captured. |
| History outside continuation state | PASS | No history/audit projection store is added. |
| Checkpoints separate from policy | PASS | Fault state and incident state use `IncidentRecorded` checkpoint. |
| Runtime must not depend on Design | PASS | Uses runtime scheduler work, executable node, and activity execution IDs. |

## Implementation Steps

1. Add slice artifacts and update active Speckit pointers.
2. Mark previous PR-loop task complete.
3. Resolve `RuntimeCheckpointCommitter` for invocation fault paths.
4. Build faulted activity state with incident ID references.
5. Commit activity state and incident state via `IncidentRecorded`.
6. Add focused invocation fault incident tests.
7. Run focused validation and self-review.

## Risks

- Incident ID format is deterministic and local to the runtime work item for replay idempotency; broader ID policy can be centralized later.
