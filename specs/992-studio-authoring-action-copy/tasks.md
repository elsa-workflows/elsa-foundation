# Tasks: Studio Authoring Action Copy and Toolbar Consistency

**Input**: Approved design documents in specs/992-studio-authoring-action-copy/

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, quickstart.md

**Execution boundary**: These tasks stage one bounded implementation. Root owns browser geometry capture, visual/keyboard review, full local and architecture/map gates, lifecycle cleanup, GitHub reviews, exact-head checks, and resulting-main verification. The implementation worker authors the bounded unit regressions and then makes production changes sequentially. No production edit starts until the captured browser baseline and all new unit/copy RED results receive explicit root review and approval.

**Test-first gate**: T002–T006 establish the real-component geometry baseline and all new unit/copy regressions before production changes. If a RED result shows an unexpected eligibility, identity, permission, callback, request, warning, or mode regression, stop for root investigation; do not change existing behavior to make a copy assertion pass.

## Format

Every task uses the required unchecked checklist form, sequential ID, optional parallel marker, user-story label only in story phases, and exact file path(s).

## Phase 1: Setup

**Purpose**: Confirm the approved scope and existing workspace baseline; no new project initialization or dependencies are needed.

- [x] T001 Confirm the approved scope, branch, source pin, and existing unit baseline in specs/992-studio-authoring-action-copy/spec.md, specs/992-studio-authoring-action-copy/plan.md, specs/992-studio-authoring-action-copy/quickstart.md, and /Users/sipke/Documents/Codex/2026-10-19/studio-ux-audit/execution/3072-unit-baseline.log; keep the recorded 2124 tests across 167 files separate from future added-test counts.

## Phase 2: Foundational Baselines and RED Regressions

**Purpose**: Establish real-component browser geometry and all new copy/behavior regressions before any production source edit.

- [x] T002 [P] Root: extend the real-component browser fixture and capture the pre-change action-position baseline for the same workflow and Activity Definition at 320, 768, 1024, and 1440 pixels in light and dark themes, including conditional TestRunStatus and Activity publication status; record results from Studio/tests/browser/src.tsx, Studio/tests/browser/workflow-toolbar.spec.ts, and Studio/tests/browser/activity-definitions.spec.ts without editing production source.
- [x] T003 [P] Add workflow Test Run component regressions in Studio/src/essentials/Elsa.Studio.Workflows/Client/src/__tests__/workflowEditorToolbar.test.tsx, Studio/src/essentials/Elsa.Studio.Workflows/Client/src/__tests__/workflowTestDrive.test.tsx, and Studio/src/essentials/Elsa.Studio.Workflows/Client/src/__tests__/lazyTestDriveAnnouncement.test.tsx for toolbar name, loading copy, generic “Start Test Run” fallback, module-supplied submit `startLabel` and trigger/input labels and associations, warning visibility, full result status, and direct-details callback.
- [x] T004 [P] Extend activity action coverage in Studio/src/essentials/Elsa.Studio.Workflows/Client/src/__tests__/activityDefinitions.test.tsx for approved labels and exact draft/version destinations; prove that a complete confirmed list with exactly one active editable draft takes precedence even without a current head or create permission, and that creation requires a complete confirmed zero-active list plus authoring permission and either an explicitly confirmed no-head starting point or an exact confirmed cloneable head. Preserve the chooser for incomplete/ambiguous state, including an existing but unreadable or unconfirmed head, and retain denied-permission and exact-ID assertions.
- [x] T005 [P] Add publication-entry and save-promise regressions in Studio/src/essentials/Elsa.Studio.Workflows/Client/src/__tests__/publishActionButton.test.tsx, Studio/src/essentials/Elsa.Studio.Workflows/Client/src/__tests__/activityDefinitionAuthoring.test.tsx, and Studio/src/essentials/Elsa.Studio.Workflows/Client/src/__tests__/activityDefinitionPublicationReview.test.tsx for “Review & publish”, remembered workflow Publish mode, “Continue publication review”, “Save now” immediate-flush promise behavior, and truthful pending/failed status.
- [x] T006 Run the targeted component suite from Studio using the commands in specs/992-studio-authoring-action-copy/quickstart.md; capture the expected copy-only RED results and verify existing exact IDs, authorization precedence, callback/request behavior, module labels, warning, and publication mode remain intact.
- [x] T007 Root review gate: inspect the real-component geometry baseline and T006 RED output for Studio/tests/browser/workflow-toolbar.spec.ts, Studio/tests/browser/activity-definitions.spec.ts, and the listed Workflows component tests; explicitly approve the baseline and preservation evidence before any production source change.

**Checkpoint**: All new unit/copy assertions and real-component geometry baselines exist; root has reviewed expected RED and preserved behavior.

## Phase 3: User Story 1 — Recognize and Safely Submit a Workflow Test (P1)

**Goal**: Make the transient workflow test action and setup copy identify Test Run while preserving submission inputs, labels, warning, result text, and details access.

**Independent Test**: Pass the targeted WorkflowEditorToolbar, WorkflowTestDrivePanel, and lazy-loading component tests; the root-owned browser fixture verifies the actual status component and direct-details callback.

- [x] T008 [US1] Update the transient workflow action, setup/loading copy, and generic submission fallback in Studio/src/essentials/Elsa.Studio.Workflows/Client/src/workflow-editor/WorkflowEditorToolbar.tsx, Studio/src/essentials/Elsa.Studio.Workflows/Client/src/workflow-editor/WorkflowTestDrivePanel.tsx, and Studio/src/essentials/Elsa.Studio.Workflows/Client/src/workflow-editor/lazyTestDriveUi.tsx; preserve supplied startLabel, trigger/input labels and associations, connected-environment/side-effects warning, and all submit callback arguments.
- [x] T009 [US1] Align the existing TestRunStatus outcome prefix with “Test Run” in Studio/src/essentials/Elsa.Studio.Workflows/Client/src/workflow-editor/editorPanels.tsx while retaining complete accepted/rejected/runtime-issue text, stale-draft cue, and the existing onOpenDetails callback into Runtime details.

## Phase 4: User Story 2 — Choose the Truthful Activity Draft Action (P1)

**Goal**: Present the approved draft-action labels without changing eligibility, permissions, selected records, or destinations.

**Independent Test**: Pass Studio/src/essentials/Elsa.Studio.Workflows/Client/src/__tests__/activityDefinitions.test.tsx with exact draftId/sourceVersionId assertions for direct, create, and chooser branches.

- [x] T010 [US2] Update only the primary action copy in Studio/src/essentials/Elsa.Studio.Workflows/Client/src/ActivityDefinitionWorkbench.tsx for a single editable draft, confirmed cloneable head, confirmed explicit no-head starting point, and fallback chooser; preserve branch precedence, permission checks, exact IDs, requests, and navigation.

## Phase 5: User Story 3 — Understand Save and Publication Status from Action Copy (P2)

**Goal**: Align only equivalent review-before-publication entry wording while preserving distinct workflow Publish mode and existing save/publication promises.

**Independent Test**: Pass Studio/src/essentials/Elsa.Studio.Workflows/Client/src/__tests__/publishActionButton.test.tsx, Studio/src/essentials/Elsa.Studio.Workflows/Client/src/__tests__/activityDefinitionAuthoring.test.tsx, and Studio/src/essentials/Elsa.Studio.Workflows/Client/src/__tests__/activityDefinitionPublicationReview.test.tsx.

- [x] T011 [US3] Change the existing workflow review-mode primary and menu labels to “Review & publish” in Studio/src/essentials/Elsa.Studio.Workflows/Client/src/workflow-editor/PublishActionButton.tsx; preserve the separate “Publish” label, remembered choice, mode callback, Activity entry, “Continue publication review”, and “Save now” immediate flush behavior.

## Phase 6: Polish and Cross-Cutting Verification

**Purpose**: Apply only a measured layout repair if needed, then complete root-owned browser, workspace, repository, and delivery gates.

- [x] T012 Root: apply the smallest approved presentation adjustment in Studio/src/essentials/Elsa.Studio.Workflows/Client/src/workflow-editor/WorkflowEditorToolbar.tsx, Studio/src/essentials/Elsa.Studio.Workflows/Client/src/workflow-editor/WorkflowEditorToolbar.css and/or Studio/src/essentials/Elsa.Studio.Workflows/Client/src/activityDefinitionAuthoring.css only if the approved pre-change baseline or post-copy measurement fails the 1 CSS pixel criterion; otherwise leave styles unchanged and record no repair was needed.
- [x] T013 Root: verify the finished real-component browser matrix with CI=1 and one Playwright worker using Studio/tests/browser/workflow-toolbar.spec.ts and Studio/tests/browser/activity-definitions.spec.ts; cover 320/768/1024/1440 widths, light/dark themes, status transitions, accessible names, keyboard access, full Test Run details, exact action behavior, visibility, overflow, and same-definition geometry. This fixture evidence does not establish composed Workbench acceptance for #3075 or backend/deployment readiness.
- [x] T014 Root: run the final canonical workspace typecheck and lint, affected Workflows client typecheck/build and full unit suite, targeted Playwright suite, locked architecture restore and complete architecture test project, and dependency-map check using the exact commands in specs/992-studio-authoring-action-copy/quickstart.md; report original baseline and added tests separately.
- [x] T015 After implementation and required local gates pass, set the Status line in specs/992-studio-authoring-action-copy/spec.md to Implemented and remove specs/992-studio-authoring-action-copy/checklists/requirements.md in the same implementation PR before final reviews, as required by docs/reference/spec-lifecycle.md.
- [ ] T016 Root: obtain three independent non-author review axes and qualifying hosted checks on the exact reviewed head for the implementation diff containing specs/992-studio-authoring-action-copy/spec.md and its Workflows changes; merge only through the approved GitHub flow, then verify resulting-main CI, architecture/maps, and required result-main evidence.

## Dependencies and Execution Order

- T001 establishes the approved starting context.
- After T001, T002–T005 may proceed in parallel because the browser fixture and distinct component-test files are independent and all are pre-production.
- T006 depends on T003–T005 and runs after the test assertions exist; T007 depends on T002 and T006 and is a blocking root approval gate.
- T008–T011 are production changes and remain blocked until T007. Implement them sequentially in priority order because these actions share toolbar/status surfaces and one active worker owns the feature.
- T012 depends on approved measurements and the copy implementation; skip the CSS edit if measurements already pass.
- T013 depends on all applicable implementation tasks. T014 follows the browser result and covers final local/repository gates. T015 follows successful implementation and local gates but must precede final reviews. T016 follows T015.

## User Story Dependencies and Independent Tests

| Story | Dependency | Independent test |
|---|---|---|
| US1 — Test Run (P1) | T007 root gate; implemented first | Workflow toolbar, setup, loading and actual status/detail callback component tests; browser geometry for the same workflow at fixed widths/themes. |
| US2 — Activity draft action (P1) | T007 root gate; follows US1 in this serial implementation | Activity action component tests verify exact branch, draft/version IDs, authorization and chooser behavior. |
| US3 — Save/publication wording (P2) | T007 root gate; follows US2 in this serial implementation | Workflow remembered-mode tests and Activity publication/save tests preserve exact subsequent flow and truthful completion status. |

The story semantics do not introduce data or runtime dependencies. The execution order is serial to preserve the root approval gate and avoid overlapping edits to shared toolbar surfaces.

## Parallel Opportunities

- Before implementation only: T002 can run in parallel with T003, T004, and T005. The root owns browser-baseline capture; the implementation worker owns the separate unit assertions.
- T003, T004, and T005 touch different unit-test files and can be authored in parallel if staffing permits. The current feature owner may instead complete them sequentially.
- No production tasks are marked parallel: they touch shared UI semantics and must wait for T007.
- Root-owned final browser and repository checks are ordered after implementation; architecture/build checks remain serial on the shared machine.

## Parallel Example: Pre-Implementation Baselines

- Root runs T002 using the real TestRunStatus and Activity publication toolbar status.
- Worker adds the US1 regression cases in T003, the US2 exact-state cases in T004, and US3 wording/promise cases in T005.
- Both tracks stop before production edits until T006 captures the RED/preservation results and root explicitly approves T007.

## Implementation Strategy

### MVP

Deliver US1 as the first independently validated checkpoint after the baseline and root approval gates. The implementation PR remains incomplete until US2, US3, cross-cutting geometry, and final repository/review gates pass.

### Incremental Delivery

1. Establish the real-component browser baseline and all unit RED cases.
2. Obtain explicit root approval of the captured baseline and preserved behavior.
3. Implement and validate US1, then US2, then US3.
4. Apply a CSS repair only if the measured geometry requires it and root approves the bounded change.
5. Complete root-owned browser, full local, architecture/map, spec lifecycle, exact-head review, and resulting-main gates.


## R1 review corrections

- [x] R1-01 Add and run rendered RED regressions for an omitted head retaining the drafts chooser and for both Test Run result/control loading announcements in the existing activityDefinitions.test.tsx and lazyTestDriveAnnouncement.test.tsx fixtures.
- [x] R1-02 Apply the bounded explicit-null guard and two loading-copy corrections, correct quickstart status, and verify the affected tests and root-owned qualification.

Delivery continues under T016: reply to each adopted finding with the pushed fix SHA and obtain fresh independent correctness, standards and acceptance reviews against that exact head. These GitHub delivery gates remain pending until their public receipts exist.
