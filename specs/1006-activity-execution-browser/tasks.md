# Tasks: Activity execution browsing

## Setup and foundations
- [x] T001 Record/review owner-approved requirements in `specs/1006-activity-execution-browser/spec.md` and quality checklist.
- [x] T002 Research providers/ancestry and review contracts/design in `specs/1006-activity-execution-browser/plan.md`.
- [x] T003 Extend query/result contract in `Essentials/src/Workflows/Runtime/Core/Models/ActivityExecutionBrowsePage.cs` and inspection store contract.

## US1 — Inspect any occurrence
- [x] T004 [US1] Implement/test generic bounded history queries in existing in-memory and EF inspection stores under `Essentials/src/Workflows/Runtime/`.
- [x] T005 [US1] Add authorized collection endpoint/capability and API tests under `Essentials/src/Workflows/Runtime/Api/` and `Essentials/tests/Workflows/`.
- [x] T006 [US1] Add collection client/types and compact execution navigator in `Studio/src/essentials/Elsa.Studio.Workflows/Client/src/`.
- [x] T007 [US1] Integrate latest/explicit exact selection, refresh and faulted-value inspection in `Studio/src/essentials/Elsa.Studio.Workflows/Client/src/workflow-editor/WorkflowInstances.tsx`.

## US2 — Explore child execution context
- [x] T008 [US2] Implement generic child grouping, scope breadcrumbs and occurrence navigation in `Studio/src/essentials/Elsa.Studio.Workflows/Client/src/workflow-editor/`.
- [x] T009 [US2] Scope overlays/selection and retain reusable boundary navigation in `Studio/src/essentials/Elsa.Studio.Workflows/Client/src/workflow-editor/WorkflowInstances.tsx`.
- [x] T010 [US2] Cover ordinary/back-edge, nested, parallel and reusable identities in `Studio/src/essentials/Elsa.Studio.Workflows/Client/src/__tests__/`.

## US3 — Large histories and exact navigation
- [x] T011 [US3] Add paging/tie/count/cursor/access regressions in affected existing Runtime provider/API test projects under `Essentials/tests/Workflows/`.
- [x] T012 [US3] Cover stale requests, exact timeline/incident/deep links and unsupported/denied/partial states in `Studio/src/essentials/Elsa.Studio.Workflows/Client/src/__tests__/`.

## Integration and delivery
- [x] T013 Run affected whole suites, frontend type/lint checks, architecture and maps gates; record causal red/green in `specs/1006-activity-execution-browser/quickstart.md` and issue/PR.
- [x] T014 Verify rebuilt-host Looping4/10 and keyboard/narrow/Light/Dark acceptance per `specs/1006-activity-execution-browser/quickstart.md`.
- [x] T015 Review full diff/acceptance, update public APIs/docs and completion status in `specs/1006-activity-execution-browser/`, commit and open draft PR with evidence.

Dependencies: T001→T002→T003→T004→T005→T006→T007; US2 builds on US1; US3 verifies both. Provider/client tests may run independently once their implementation is ready; mutations stay sequential. Deliver complete owner-approved feature, not an isolated MVP.

Hooks: mandatory before-specify branch hook executed once with repository branch naming. Optional auto-commit hooks deferred to reviewed feature commits; disabled context hooks skipped. Five specification quality checks pass. Specification→plan→tasks reviewed against owner approval before implementation.
