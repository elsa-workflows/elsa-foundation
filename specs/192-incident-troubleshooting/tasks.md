# Tasks: Workflow Incident Troubleshooting

**Input**: `specs/192-incident-troubleshooting/` specification, plan, research, data model and contract.

## Phase 1 — Setup

- [x] T001 Preserve QA report/screenshots and establish Program/Epic/Feature in `docs/program-goals/incident-troubleshooting.md` and `docs/reports/incident-troubleshooting/`.
- [x] T002 Record approved policy/scope and additive contracts in `specs/192-incident-troubleshooting/{spec,plan,research,data-model}.md` and `contracts/incident-troubleshooting.md`.

## Phase 2 — Foundation

- [x] T003 Claim scoped task issues and create isolated Foundation/Studio worktrees; record ownership/dependencies in `specs/192-incident-troubleshooting/tasks.md`.
- [x] T004 Finalize centrally defined causal/input-failure metadata keys and wire evidence mapping in `specs/192-incident-troubleshooting/contracts/incident-troubleshooting.md`.

## Phase 3 — US1: Recognize incident health (P1)

Independent check: multiple pages contain healthy, active nonblocking, blocking and resolved-only runs; health filters preserve authorization/count/cursor semantics.

- [x] T005 [US1] Add failing health/count/paging/authorization contract regressions in `tests/essentials/Workflows/Runtime/Api/Tests/WorkflowInstanceListContractTests.cs`.
- [x] T006 [US1] Add additive counts and authoritative health filter in `src/essentials/Workflows/Runtime/Api/{Requests/ListWorkflowInstances.cs,Handlers/WorkflowInstanceListService.cs,Models/WorkflowExecutionViews.cs}` and list endpoint.
- [x] T007 [US1] Add Studio list/header/legacy tests in `elsa-foundation-studio/src/essentials/Elsa.Studio.Workflows/Client/src/__tests__/{workflowInstanceListApi,workflowRunHistory,workflowInstances}.test.*`.
- [x] T008 [US1] Implement list current health/filter and persistent viewer action in Studio `api/runtime.ts`, `workflowTypes.ts` and `workflow-editor/WorkflowInstances.tsx`.

## Phase 4 — US2: Identify activity/input (P1)

Independent check: undefined JS input produces durable associated incident/failed input; cancellation, infrastructure, retry and checkpoint behavior remain unchanged.

- [x] T009 [US2] Add failing causal/failure/persistence regressions in `tests/essentials/Workflows/Runtime/Tests/{PoisonedSchedulerWorkIncidentObserverTests,ActivityInputSnapshotCheckpointTests,WorkflowSchedulerPoisonDrainTests}.cs` and relevant provider tests.
- [x] T010 [US2] Capture attributable typed input failure and payload-derived IDs in `src/essentials/Workflows/Runtime/Services/{Values,Scheduler,Incidents}/` and existing Core metadata/evidence model.
- [x] T011 [US2] Expose failed input evidence without fabricated values through runtime inspection projection in `src/essentials/Workflows/Runtime/{Core/Models,Services,Api/Models}/`.
- [x] T012 [US2] Cover current/resolved/unassociated/nested/BPMN overlay mapping in Studio `__tests__/workflowAdapter.test.ts` and BPMN tests.
- [x] T013 [US2] Implement accessible restrained incident cues and honest input state in Studio `workflowAdapter.ts`, `inputInspectionRows.ts`, `workflow-editor/graph.tsx`, `bpmn/` and semantic-token CSS.

## Phase 5 — US3: Direct troubleshooting navigation (P1)

Independent check: node and incident activation select exact occurrence, enter nested scope, frame node and expose incident; healthy selection retains ordinary inspection.

- [x] T014 [US3] Add node/incident/keyboard/nested/repeated occurrence navigation regressions in Studio `__tests__/workflowInstances.test.tsx` and graph/BPMN tests.
- [x] T015 [US3] Implement reciprocal incident/activity navigation and incident-first affected selection in Studio `workflow-editor/WorkflowInstances.tsx`, `graph.tsx` and BPMN mapping.

## Phase 6 — US4: Useful cause and dispatch feedback (P2)

Independent check: root cause and input lead summary; full evidence remains available; accepted-with-incident dispatch provides direct review.

- [x] T016 [US4] Add cause/dispatch/permission/unavailable regressions in Studio `__tests__/workflowInstances.test.tsx` and test-run suite.
- [x] T017 [US4] Implement cause-first evidence and incident-aware dispatch feedback in Studio `workflow-editor/WorkflowInstances.tsx` and existing test-run surfaces.

## Phase 7 — Integration, review and delivery

- [x] T018 Update backend/Studio behavior docs and contract evidence in `docs/runtime-fault-behavior.md`, affected API README and Studio module docs.
- [x] T019 Run scoped suites/typecheck/lint/build, architecture/maps and root diff review; record results in `docs/reports/incident-troubleshooting/acceptance.md`.
- [x] T020 Add real HTTP regression for missing JS input and authoritative health paging in `e2e-tests/` and run against rebuilt normally composed isolated Workbench.
- [ ] T021 Repeat rebuilt real Studio story/control/layout/theme/keyboard matrix; save after screenshots in `docs/reports/incident-troubleshooting/screenshots/` and acceptance report.
- [x] T022 Obtain independent code/evidence review and fix findings; record review and targeted mutation/revert bite proof in `docs/reports/incident-troubleshooting/acceptance.md`.
- [ ] T023 Commit/push/open and attach PRs via approved Git route; publish exact-head gates, merge green and verify post-merge CI/Maps; update `docs/program-goals/incident-troubleshooting.md` and issue/Project hierarchy.

## Dependencies and parallel execution

T001–T004 precede product writers. Backend T005/T006 and T009–T011 are independent concerns but the root and backend evidence worker reconcile shared API model changes. Studio T007/T008, T012–T017 uses the approved additive contract; final verification waits for backend implementation. One Studio writer owns shared view files. Root owns T018–T023 integration/QA and reviews worker output.

Within each story, write targeted regression before implementation and prove its failure; where host build queue prevents an initial run, use targeted revert/mutation proof later and record the limitation. No copied implementation-only tests. Each story is independently testable with approved fixtures, while complete delivery requires real-host proof.

First useful increment: associated failed input and visible health. Continue through navigation and final acceptance; this user requested the complete program, so no MVP stopping point applies.

## Issue ownership

Backend evidence [#2338](https://github.com/elsa-workflows/elsa-foundation/issues/2338) owns T009–T011; backend health [#2339](https://github.com/elsa-workflows/elsa-foundation/issues/2339) owns T005–T006. Root owns health; the backend worker owns causal evidence. Studio [#550](https://github.com/elsa-workflows/elsa-foundation-studio/issues/550) owns T007–T008/T012–T017. Root integration/QA [#2340](https://github.com/elsa-workflows/elsa-foundation/issues/2340) owns T018–T023.
