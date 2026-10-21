# Tasks: Task-first Runs triage

Input:[spec.md](spec.md), [plan.md](plan.md), [research.md](research.md), [data-model.md](data-model.md), [interaction contract](contracts/runs-interaction.md).

## Setup

- [x] T001 Validate #3062 claim, phase acceptance, source baseline and scoped contracts in specs/999-studio-runs-task-first/plan.md.
- [x] T002 Record behavioral test cadence and architecture boundaries in specs/999-studio-runs-task-first/plan.md; no application-wide TDD mandate is inferred.

## US1: Primary triage and workflow choice

Independent result: operators can select attention, Running and their intersection and select the exact workflow from duplicate names.

- [x] T003 [US1] Add a Runs-local server-backed definition chooser with selected-ID hydration, duplicate-name disambiguation, stale-response protection, paging/continuation and accessible states in Studio/src/essentials/Elsa.Studio.Workflows/Client/src/workflow-editor/RunWorkflowPicker.tsx; reuse api/workflowDesign.ts.
- [x] T004 [US1] Add independent immediate primary controls preserving unrelated drafts and existing health capability gates in Studio/src/essentials/Elsa.Studio.Workflows/Client/src/workflow-editor/WorkflowInstances.tsx (FR-001–003,FR-007,FR-012).
- [x] T005 [US1] Extend Studio/src/essentials/Elsa.Studio.Workflows/Client/src/__tests__/workflowRunHistory.test.tsx and add focused chooser tests for exact identity, duplicate names, stale/failed/empty responses and keyboard behavior.

## US2: Advanced criteria and applied summary

Independent result: detailed edits apply together; chips reflect applied state; disclosure preserves drafts and supports collapse.

- [x] T006 [US2] Use default-false allowCollapseWhenSet from Studio/src/apps/Elsa.Studio.Web/Client/src/app/ui/disclosure/AdvancedSection.tsx and tests in its __tests__/disclosure.test.tsx, preserving default forcing, error/explicit forcing and mounted inputs (FR-006). During reconciliation, retain main's existing production implementation and add Runs draft-preservation coverage.
- [x] T007 [US2] Group existing detailed filters under the public disclosure, implement truthful applied chips/count, individual removal and Clear all in Studio/src/essentials/Elsa.Studio.Workflows/Client/src/workflow-editor/WorkflowInstances.tsx; let the picker own the definition filter and retain current date semantics (FR-004–007).
- [x] T008 [US2] Prove Apply/remove/clear/reset, preserved unrelated drafts, page size and cursor/selection behavior in Studio/src/essentials/Elsa.Studio.Workflows/Client/src/__tests__/workflowRunHistory.test.tsx.

## US3: Row hierarchy and selection

Independent result: names lead rows, exact IDs stay available/copyable, and bulk actions appear only for eligible selection.

- [x] T009 [US3] Update run identity layout and selection-only toolbar in Studio/src/essentials/Elsa.Studio.Workflows/Client/src/workflow-editor/WorkflowInstances.tsx with public identity/copy components, preserving row/checkbox/action semantics and all existing permission/capability gates (FR-008–009).
- [x] T010 [US3] Adjust scoped responsive columns/layout in Studio/src/essentials/Elsa.Studio.Workflows/Client/src/styles.css and test exact copied values, name hierarchy and selection/keyboard behavior in src/__tests__/workflowRunHistory.test.tsx.

## US4: Detail return and states

Independent result: visiting a run preserves list context, while direct/invalid destinations are safe and failures remain truthful.

- [x] T011 [US4] Extend existing Runs URL helpers and detail navigation in Studio/src/essentials/Elsa.Studio.Workflows/Client/src/workflow-editor/WorkflowInstances.tsx and pages.tsx with validated canonical local return context, preserving tab/activity query inputs (FR-010).
- [x] T012 [US4] Wire request Retry and preserve distinct loading/empty/unsupported/error plus successful-empty-cursor recovery in Studio/src/essentials/Elsa.Studio.Workflows/Client/src/workflow-editor/WorkflowInstances.tsx (FR-011–012).
- [x] T013 [US4] Cover filtered/page2 return, direct fallback, external/protocol-relative/path-confusion rejection and browser-history behavior in Studio/src/essentials/Elsa.Studio.Workflows/Client/src/__tests__/workflowRunHistory.test.tsx and workflowInstances.test.tsx.

## Integration and delivery

- [x] T014 Build the deterministic14-row real list/detail fixture and journeys in Studio/tests/browser/golden-runs.html, goldenRunsFixture.tsx and golden-runs.spec.ts; prove all four stories, both themes, laptop widths, long identities, keyboard and state transitions.
- [x] T015 Document the delivered primary/advanced/chip/selection/return convention in Studio/docs/ux/design-spec.md (FR-013); record relevant exceptions rather than a new UI framework.
- [ ] T016 Root review, composed-host smoke, focused red/green bite proof, affected full Studio Web/Workflows suites, typecheck/build, lint/CSS/layout, architecture/maps and exact-head CI according to specs/999-studio-runs-task-first/quickstart.md. Record evidence on the scoped PR.
- [x] T017 Set terminal spec status in specs/999-studio-runs-task-first/spec.md for the implementation PR; remove completed checklist residue per lifecycle policy. Preserve main's removal of generated map snapshots and the active-feature pointer; current map publication does not require committing a manifest.
- [ ] T018 Complete independent correctness/standards/acceptance review, gated merge, resulting-main checks and #3062/#3057/Project61 reconciliation. Root owns delivery.

## Dependencies and execution

T001–002 precede source work. US1 establishes primary query/chooser behavior; US2 builds on it; US3 and US4 reuse the same existing component and must be serialized. T014 integrates all stories; T015–018 finish the unit. A single implementation writer owns the source patch, then root takes it back for QA/integration. Read-only reviews may run in parallel; no [P] implementation markers are used because the shared files make concurrent writers unsafe.

US1 is the first reviewable increment, not permission to deliver only a partial issue. Complete all acceptance criteria in this one scoped PR. Behavior tests and revert/mutation proof are required by the org protocol; the constitution's undeclared greenfield cadence is recorded in the plan without waiving tests or imposing an unrelated TDD policy.

Coverage:FR-001–003→T003–005;FR-004–007→T004,T006–008;FR-008–009→T009–010;FR-010→T011,T013;FR-011–012→T004,T012–014;FR-013→T015. SC-001–005→T005,T008,T010,T013–016.
