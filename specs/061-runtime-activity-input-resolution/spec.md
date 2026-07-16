# Feature Specification: Runtime Activity Input Resolution

> **Current status (2026-07-16): superseded by [spec 095](../095-value-flow-redesign/spec.md).** Inputs now resolve through role-owned canonical bindings into one immutable `ActivityInputSnapshot`; no execution-local memory reference or assembly-qualified type metadata remains.

**Feature Branch**: `codex/runtime-activity-input-resolution`
**Created**: 2026-06-11
**Status**: Draft
**Input**: Continue Runtime Execution Seam Slice 6 after active output publication and durable capture. Activity invocation must materialize compiled runtime input bindings from literal, active activity output, and durable value sources without loading authored Design models or history output snapshots.

## Scenarios & Tests

1. Given a runtime input binding references an active activity output by producer `ActivityExecutionId`, when the activity is invoked, then the input argument receives that active output value.
2. Given a runtime input binding references a declared durable value, when the activity is invoked, then the input argument receives the durable value inline payload.
3. Given an input binding cannot be resolved, then invocation faults the activity through the existing input materialization failure path.

## Requirements

- **FR-001**: Activity invocation MUST resolve runtime input bindings using `RuntimeInputBindingResolver`.
- **FR-002**: Activity-output bindings MUST resolve through `IRuntimeActivityOutputReader` by workflow execution ID, producer activity execution ID, and output name.
- **FR-003**: Durable-value bindings MUST resolve from `IDurableValueStateStore` by value ID within the current workflow execution.
- **FR-004**: Materialized input arguments MUST continue to use runtime type metadata and execution-local memory references.
- **FR-005**: Unresolved bindings MUST fault activity invocation as `InputMaterializationFailed`.
- **FR-006**: Runtime input resolution MUST NOT read Design-owned authored workflow models or history/audit output snapshots.

## Non-Goals

- Expression engine execution.
- Reference value provider resolution.
- Compile/publish data-link transformation.
- Scheduler data-dependency ordering.

## Acceptance Criteria

- Tests prove active activity output bindings materialize into activity inputs.
- Tests prove durable value bindings materialize into activity inputs.
- Tests prove unresolved bindings fault before activity construction.
- Focused runtime/activity and architecture tests pass.
