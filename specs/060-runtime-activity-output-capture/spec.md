# Feature Specification: Runtime Activity Output Capture

**Feature Branch**: `codex/runtime-activity-output-capture`
**Created**: 2026-06-11
**Status**: Draft
**Input**: Continue Runtime Execution Seam Slice 6 after runtime value binding contracts. Activity invocation must publish successful outputs by `ActivityExecutionId` and capture declared durable values without introducing a durable raw output store or Design-owned dependencies.

## Scenarios & Tests

1. Given an invoked activity sets an output and completes successfully, when invocation returns, then the runtime publishes an active activity output keyed by workflow execution ID, activity execution ID, and output name.
2. Given an executable node declares an output capture and the activity sets that output, when invocation completes successfully, then runtime commits a `DurableValueCaptured` checkpoint with a durable value state sourced from the activity execution.
3. Given an executable node declares output captures but the activity requests a durable bookmark instead of completing, then no active output publication or durable capture checkpoint is produced.
4. Given an activity completes without outputs or captures, then existing completion behavior remains unchanged.

## Requirements

- **FR-001**: The activity invoke handler MUST pass runtime output arguments to activity construction for executable node output capture names.
- **FR-002**: Activity output publication MUST use `WorkflowExecutionId`, `ActivityExecutionId`, and output name rather than authored activity IDs.
- **FR-003**: Successful activity execution MUST publish recorded outputs into `IRuntimeActivityOutputRegister`.
- **FR-004**: Declared output captures with `CaptureOnSuccessfulCompletion` MUST create `DurableValueState` through a `DurableValueCaptured` checkpoint.
- **FR-005**: Output capture MUST be skipped when activity execution does not complete successfully, including durable bookmark suspension and faults.
- **FR-006**: Runtime MUST NOT introduce a durable raw activity-output store or read history/audit output snapshots as continuation state.
- **FR-007**: Runtime output capture code MUST remain free of Design-owned authored workflow model dependencies.

## Non-Goals

- Full expression output binding scheduling.
- Compile/publish transformation from authored data links.
- External/custom durable value storage writers.
- History/audit output snapshot policy.
- Workflow-level output aggregation.

## Acceptance Criteria

- Tests prove successful activity invocation publishes active outputs by `ActivityExecutionId`.
- Tests prove declared durable output capture commits `DurableValueCaptured`.
- Tests prove bookmark suspension does not publish or capture completion outputs.
- Focused runtime/activity and architecture tests pass.
