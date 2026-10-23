# Tasks: Connection picker load-failure explanation

**Input**: Design documents in `specs/253-connection-picker-failure/`

**Prerequisites**: `plan.md`, `spec.md`, `research.md`, `data-model.md`, and `quickstart.md` are complete. No `contracts/` directory applies because no external contract changes.

**Tests**: Add a new direct React DOM/Vitest regression for `ConnectionPickerEditor`; the existing Connections client tests do not directly render the picker. Reuse and strengthen the existing Studio browser layout test for accessible failure guidance, keyboard recovery, and narrow/light/dark presentation. Issue #3102 requires failure-before/change-after evidence, so add and run the regression assertions before the UI change, then rerun them afterward. No registration or backend tests are in scope.

**Delivery State**: Root reviewed and approved on 2026-10-20. Tasks remain unchecked until their work and evidence are complete.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Tasks touch different files and have no incomplete prerequisites.
- **[US1]**: Task belongs to the single user story in `spec.md`.
- Each task names its exact file path.

## Path Conventions

- The Connections editor and focused Vitest test live under `Extensions/Connections/studio/Elsa.Studio.Connections/Client/`.
- The browser proof lives under `Studio/tests/browser/`.

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Reuse existing Studio and Connections test setup.

No setup changes are required; the current client Vitest package and Studio Playwright harness cover this work.

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: No shared foundation is needed for this single editor behavior.

No foundational changes are required.

## Phase 3: User Story 1 - Understand and recover from a Connection picker load failure (Priority: P1) 🎯 MVP

**Goal**: Explain a failed metadata load and let the author retry while preserving the stored reference and existing picker behavior.

**Independent Test**: Use a metadata-only fixture that rejects once and then succeeds. Verify an accessible generic explanation and Retry, the loading/recovery transition, a disabled selector during failure, and preservation of the current reference. Confirm browser keyboard use and narrow/desktop light/dark presentation.

### Tests for User Story 1

- [x] T001 [US1] Add a focused React DOM regression for rejection, generic accessible guidance, disabled selection, retry/loading, and recovery with a retained reference in `Extensions/Connections/studio/Elsa.Studio.Connections/Client/src/__tests__/connectionPickerEditor.test.tsx`; run it against the current editor and record the expected failure before implementation.
- [x] T002 [P] [US1] Extend the existing failure/retry browser proof for accessible explanation, keyboard recovery, narrow/desktop widths, and light/dark themes using its metadata-only fixture in `Studio/tests/browser/connection-syntax-layout.spec.ts`; confirm the new assertion fails before the UI change.

### Implementation for User Story 1

- [x] T003 [US1] Render a generic accessible load-failure explanation beside the existing Retry action in `Extensions/Connections/studio/Elsa.Studio.Connections/Client/src/ConnectionPickerEditor.tsx`, preserving the current request lifecycle, context and unmount guards, disabled selection, stored reference, and unavailable-reference behavior; do not add status-specific messaging or a new error format (depends on T001 and T002).

**Checkpoint**: The failing component and browser assertions pass after the UI change, and all three issue acceptance criteria are covered.

## Final Phase: Polish & Cross-Cutting Concerns

**Purpose**: Run focused and integration acceptance checks, then complete the documented delivery gates.

- [x] T004 Run the focused picker Vitest regression from `specs/253-connection-picker-failure/quickstart.md`; record its exact-head result and confirm the component-level acceptance behavior without claiming a permission-denied journey.
- [x] T005 Run the full Connections client Vitest suite and build with the package scripts in `Extensions/Connections/studio/Elsa.Studio.Connections/Client/package.json`; record exact-head results.
- [x] T006 Run canonical Studio workspace typecheck and lint with `pnpm --dir Studio typecheck` and `pnpm --dir Studio lint`; record exact-head results.
- [x] T007 Run the browser accessibility/layout proof in `Studio/tests/browser/connection-syntax-layout.spec.ts` using `pnpm --dir Studio exec playwright test tests/browser/connection-syntax-layout.spec.ts --workers=1`; verify accessible failure guidance, keyboard recovery, narrow and desktop widths, light and dark themes, and metadata-only fixtures.
- [x] T008 Run the complete architecture guard suite serially and reconcile discovered/executed/passed/failed/skipped test counts, then run the authoritative generated-maps check from `specs/253-connection-picker-failure/quickstart.md`; do not run a full-solution local build. If the spec inventory/status makes a map stale, deliberately refresh only the relevant maps layer, review generated findings, and explicitly stage each changed generated map path including `docs/maps/manifest.json` when changed.
- [x] T009 Review the complete diff for scope, DRY, and repository/constitution compliance; confirm focused and integration evidence is exact-head. Re-read issue #3102 comments and open PRs immediately before delivery to catch a competing claim.
- [ ] T010 Commit with the Elsa Workflows GitHub App identity, push the issue branch, open a PR containing `Fixes #3102`, comment `pr: <url>` on the issue, request optional review from `@elsa-workflows-bot review`, and attach the created PR to this task.
- [ ] T011 Obtain three independent reviews with isolated outputs, one on each axis (correctness, repository/constitution conformance, and spec/issue conformance), and include all relevant PR review threads and issue comments. Run a clean self-review loop on the PR diff, fixing findings in new commits and reviewing each new head, with no more than six rounds. Verify all required checks are green and no check has failed on the exact latest reviewed head; confirm no human hold, changes-requested review, open review thread, or `blocked`/`needs:decision` label exists and standing self-merge conditions hold. Code Review is optional; do not wait for an unavailable review. Before merging, add `self-merged` and post the required audit comment with rounds, findings fixed, remaining low notes, and reviewed SHA; merge under standing authorization and record the audit comment URL in the merge commit.
- [ ] T012 Verify resulting-main CI and generated-maps gates, reconcile the merged result with issue #3102 and its program/project status, then release the issue claim with the completion evidence.

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No setup work.
- **Foundational (Phase 2)**: No foundational work.
- **User Story 1 (Phase 3)**: T001 and T002 can proceed in parallel. T003 depends on both failing regression proofs. T004 through T008 depend on T003; T009 depends on all implementation and local validation tasks. T010 follows the ownership recheck in T009; T011 follows PR creation; T012 follows merge.
- **Final Phase**: T004 through T008 run after the UI and tests are integrated. T009 through T012 are ordered delivery gates.

### User Story Dependencies

- **User Story 1 (P1)**: Independent MVP; no other story or module change is required.

### Within User Story 1

- Add and run the component and browser regression assertions before changing the UI because #3102 requires failing-before/passing-after evidence.
- Keep the implementation limited to the existing failed presentation; do not alter connection selection or request semantics.
- Run the focused acceptance checks and the bounded affected-package/workspace gates in T004-T008. Do not run a full-solution local build.
- Since spec 253 changes the generated spec inventory/status input, establish map freshness. If stale, refresh the narrow maps layer, review its findings, and stage every changed generated path explicitly, including the manifest when changed.
- Complete the ownership, review, exact-head CI, merge, resulting-main, project-status, and claim-release steps in T009-T012.

### Parallel Opportunities

- T001 and T002 edit separate test files and can be prepared in parallel before implementation.
- T003 must wait for both test additions. T004-T008 are post-implementation gates; T009-T012 follow sequentially through delivery.

## Parallel Example: User Story 1

```text
Task: T001 Add the focused ConnectionPickerEditor component regression.
Task: T002 Extend the Connection syntax layout browser proof.
```

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Add the component regression and browser assertion; confirm each exposes the missing explanation on the current implementation.
2. Add the generic inline explanation while retaining the existing retry, loading, and reference behavior.
3. Run the focused checks and bounded integration gates listed in `quickstart.md`; do not run a full-solution local build.

### Incremental Delivery

The feature is one small authoring journey. Complete the single user story and its focused proof as one unit.

### Parallel Team Strategy

The unit is small and has one active implementation leaf. T001 and T002 are the only independent file-level tasks; root owns integration and final review.

## Notes

- Checkboxes record completed work only. T001–T009 have component and browser red/green, full Connections client, build, canonical typecheck/lint, architecture/map and root diff-review evidence; PR, independent review/CI and resulting-main delivery gates remain pending.
- No implementation, test, commit, push, map refresh, or GitHub delivery action was performed during this planning result. T004-T012 describe future gates and remain unchecked.
