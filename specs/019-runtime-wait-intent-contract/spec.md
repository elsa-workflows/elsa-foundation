# Feature Specification: Runtime Wait Registration And Post-Commit Intent Contract

**Feature Branch**: `codex/runtime-wait-intent-contract`
**Created**: 2026-06-11
**Status**: Draft
**Input**: Locked Runtime Execution Seam addendum decision: wait registrations that depend on Elsa-caused outbound side effects use wait-dependent post-commit intents, not a global bookmark inbox.

## Scenarios & Tests

1. Given Elsa records a wait before dispatching an outbound side effect, when the checkpoint commits, then the wait registration has durable correlation identity and can remain reserved until the post-commit intent is delivered.
2. Given a signal arrives before the dependent intent is delivered, when runtime matches by correlation, then the contract permits matching a reserved wait without requiring a global inbox.
3. Given a wait-dependent post-commit intent is recorded, when inspected, then it references the wait registration and carries a failure policy.
4. Given a wait registration is terminal, when inspected, then it cannot be treated as a matchable reserved/active wait.

## Requirements

- **FR-001**: Runtime contracts MUST define durable wait registration/correlation identity separate from a broad global bookmark inbox.
- **FR-002**: Wait registrations MUST identify workflow execution, activity execution, correlation ID, stimulus type, match criteria, status, and failure policy.
- **FR-003**: Wait registration status MUST include reserved, active, satisfied, cancelled, expired, and faulted states.
- **FR-004**: Reserved and active waits MUST be matchable by correlation; terminal waits MUST not be matchable.
- **FR-005**: Wait registrations MAY reference the post-commit intent that activates or delivers the side effect they depend on.
- **FR-006**: Wait-dependent post-commit intents MUST continue to require a wait registration dependency and failure policy.
- **FR-007**: Failure policy MUST include the locked options, including compensation.
- **FR-008**: Runtime contracts MUST avoid introducing a global unmatched bookmark inbox.
- **FR-009**: Runtime execution projects MUST remain free of Design-owned authored workflow model dependencies.

## Non-Goals

- Implementing a wait/bookmark store.
- Implementing signal dispatch or matching algorithms.
- Implementing an outbox processor.
- Implementing durable event inbox retention.
- Changing bookmark resume resolution behavior.

## Acceptance Criteria

- `RuntimeWaitRegistration` records correlation/match state and wait status.
- Reserved waits can be matched by correlation before dependent intent delivery.
- Terminal waits are not matchable.
- `RuntimeWaitDependentIntentFailurePolicy` includes compensation.
- Focused runtime and architecture tests pass.
