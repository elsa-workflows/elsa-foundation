# Tasks: Studio Definition List Consistency

**Input**: Design documents from `specs/1003-studio-list-consistency/`

**Prerequisites**: [spec.md](spec.md), [plan.md](plan.md), [research.md](research.md), [data-model.md](data-model.md), [UI context and identity contract](contracts/ui-context-and-identity.md), [quickstart.md](quickstart.md), and [requirements checklist](checklists/requirements.md).

**Test policy**: This phase creates planning documents only; no tests are added or run here. Implementation begins with the existing Activity return-context defect, then adds exact identity behavior, then visual alignment. Record actual baseline results; a missing new disclosure is an expected feature gap, not proof of a pre-existing row-event bug. Prove nested-action test sensitivity with a temporary guard-removal mutation, restore the guard, and report the real result.

**Execution model**: One implementation writer owns the full list change after root reviews and approves this Draft package. The approved pinned source base is `58a36c93f1395a0479868af19754aac2043be818`, unless root chooses a newer base. Before source work, confirm there is no failed required base gate or conflicting ownership. Pending Home CI remains pending and is not waived. Root owns integration of qualified resulting main before final PR qualification/merge, final review, delivery gates, and GitHub state. All tasks below are future work and remain unchecked.

## Phase 1: Setup and approval

**Purpose**: Refresh the selected issue and establish the authorized source boundary.

- [x] T001 Root's fresh issue, prerequisite, ownership, open-PR, and base checks confirmed the approved pinned head and no failed required base gate or conflicting claim. Home Core and Chromium checks remain pending, not waived. Root integrates qualified resulting main before final PR qualification or merge.
- [x] T002 Root approved this package in [issue comment 6343367959](https://github.com/elsa-workflows/elsa-foundation/issues/3070#issuecomment-6343367959); the spec is now `Approved` before source implementation.
- [ ] T003 At the approved source base, refresh the relevant consumer/API contracts in `Studio/src/essentials/Elsa.Studio.Workflows/Client/src/ActivityDefinitionsPage.tsx`, `workflow-editor/WorkflowDefinitions.tsx`, `workflow-editor/pages.tsx`, and `workflow-editor/WorkflowInstances.tsx`; confirm current public controls still fit without a new dependency or API.

## Phase 2: User Story 3 — Return to the Activity Definition collection (Priority: P1)

**Goal**: Restore the specific Activity Definition collection view that opened the workbench/editor, including when there is more than one collection history entry. Implement this known functional defect before new identity behavior or visual alignment.

**Independent Test**: On the approved source base, open an Activity Definition from a collection with search, provider, authority, page size, and continuation state. Test editor Back and browser Back, then repeat from a second distinct history entry. Record the actual base result. After implementation, both paths restore the matching context; stale cursors recover using the existing contract.

### Tests for User Story 3

- [ ] T004 [US3] Add focused Activity return-context tests in `Studio/src/essentials/Elsa.Studio.Workflows/Client/src/__tests__/activityDefinitions.test.tsx` and `Studio/tests/browser/activity-definitions.spec.ts` for editor Back, browser Back, two distinct origins, direct-entry fallback, and reload; assert entered and settled search/provider values, authority, page size, and valid cursor state. Run against the approved source base and record whether the actual behavior fails; do not assume a red result.
- [ ] T005 [US3] Cover expired/invalid continuation recovery, auth-session/base-URL changes, denied/unavailable redaction, and failed-refresh retention in `activityDefinitions.test.tsx`; use the existing local redaction seam to verify restored history cannot recommit previously displayed rows, without claiming a global cache defect.

### Implementation for User Story 3

- [ ] T006 [US3] Preserve Activity Definition collection context per browser history entry through workbench/editor navigation in `Studio/src/essentials/Elsa.Studio.Workflows/Client/src/ActivityDefinitionsPage.tsx`; preserve entered/settled search and provider, authority, page size, and scoped opaque cursor state without inventing a cursor URL. Use the existing `workflowDefinitionBrowseSession.ts` auth/base-URL invalidation pattern only as a local design reference; do not alter it or global query caching/authentication.
- [ ] T007 [US3] Verify the accepted workflow browse session remains unchanged with `workflowDefinitionBrowseLocation.test.ts`, `workflowDefinitionSearch.test.tsx`, `workflowDefinitionBrowseBoundaries.test.tsx`, `workflowDefinitionRoute.test.tsx`, and `Studio/tests/browser/workflow-definition-browse-context.spec.ts`; do not reimplement #3068 inside Activity return work.

## Phase 3: User Story 2 — Find and copy exact definition identities (Priority: P1)

**Goal**: Keep all three lists compact by default while retaining keyboard-accessible, exact technical identifiers; nested identity controls must not navigate their row.

**Independent Test**: Use rows with repeated names and long IDs. Navigate, expand, collapse, and copy each identifier by keyboard; verify exact clipboard values and that pointer, Enter, or Space on a nested control never activates the row.

### Tests for User Story 2

- [ ] T008 [US2] Add identity behavior tests in `Studio/src/essentials/Elsa.Studio.Workflows/Client/src/__tests__/activityDefinitions.test.tsx`, the new `workflowDefinitions.test.tsx`, `workflowDefinitionStatus.test.tsx`, `workflowInstances.test.tsx`, and `workflowRunHistory.test.tsx` for compact/default versus expanded details, exact keyboard copy, and nested pointer/Enter/Space actions. Record that absent controls on the old base are a new-feature gap, not evidence of an existing row-handler defect.

### Implementation for User Story 2

- [ ] T009 [US2] Use existing public `CopyableIdentifier` and disclosure controls in `Studio/src/essentials/Elsa.Studio.Workflows/Client/src/ActivityDefinitionsPage.tsx`, `workflow-editor/WorkflowDefinitions.tsx`, and `workflow-editor/WorkflowInstances.tsx` to keep names and primary identity/status/timing visible and reveal exact secondary IDs on demand.
- [ ] T010 [US2] Guard Activity Definition row click and Enter/Space handling against nested disclosure and copy controls in `ActivityDefinitionsPage.tsx`; retain workflow menu/selection and Runs row semantics.
- [ ] T011 [US2] Prove the nested-action test is meaningful by temporarily removing the row guard, run the focused test and capture its failing result, restore the guard, and rerun green. Do not leave the mutation in the source diff.

## Phase 4: User Story 1 — Scan workflow and activity definitions consistently (Priority: P1)

**Goal**: Give Workflow Definitions and Activity Definitions a predictable hierarchy, align the scoped Runs row action/identity treatment, and retain all domain-specific behavior.

**Independent Test**: Compare the two definition collections in populated, empty, long-name, and failed-read states. Verify heading/action/search/filter hierarchy, semantic tables, keyboard actions, and distinct empty/error/denied/unavailable results.

### Tests and behavior protection for User Story 1

- [ ] T012 [US1] Run and retain the relevant existing Activity, Workflow Definition lifecycle/search/route, and Runs action/permission suites at the approved base and after changes; update tests only where a meaningful changed behavior requires it.

### Implementation for User Story 1

- [ ] T013 [US1] Align page inset, heading/action order, search/filter placement, table density, and truthful empty/error treatment in `workflow-editor/pages.tsx`, `workflow-editor/WorkflowDefinitions.tsx`, `ActivityDefinitionsPage.tsx`, and `activityDefinitionCollection.css`; reuse the current internal frame only where its proven consumers fit.
- [ ] T014 [US1] Apply existing themed action controls to scoped row actions in the Activity Definitions, Workflow Definitions, and Runs consumers; preserve confirmations, permissions, focus, disabled states, destructive treatment, lifecycle behavior, and Activity domain boundaries without adding a button variant.
- [ ] T015 [US1] Record the delivered three-list hierarchy, progressive identity disclosure, action hierarchy, semantic table expectations, return-context rule, and domain exceptions in `Studio/docs/ux/design-spec.md`.

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

1. Keep the package Draft until root review. Check base gates and ownership; after approval, start from the pinned frozen source head unless root selects a newer base. Pending Home CI remains pending.
2. Fix the existing Activity return-context defect first. Add and run tests against the approved base, record the real baseline, and prove correct editor/browser Back behavior across multiple entries and invalid-cursor recovery.
3. Add exact identity disclosure and keyboard copy next. Distinguish feature-gap tests from existing defect regressions, then prove nested-action guard sensitivity with a temporary mutation and restore it.
4. Complete visual hierarchy and themed row actions, update the Studio design specification, and preserve each domain contract.
5. Perform the bounded normal-host acceptance and cleanup. Root integrates qualified resulting main before final PR qualification/merge and owns the final exact-head gates.

All tasks above are future work and remain unchecked. No implementation, runtime reproduction, test, build, or CI result is claimed by this planning package.
