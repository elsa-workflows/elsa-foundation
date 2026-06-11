# Runtime Activity Start State Transition

**Feature Branch**: `codex/runtime-activity-start-state`
**Created**: 2026-06-11
**Input**: Runtime Execution Seam next slice after Runtime schedule activity state creation.

## User Scenarios & Testing

1. Given `ScheduleActivity` scheduler work records a new scheduled activity execution, when the scheduler continues draining, then it enqueues deterministic `StartActivity` work for the same `ActivityExecutionId`.
2. Given `StartActivity` work is drained for an existing scheduled activity execution, when the handler runs, then it transitions that activity state to `Running` and records `StartedAt`.
3. Given `StartActivity` work is replayed after the activity already moved beyond `Scheduled`, when handled, then it is idempotent and does not regress lifecycle state.
4. Given the start payload references missing state, a mismatched executable node, a missing executable artifact, or mismatched pinned artifact identity, when handled, then the scheduler work faults deterministically before state changes.

## Requirements

- **FR-001**: Runtime.Core MUST expose a `StartActivity` scheduler command kind without changing existing command kind ordinals.
- **FR-002**: `ScheduleActivity` handling MUST enqueue `StartActivity` scheduler work only after a `Scheduled` activity execution state exists.
- **FR-003**: `StartActivity` work MUST carry pinned executable artifact identity, executable node ID, activity execution ID, and reason.
- **FR-004**: Runtime.Core MUST expose a default `StartActivity` scheduler work handler.
- **FR-005**: The handler MUST transition `ActivityExecutionState` from `Scheduled` to `Running`, set `StartedAt`, and preserve existing durable identity fields.
- **FR-006**: Replayed `StartActivity` work MUST NOT overwrite later lifecycle states.
- **FR-007**: This slice MUST NOT construct or invoke activity bodies, evaluate inputs, traverse executable edges, write checkpoints, process bookmarks, or add durable persistence providers.

## Success Criteria

- Runtime tests prove `ScheduleActivity` enqueues `StartActivity` work for newly scheduled activity executions and re-enqueues it when replay finds an existing `Scheduled` state.
- Runtime tests prove `StartActivity` handling records `Running` activity execution state.
- Runtime tests prove replay and invalid start work do not corrupt state.
- Runtime composition tests prove a start command drains through `Start`, `ScheduleActivity`, and `StartActivity` without invoking activity bodies.
