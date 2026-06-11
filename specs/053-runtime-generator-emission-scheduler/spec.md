# Feature Specification: Runtime Generator Emission Scheduler

**Feature Branch**: `codex/runtime-generator-emission-scheduler`
**Created**: 2026-06-11
**Status**: Draft
**Input**: Continue the Runtime Execution Seam after generator contracts and control-plane pause-boundary contracts. Add the narrow runtime seam that records generator emissions as ordered scheduler work without creating a separate generator state store.

## Scenarios & Tests

1. Given an in-workflow generator emits a `GeneratedEvent`, when runtime schedules the emission, then a scheduler work item is enqueued for the same workflow execution and generator activity execution.
2. Given the same generated event is scheduled more than once, when the default scheduler uses the in-memory queue, then the existing scheduler work item is preserved idempotently.
3. Given runtime API services are composed, when `IRuntimeGeneratorEmissionScheduler` is resolved, then the default implementation is available and replaceable.

## Requirements

- **FR-001**: Runtime.Core MUST expose `IRuntimeGeneratorEmissionScheduler` as the boundary that turns a generator emission into scheduler work.
- **FR-002**: The default implementation MUST enqueue `WorkflowExecutionCommandKind.GeneratedEvent` scheduler work through `IWorkflowSchedulerWorkQueue`.
- **FR-003**: Generated-event scheduler work MUST carry deterministic identity derived from `GeneratedEvent.GeneratedEventId` so duplicate emission scheduling is idempotent within the workflow execution queue.
- **FR-004**: Generated-event scheduler work MUST preserve workflow execution ID, generator activity execution ID, branch ID, event name, durability, sequence, and payload durable-value reference in the serialized payload or metadata.
- **FR-005**: Generator emissions MUST remain scheduler work/state, not a new durable generator-state bucket and not external trigger infrastructure.
- **FR-006**: Runtime API composition MUST register the default scheduler with `TryAddSingleton` so providers can replace it.
- **FR-007**: Runtime execution projects MUST remain free of Design-owned authored workflow model dependencies.

## Non-Goals

- Implementing generator activity execution behavior.
- Implementing trigger infrastructure.
- Implementing durable generated-event storage or replay.
- Mutating `SchedulerState.ActiveGenerators` or `SchedulerState.PendingGeneratedEvents` snapshots.
- Enforcing pause decisions before generator emission.
- Implementing backpressure execution behavior.

## Acceptance Criteria

- Tests prove generator emissions enqueue ordered scheduler work with deterministic identity and metadata.
- Tests prove duplicate emission scheduling preserves the first queued work item.
- Tests prove DI resolves and can replace the default scheduler.
- Focused runtime and architecture tests pass.
