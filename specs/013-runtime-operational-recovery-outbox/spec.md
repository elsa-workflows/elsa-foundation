# Feature Specification: Runtime Operational Recovery And Post-Commit Outbox

**Feature Branch**: `codex/runtime-operational-recovery-outbox`
**Created**: 2026-06-10
**Input**: Slice 8 from `docs/reports/elsa-4-runtime-execution-action-plan.md`

## User Scenarios & Testing

### User Story 1 - Operational Recovery Is Not Domain Retry (Priority: P1)

Runtime can represent leases, heartbeats, drains, and interrupted executions as operational continuation state without marking the workflow/activity as having taken a domain retry.

**Independent Test**: Create operational state and a recovery candidate for a lost lease; assert the candidate requeues from the last checkpoint while the domain retry decision remains explicit and separate.

### User Story 2 - Post-Commit Intent Delivery Is Durable And Ordered (Priority: P1)

Runtime records post-commit intent delivery state separately from checkpoint state mutation, preserving record, commit, deliver, and mark-delivered ordering.

**Independent Test**: Create pending, delivering, and delivered outbox items and assert delivered state requires a delivered timestamp.

### User Story 3 - Wait-Dependent Intents Avoid A Global Bookmark Inbox (Priority: P1)

Runtime can declare that a post-commit intent depends on a durable wait registration/correlation, without introducing a broad global inbox.

**Independent Test**: Create a post-commit intent with a wait registration dependency and failure policy.

## Requirements

- **FR-001**: Runtime.Core MUST define typed operational state for execution lease, heartbeat, drain/quiescence, and interrupted execution markers.
- **FR-002**: Checkpoint state-change envelopes MUST carry typed operational state, not opaque operational references.
- **FR-003**: Runtime.Core MUST define recovery scanner and recovery candidate contracts.
- **FR-004**: Runtime.Core MUST define post-commit outbox item, status, retry policy, query, and delivery result contracts.
- **FR-005**: Runtime.Core MUST preserve post-commit intent delivery ordering: record intent, checkpoint commit succeeds, deliver intent, mark delivered.
- **FR-006**: Runtime.Core MUST define wait-dependent post-commit intent fields and failure policy.
- **FR-007**: Runtime.Core MUST define a domain retry policy boundary separate from operational recovery.
- **FR-008**: Runtime operational recovery/outbox contracts MUST NOT introduce Design-owned workflow document dependencies.

## Out of Scope

- Full durable outbox processor.
- Full persistence provider implementation.
- Full recovery scanner implementation.
- Full distributed actor provider.
- Full wait registration/bookmark store.
- Domain retry policy implementation.

## Success Criteria

- **SC-001**: Tests prove lost-lease recovery can requeue from the last checkpoint without using domain retry.
- **SC-002**: Tests prove outbox delivery state has valid ordered transitions.
- **SC-003**: Tests prove wait-dependent post-commit intents carry wait dependency and failure policy.
- **SC-004**: Runtime and architecture dependency tests pass.
