# Feature Specification: Runtime Remove Execution Pool

**Feature Branch**: `codex/runtime-remove-execution-pool`
**Created**: 2026-06-11
**Status**: Draft
**Input**: Continue runtime execution seam cleanup after actor-style execution agents exist. `IWorkflowExecutionPool` is an unused legacy-shaped contract that does not carry pinned executable identity, cancellation, checkpoint semantics, or actor mailbox ownership.

## Scenarios & Tests

1. Given runtime code needs execution ownership, when contracts are inspected, then the runtime exposes `IWorkflowExecutionAgentProvider` instead of an execution pool abstraction.
2. Given runtime API composition is built, when execution ownership services are resolved, then agent provider registration remains available and no pool registration is introduced.

## Requirements

- **FR-001**: Runtime.Core MUST remove the unused `IWorkflowExecutionPool` contract.
- **FR-002**: Runtime execution ownership MUST remain represented by `IWorkflowExecutionAgentProvider`.
- **FR-003**: Runtime API composition MUST NOT register `IWorkflowExecutionPool`.
- **FR-004**: Tests MUST prove the agent-provider contract remains the execution ownership seam.
- **FR-005**: Removal MUST NOT introduce Design-owned model dependencies or Elsa 3 live-instance resume compatibility.

## Non-Goals

- New distributed actor provider implementation.
- Workflow start API changes.
- Workflow execution context persistence.
- Scheduler behavior changes.

## Acceptance Criteria

- No production source reference to `IWorkflowExecutionPool` remains.
- Runtime API feature tests still prove `IWorkflowExecutionAgentProvider` registration.
- Runtime and architecture validation pass.
