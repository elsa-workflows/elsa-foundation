# Tasks: Workflow Library Organization

## Setup and foundation

- [x] T001 Create/claim #3499/#3500, isolated worktree and issue branch; record `docs/program-goals/workflow-library-organization.md` and ADR 0080 authorization.
- [x] T002 Research seams and prepare `specs/1172-workflow-library/{spec,plan,research,data-model,quickstart}.md` plus `contracts/api.md`.
- [x] T003 Fix shared library models/store port in `Essentials/src/Workflows/Design/Persistence/Core/` before dependent implementation.

## US1 / US4: Durable folders and bounded library

- [x] T004 [P] [US1] Implement EF hierarchy/placement/create integration and SQL paging in `Essentials/src/Workflows/Design/Persistence/EntityFrameworkCore/` with durable tests.
- [x] T005 [P] [US1] Add folder/page capability/API and native contract tests in `Essentials/src/Workflows/Design/Api/` and its API Tests project.
- [x] T006 [P] [US1] Integrate canonical query/subtree identity and existing folders in `Studio/src/essentials/Elsa.Studio.Workflows/Client/src/workflow-editor/` with browse-state tests.

## US2 / US3: Overlapping classification and views

- [x] T007 [P] [US2] Implement tags/revisions/audit/bulk outcomes/views/favorites/cleanup in the EF files owned by T004.
- [x] T008 [P] [US2] Implement organization/tag/view/favorite API, identity/permissions/wire metadata in the API files owned by T005.
- [x] T009 [P] [US3] Add lazy organization/bulk dialogs, filters/views/favorites in Studio files owned by T006; preserve compact rows/accessibility/stale-data boundaries.

## Integrated qualification and delivery

- [x] T010 [US4] Regenerate provider Initial migrations with `tools/ef/generate-module-migrations.sh`; run provider/migration suites and deterministic large-library budgets in existing EF test projects.
- [x] T011 [US4] Add/run real-host and fixture journeys in `Studio/tests/`, including restart, lifecycle/source, tenant/user and narrow keyboard controls.
- [x] T012 Document delivered user/integrator behavior, permissions and bounds in canonical docs/module catalogs.
- [x] T013 Run affected whole suites, architecture/maps, lint/typecheck/build and package-consumer qualification; retain exact receipts on #3500/the PR.
- [ ] T014 Review independently to convergence under `docs/skills/catalog.md`; complete draft PR, required CI, merge/resulting-main qualification and program reconciliation.

## Dependencies and execution

T003 precedes implementation. T004/T007 form one sequential persistence lane, T005/T008 one API lane, T006/T009 one Studio lane. Lanes touch disjoint files and can run in parallel under the Spec Kit implementation workflow. T010–T014 follow integration; shared files are coordinated sequentially by the root. Risk-based tests may accompany implementation; red/green behavioral bite-proofs remain a delivery gate. Every story is required; folders alone do not complete the program.
