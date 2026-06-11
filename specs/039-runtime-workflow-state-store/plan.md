# Implementation Plan: Runtime Workflow Execution State Store

**Branch**: `codex/runtime-workflow-state-store` | **Date**: 2026-06-11 | **Spec**: [spec.md](./spec.md)

## Summary

Add the first queryable workflow execution continuation-state store. Previous slices made `WorkflowStarted` and `WorkflowCompleted` checkpoint commits carry workflow execution state changes; this slice projects those changes through the default in-memory checkpoint writer so state remains checkpoint-driven and queryable without introducing a durable persistence provider.

## Technical Context

- `WorkflowExecutionState` already exists in `Elsa.Workflows.Runtime.Core.Models`.
- `RuntimeCheckpointCommit.StateChanges.WorkflowExecution` carries workflow execution upserts for start and completion checkpoints.
- `InMemoryRuntimeCheckpointWriter` is the current default writer and is idempotent by commit ID.
- `IActivityExecutionStateStore` / `InMemoryActivityExecutionStateStore` provide the local pattern for split runtime state stores.

## Constitution Check

| Gate | Status | Notes |
| --- | --- | --- |
| Runtime must not depend on Design | PASS | Store and projection use Runtime.Core models only. |
| Checkpoint semantics remain separate from policy | PASS | Projection happens in the writer after policy selects a write; checkpoint names do not choose persistence mode. |
| Runtime state remains split | PASS | Adds workflow execution store without folding activity/scheduler/bookmark state into it. |
| Scope control | PASS | No durable provider, recovery scanner, outbox, or non-workflow state projection. |

## Implementation Steps

1. Add `IWorkflowExecutionStateStore` and `InMemoryWorkflowExecutionStateStore`.
2. Register the store in `WorkflowsRuntimeApiFeature`.
3. Extend `InMemoryRuntimeCheckpointWriter` to optionally project workflow execution upserts into the store when a commit is first accepted.
4. Add focused store, writer, and checkpoint-handler projection tests.
5. Run focused runtime/API/architecture validation.

## Risks

- Projecting from a test in-memory writer should not imply a final durable persistence architecture. Provider implementations remain responsible for atomic checkpoint/state application later.
- This slice only handles workflow execution state changes. Other checkpoint state categories stay in the envelope until their own store/projection slices.
