# Implementation Plan: Runtime Remove Direct Executor

**Branch**: `codex/runtime-remove-direct-executor` | **Date**: 2026-06-11 | **Spec**: [spec.md](./spec.md)

## Summary

Remove the legacy direct workflow executor path so runtime execution starts through workflow execution agents and scheduler work instead of inline artifact traversal.

## Technical Context

- Runtime API already dispatches `ExecuteWorkflow` through `IWorkflowExecutionStartDispatcher`.
- `IWorkflowExecutor` is not registered by `WorkflowsRuntimeApiFeature`.
- `SequentialWorkflowExecutor` bypasses pinned workflow execution state, scheduler work, checkpoint boundaries, activity execution state stores, bookmark creation, outbox ordering, and incident recording.
- The old `WorkflowExecutionResult`/`ActivityExecutionResult` models are now only coupled to the removed direct executor path.

## Constitution Check

| Gate | Status | Notes |
| --- | --- | --- |
| Actor-style execution agents | PASS | Keeps `WorkflowExecutionId -> agent/mailbox` as the execution ownership seam. |
| Runtime executes pinned artifacts | PASS | Starts by loading/pinning an artifact into a command envelope instead of executing the artifact inline. |
| Runtime state split | PASS | Avoids an execution result model that bypasses continuation-state projections. |
| Runtime must not depend on Design | PASS | Does not add authored workflow model dependencies. |
| Elsa 3 compatibility bounded | PASS | Does not add live-instance resume compatibility. |

## Implementation Steps

1. Add slice artifacts and update active Speckit pointers.
2. Mark previous slice PR-loop task complete.
3. Remove `IWorkflowExecutor`, `SequentialWorkflowExecutor`, direct execution result models, and direct executor tests.
4. Remove stale direct-executor API view conversion helpers and extension-point documentation.
5. Add/adjust regression tests proving agent/start-dispatch seams remain and direct executor types are absent.
6. Refresh generated extension maps if required.
7. Run focused validation and self-review.

## Risks

- External consumers may have referenced the direct executor. Runtime API no longer uses it, and the locked runtime direction requires agent/scheduler-owned execution with checkpointed continuation state.
