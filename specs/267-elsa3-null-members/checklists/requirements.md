# Specification Quality Checklist: Elsa 3 converter: a null activity member converts to what Elsa 3 ran with

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

- The spec names Elsa 3 and Elsa 4 activity members, because they are the domain the converter's users work in (as in
  specs 236, 248 and 264). It names no code, class or service of the converter.
- All decisions were settled with the maintainer before specification (see Clarifications).
- 2026-10-23: reframed after Frans van Ek's review (PR #3264). The rule is now stated per member from the Elsa 3
  runtime, the absent-member changes moved to #3281, and the null intrinsic gap is #3280. Re-validated: all items pass.

## #3281 amendment quality

- [x] Source shapes/effective values include supported empty wrappers versus Literal-without-value.
- [x] Exact source revisions/differences are recorded without claiming historical runtime execution.
- [x] Cleared status lists account for non-2xx routing and refuse with current target semantics.
- [x] Timer/status zero, negative and invalid values cannot silently choose target defaults.
- [x] Delivered bare-null and spec1232 policy/null-intrinsic ownership have explicit boundaries.
- [x] Existing proposal/separate implementation stages and current local/CI workflow are concrete.
- [x] Requirements, plan, tasks, model, diagnostic contract and verification route share one scope.
- [ ] Coordinator agreement on the final pushed proposal head and initiative proposal gate/merge.
- [ ] Separate implementation with actual runtime/corpus/local review evidence.

These are proposal-quality and pending-delivery checks, not #3281 completion.
