# Specification Quality Checklist: Warn about writes to one variable whose order is not defined

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
- No [NEEDS CLARIFICATION] markers. Decisions D-1 to D-5 were confirmed by the owner on 2026-10-23, after Frans van
  Ek's review turned the refusal into a warning and amended ADR 0048 §4 and §5.
- The open fact is settled: today's rule ignores outcome ports and treats decision outcomes in a Flowchart as
  unordered. D-2a and FR-003 fix that.
- Facts behind the warning direction: commits are totally ordered per instance (last commit wins is well defined),
  and Merge and Reduce overwrite like Set today.
- FR-010 (two captures of one node into one variable) is a separate finding, not about ordering; it is kept because
  the same variable-write grouping finds it.
- Follow-ups #3282 to #3285 and #3290 to #3292 are filed under epic #3052 and are out of scope.
