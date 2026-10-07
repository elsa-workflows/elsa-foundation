# Feature Specification: Coalesced Command Scope

**Feature Branch**: `claude/runtime-db-coalesced-scope`

**Created**: 2026-10-06

**Status**: Approved

**Input**: Deliver Program #2382 end to end, including integrated correctness. Correction unit [T19/#2450](https://github.com/elsa-workflows/elsa-foundation/issues/2450) addresses the source-confirmed Coalesced drain factory lifetime mismatch discovered during T02/T03 follow-up. It blocks [T17/#2412](https://github.com/elsa-workflows/elsa-foundation/issues/2412).

## User Scenarios & Testing

### User Story 1 - Independent commands remain isolated (Priority: P1)

An operator runs independent workflows concurrently with Coalesced persistence. Each command retains its own persistence collaborators throughout its execution and cleanup, so another command cannot use them through a shared drain factory.

**Why this priority**: A shared persistence context can corrupt execution correctness or fail concurrent operations; a fast response alone does not establish a successful workflow.

**Independent Test**: Compose the existing scoped persistence services, create two command scopes, and verify the default Coalesced drain services resolve successfully with scope validation, share the expected identity within one scope, and use distinct scoped identities across scopes. No database operation is required for this contract check.

**Acceptance Scenarios**:

1. **Given** Coalesced persistence and independently scoped command collaborators, **When** the default drain factory is resolved twice inside one command scope, **Then** it uses that scope's collaborators consistently.
2. **Given** two independent command scopes, **When** their default drain factories and scoped persistence collaborators are resolved, **Then** the scopes do not share those instances through a longer-lived factory.
3. **Given** scope validation enabled, **When** the existing runtime persistence composition resolves its Coalesced drain services, **Then** no default singleton captures a scoped collaborator along this factory path.

### User Story 2 - Workflow behavior and explicit customization remain stable (Priority: P2)

An author keeps the same Coalesced workflow and an integrator keeps an explicitly supplied drain factory. The correction preserves existing response, committed-state, checkpoint and extension behavior.

**Why this priority**: Scope isolation must not weaken durability or silently replace a custom integration.

**Independent Test**: Run existing affected behavior suites and registration controls, then execute the valid HttpEndpoint workflow and REST companion against a rebuilt host and fresh owned database.

**Acceptance Scenarios**:

1. **Given** the deterministic HttpEndpoint workflow, **When** it completes, **Then** the expected response and terminal state are preserved without incidents.
2. **Given** the valid REST-start companion, **When** its admitted workflow settles, **Then** its expected output and terminal state remain correct.
3. **Given** repeated Coalesced registration or an explicitly pre-registered custom drain factory, **When** services are composed, **Then** registration remains idempotent and the custom registration is preserved.
4. **Given** the ordinary runtime default, **When** this correction is delivered, **Then** Immediate remains the host default and authored cadence and inspection settings remain unchanged.

### Edge Cases

- Scope validation is disabled in the normal shell container; isolation must still hold.
- Independent commands overlap and dispose in either order.
- Repeated registration must not introduce duplicate factories or decorators.
- A custom factory's own lifetime remains the integrator's contract; this unit corrects only the default factory.
- Nested drain/session behavior, checkpoint caps and failure/replay boundaries retain their existing tests and guarantees.
- A workflow may return a response but later fault; acceptance checks terminal state and incidents as well as response.

## Requirements

### Functional Requirements

- **FR-001**: The default Coalesced drain factory MUST resolve persistence collaborators from its command scope and MUST NOT retain them across independent command scopes.
- **FR-002**: Default factory identity MUST be stable within one command scope and distinct across independent scopes, with scope validation both enabled and disabled.
- **FR-003**: Registration MUST remain idempotent and MUST preserve an explicitly pre-registered custom factory.
- **FR-004**: Existing atomic checkpoint, participant/marker, ownership fencing, queue/outbox, replay and mandatory boundary behavior MUST remain unchanged.
- **FR-005**: HttpEndpoint remains the primary representative scenario; a valid REST-start companion MUST be checked separately, without equating its admission window with full HTTP response timing.
- **FR-006**: Evidence MUST distinguish the source contract repair, successful integrated workflow outcomes and unresolved attribution of previously captured failures.
- **FR-007**: Existing tests MUST retain their behavioral subjects and assertions; no serialization, pooling, timeout, durability, cadence-default or global container-validation policy change is part of this correction.

## Success Criteria

### Measurable Outcomes

- **SC-001**: The before-fix contract control demonstrably rejects the longer-lived capture or shows shared default factory identity across two independent command scopes; the corrected default-factory control passes both validation modes with zero shared scoped identities across the scopes.
- **SC-002**: Existing affected correctness checks pass without reducing their assertions or changing their tested behavior.
- **SC-003**: The T19 reviewed rebuilt-host HttpEndpoint and REST controls each produce the expected output, a completed workflow and zero incidents. Final four-client workload acceptance is a separate mandatory T17/T18 gate, rather than a T19 completion criterion.
- **SC-004**: The delivery packet identifies exactly what the scope proof establishes and does not label earlier 500/202 responses or the historical isolated 202 as causally repaired without their missing joins.

## Assumptions

- The source-confirmed singleton-to-scoped mismatch justifies a narrow contract correction independently of unresolved per-request attribution.
- The current Coalesced concurrency baseline observed failures; it is not a successful performance comparison. It remains a mandatory downstream T17/T18 integrated correctness gate; T19 establishes the scope contract and representative correctness without claiming that downstream gate passed.
- The scoped EF runtime composition is the contract-test composition; existing provider registration fixtures can be reused without querying a database.
- The separate activation-write renewal issue #2287, main SQLite failure #2185/#2293, observer transaction-identity correction and package-publication boundary retain separate ownership.
- No elapsed-time target, new global benchmark infrastructure, deployment or package publication is introduced.
