# Feature Specification: Runtime Domain Retry Policy

**Feature Branch**: `codex/runtime-domain-retry-policy`
**Created**: 2026-06-11
**Status**: Draft
**Input**: Complete the remaining Slice 8 domain retry boundary after operational recovery and post-commit outbox delivery. Add a default runtime domain retry policy that stays separate from operational recovery, without implementing workflow/activity retry scheduling.

## Scenarios & Tests

1. Given a runtime composition with default services, when `IRuntimeDomainRetryPolicy` is resolved, then it returns a default policy implementation.
2. Given a workflow or activity failure request, when the default policy decides, then it returns an explicit do-not-retry decision with stable metadata.
3. Given a recovery candidate for a lost lease, when operational recovery is represented, then the candidate can requeue from the last checkpoint without invoking or incrementing domain retry.

## Requirements

- **FR-001**: Runtime.Core MUST provide a default `IRuntimeDomainRetryPolicy` implementation.
- **FR-002**: The default policy MUST return `RuntimeDomainRetryMode.DoNotRetry`.
- **FR-003**: The default decision MUST preserve the request workflow execution id and optional activity execution id in metadata for diagnostics.
- **FR-004**: Runtime API composition MUST register the default policy with `TryAddSingleton` so providers or shells can replace it.
- **FR-005**: Operational recovery scanner and recovery candidate behavior MUST remain independent of `IRuntimeDomainRetryPolicy`.
- **FR-006**: Runtime execution projects MUST remain free of Design-owned authored workflow model dependencies.

## Non-Goals

- Workflow/activity retry scheduling.
- Retry counters or mutable failure history.
- Backoff algorithms beyond the default no-retry boundary.
- Incident policy integration.
- Elsa 3 retry model migration.

## Acceptance Criteria

- Tests prove default DI resolves `IRuntimeDomainRetryPolicy`.
- Tests prove the default decision is explicit do-not-retry and contains diagnostic metadata.
- Tests prove operational recovery candidates remain separate from domain retry decisions.
- Focused runtime and architecture tests pass.
