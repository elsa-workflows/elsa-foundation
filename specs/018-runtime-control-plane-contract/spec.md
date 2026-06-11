# Feature Specification: Runtime Control Plane Contract

**Feature Branch**: `codex/runtime-control-plane-contract`
**Created**: 2026-06-11
**Status**: Draft
**Input**: Locked Runtime Execution Seam addendum decision: pause/unpause are runtime control-plane policy with explicit scopes and are distinct from durable suspension/resume.

## Scenarios & Tests

1. Given an administrator pauses a workflow execution, when runtime records that request, then the pause is represented in `ControlPlaneState` instead of ordinary workflow continuation state.
2. Given scheduler work reaches a safe pause boundary, when a matching workflow execution pause is active, then the contract can express that scheduler advancement must stop before unrelated or new activity execution work.
3. Given a volatile wait completes while the workflow is paused, when the scheduler evaluates the boundary, then workflow pause defaults to strict pause and host drain defaults to drain-in-flight semantics.
4. Given ingress is paused, when the ingress source type is HTTP, timer, queue, webhook, or manual/API start, then the contract exposes deterministic default handling.
5. Given runtime command names are inspected, when pause, unpause, bookmark resume, and volatile wait continuation are compared, then `Unpause` is not modeled as `Resume`.

## Requirements

- **FR-001**: Runtime contracts MUST represent administrative pause/unpause through `ControlPlaneState`.
- **FR-002**: Control-plane pause records MUST carry explicit scope: ingress, workflow execution, activity execution, generator, worker/dispatcher, or host drain.
- **FR-003**: Workflow execution pauses MUST identify the workflow execution and MUST default to strict pause behavior for volatile continuations.
- **FR-004**: Host drain control-plane pauses MUST default to drain-in-flight behavior.
- **FR-005**: The contract MUST name safe pause boundaries without representing unsafe mid-mutation boundaries as scheduler decisions.
- **FR-006**: Scheduler pause decisions MUST distinguish whether work may advance, why advancement is blocked, and which boundary was evaluated.
- **FR-007**: Ingress pause defaults MUST distinguish synchronous rejection from native source backpressure and skipped timer firings.
- **FR-008**: Runtime command terminology MUST distinguish `PauseWorkflowExecution`, `UnpauseWorkflowExecution`, `ResumeBookmark`, and `ContinueVolatileWait`.
- **FR-009**: Control-plane contracts MUST validate required identifiers for each pause scope.
- **FR-010**: Runtime execution projects MUST remain free of Design-owned authored workflow model dependencies.

## Non-Goals

- Implementing scheduler pause behavior.
- Implementing ingress adapters or HTTP responses.
- Implementing a full distributed actor provider.
- Implementing durable control-plane stores.
- Implementing durable suspension or bookmark resume behavior.
- Changing generator backpressure execution behavior.

## Acceptance Criteria

- `ControlPlaneState` holds scoped administrative holds separately from `WorkflowExecutionState`.
- Pause scopes validate required target IDs and reject ambiguous targets.
- Safe pause boundaries and continuation policies are explicitly named.
- Runtime command kinds include pause/unpause without overloading resume/continue.
- Focused runtime and architecture tests pass.
