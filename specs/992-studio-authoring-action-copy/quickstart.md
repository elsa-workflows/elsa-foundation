# Quickstart: Validate Studio Authoring Action Copy

This guide records the test-first sequence and local qualification below. The delivery record in [PR #3128](https://github.com/elsa-workflows/elsa-foundation/pull/3128) and [issue #3072](https://github.com/elsa-workflows/elsa-foundation/issues/3072) separately tracks independent review, exact-head hosted checks, merge, and resulting-main verification.

## Prerequisites

- Use the Studio workspace’s declared pnpm 11.9.0 toolchain and installed dependencies.
- Playwright’s Chromium browser must be installed for browser tests.
- Run commands from the Studio directory at the repository root.

## Test-driven sequence

First add the user-visible regression assertions and run the targeted tests before production edits. Expected RED results concern the specified labels, accessible copy or measured status geometry. Record them. An unexpected eligibility, identity, request or callback failure requires investigation and root scope review; do not alter existing selection or operation behavior to make a copy assertion pass.

Run targeted Workflows component tests:

    pnpm --filter @elsa-workflows/studio-workflows exec vitest run src/__tests__/workflowTestDrive.test.tsx src/__tests__/lazyTestDriveAnnouncement.test.tsx src/__tests__/workflowEditorToolbar.test.tsx src/__tests__/publishActionButton.test.tsx src/__tests__/activityDefinitions.test.tsx src/__tests__/activityDefinitionAuthoring.test.tsx src/__tests__/activityDefinitionPublicationReview.test.tsx

Run the full affected Workflows client unit suite:

    pnpm --filter @elsa-workflows/studio-workflows test

Run Workflows client typecheck and bundle build:

    pnpm --filter @elsa-workflows/studio-workflows typecheck
    pnpm --filter @elsa-workflows/studio-workflows build

Run the targeted Chromium browser specs:

    CI=1 pnpm exec playwright test tests/browser/workflow-toolbar.spec.ts tests/browser/activity-definitions.spec.ts --workers=1

Run canonical workspace typecheck and lint from Studio:

    pnpm typecheck
    pnpm lint

Run the repository gates from the repository root:

    bash tools/architecture/restore-ci-project-graph.sh --locked-mode
    dotnet test Essentials/tests/Architecture/Tests/Elsa.Architecture.Tests.csproj -c Release --no-restore
    dotnet run --project tools/maps/Elsa.Maps.Generator -- check

The map check compares the committed dependency map. Do not stage or force-add ignored documentation maps or manifests. Keep heavy checks serial and respect the shared-machine build slots.

## Expected verification

- Workflow setup fallback is “Start Test Run”; module-supplied submit label, input labels and associations, loading announcements, connected-environment/real-side-effects warning, and existing request/callback payloads remain correct.
- Workflow status geometry uses the actual TestRunStatus component and detail callback, not a lookalike button; capitalization is consistent with “Test Run,” and complete outcome, stale-draft, and runtime-issue information remains accessible. Save, publication, and Test Run status changes are observed semantically at each fixed width of 320, 768, 1024, and 1440 pixels, in light and dark themes. Every primary action remains visible, and each compared action position differs by no more than 1 CSS pixel for the same definition and viewport.
- Activity action states confirm complete list coverage, exact editable draft precedence, exact clone source version, explicit no-head create behavior, and chooser fallback for unconfirmed, ambiguous, or denied states.
- The Activity authoring route verifies actual publication statuses and keeps its primary actions visible and within 1 CSS pixel while those statuses change. Workflow and Activity publication entry copy changes preserve their existing subsequent review, publish, and continuation behavior.
- Keyboard access, full accessible Test Run status, direct result-details access, and absence of primary-action overflow are checked through rendered roles and geometry.

The untouched Workflows baseline passed 2124 tests across 167 files with no skips (`3072-unit-baseline.log`, source 3255701, docs-only branch78189151b). Retain this existing coverage and account separately for added cases; 2124 is not a fixed target for the completed suite. The pre-change copy baseline recorded 121 passing and 13 expected copy-only failing tests across 134 cases; actual-component layout baselines reproduced the movement before the repair. Initial integrated-tree qualification passes 2133 Workflows tests across 167 files (nine added cases), canonical workspace typecheck/lint, the unchanged Workflows bundle budgets, 63 Chromium cases with zero skips/flaky cases, locked architecture restore, 578 architecture tests with zero skips, and the dependency-map check. At all four widths in both themes, the sixteen expanded geometry cases measure zero primary-action movement across the recorded save/Test Run/publication states. Root visually inspected workflow and Activity layouts at 320/1440 in light/dark and verified natural keyboard access to result details.

## Lifecycle and delivery

Before final implementation review, set the spec to Implemented and remove its completed specification-quality checklist in the same PR. Preserve delivery verification separately from that lifecycle marker. Root requires three independent non-author review axes, qualifying hosted checks on the exact reviewed head, and resulting-main checks after merge. A fixture pass does not establish final composed Workbench acceptance (#3075), backend-service availability or deployment.


## R1 correction qualification

The two-file regression run on unchanged production recorded 42 passing and three expected failing cases: omitted-head shortcut selection and the two loading announcements. Root reviewed and approved RED before the guard and string edits. The corrected two-file run passes all 45 cases, including the actual chooser click with no dialog or create request and the deferred module-result props. Full Workflows qualification now passes 2135 tests across 167 files, eleven added to the untouched 2124 baseline. Workspace types/lint (48 existing warnings, zero errors), Workflows build/unchanged budgets, all 63 Chromium journeys without skips or flakes, all 578 architecture tests without skips and the dependency-map check pass. The sixteen expanded geometry cases enforce the same 1 CSS pixel bound; the initial report remains the direct zero-displacement measurement. Root reviewed four fresh 320-dark/1440-light workflow/Activity screenshots in addition to the earlier eight views. Delivery reviews, exact-head hosted checks and resulting-main verification remain separate public gates in PR #3128.
