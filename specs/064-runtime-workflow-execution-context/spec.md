# Feature Specification: Runtime Workflow Execution Context

**Feature Branch**: `codex/runtime-workflow-execution-context`
**Created**: 2026-06-11
**Status**: Draft
**Input**: Continue the runtime execution seam by replacing the runtime workflow execution context stub with a narrow runtime-owned implementation used by expression/JavaScript surfaces.

## Scenarios & Tests

1. Given a workflow execution state pinned to an executable artifact, when runtime code asks for workflow identity, then the context returns workflow execution ID plus pinned executable definition/version identity without loading authored workflow models.
2. Given runtime inputs, variables, and activity outputs are seeded into the context, when expression helpers read them, then values resolve from the runtime-owned in-memory context.
3. Given JavaScript helpers update correlation ID, workflow name, or variables, when the context is read again, then the updated values are visible through the runtime context.

## Requirements

- **FR-001**: `WorkflowExecutionContext` MUST expose workflow execution identity from `WorkflowExecutionState`.
- **FR-002**: Definition identity properties MUST come from `WorkflowExecutionState.PinnedExecutable`.
- **FR-003**: Correlation ID and name MUST be mutable runtime context values without mutating authored workflow models.
- **FR-004**: Workflow inputs MUST be explicit runtime `InputArgument` entries.
- **FR-005**: Variables MUST be runtime memory references that can be read, listed, and updated.
- **FR-006**: Activity outputs MUST be keyed by activity execution ID or runtime activity name without using authored activity IDs as durable lookup keys.
- **FR-007**: Unsupported or missing inputs, variables, outputs, and activity expression contexts MUST fail with deterministic `InvalidOperationException` messages.
- **FR-008**: The implementation MUST NOT reference Design-owned workflow/activity models or Elsa 3 object models.

## Non-Goals

- Full workflow execution pool implementation.
- Durable variable/value persistence.
- Expression evaluation pipeline redesign.
- Activity output history snapshots.
- Authored workflow document execution.

## Acceptance Criteria

- Tests prove workflow identity and correlation/name behavior are state-backed and mutable where expected.
- Tests prove inputs, variables, and activity outputs resolve from runtime-owned context data.
- Tests prove missing runtime values produce deterministic errors instead of `NotImplementedException`.
- Runtime and architecture validation pass.
