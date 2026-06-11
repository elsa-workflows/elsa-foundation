# Feature Specification: Runtime Recovery Scanner

**Feature Branch**: `codex/runtime-recovery-scanner`
**Created**: 2026-06-11
**Status**: Draft
**Input**: Continue Runtime Execution Seam Slice 8 after post-commit outbox delivery. Add the default recovery scanner over operational state without implementing requeue execution, actor placement, or domain retry.

## Scenarios & Tests

1. Given operational state with an expired execution lease, when the scanner runs, then it returns a lease-lost recovery candidate.
2. Given operational state with a stale heartbeat and no expired lease, when the scanner runs, then it returns a heartbeat-expired recovery candidate.
3. Given operational state with a detected interrupted execution, when the scanner runs, then it returns the detected interruption as a recovery candidate and preserves the last checkpoint reference when present.
4. Given owner and limit filters, when the scanner runs, then it returns only matching candidates up to the requested limit in deterministic order.
5. Given operational state that is still live, when the scanner runs, then it is not returned as recoverable.

## Requirements

- **FR-001**: Runtime.Core MUST provide a default `IRuntimeRecoveryScanner` implementation.
- **FR-002**: The scanner MUST read `OperationalState` through the operational state store boundary.
- **FR-003**: The scanner MUST identify expired execution leases using `RuntimeExecutionLease.ExpiresAt` or the request lease timeout.
- **FR-004**: The scanner MUST identify stale heartbeats using the request heartbeat timeout.
- **FR-005**: The scanner MUST report detected interrupted executions without classifying them as domain retries.
- **FR-006**: Scanner results MUST be deterministic and honor request owner and limit filters.
- **FR-007**: Runtime execution projects MUST remain free of Design-owned authored workflow model dependencies.

## Non-Goals

- Requeueing workflow execution work.
- Claiming recovery ownership.
- Actor placement or distributed lease fencing.
- Domain retry policy behavior.
- Durable provider implementation.

## Acceptance Criteria

- Tests prove expired leases and stale heartbeats become recovery candidates.
- Tests prove detected interruption state preserves checkpoint-based recovery metadata.
- Tests prove live operational state is ignored.
- Tests prove owner and limit filters are honored.
- Runtime API composition resolves `IRuntimeRecoveryScanner`.
- Focused runtime and architecture tests pass.
