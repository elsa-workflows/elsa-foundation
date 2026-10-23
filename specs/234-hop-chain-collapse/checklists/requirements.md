# Specification Quality Checklist: Collapse the per-activity hop chain under per-hop persistence

**Purpose**: Validate specification completeness and quality before proceeding to planning
**Created**: 2026-10-11
**Feature**: [spec.md](../spec.md)

## Content Quality

- [x] No implementation details (languages, frameworks, APIs)
- [x] Focused on user value and business needs
- [x] Written for non-technical stakeholders
- [x] All mandatory sections completed

## Requirement Completeness

- [x] No [NEEDS CLARIFICATION] markers remain
- [x] Requirements are testable and unambiguous
- [x] Success criteria are measurable
- [x] Success criteria are technology-agnostic (no implementation details)
- [x] All acceptance scenarios are defined
- [x] Edge cases are identified
- [x] Scope is clearly bounded
- [x] Dependencies and assumptions identified

## Feature Readiness

- [x] All functional requirements have clear acceptance criteria
- [x] User scenarios cover primary flows
- [x] Feature meets measurable outcomes defined in Success Criteria
- [x] No implementation details leak into specification

## Notes

- This is a runtime-internals feature; its "users" are operators and the runtime's own correctness suites. The spec
  names runtime concepts (checkpoint, work item, outbox, drain) because they are the domain vocabulary
  (see `docs/glossary/`), not because it prescribes code. Class and file names are confined to the measured baseline
  in **Why** and to the plan.
- SC-001 counts durable commits on the runtime database with the P0 gate's deterministic commit counter (the unit #2553
  measured with fsyncs on the runtime WAL); SC-002 uses the CI command-count gate.
- 2026-10-11 owner decisions after planning (#2584, PR #2639): the workflow tail is routed in-process and SC-001 is
  restated (≤ 52 with #2586 unchanged, ≤ 45 once it lands); SC-003's crash suite is the EF suite
  `EfHopCollapseCrashConvergenceTests` (ADR 0073 retired Groundwork); the existing-item edge case follows enqueue
  (kept as-is, no conflict); FR-008 deletes a two-commit dispatch's item in its first non-attempt-claim commit.
- The ADR 0020 reading that a durable queue write inside the commit is recording, not inline dispatch, was confirmed
  by the owner on 2026-10-11 (#2584) and is recorded as a clarification in ADR 0020.
