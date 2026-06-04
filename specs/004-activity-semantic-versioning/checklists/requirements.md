# Specification Quality Checklist: Activity Semantic Versioning

**Purpose**: Validate specification completeness and quality before proceeding to planning
**Created**: 2026-06-03
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

- This spec is architectural by nature (Unit 3 of an entity-design refactor authored for a software architect audience). It deliberately names model surfaces (entity, read contract, projection, reconciliation model, API) because the *what* of this unit **is** a typed-model change. Per house style of `specs/001`–`003`, this is treated as domain vocabulary, not a leaked implementation detail.
- **All clarifications resolved (session 2026-06-03/04)**:
  - **FR-011** — accepted semver format → **full SemVer 2.0.0**.
  - **FR-018** — runtime `int Version` on `ActivityBase`/`IActivity` → **in scope** (re-typed to semver; one version meaning across design + runtime).
  - **FR-009 / FR-012** — version source of truth → **the declaring assembly's version**; the `Version` attribute is an **optional override**. Working home for the attribute + activity base abstractions → a new `Elsa.Activities.Runtime.Core` extracted from `Elsa.Workflows.Runtime.Core`.
- **Two architectural decisions are flagged, not blocking the spec**, and carry to the plan's Constitution Check / the architecture touchpoint:
  - The `Elsa.Activities.Runtime.Core` package extraction (FR-009/FR-020) — module-decomposition + dependency-direction validation (§E2.2). Contested by Frans/Sipke.
  - The semver-ordering mechanism (FR-008) — persisted normalised sortable form vs in-memory comparison.
- Other follow-up open questions resolved in-spec: migration of int rows (no data migration — Unit B convention, FR-015); semver-ordering *correctness* is required (FR-008/SC-002), only the *mechanism* is deferred to the plan.
- **Spec is ready for `/speckit-plan`.** The two flagged decisions are plan-stage Constitution-Check inputs, not spec blockers.
