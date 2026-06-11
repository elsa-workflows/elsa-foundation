# Feature Specification: Runtime Pause Boundary Enforcement

**Feature Branch**: `codex/runtime-pause-boundary-enforcement`
**Created**: 2026-06-11
**Status**: Draft
**Input**: Continue the Runtime Execution Seam after control-plane state store and generator emission scheduling. The runtime has pause decisions but the scheduler drain path does not enforce them.

## Scenarios & Tests

1. Given queued scheduler work for a paused workflow execution, when the scheduler drain reaches a safe pause boundary, then it stops before dequeuing the work item.
2. Given the matching pause hold is released, when the scheduler drain runs again, then the queued work item can be dequeued and dispatched normally.
3. Given generated-event work is queued while the workflow is paused, when the scheduler evaluates the generated-event boundary, then the work remains queued.

## Requirements

- **FR-001**: Runtime.Core MUST expose a scheduler pause gate that maps scheduler work items to safe pause-boundary decisions.
- **FR-002**: The default scheduler drainer MUST consult the pause gate before dequeueing pause-gated work.
- **FR-003**: When a pause decision blocks advancement, the scheduler drain MUST stop without removing the queued work item.
- **FR-004**: Drain results MUST distinguish pause-blocked work from completed and faulted work.
- **FR-005**: The default pause gate MUST evaluate `StartActivity` and `InvokeActivity` at `BeforeActivityExecutionStart`.
- **FR-006**: The default pause gate MUST evaluate `GeneratedEvent` at `BeforeGeneratorEmission`.
- **FR-007**: Runtime API composition MUST register the pause gate as an overridable default.
- **FR-008**: Runtime execution projects MUST remain free of Design-owned authored workflow model dependencies.

## Non-Goals

- Administrative pause/unpause endpoints.
- Ingress adapter enforcement.
- Durable suspension/resume behavior.
- Generator-specific hold matching beyond metadata available on generated-event work.
- Queue API redesign or distributed actor/provider changes.

## Acceptance Criteria

- Tests prove pause-gated scheduler work remains queued when blocked.
- Tests prove released/unmatched holds allow the existing drain path to proceed.
- Tests prove generated-event work uses the generator-emission pause boundary and does not fall through to noop.
- Focused runtime and architecture tests pass.
