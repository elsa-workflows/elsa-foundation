# Specification Quality Checklist: Elsa 3 activity map

**Purpose**: Validate specification completeness and quality before proceeding to planning
**Created**: 2026-10-13
**Feature**: [spec.md](../spec.md)

## Content Quality

- [x] No implementation details (languages, frameworks, APIs) — see note 1
- [x] Focused on user value and business needs
- [x] Written for non-technical stakeholders — see note 1
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
- [x] No implementation details leak into specification — see note 1

## Notes

1. Like spec 235, this is a converter spec whose "users" are migrators and the spec 235 reconciliation. It names
   existing types in a verified Context section and in FR-016…FR-019 because the requirement is about those existing
   seams (which code path runs lowering, what must not be restructured). Class layout, project placement, attribute
   name and code strings are left to the plan.
2. Open decisions from the 2026-10-13 interview are resolved in the spec (member-level decisions in member-map.md; JavaScript host functions split to #2717): version = highest installed (FR-008);
   intrinsics in scope (FR-003); FlowDecision/FlowSwitch mapped, FlowFork/FlowJoin deferred to #2705 (FR-001, US2-5);
   apply-time diagnostics, analyzer untouched (FR-018, FR-019); custom activities via an alias attribute on the
   rebuilt Elsa 4 activity (FR-009); complete member map with an error for leftovers (FR-002, FR-010); For inclusive
   default, ParsedContent refused, loop CurrentValue lowered (FR-004); R owned by spec 235 (FR-005).
3. Plan-level items carried as assumptions:
   alias attribute placement and catalog carrier; SendHttpRequest.ContentType effective Elsa 3 default.
