# Feature Specification: Groundwork Runtime Evaluation And Hardening

**Feature Branch**: `codex/groundwork-runtime-evaluation-hardening`

**Created**: 2026-06-10

**Status**: Draft

**Input**: User description: "Groundwork G8 runtime evaluation and hardening. Produce explicit go/no-go decisions for Elsa workflow runtime stores: Groundwork default, Groundwork with physicalization, benchmark-gated, or specialized provider. Runtime hot paths must not migrate silently. The result must include benchmark/concurrency/retry/operational gates and tests that preserve the conservative classification."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Classify Runtime Store Candidates (Priority: P1)

An Elsa architect can evaluate each runtime store candidate and see whether Groundwork is a default fit, a physicalization candidate, a benchmark-gated candidate, or a specialized-provider responsibility.

**Why this priority**: G8 is the final roadmap guardrail before runtime hot-path work can begin.

**Independent Test**: Run evaluator tests over checkpoint state, bookmarks, workflow execution mailboxes, execution logs, post-commit intents, locks, and runtime-defined business data and verify the expected recommendation for each.

**Acceptance Scenarios**:

1. **Given** runtime-defined business data, **When** it is evaluated, **Then** Groundwork default is allowed.
2. **Given** checkpoint/bookmark continuation state, **When** it is evaluated, **Then** it is classified as benchmark-gated rather than Groundwork default.
3. **Given** operational queues, outbox records, logs, or locks, **When** they are evaluated, **Then** specialized provider ownership is recommended.

---

### User Story 2 - Preserve Migration Gates (Priority: P1)

An implementation planner can see which benchmark, concurrency, retry, and operational evidence must exist before a runtime store can move to Groundwork.

**Why this priority**: Runtime store migration risk is operational, not just schema-related.

**Independent Test**: Verify each non-default runtime recommendation includes at least one required evidence gate and a reason.

**Acceptance Scenarios**:

1. **Given** a benchmark-gated recommendation, **When** it is inspected, **Then** benchmark and concurrency gates are present.
2. **Given** a specialized-provider recommendation, **When** it is inspected, **Then** the reason identifies the operational behavior that exceeds the portable document-store contract.

---

### User Story 3 - Publish A Runtime Evaluation Report (Priority: P2)

Maintainers can review a committed report that summarizes the go/no-go decisions and the required evidence before any workflow runtime migration.

**Why this priority**: The roadmap asks for an explicit decision artifact, not just code.

**Independent Test**: Review the report and verify it covers all runtime candidates and links to the evaluator contract.

**Acceptance Scenarios**:

1. **Given** the runtime evaluation report, **When** a maintainer reviews it, **Then** no workflow runtime hot path is marked as silently migrated.
2. **Given** a future runtime migration proposal, **When** it references the report, **Then** the missing evidence gates are clear.

### Edge Cases

- A store that requires atomic checkpoint plus post-commit intent dispatch must not be classified as Groundwork default.
- A store that represents an operational stream must not be classified as ordinary document storage.
- A store that needs leases, mailboxes, or distributed locks must stay specialized until a provider contract exists.
- Runtime-defined business data remains Groundwork default only because it is business data, not workflow continuation state.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Add an Elsa Groundwork runtime evaluation surface that returns a recommendation for a runtime store candidate.
- **FR-002**: Recommendations MUST use the existing Groundwork candidate categories: Groundwork default, Groundwork with physicalization, benchmark-gated, and specialized provider.
- **FR-003**: Benchmark-gated recommendations MUST include benchmark and concurrency evidence gates.
- **FR-004**: Specialized-provider recommendations MUST include a reason that identifies the operational behavior requiring specialization.
- **FR-005**: Workflow checkpoint/bookmark continuation state MUST NOT be classified as Groundwork default.
- **FR-006**: Workflow execution mailboxes, post-commit intents/outbox, execution logs, queues, locks, and leases MUST be classified as specialized provider candidates by default.
- **FR-007**: Runtime-defined business data MAY remain Groundwork default.
- **FR-008**: Publish a report summarizing the go/no-go decisions and migration gates.
- **FR-009**: Tests MUST preserve the classification matrix so future changes cannot relax runtime migration gates accidentally.

### Key Entities

- **Runtime Store Candidate**: A workflow-runtime persistence surface being evaluated for Groundwork.
- **Runtime Store Recommendation**: The resulting candidate category plus decision, reason, and evidence gates.
- **Evidence Gate**: Required benchmark, concurrency, retry, diagnostic, or operational evidence before migration.
- **Runtime Evaluation Report**: Committed decision artifact summarizing candidate classifications.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Tests cover at least seven runtime store candidates.
- **SC-002**: No workflow runtime hot-path candidate is classified as Groundwork default.
- **SC-003**: Every benchmark-gated or specialized recommendation includes required evidence gates.
- **SC-004**: Full solution tests pass with the runtime evaluation surface included.

## Assumptions

- G8 is a decision and hardening slice, not a runtime store migration slice.
- Actual runtime benchmarks are required before migration; G8 defines the gates and preserves the conservative default.
- Existing runtime contracts are still evolving, so evaluator output should be explicit and easy to revise in a future architect-owned runtime spec.
