# Runtime Activity Invocation Boundary

> **Current status (2026-07-16): invocation sequencing remains, while its construction and value-flow mechanism is superseded by [spec 095](../095-value-flow-redesign/spec.md).** Invocation now reuses a committed input snapshot, acquires a transient activation lease, and commits one closed typed transition.

**Feature Branch**: `codex/runtime-activity-invocation-boundary`
**Created**: 2026-06-11
**Input**: Runtime Execution Seam next slice after Runtime activity start state transition.

## User Scenarios & Testing

1. Given `StartActivity` scheduler work transitions an activity execution to `Running`, when scheduler draining continues, then it enqueues deterministic `InvokeActivity` work for the same `ActivityExecutionId`.
2. Given `InvokeActivity` work is drained with a matching running activity execution, when an activity runtime feature contributes invocation support, then runtime constructs the activity from the executable node descriptor, invokes the activity body, and records the activity execution as `Completed`.
3. Given the activity cannot execute or throws while evaluating/executing the activity body, when `InvokeActivity` work is handled, then runtime records a deterministic terminal activity state without traversing executable edges.
4. Given input materialization fails, when `InvokeActivity` work is handled, then runtime records a deterministic faulted activity state instead of leaving the activity execution running.
5. Given activity construction fails, when `InvokeActivity` work is handled, then scheduler work faults clearly and the running activity state is left unchanged.
6. Given `InvokeActivity` work is replayed after the activity already moved beyond `Running`, when handled, then it is idempotent and does not regress lifecycle state.
7. Given Workflows Runtime is composed without an activity invocation provider, when `InvokeActivity` work is drained, then the scheduler faults with a clear runtime dependency error instead of silently acknowledging the work.

## Requirements

- **FR-001**: Runtime.Core MUST expose an `InvokeActivity` scheduler command kind without changing existing command kind ordinals.
- **FR-002**: `StartActivity` handling MUST enqueue `InvokeActivity` scheduler work only after a `Running` activity execution state exists.
- **FR-003**: `InvokeActivity` work MUST carry pinned executable artifact identity, executable node ID, activity execution ID, and reason.
- **FR-004**: Workflows Runtime MUST expose a fallback handler that faults clearly when `InvokeActivity` work is drained without an activity invocation provider.
- **FR-005**: Activities Runtime MUST contribute the default activity invocation scheduler work handler.
- **FR-006**: The invocation handler MUST construct activities from the runtime-owned executable node descriptor and invoke `CanExecuteAsync`/`ExecuteAsync` without loading Design-owned authored workflow models.
- **FR-007**: The invocation handler MUST transition `Running` activity executions to `Completed` or `Faulted`, set `CompletedAt`, and preserve durable identity fields.
- **FR-008**: Replayed `InvokeActivity` work MUST NOT overwrite terminal or waiting lifecycle states.
- **FR-009**: This slice MUST NOT traverse executable edges, propagate completion to downstream nodes, write checkpoints, process bookmarks, or implement retry/outbox behavior.

## Success Criteria

- Runtime tests prove `StartActivity` enqueues `InvokeActivity` work for newly running activity executions and re-enqueues it when replay finds an existing `Running` state.
- Activities Runtime tests prove `InvokeActivity` constructs and invokes an activity from executable-node descriptor data and records `Completed`.
- Activities Runtime tests prove invalid payloads, mismatched state, missing activity state, non-running state replay, unsupported input materialization, persistence failure separation, `CanExecuteAsync == false`, and activity exceptions do not corrupt state.
- Runtime composition tests prove Workflows Runtime without Activities Runtime reports a clear missing invocation provider fault.
- Architecture tests continue proving Runtime execution code does not depend on Design-owned authored workflow models.
