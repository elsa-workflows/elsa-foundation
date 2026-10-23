# Specification Quality Checklist: Refuse concurrent captures into one variable

**Purpose**: Validate specification completeness and quality before proceeding to planning
**Created**: 2026-10-22
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

- The readers are the repository's architects. The spec names engine concepts (structures, intrinsics, captures,
  BPMN gateways), the existing `ValueFlow/ConcurrentWrite` rule and two existing test names, because they define the
  behaviour to keep. It does not prescribe types, members or files; that belongs in the plan.
- No [NEEDS CLARIFICATION] markers. Decisions D-1 to D-4, and D-2a, were confirmed by the owner on 2026-10-23.
- The open fact is settled: today's rule ignores outcome ports and treats decision outcomes in a Flowchart as
  concurrent. D-2a and FR-003 fix that. No built-in activity completes with more than one outcome; #3285 makes the
  runtime enforce it.
- Follow-ups #3282 to #3285 are filed under epic #3052 and are out of scope.
- FR-009 (two captures of one node into one variable) is a new refusal that is not concurrency; it is kept because
  the same variable-write grouping finds it.
