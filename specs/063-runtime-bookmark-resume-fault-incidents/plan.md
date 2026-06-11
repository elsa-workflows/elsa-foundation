# Implementation Plan: Runtime Bookmark Resume Fault Incidents

**Branch**: `codex/runtime-bookmark-resume-fault-incidents` | **Date**: 2026-06-11 | **Spec**: [spec.md](./spec.md)

## Summary

Route bookmark resume fault handling through the same blocking incident checkpoint behavior used by activity invocation faults, preserving bookmark state and existing fault metadata.

## Technical Context

- `WorkflowResumeBookmarkSchedulerWorkHandler` currently saves faulted activity state directly.
- `WorkflowInvokeActivitySchedulerWorkHandler` already commits activity fault incidents through `RuntimeCheckpointNames.IncidentRecorded`.
- `RuntimeCheckpointCommitter` and `InMemoryRuntimeCheckpointWriter` already project activity and incident state changes.

## Constitution Check

| Gate | Status | Notes |
| --- | --- | --- |
| Runtime state is continuation state | PASS | Only minimal blocking incident state is captured. |
| History outside continuation state | PASS | No history/audit projection store is added. |
| Checkpoints separate from policy | PASS | Fault state and incident state use `IncidentRecorded` checkpoint. |
| Runtime must not depend on Design | PASS | Uses runtime scheduler work, executable node, bookmark, resume target, and activity execution IDs. |

## Implementation Steps

1. Add slice artifacts and update active Speckit pointers.
2. Mark previous PR-loop task complete.
3. Extract shared activity fault incident checkpoint recorder.
4. Use the recorder from invocation and bookmark resume fault paths.
5. Add focused bookmark resume fault incident tests.
6. Run focused validation and self-review.

## Risks

- Resume fault incidents must not consume bookmarks; operators need the bookmark to remain queryable while the activity is faulted.
