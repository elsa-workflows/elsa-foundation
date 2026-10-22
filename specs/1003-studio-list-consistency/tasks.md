# Tasks: Studio Definition List Consistency

**Input**: Design documents from `specs/1003-studio-list-consistency/`

**Prerequisites**: [spec.md](spec.md), [plan.md](plan.md), [research.md](research.md), [data-model.md](data-model.md), [UI context and identity contract](contracts/ui-context-and-identity.md), [quickstart.md](quickstart.md), and [requirements checklist](checklists/requirements.md).

**Test policy**: The planning phase created documentation only. The approved implementation fixes Activity return context first, then adds exact identity behavior, then visual alignment. Record observed baseline results; a missing new disclosure is a feature gap, not proof of a pre-existing row-event bug. Prove nested-action test sensitivity with guard-removal mutations, restore each guard, and report the real result. Unit tests with mocked history and route-mocked Chromium history tests are supplemental; root owns normal-host acceptance.

**Execution model**: One implementation writer owns the full list change after root reviews and approves the package. Source starts from pinned frozen Home head `58a36c93f1395a0479868af19754aac2043be818`. Root confirmed no failed required base gate or conflicting ownership before source work. Home PR #3250 merged; its PR-level Core, Studio, and GitGuardian checks passed. Resulting-main Core and Studio checks were queued at the latest root update and remain a root-owned integration prerequisite, not a waiver. Root owns integration, final review, delivery gates, and GitHub state. Completed component work is recorded below; host acceptance and final gates remain pending.

## Phase 1: Setup and approval

**Purpose**: Refresh the selected issue and establish the authorized source boundary.

- [x] T001 Root's fresh issue, prerequisite, ownership, open-PR, and base checks confirmed the approved pinned head and no failed required base gate or conflicting claim. Home PR #3250 merged frozen source `58a36c9` to `main`; its PR Core, Studio, and GitGuardian checks passed. Resulting-main Core and Studio checks were queued at the latest root update; root integrates qualified resulting main before final PR qualification or merge.
- [x] T002 Root approved this package in [issue comment 6343367959](https://github.com/elsa-workflows/elsa-foundation/issues/3070#issuecomment-6343367959); the spec is now `Approved` before source implementation.
- [x] T003 At the approved source base, refresh the relevant consumer/API contracts in `ActivityDefinitionsPage.tsx`, `workflow-editor/WorkflowDefinitions.tsx`, `workflow-editor/pages.tsx`, and `workflow-editor/WorkflowInstances.tsx`; confirm existing public controls fit without a new dependency or API.

## Phase 2: User Story 3 — Return to the Activity Definition collection (Priority: P1)

**Goal**: Restore the specific Activity Definition collection view that opened the workbench/editor, including when there is more than one collection history entry. Implement this known functional defect before new identity behavior or visual alignment.

**Independent Test**: On the approved source base, open an Activity Definition from a collection with search, provider, authority, page size, and continuation state. Test editor Back and browser Back, then repeat from a second distinct history entry. Record the actual base result. After implementation, both paths restore the matching context; stale cursors recover using the existing contract.

### Tests for User Story 3

- [x] T004 [US3] Add focused Activity return-context unit tests in `activityDefinitions.test.tsx` and `activityDefinitionAuthoring.test.tsx` for collection state, multiple origins, browser-popstate restoration, and dirty-editor veto; assert entered and settled filters, authority, page size, and the request cursor. The original selected-base regression was observed RED in the original tool result; no raw log was captured then. Unit tests mock history operations. The supplemental route-mocked Chromium regression now exercises real browser Back/Forward across two origin entries and checks the resulting cursor requests; normal-host acceptance remains T017.
- [x] T005 [US3] Cover expired/invalid continuation recovery, auth-session/base-URL changes, denied/unavailable redaction, failed-refresh retention, and new-entry cache redaction in `activityDefinitions.test.tsx`; use the existing local redaction seam without claiming a global cache defect. The specific new-entry stale-row RED and final green are retained at `/tmp/3070-us3-new-entry-auth-red.txt` and `/tmp/3070-us3-focused-context-final.txt` on the local host.

### Implementation for User Story 3

- [x] T006 [US3] Preserve Activity Definition collection context per browser history entry through workbench/editor navigation in `ActivityDefinitionsPage.tsx`; preserve entered/settled search and provider, authority, page size, and scoped opaque cursor state without inventing a cursor URL. Reuse only the local auth/base-URL invalidation pattern; the accepted workflow session and global auth/cache remain unchanged.
- [x] T007 [US3] Verify the accepted workflow browse session remains unchanged with unit suites `workflowDefinitionBrowseLocation.test.ts`, `workflowDefinitionSearch.test.tsx`, `workflowDefinitionBrowseBoundaries.test.tsx`, `workflowDefinitionRoute.test.tsx`, `workflowDefinitionStatus.test.tsx`, and `workflowInstances.test.tsx`; all 54 tests passed on the final candidate. A supplemental route-mocked Chromium fixture exercises real browser Back/Forward for Activity; normal-host acceptance remains root-owned in T017. Do not reimplement #3068 inside Activity return work.

## Phase 3: User Story 2 — Find and copy exact definition identities (Priority: P1)

**Goal**: Keep all three lists compact by default while retaining keyboard-accessible, exact technical identifiers; nested identity controls must not navigate their row.

**Independent Test**: Use rows with repeated names and long IDs. Navigate, expand, collapse, and copy each identifier by keyboard; verify exact clipboard values and that pointer, Enter, or Space on a nested control never activates the row.

### Tests for User Story 2

- [x] T008 [US2] Add list identity behavior tests in `activityDefinitions.test.tsx`, the new `workflowDefinitions.test.tsx`, and `workflowRunHistory.test.tsx` for visible primary IDs, expanded secondary IDs, exact clipboard values, and nested pointer/Enter/Space actions including an SVG path. Preserve stale Workflow Definition results as non-actionable, verified by `workflowDefinitionSearch.test.tsx`. Missing disclosures on the old base were treated as a feature gap.

### Implementation for User Story 2

- [x] T009 [US2] Use existing public `CopyableIdentifier` and disclosure controls in the Activity Definitions, Workflow Definitions, and Runs lists to keep names and primary identity/status/timing visible and reveal exact secondary IDs on demand.
- [x] T010 [US2] Guard Activity Definition and Workflow Definition row pointer activation and Activity Definition Enter/Space handling against nested disclosure and copy controls; preserve workflow menu/selection, stale-result, and Runs row behavior.
- [x] T011 [US2] Prove nested-action test sensitivity with temporary guard-removal mutations on all three list rows. Each SVG-copy/disclosure interaction test failed on unintended row navigation; the guards were restored and the green tests are retained at `/tmp/3070-us2-activity-guard-mutation-red.txt`, `/tmp/3070-us2-workflow-definitions-guard-mutation-red.txt`, `/tmp/3070-us2-runs-guard-mutation-red.txt`, and `/tmp/3070-us2-identifiers-final.txt`.

## Phase 4: User Story 1 — Scan workflow and activity definitions consistently (Priority: P1)

**Goal**: Give Workflow Definitions and Activity Definitions a predictable hierarchy, align the scoped Runs row action/identity treatment, and retain all domain-specific behavior.

**Independent Test**: Compare the two definition collections in populated, empty, long-name, and failed-read states. Verify heading/action/search/filter hierarchy, semantic tables, keyboard actions, and distinct empty/error/denied/unavailable results.

### Tests and behavior protection for User Story 1

- [x] T012 [US1] Run relevant Activity, Workflow Definition lifecycle/search/route, and Runs action/permission suites after changes. The affected four-file suite passed 133 tests and the six browse/lifecycle/Runs suites passed 54 tests after the shared identity-control refactor. Final-candidate logs: `/tmp/3070-affected-after-shared-identity.txt` and `/tmp/3070-workflow-browse-lifecycle-tests-final-candidate.txt`. The two supplemental real-browser fixture tests passed at `/tmp/3070-activity-browser-history-identities-final.txt`.

### Implementation for User Story 1

- [x] T013 [US1] Align Activity page inset, heading/action order, search/filter density, table spacing, and empty/error treatment with the existing Workflows page frame and definitions toolbar. The proven `WorkflowsPageFrame` consumers remain unchanged.
- [x] T014 [US1] Reuse existing themed header and row-action treatment; copy/disclosure use public Studio UI controls. Preserve menu, confirmation, permission, focus, disabled-state, lifecycle, and Activity domain behavior without adding a button variant.
- [x] T015 [US1] Record the three-list hierarchy, progressive identity disclosure, action hierarchy, semantic table expectations, Activity return-context rule, and domain exceptions in `Studio/docs/ux/design-spec.md`; the new behavior remains a target until it reaches `main`.

## Phase 5: Integrated acceptance and root-owned delivery

- [ ] T016 Build the real source-built `Essentials/apps/Elsa.Workbench` with the normal `Studio.Web` shell and backing APIs. Seed authorized multi-page Activity and Workflow Definitions with distinct activity authorities, draft/published states, folders/unfiled rows, repeated/long names, and long IDs. Record exact source SHA, build/asset provenance, authorization/data setup, and cleanup evidence.
- [ ] T017 Exercise a bounded normal-host matrix: all three lists at 1280px/light and 1440px/dark with representative populated/empty/error/denied/unavailable/snapshot cases; at 390px verify long identities, keyboard operation, relevant row actions, and selected recovery/return states. Capture side-by-side screenshots for root review. Confirm both Activity return paths across two history entries and exact keyboard copy. Route-mocked fixtures remain supplemental.
- [ ] T018 Remove seeded rows and temporary host data/processes. Run complete affected Studio/Workflows client suites, relevant builds and CSS/layout checks, applicable e2e, architecture guard, and generated-map check on the integrated candidate; bind every result to the exact head and leave unavailable gates explicitly pending.
- [ ] T019 Root integrates qualified resulting main before final PR qualification/merge, reviews the complete diff against #3070, records issue/PR state transitions and exact-head evidence, owns PR publication/reviews/CI plus resulting-main CI/Maps, and sets terminal `Implemented` status in the implementation PR before merge.

## Dependencies and execution order

### Phase dependencies

- Setup and root approval (T001–T003) block all source work. Pending Home CI is not waived; root decides when to start from the pinned source head and integrates qualified resulting main before final PR qualification/merge.
- Fix and verify the existing Activity return-context behavior first (T004–T007). The targeted test result on the approved base is recorded as observed; no red result is assumed.
- Add exact identity and nested-control behavior next (T008–T011). Missing controls on the old base are treated as a new-feature gap. The nested-action test is validated with a temporary guard-removal mutation, then restored.
- Finish page/action presentation last (T012–T015), then complete normal-host acceptance and root-owned gates (T016–T019).

### User story dependencies

- **US3 (P1)** is implemented first because it fixes the known collection-return defect.
- **US2 (P1)** follows, adding compact exact identity and nested-control behavior.
- **US1 (P1)** completes the visual and action hierarchy after the functional behavior is sound.
- All stories are delivered in one scoped #3070 implementation PR.

### Parallel opportunities

There are no source-task parallel opportunities: the consumers share row presentation and the Activity page owns both row and history behavior. One writer owns the stack. Root may do a read-only review when the integrated diff is ready.

### Requirement and success-criteria coverage

- FR-001, FR-003, FR-009, FR-010, FR-011 → T012–T017.
- FR-002, FR-004 → T007, T012–T014, T017.
- FR-005, FR-006 → T008–T011, T017.
- FR-007, FR-008 → T004–T007, T017.
- FR-012 → T015.
- SC-001, SC-004 → T013, T016–T017.
- SC-002 → T008–T011, T017.
- SC-003 → T004–T007, T017.
- SC-005 → T007, T012, T018.

## Implementation strategy

1. Root approved the package before implementation. Start from the pinned frozen source head and preserve its Home changes. Root integrates qualified resulting main before final PR qualification or merge.
2. Fix the existing Activity return-context defect first. Add and run tests against the approved base, record the real baseline, and prove correct editor/browser Back behavior across multiple entries and invalid-cursor recovery.
3. Add exact identity disclosure and keyboard copy next. Distinguish feature-gap tests from existing defect regressions, then prove nested-action guard sensitivity with a temporary mutation and restore it.
4. Complete visual hierarchy and themed row actions, update the Studio design specification, and preserve each domain contract.
5. Perform the bounded normal-host acceptance and cleanup. Root integrates qualified resulting main before final PR qualification/merge and owns the final exact-head gates.

The component work through T015 is complete locally and covered by the recorded client tests, package typecheck (`/tmp/3070-final-typecheck.txt`), changed-file ESLint (`/tmp/3070-final-eslint.txt`), and changed-CSS stylelint (`/tmp/3070-final-stylelint.txt`). Route-mocked Chromium coverage supplements, but does not replace, normal-host acceptance. T016–T019 remain pending for root-owned normal-host acceptance, resulting-main integration, final review, exact-head CI, applicable e2e/architecture/maps gates, PR state, and delivery. No normal-host acceptance, resulting-main gate, or final implementation status is claimed here.
