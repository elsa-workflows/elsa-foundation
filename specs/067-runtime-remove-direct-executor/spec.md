# Feature Specification: Runtime Remove Direct Executor

**Feature Branch**: `codex/runtime-remove-direct-executor`
**Created**: 2026-06-11
**Status**: Draft
**Input**: Continue runtime execution seam cleanup after agent dispatch, scheduler state, and activity execution state exist. The legacy `IWorkflowExecutor`/`SequentialWorkflowExecutor` path executes artifacts inline and bypasses workflow execution agents, scheduler work, checkpoints, activity execution state, and incident/outbox behavior.

## Scenarios & Tests

1. Given runtime API receives an execute request, when handler dependencies are inspected, then it depends on `IWorkflowExecutionStartDispatcher` and not on a direct executor.
2. Given Runtime.Core contracts are inspected, when execution ownership seams are enumerated, then `IWorkflowExecutionAgentProvider` and scheduler command processing remain and no direct executor contract is exposed.
3. Given runtime API service composition is built, when services are registered, then no `IWorkflowExecutor` service is registered.

## Requirements

- **FR-001**: Runtime.Core MUST remove the `IWorkflowExecutor` contract.
- **FR-002**: Runtime.Core MUST remove the `SequentialWorkflowExecutor` inline artifact executor.
- **FR-003**: Runtime.Core MUST remove direct execution result models that existed only for the inline executor path.
- **FR-004**: Runtime API MUST keep returning start-dispatch views from `IWorkflowExecutionStartDispatcher`.
- **FR-005**: Runtime API composition MUST NOT register `IWorkflowExecutor`.
- **FR-006**: Runtime execution ownership MUST remain represented by `IWorkflowExecutionAgentProvider`, command envelopes, and scheduler work.
- **FR-007**: Removal MUST NOT introduce Design-owned execution dependencies or Elsa 3 live-instance resume compatibility.

## Non-Goals

- New scheduler behavior.
- New activity invocation provider behavior.
- New distributed actor provider implementation.
- Backward-compatible direct execute endpoint response shape.

## Acceptance Criteria

- No production source reference to `IWorkflowExecutor` or `SequentialWorkflowExecutor` remains.
- Runtime tests prove the direct executor type is absent and agent/start-dispatch seams remain.
- Runtime API tests prove execute request handling stays agent-dispatch based.
- Runtime and architecture validation pass.
