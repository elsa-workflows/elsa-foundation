# Runtime Schedule Activity State Creation

**Feature Branch**: `codex/runtime-schedule-activity-state`
**Created**: 2026-06-11
**Input**: Runtime Execution Seam next slice after Runtime start command scheduling.

## User Scenarios & Testing

1. Given `Start` scheduler work schedules an executable start node, when the `ScheduleActivity` work item is created, then it carries a concrete `ActivityExecutionId`.
2. Given `ScheduleActivity` scheduler work is drained, when the scheduler handler runs, then it records an `ActivityExecutionState` in `Scheduled` status for the executable node.
3. Given the schedule payload references a missing executable node or mismatched pinned artifact, when handled, then the scheduler work faults deterministically before state is recorded.
4. Given Runtime API default composition is used, when a start command drains, then start-node activity execution state is recorded without invoking activity bodies.

## Requirements

- **FR-001**: Runtime.Core MUST generate runtime-owned activity execution IDs for scheduled activity work.
- **FR-002**: `RuntimeScheduleActivityCommandPayload` MUST carry a concrete `ActivityExecutionId`.
- **FR-003**: Runtime.Core MUST expose a default `ScheduleActivity` scheduler work handler that records `ActivityExecutionState`.
- **FR-004**: Recorded activity execution state MUST reference `WorkflowExecutionId`, `ActivityExecutionId`, `ExecutableNodeId`, activity type metadata, scheduling relationship fields, and scheduled timestamp.
- **FR-005**: Runtime.Core MUST expose an activity execution state store boundary with an in-memory default for the current runtime slice.
- **FR-006**: Missing executable artifacts, pinned identity mismatches, missing executable nodes, and malformed schedule payloads MUST fault deterministically.
- **FR-007**: This slice MUST NOT invoke activity bodies, evaluate inputs, traverse executable edges, write checkpoints, process bookmarks, or add a durable persistence provider.

## Success Criteria

- Runtime tests prove `Start` scheduling emits `ScheduleActivity` work with `ActivityExecutionId`.
- Runtime tests prove `ScheduleActivity` handling records a scheduled `ActivityExecutionState`.
- Runtime tests prove malformed/mismatched schedule work does not record state.
- Runtime composition tests prove a start command records start-node activity state without activity invocation.
