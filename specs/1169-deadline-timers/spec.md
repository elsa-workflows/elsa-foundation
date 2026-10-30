# Feature Specification: Durable timer deadlines

**Feature Branch**: `agent/codex/3478-deadline-timers`
**Created**: 2026-10-30
**Status**: Implemented — delivered by PR #3481; terminal status takes effect on merge.
**Input**: Owner instruction to open an issue for deadline-driven durable Delay scheduling and deliver it end to end. Issue [#3478](https://github.com/elsa-workflows/elsa-foundation/issues/3478). Program route: none/free-flow.

## User Scenarios & Testing

### User Story 1 - Resume when the delay expires (Priority: P1)

A workflow author places a five-second Delay between two Write Lines and sees it resume promptly after those five seconds, without an extra recovery-poll wait.

**Independent Test**: Run that workflow on a rebuilt Workbench, retaining activity timestamps and the original polling failure as a control.

**Acceptance Scenarios**:
1. Given a healthy idle host and a five-second Delay, when its deadline arrives, then resumption starts after the deadline and without waiting for the next ten-second recovery sweep.
2. Given a worker waiting for a later timer, when an earlier timer is persisted on this host, then the earlier timer advances the wakeup without overlapping sweeps.

### User Story 2 - Recover after a restart or missed notification (Priority: P1)

An operator restarts a host with pending timers, or another host persists a timer, and the workflow still resumes once.

**Independent Test**: Rebuild the runtime services against persisted SQLite state; separately insert a timer without a local wakeup hint.

**Acceptance Scenarios**:
1. Startup discovers pending future and overdue timers and resumes them when eligible.
2. A periodic recovery pass discovers timers created by another host without a local notification.
3. Duplicate hints, lost claims and expired ownership never authorize duplicate workflow execution.

### Edge Cases

- A notification races startup, deadline discovery, a running sweep or disposal.
- A timer is not yet visible because a peer owns its claim or it is backed off.
- A due backlog exceeds one bounded batch; an empty store must not busy-loop.
- One persistence partition fails while others remain usable.
- A custom timer store supplies only the existing contract.
- A very short timer fires before its bookmark commits; existing grace/retry behavior remains authoritative.

## Requirements

- **FR-001**: The worker MUST schedule its next healthy wakeup for the earliest known eligible persisted deadline or the next recovery pass, whichever comes first.
- **FR-002**: Successfully persisting a timer on this host MUST provide a retained wakeup hint, using the stored existing-wins deadline.
- **FR-003**: Notifications MUST be hints only; storage, claims, fencing and bookmark deduplication remain execution authority.
- **FR-004**: Startup and periodic recovery MUST rediscover persisted timers across the configured persistence partitions.
- **FR-005**: Claims, visibility, retry/backoff, batch limits, non-overlap, cancellation and orderly disposal MUST remain effective.
- **FR-006**: Existing custom stores MUST remain compatible with periodic recovery. No authored Delay or persistence schema change.
- **FR-007**: No recurring Timer/Cron, admission-control or parallel-dispatch changes are included.

## Success Criteria

- **SC-001**: In the idle-host five-second journey, Delay never resumes early and finishes within one additional second of its deadline; deterministic tests prove the actual deadline independent of machine load. This is a correctness witness, not a performance benchmark or real-time guarantee.
- **SC-002**: Earlier insertion, startup recovery and missed notifications each resume exactly once in the covered deterministic and persistent-store scenarios.
- **SC-003**: Failure, claim visibility, empty-store and disposal scenarios do not spin, overlap or bypass ownership.
- **SC-004**: Whole affected suites, provider-backed coverage, architecture/maps, independent review, exact-head required CI and resulting-main verification succeed with recorded evidence.

## Assumptions

The owner approved the recommended design in the preceding conversation. Precision is for healthy eligible timers on this host; another host's write without a distributed notification is discovered by the retained recovery interval. Ordinary execution contention and infrastructure failures may delay work.
