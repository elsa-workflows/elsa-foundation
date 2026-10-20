# Tasks: Stable workflow search

**Input**: Approved spec and planning artifacts in `specs/256-stable-workflow-search/`.

**Prerequisites**: `plan.md`, `spec.md`, `research.md`, `data-model.md`, `contracts/search-refresh-ui.md`, and `quickstart.md`.

**Test policy and evidence**: This is an existing Studio client module, not greenfield feature registration. The derived constitution does not declare a general TypeScript TDD cadence. Framework §2.23.2 still requires behavior tests for the logic-bearing request, access, and navigation decisions. Foundation #3068 explicitly requires RED/GREEN evidence, so the new story regressions below are to be added and run before their corresponding behavior implementation, then rerun afterward. The existing Workflows Client baseline recorded 137 passing tests in `/Users/sipke/Documents/Codex/2026-10-19/studio-ux-audit/execution/3068-baseline-module.log`; every new regression listed here is currently **UNRUN**, and no new failure is claimed before it is observed. There is no new Studio feature registration wiring, so no registration test is added.

**Scope boundary**: Keep implementation inside the existing Workflows Client and its Studio browser fixture. Do not add endpoint/DTO/auth contracts, permissions, token-derived keys, page/cursor URL state, cursor replay, new dependencies, or changes to other workflow/activity/run/inventory/connection lists.

## Phase 1: Setup

**Purpose**: Confirm the existing package/test entry points and retain the observed baseline before adding behavior regressions.

- [x] T001 Confirm the existing Workflows Client scripts in `Studio/src/essentials/Elsa.Studio.Workflows/Client/package.json`, use the existing pnpm workspace, and retain the 137-test baseline reference in this task record before changing the list.

## Phase 2: Foundational Test-Harness Preparation

**Purpose**: Prepare a real-route test harness while leaving production source unchanged, so every new search/status/route regression can be run against the same baseline.

- [x] T002 Prepare a browser-only fixture path in `Studio/tests/browser/src.tsx` that composes the existing `WorkflowManagementPage` and makes its current page/search/cursor requests observable to `Studio/tests/browser/workflow-definition-browse-context.spec.ts`; inspect the direct mounts at `src.tsx:978,1126` without changing production files.

**Checkpoint**: Test fixture preparation is complete; production files remain at the unchanged baseline while all new regressions are added and run.

## Phase 3: User Story 1 - Search without losing useful results (Priority: P1)

**Goal**: Keep search input/address immediate, retain clearly identified previous successful rows while eligible, and allow only the latest entered search to become current.

**Independent Test**: With a successful result visible, make rapid search edits and resolve requests out of order. Verify immediate input/address changes, one settled request, immediate supersession before debounce, first-load waiting when no prior success exists, and an old search/page label on eligible retained rows without current-result controls.

### Tests for User Story 1

- [x] T003 [US1] Add rapid-edit, first-load, supersession-before-debounce, and later-page prior-result cases (including old search/page labeling and hidden current-view controls) in `Studio/src/essentials/Elsa.Studio.Workflows/Client/src/__tests__/workflowDefinitionSearch.test.tsx`; run the focused Vitest file against the baseline and record the actual outcome before implementation.

**Checkpoint**: Search regression tests have been run against unchanged production; no search behavior implementation begins until all story baselines are recorded.

## Phase 4: User Story 2 - Understand refresh, no matches, and failure (Priority: P1)

**Goal**: Distinguish current refresh, successful no matches, failed search, retry, and observed access-context invalidation.

**Independent Test**: Exercise same-view refresh, empty success, current failure/retry, a failed search with eligible previous rows, and endpoint/session/401/403 invalidation. Verify status describes the current view and protected state is cleared at the reported boundary.

### Tests for User Story 2

- [x] T004 [US2] Add same-view refresh, empty-versus-failure, current-search retry, and previous-rows-after-failure behavior tests in `Studio/src/essentials/Elsa.Studio.Workflows/Client/src/__tests__/workflowDefinitionStatus.test.tsx`; run against the baseline and record actual results before implementation.
- [x] T005 [US2] Add baseline route tests using the existing exported `WorkflowManagementPage` in `Studio/src/essentials/Elsa.Studio.Workflows/Client/src/__tests__/workflowDefinitionRoute.test.tsx`; cover endpoint change and public auth-session start/end while the editor is open, plus a current typed 401/403 result, and run them against unchanged production without importing a not-yet-created helper.

**Checkpoint**: Status and access-boundary regressions have recorded actual baseline outcomes against unchanged production. Expected failures are not implementation evidence.

## Phase 5: User Story 3 - Return to the same workflow browse page (Priority: P2)

**Goal**: Preserve the selected page and its observed cursor across same-visit editor and browser Back, while filter changes and full remounts follow the approved defaults.

**Independent Test**: Browse a later page with search, folder, lifecycle, and non-default page size; open a definition and return using editor Back and browser Back. Verify the same page is requested with the real cursor, definition-only `popstate` does not rebase, browse-key changes reset as specified, unresolved folder capability does not erase a saved cursor, and a full remount uses defaults.

### Tests for User Story 3

- [x] T006 [US3] Add baseline route-state tests in `Studio/src/essentials/Elsa.Studio.Workflows/Client/src/__tests__/workflowDefinitionRoute.test.tsx` for definition-only/same-browse `popstate`, changed browse-key reset, explicit page navigation with an observed cursor, page-size/filter reset, and unresolved-then-resolved folder capability; exercise only currently existing modules and record already-green versus actually failing cases.
- [x] T007 [US3] Add a Playwright regression using the real `WorkflowManagementPage` in `Studio/tests/browser/workflow-definition-browse-context.spec.ts` and the prepared fixture in `Studio/tests/browser/src.tsx`; verify editor Back, browser Back, page/cursor restoration, synthetic definition-only `popstate` ordering, and full-remount defaults against unchanged production and record the observed baseline.

**Checkpoint**: Search, status, authority, route-state, and browser return regressions have all been run against unchanged production before any production file is edited. Record actual outcomes; existing-green cases are not reported as new failures.

## Phase 6: User Story 1 - Implement Stable Search (Priority: P1)

**Goal**: Introduce the route-owned controlled session needed by the list and deliver the P1 search behavior.

**Independent Test**: Rerun `workflowDefinitionSearch.test.tsx` after implementation; input/address remain immediate, requests settle once, and eligible prior rows stay labelled and non-actionable.

- [ ] T008 [US1] Create the private `useWorkflowDefinitionBrowseSession` and controlled list contract in `Studio/src/essentials/Elsa.Studio.Workflows/Client/src/workflow-editor/workflowDefinitionBrowseSession.ts`, `pages.tsx`, and `WorkflowDefinitions.tsx`; make the route the sole owner of browse key/page/page size/cursors/invalidation revision, then implement immediate search supersession, the 250 ms text-only delay, and labelled display-only prior results without old counts/pager/empty state/selection/actions; rerun `workflowDefinitionSearch.test.tsx` and record RED/GREEN.

## Phase 7: User Story 2 - Implement Truthful Status and Invalidation (Priority: P1)

**Goal**: Preserve truthful refresh/failure/empty status and clear protected state at existing reported authority boundaries.

**Independent Test**: Rerun `workflowDefinitionStatus.test.tsx` and `workflowDefinitionRoute.test.tsx`; current results/status match the latest view and endpoint/session/typed-denial changes clear protected state even while the editor replaces the list.

- [ ] T009 [US2] Implement same-view retained refresh, distinct current empty/failure/retry status, and stale labeling after failed search in `Studio/src/essentials/Elsa.Studio.Workflows/Client/src/workflow-editor/WorkflowDefinitions.tsx`; implement baseUrl/public auth-session/remount invalidation in `workflowDefinitionBrowseSession.ts` and `pages.tsx`, and typed `StudioHttpError.status` 401/403 clearing in the list callback; rerun both US2 test files and record RED/GREEN without token inspection, message parsing, or new auth APIs.

## Phase 8: User Story 3 - Implement Browse-Page Return (Priority: P2)

**Goal**: Keep explicit paging and same-route Back behavior consistent with the observed cursor and existing browse URL.

**Independent Test**: Rerun `workflowDefinitionRoute.test.tsx` and the real-route Playwright spec; explicit page navigation remains on the selected page with its actual cursor, Back restores the same visit, and remount starts at defaults.

- [ ] T010 [US3] Implement route-owned page/page-size/cursor transitions and `popstate` key comparison in `Studio/src/essentials/Elsa.Studio.Workflows/Client/src/workflow-editor/workflowDefinitionBrowseSession.ts` and `pages.tsx`; keep definition-only/same-browse transitions, rebase only changed search/folder/lifecycle keys, preserve explicit-page cursors, and defer effective-folder comparison until capability settles.
- [ ] T011 [US3] Adapt both direct `WorkflowDefinitions` browser fixtures at `Studio/tests/browser/src.tsx:978,1126` to the route-owned controlled session and keep the return scenario on the real `WorkflowManagementPage` in `Studio/tests/browser/workflow-definition-browse-context.spec.ts`.
- [ ] T012 [US3] Rerun `workflowDefinitionRoute.test.tsx` and `pnpm exec playwright test tests/browser/workflow-definition-browse-context.spec.ts --workers=1` after route integration; record RED/GREEN for editor Back, browser Back, synthetic-popstate-before-unmount, explicit cursor use, and full-remount defaults.

**Checkpoint**: Same-visit Back restores search, filters, size, page, and observed cursor; ordinary page navigation honors its selected page; changed browse keys and cold remounts follow the spec.

## Phase 9: Polish, Independent Review, and Delivery Gates

**Purpose**: Review the finished behavior from independent angles, run the affected and standing checks, and complete the spec lifecycle in the implementation unit.

- [ ] T013 [P] Complete an independent correctness/privacy review of `Studio/src/essentials/Elsa.Studio.Workflows/Client/src/workflow-editor/WorkflowDefinitions.tsx`, `Studio/src/essentials/Elsa.Studio.Workflows/Client/src/workflow-editor/workflowDefinitionBrowseSession.ts`, and `Studio/src/essentials/Elsa.Studio.Workflows/Client/src/workflow-editor/pages.tsx`, focusing on latest-request authority, stale-row visibility, typed denial handling, and reported-boundary invalidation; record actionable findings for resolution.
- [ ] T014 [P] Complete an independent repository-standards review of `Studio/src/essentials/Elsa.Studio.Workflows/Client/src/workflow-editor/WorkflowDefinitions.tsx`, `Studio/src/essentials/Elsa.Studio.Workflows/Client/src/workflow-editor/workflowDefinitionBrowseSession.ts`, and `Studio/tests/browser/workflow-definition-browse-context.spec.ts`, including React/state ownership, existing UI-token and accessibility patterns, test quality, and no new public SDK/auth surface.
- [ ] T015 [P] Complete an independent issue-acceptance review of `specs/256-stable-workflow-search/spec.md`, `specs/256-stable-workflow-search/contracts/search-refresh-ui.md`, `Studio/tests/browser/workflow-definition-browse-context.spec.ts`, and `Studio/tests/browser/src.tsx`, verifying all user stories, access boundaries, status distinctions, same-visit Back, explicit cursor paging, full-remount defaults, and unchanged unrelated lists.
- [ ] T016 Resolve accepted independent-review findings in `Studio/src/essentials/Elsa.Studio.Workflows/Client/src/workflow-editor/WorkflowDefinitions.tsx`, `workflowDefinitionBrowseSession.ts`, `pages.tsx`, and the affected `Studio/src/essentials/Elsa.Studio.Workflows/Client/src/__tests__/` and `Studio/tests/browser/` files; rerun the affected regressions before final gates.
- [ ] T017 Run `pnpm exec vitest run`, `pnpm typecheck`, and `pnpm build` from `Studio/src/essentials/Elsa.Studio.Workflows/Client` using `Studio/src/essentials/Elsa.Studio.Workflows/Client/package.json`; record exact results and keep the existing paged and legacy-unpaged list tests green.
- [ ] T018 Run `pnpm exec playwright test tests/browser/workflow-definition-browse-context.spec.ts --workers=1`, `pnpm typecheck`, `pnpm lint`, and `pnpm build` from `Studio` using `Studio/package.json`; record exact results for the real route scenario and Studio integration gates.
- [ ] T019 Run `dotnet test Essentials/tests/Architecture/Tests/Elsa.Architecture.Tests.csproj` and `dotnet run --project tools/maps/Elsa.Maps.Generator -- check` from the repository root; record exact results.
- [ ] T020 Before merge, refresh Foundation #3068 comments/ownership, open PRs and competing claims, all review and inline threads, required checks for the exact implementation head, and fresh-main CI/Maps health using `docs/agents/issue-tracker.md`; record the verified head and run IDs in the GitHub issue/PR comments and resolve any conflict or failed gate before proceeding.
- [ ] T021 In the implementation PR, set `**Status**: Implemented` in `specs/256-stable-workflow-search/spec.md` before merge; remove the actual terminal process file `specs/256-stable-workflow-search/checklists/requirements.md`, verify `plan.md` has no reference to that process checklist, regenerate maps with `dotnet run --project tools/maps/Elsa.Maps.Generator -- all`, and review `docs/maps/spec-status-map.md` plus every changed map including `docs/maps/manifest.json`.
- [ ] T022 After the implementation PR merges, verify fresh-main `CI` and `Maps` checks are green and record the merged source revision in the GitHub issue/PR comments using the delivery protocol in `docs/agents/issue-tracker.md`; this is a post-merge verification task, not merge authorization.

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)** confirms the existing package scripts and verified 137-test baseline; task generation does not claim a new test pass.
- **Foundational test-harness preparation (Phase 2)** depends on Setup and changes only the browser fixture, not production source.
- **Baseline story tests (Phases 3–5)** run against unchanged production before any implementation task begins.
- **US1 implementation (Phase 6)** follows all baseline tests and is the recommended MVP (P1).
- **US2 implementation (Phase 7)** follows US1 because failure labeling with retained previous rows depends on its snapshot behavior (P1).
- **US3 implementation (Phase 8)** follows the common session foundation created in US1 and the baseline tests to avoid conflicting edits to `WorkflowDefinitions.tsx` and route ownership (P2).
- **Polish (Phase 9)** depends on implemented stories. Independent reviews can run in parallel; findings must be resolved before the final local and hosted gates.

### User Story Dependencies

- **US1 (P1)**: Baseline in T003; implementation in T008 after all story baseline tests.
- **US2 (P1)**: Baselines in T004–T005; implementation in T009 depends on US1's last-success snapshot for the stale-row failure case.
- **US3 (P2)**: Baselines in T006–T007; implementation and route verification in T010–T012 use the shared route owner created after baselines.

### Parallel Opportunities

- The three independent review passes T013–T015 can run in parallel after story implementation because they examine separate concerns and do not edit files.
- Within story implementation, regression tests must be run before corresponding behavior changes because #3068 requires RED/GREEN evidence; do not parallelize an implementation ahead of its baseline test run.
- The single production list and route-owner files are shared across stories, so implementation work itself is sequential in this one work unit.

## Implementation Strategy

### MVP First (User Story 1)

Complete Setup and test-harness preparation, capture every story baseline against unchanged production, then implement and independently verify US1’s search debounce, immediate supersession, and eligible previous-result display. Treat this as an incremental checkpoint, not a release or merge authorization.

### Incremental Delivery

1. Prepare a real-route test fixture and run every new story regression against unchanged production.
2. Create the single controlled session as part of the first post-baseline implementation task and deliver US1 stable search behavior with RED/GREEN evidence.
3. Add US2 truthful refresh/failure/empty status and observed authority invalidation.
4. Add US3 explicit-page cursor handling and same-route editor/browser Back restoration.
5. Finish correctness/privacy, repository-standards, and issue-acceptance reviews; run affected package/Studio/architecture/map checks; audit exact-head CI and current claims/reviews; complete terminal spec/map bookkeeping; verify post-merge main checks.

## Notes

- Every task uses the required `- [ ] T### [P?] [Story?] ...` checklist form and names the exact implementation or test path.
- The task list specifies behavior tests because the UI logic, request races, access boundaries, and route cursor transitions are at risk; it does not invent a general Studio TypeScript TDD policy.
- Existing tests may already pass some baseline cases. Record actual results; do not call an expected failure RED until a test was run and failed.
- No tests, builds, browser scenarios, architecture checks, maps checks, or hosted gates are claimed as run by this task-generation phase.

## Baseline execution record

Root integrated and reviewed the test-only baseline at production source f43438ee5 (main61):15 focused unit cases,8 existing guards pass and7 behavior assertions fail;4 focused browser cases,2 pass and2 Back-restoration assertions fail. The additional older-response-during-delay case passes against the existing immediate-load generation guard; it is a preservation guard for the new debounce, not a newly reproduced defect. Existing module suite137passed after the shared harness change. Workflows typecheck passes after test-only readonly-context/timer callback corrections. No production edits or implementation acceptance are claimed by these test tasks. Exact commands and full raw logs are retained externally; public issue comments are the delivery record.
