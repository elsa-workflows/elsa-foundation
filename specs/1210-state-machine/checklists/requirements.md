# Specification Quality Checklist: Elsa 4 StateMachine

**Purpose**: Check this provisional specification before planning or implementation.

**Created**: 2026-11-04

**Feature**: [specification](../spec.md)

## Content Quality

- [x] User-facing specification describes outcomes and constraints without choosing an internal implementation.
- [x] Generic infrastructure is separated from sample approval-domain concepts.
- [x] Mandatory user scenarios, requirements, entities, success criteria and assumptions are present.
- [x] Accepted OD1–OD28 are separated from proposals and open choices.

## Requirement Completeness

- [ ] No critical clarification remains. Q1–Q3 are open.
- [x] Accepted direction has requirement/scenario coverage in the acceptance matrix.
- [ ] Complete execution/selection and phase rules are testable without unagreed defaults. Q1 remains open.
- [ ] Bounded admission/delivery/outcome and fault/recovery contracts are concrete. Q2 remains open.
- [ ] Exact Studio journeys and remaining advanced/data/outcome boundaries are agreed. Q3 remains open.
- [x] Scope includes both hierarchy and parallel regions; early slices do not count as full completion.
- [x] Dependencies, initiative boundaries and existing saga ownership are explicit.

## Feature Readiness

- [x] Primary author/operator journeys and required actual-host/Studio witnesses are identified.
- [x] The matrix distinguishes expected witnesses from evidence that has passed.
- [x] No source observation is called runtime qualification.
- [ ] Ready for technical planning or production implementation: blocked by Q1–Q3, the architecture decision/witness plan and applicable agreement.

## Validation notes

Current draft validation: accepted OD1–OD28 are traced to requirements/scenarios/witnesses. The complete execution, delivery/recovery and Studio contracts intentionally remain open in Q1–Q3; no checklist box substitutes for agreement.

Initial independent review found that non-pre-emption alone did not forbid premature evaluation against an old configuration. FR-015 and its scenario now explicitly require selected work to finish before later event evaluation, matching OD10.

Accepted extensions include authored same-state order, automatic progress before queued business events, explicit finality, machine/state-visit value scopes and exit/action/entry ordering. Their remaining detailed integration/protection/recovery boundaries stay visible in Q1–Q3.

OD23 requires normal failed-activity recovery with completed earlier phases preserved. Source integration, propagation, blocked inputs and supported recovery paths still require Q2 agreement and proof.

OD24 chooses a focused chart with activity summaries and normal activity-editor drill-in, breadcrumbs and preserved chart context. The remaining detailed Studio journeys stay open in Q3.

OD25 chooses a structured transition inspector with the normal activity and expression editors. Detailed valid choices, diagnostics and keyboard/read-only/responsive journeys remain Q3.

OD26 requires explicit, validated initial states at each applicable level and starts all regions on parallel entry. Complete entry/exit boundaries and scoped completion remain Q1.

OD27 requires child-first Exit and parent-first Entry with durable phase completion. Complete boundary calculation remains Q1.

OD28 allows independent regional work to progress concurrently within each phase, preserving hierarchy dependencies and selected-set phase barriers. Partial-failure behavior and runtime integration remain open; independent branch completion order is not promised.

The next step is clarification and design review. No implementation readiness or approved specification is claimed.
