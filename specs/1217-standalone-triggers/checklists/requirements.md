# Specification Quality Checklist: Standalone triggers

**Purpose**: Review spec 1217 before implementation planning.

**Created**: 2026-11-05

**Feature**: [spec.md](../spec.md)

## Content quality

- [x] User goals, journeys and business outcomes lead the specification.
- [x] Mandatory sections are complete; implementation placement belongs in the plan/design.
- [x] Scope and initial trigger families are explicit.
- [x] Confirmed owner decisions are distinguished from proposed defaults and source findings.

## Requirements

- [x] FR-015 HTTP admission choice resolved: owner selected configurable policy once per trigger on 6 November 2026.
- [x] Requirements have observable failure and recovery cases.
- [x] Both association surfaces, input contracts and publication replacement are covered.
- [x] HTTP acceptance/response, Event broadcast, schedule policy and retained work are distinct.
- [x] Idempotency, partial recovery, concurrent edits, restart and tenant scope are covered.
- [x] Module/model capability and genuine BPMN acceptance are explicit.
- [x] Success criteria can be disproved through user/runtime observations.

## Readiness

- [x] Independent specification review completed; acceptance R3 has no surviving findings.
- [x] Necessary authority amendment and scoped technical design reviewed; correctness and standards R2 have no surviving blockers.
- [x] Plan/tasks and actual qualification instructions prepared and reviewed.

## Notes

M0 is approved for scoped implementation. No code, composed host, product UX inspection, merge or resulting-main gate has passed for the feature. A written requirement is not implementation evidence.

Initial independent review: slot-following/retained-work choices and both association surfaces are consistent. Resolved findings: deletion/source-removal retention, failed-update preservation, zero-eligible operational state, first HTTP reservation and unpublished-target authoring. FR-015 is resolved through the owner's per-trigger configurable policy choice; final review must verify both policies and their explicit partial/zero-acceptance outcomes against the technical design.

Final design reconciliation, 6 November: correctness P1 direct-model publication/queue race now has a protected provider boundary; standards findings split discovery/runtime, preserve immutable seal authority and define a public producer-bound submission seam; acceptance findings now cover HTTP partial/postaccept outcomes, seal-before-enqueue cancellation, actual BPMN worker-input editing and retained overdue schedules. Event/schedule admission is fixed independent behavior; configurable RequireAll/AcceptAvailable remains HTTP-only. Three independent review lenses converged. Product UX acceptance is incomplete until actual rendered qualification.
