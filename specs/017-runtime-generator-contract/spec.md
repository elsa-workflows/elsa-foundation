# Feature Specification: Runtime Generator Contract

**Feature Branch**: `codex/runtime-generator-contract`
**Created**: 2026-06-10
**Status**: Draft
**Input**: Locked Runtime Execution Seam addendum decision: generators are in-workflow activities that emit execution events over time.

## Scenarios & Tests

1. Given a running workflow has an in-workflow generator activity, when runtime state records it, then the generator is scoped to a long-lived `ActivityExecution`.
2. Given a generator emits an event, when scheduler state records the emission, then the emission appears in a generated-event lane separate from ordinary scheduled activity work, completion-drain work, and continuations.
3. Given a generated event schedules downstream work, when the contract is inspected, then the emission itself is not modeled as an `ActivityExecution`; downstream activities still get their own scheduled work.
4. Given generator lifetime is scoped, when a registration is created, then the default policy ends the generator with the owning execution scope.

## Requirements

- **FR-001**: Runtime contracts MUST distinguish in-workflow generators from external triggers.
- **FR-002**: A generator registration MUST identify the owning workflow execution and generator activity execution.
- **FR-003**: Generator state MUST carry the owning execution scope boundary and default to scope-end lifetime.
- **FR-004**: Each generated event MUST have durable diagnostic identity, sequence, name, occurrence time, and generator activity execution identity.
- **FR-005**: Scheduler state MUST carry generated-event work separately from ordinary scheduled activity work.
- **FR-006**: Scheduler state MUST carry generated-event work separately from completion-drain work and continuation work.
- **FR-007**: Generated event durability MUST be explicit as volatile, durable, or policy controlled.
- **FR-008**: Generated event payloads that need runtime capture MUST reference durable values instead of embedding authored/design payload models.
- **FR-009**: Contract tests MUST reject invalid generator registrations and generated events.
- **FR-010**: Runtime execution projects MUST remain free of Design-owned authored workflow model dependencies.

## Non-Goals

- Implementing generator activity execution behavior.
- Implementing trigger infrastructure.
- Implementing full scheduler emission processing.
- Implementing durable generated-event storage or replay.
- Implementing control-plane pause/unpause behavior.
- Implementing backpressure execution behavior.

## Acceptance Criteria

- `SchedulerState` carries active generator registrations and pending generated-event work in dedicated collections.
- Generator registrations are tied to `ActivityExecution` identity and owning scope lifetime.
- Generated events carry emission identity, sequence, name, occurrence time, durability, optional durable payload reference, and metadata.
- Tests prove generated emissions are scheduler/history data, not activity execution state.
- Focused runtime and architecture tests pass.
