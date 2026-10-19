# Feature Specification: Native hosting conformance

**Feature Branch**: `agent/codex/3056-native-hosting-conformance`
**Created**: 2026-10-19
**Status**: In Progress
**Input**: Qualify built-in native execution hosting with real-process recovery and stale-owner evidence. [Task #3056](https://github.com/elsa-workflows/elsa-foundation/issues/3056), [feature #2905](https://github.com/elsa-workflows/elsa-foundation/issues/2905), [program #2892](https://github.com/elsa-workflows/elsa-foundation/issues/2892).

## User Scenarios & Testing

### User Story 1 - Recover accepted native work (Priority: P1)

As an engine operator, I can lose a host after work or a result is accepted, then recover the same execution without losing owed work or inventing completion.

**Why this priority**: Hosting substitution must preserve the real workflow across process loss.

**Independent Test**: Publish a Sequence containing a Flowchart with two held background activities and a final native step; terminate the owner at a durable boundary and observe recovery in another process.

**Acceptance Scenarios**:

1. **Given** both branches are durably admitted, **when** the process dies before workers start, **then** a replacement starts the same logical invocations.
2. **Given** two executing bodies, **when** their process dies, **then** recovery preserves invocation and attempt identities; physical execution may repeat.
3. **Given** a result is durable but its notification is held, **when** the host dies, **then** recovery consumes that result without rerunning its completed body.
4. **Given** either result arrives first, **when** the other remains held, **then** the Sequence does not advance; after both complete, it advances once and retains that history across restart.
5. **Given** two concurrently running clustered hosts, **when** the owner dies, **then** the survivor routes and completes accepted work over shared persistence through the real native engine.

### User Story 2 - Refuse a stale owner's write (Priority: P1)

As an operator, I can recover from a stalled owner without letting it overwrite authoritative state when it resumes.

**Why this priority**: Routing cannot prevent a transient overlap in perceived ownership.

**Independent Test**: Hold an actual engine-generated commit, suspend its process until another host acquires ownership, then resume the original commit and observe rejection at the durable write boundary.

**Acceptance Scenarios**:

1. **Given** an uncommitted original write and a survivor with newer ownership, **when** the original resumes its unchanged write, **then** the real EF transactional fence raises `RuntimeStaleFencingTokenException` with reason `StaleToken`, identifying original token T and the persisted successor token greater than T, and leaves no original marker or participant changes.
2. **Given** no accepted marker for that write, **when** rejection is observed, **then** evidence identifies both processes, the execution, commit and ownership tokens. Cancellation or successful replay does not count as fence rejection.
3. **Given** rejection of the original write, **when** the survivor is released, **then** it completes owed work with the native continuation semantics intact.
4. **Given** temporary removal of the actual persistence fence safeguard, **when** this same scenario runs, **then** it fails because the stale write can reach authoritative state; restoration passes.

### User Story 3 - Select and operate a host honestly (Priority: P2)

As a provider author, I can select built-in single-host or clustered composition, understand its applicable guarantees, and reuse qualification scenarios for a later provider.

**Why this priority**: A passing simulated membership test alone cannot qualify a production host.

**Independent Test**: Run the same published definition and observations through applicable built-in profiles and the existing membership contracts.

**Acceptance Scenarios**:

1. **Given** the single-host profile, **when** its process restarts, **then** durable work recovers without requiring cluster membership or cross-host routing.
2. **Given** the explicit clustered profile, **when** hosts participate, **then** one membership authority and the runtime's capability and persistence-scope rules govern routing.
3. **Given** incompatible scope/capability, a draining member, a changed incarnation or duplicate provider registration, **when** the relevant operation is attempted, **then** its owning production or membership layer enforces the declared rule and the evidence identifies that layer.
4. **Given** graceful shutdown or restart, **when** admission moves away from the old host, **then** accepted executions remain readable and recoverable; their required implementations and state are retained.

### User Story 4 - Preserve outcome acceptance (Priority: P2)

As a workflow author, I retain result and cancellation behavior when execution moves between hosts.

**Why this priority**: Delivery retries must not create a second logical continuation or revive cancelled work.

**Independent Test**: Deliver duplicate/conflicting work and results and retain the existing cancellation/restart witness against selected profiles and real persistence.

**Acceptance Scenarios**:

1. **Given** accepted work or a result, **when** identical delivery repeats, **then** it creates no second logical continuation; a conflicting delivery receives the established refusal.
2. **Given** cancellation has won, **when** a late result arrives, **then** it cannot revive or complete the execution.

### Edge Cases

- An accepted replay marker or cancellation masks the stale-owner fence: neither outcome satisfies that scenario.
- A barrier holds a database transaction or lock: the control itself must not prevent real survivor acquisition.
- Startup migrations overlap, package identities differ or authentication keys are not shared: distinguish configuration failure from recovery failure.
- Test failure occurs while a process is suspended: resume or kill only owned processes and retain bounded diagnostics.
- A profile cannot support a clustered case: report inapplicability, never success.

## Requirements

### Functional Requirements

- **FR-001**: Execute one pinned published Sequence/Flowchart definition, held parallel background work and final native continuation without changing definition or activity code for provider selection.
- **FR-002**: Exercise durable acceptance, actual process loss, recovery and both completion orders with stable execution/invocation identities.
- **FR-003**: Prove routing and recovery with at least two concurrently live, separately identified processes sharing production persistence.
- **FR-004**: Reject the original engine commit under stale ownership at the actual transactional fence, preserving its commit and original cancellation token. Require `RuntimeStaleFencingTokenException` with reason `StaleToken`, the presented original token and persisted newer token; a generic store failure, expiry or cancellation is insufficient.
- **FR-005**: Preserve duplicate, conflicting-result and cancellation acceptance; distinguish logical continuation from at-least-once physical effects.
- **FR-006**: Cover tenant/persistence scope, capability-aware placement, draining, incarnation changes and single-provider registration at their owning layers, naming modeled versus actual-process coverage.
- **FR-007**: Reuse existing fixtures and membership conformance; introduce no second membership/persistence authority or universal execution abstraction.
- **FR-008**: Preserve the library's in-process default and explicit clustered opt-in. Provide narrowly parameterized qualification scenarios for the separately scoped Orleans adapter.
- **FR-009**: Retain reproducible commands, source pins, process identities, durable observations, failure diagnostics and a negative control against a real fencing/recovery safeguard.
- **FR-010**: Preserve published definitions and accepted instances; document safe admission rollback, implementation retention and untested conditions.
- **FR-011**: Complete affected validation, independent review, map freshness, exact-head CI and post-merge verification, recording skips honestly.

### Key Entities

- **Qualification profile** selects host composition and scenario applicability, without owning workflow semantics.
- **Owned host process** has recorded identity, isolated authored configuration and controlled cleanup.
- **Durable observation** identifies accepted work, receipts, ownership, commit markers or native state at a boundary.
- **Scenario evidence** links source, profile, failure control, observations and outcome, including unsupported conditions.

## Success Criteria

### Measurable Outcomes

- **SC-001**: Both completion orders preserve one logical final continuation in all applicable recovery scenarios.
- **SC-002**: An actual two-process handover completes accepted work on the survivor with both process identities recorded.
- **SC-003**: The resumed original commit receives the specific stale-token rejection in FR-004 under newer ownership with zero stale authoritative changes; the survivor completes. The corresponding negative control fails and restoration passes.
- **SC-004**: Every scenario declares profile, validation layer and result; no modeled, skipped or inapplicable case is represented as actual-process success.
- **SC-005**: Preserved outcome/isolation rules pass affected validation and the reviewed change passes merge and post-merge gates.

## Assumptions

- This bounded built-in native-hosting slice does not implement Orleans, qualify other semantic models, freeze a neutral API, take BPMN adoption, or complete ETL, agent, integrated Studio or production-presentation work.
- Initial process evidence uses existing Workbench and shared SQLite on one machine. It does not qualify multi-machine network partitions, clock skew or other databases.
- Suspension is POSIX-specific with explicit applicability. Deadlines are hang guards, not performance targets.
- Existing contracts and accepted ADR 0078 govern the fixture. Provisional constitution material is not ratified by this work.
- Physical external effects remain at least once. Passing tests do not establish the causes of historical timeouts.
