# Tasks: Connection Creation Disclosure

**Input**: Design documents from specs/254-connection-creation-disclosure/

**Status**: Root approved 2026-10-20; implementation in progress. Tasks remain unchecked until evidenced. Regression tests and browser proof must be added and shown failing before the corresponding implementation, as explicitly required for this work unit. This is a task-specific order, not a claim that the application constitution declares a general TDD cadence.

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, quickstart.md. No contracts/ directory applies because no public contract changes.

## Phase 1: User Story 1 - Create a connection with relevant options (Priority: P1)

**Goal**: Establish failing evidence for the credential-first API-key path and safe disclosure recovery before implementation.

**Independent Test**: Use the Connections dialog tests, shared disclosure regression, and actual Connections browser fixture to prove the three issue acceptance criteria without loading stored Secret values.

### Regression tests - add before implementation

- [x] T001 [P] [US1] Add a separate built-in HTTP/API-key descriptor fixture and failing coverage for credential order, Base URL-or-Allowed-hosts behavior, placement/name preservation, authored versus untouched defaults, checkbox on→off omission, required and unfamiliar contributed fields, and unchanged Secret-source submission in Extensions/Connections/studio/Elsa.Studio.Connections/Client/src/__tests__/connectionCreateDialog.test.tsx; run the focused Vitest command and record the expected red result before implementation.
- [x] T002 [P] [US1] Add a failing AdvancedSection regression for real error reveal→focused correction→recovery→manual collapse, asserting child/value/focus identity and unchanged persisted disclosure preference in Studio/src/apps/Elsa.Studio.Web/Client/src/app/ui/disclosure/__tests__/disclosure.test.tsx; record the expected red result before implementation.
- [x] T003 [P] [US1] Add a root-owned failing create-flow browser case using metadata-only route stubs in Studio/tests/browser/connections-management.spec.ts and Studio/tests/browser/connectionsFixture.tsx; cover 320px and desktop, light/dark, keyboard Enter/Space and ARIA relationships, credential-first ordering, initial collapsed disclosure, address guidance, placement switching and retained values, and record the expected red result before implementation.

### Implementation

- [x] T004 [US1] Implement exact built-in HTTP/API-key credential-first presentation, descriptor-aware address one-of validation/guidance, runtime placement handling, optional relevant-name visibility with both authored values retained/submitted, authored-default serialization, generic fallback, and descriptor-required field precedence in Extensions/Connections/studio/Elsa.Studio.Connections/Client/src/ConnectionCreateDialog.tsx; preserve ConnectionCredentialField behavior.
- [x] T005 [US1] After root approves the disclosure design, implement the smallest private AdvancedSection state refinement that retains automatic reveal through error recovery until manual collapse, keeps children/value/focus stable, preserves existing forced-error behavior, and never persists automatic reveal as a user preference in Studio/src/apps/Elsa.Studio.Web/Client/src/app/ui/disclosure/AdvancedSection.tsx; satisfy T002. Preserve the existing Workflows stale-activity behavior by scoping its AdvancedSection identity in ActivityPropertiesPanel.tsx and adapting activityPropertyGroups.test.tsx to inspect the current section after a draft/activity identity change.
- [x] T006 [US1] Add only the targeted responsive and accessible create-form layout changes in Extensions/Connections/studio/Elsa.Studio.Connections/Client/src/styles.css; satisfy the root-owned narrow and desktop browser case without altering unrelated screens.
- [x] T007 [US1] Add the internal @elsa-workflows/studio-ui workspace dependency to Extensions/Connections/studio/Elsa.Studio.Connections/Client/package.json and update Studio/pnpm-lock.yaml with the Studio workspace lockfile-only install; review the lock diff for unrelated dependency changes.

## Phase 2: User Story 1 - Acceptance verification

**Goal**: Prove the complete behavior across affected package suites, UI checks, browser, architecture, and generated-map status.

### Affected suites and build gates

- [x] T008 [US1] Re-run the focused dialog regression, then the complete Connections client Vitest suite and Vite build for Extensions/Connections/studio/Elsa.Studio.Connections/Client; record commands, counts, and results in the PR evidence.
- [x] T009 [US1] Run the focused disclosure regression with pnpm --filter @elsa-workflows/studio-web exec vitest run src/app/ui/disclosure/__tests__/disclosure.test.tsx, then the full Studio Web test suite and build for Studio/src/apps/Elsa.Studio.Web/Client; record test counts and build results.
- [x] T010 [US1] Run the StudioUi build as the public typecheck for Studio/src/apps/Elsa.Studio.Web/StudioUi; do not treat its passWithNoTests test script as disclosure coverage.
- [x] T011 [US1] Run the complete Studio Workflows client Vitest suite and build for Studio/src/essentials/Elsa.Studio.Workflows/Client because it consumes the shared disclosure package; record counts and results.
- [x] T012 [US1] Run canonical Studio typecheck and lint from Studio/, covering TypeScript, ESLint, CSS stylelint, and module-layout validation; resolve all failures and record the results.
- [x] T013 [US1] Run the direct Playwright command for Studio/tests/browser/connections-management.spec.ts with one worker; inspect the metadata-only route proof at narrow and desktop sizes and in light/dark themes, and record accessibility, keyboard, retained-value, and address-gate results.

### Architecture, maps, and review preparation

- [x] T014 [US1] Restore the CI project graph with tools/architecture/restore-ci-project-graph.sh and run the full Essentials/tests/Architecture/Tests/Elsa.Architecture.Tests.csproj suite serially; reconcile the executed test count and result with the reported output, without running a whole-solution local build.
- [x] T015 [US1] Run tools/maps/Elsa.Maps.Generator freshness check and compare with the verified pre-feature f7c417d baseline, where map job 118510576623 already failed for stale docs/maps/manifest.json and docs/reports/maps-v1-findings.md; report baseline versus feature-caused findings accurately, review any generated findings, and let root select the narrowest authorized refresh and explicitly stage changed map paths including the manifest only if it changes.
- [x] T016 [US1] Complete a DRY and full diff review across ConnectionCreateDialog.tsx, AdvancedSection.tsx, styles.css, the dialog/disclosure/browser tests, package.json, and pnpm-lock.yaml; re-read #3065 comments and open referencing PRs for ownership/competition before commit, and resolve every actionable finding.

## Phase 3: Polish & Cross-Cutting Delivery Gates

**Goal**: Deliver the reviewed change through the authorized App-owned issue and pull-request protocol and verify the resulting main state.

- [ ] T017 Create the local commit as elsa-workflows-codex[bot], open a ready pull request with Fixes #3065, map all three acceptance criteria to evidence, post the issue PR transition, and attach the pull request to the Codex task.
- [ ] T018 Complete an independent acceptance and accessibility review of the three issue criteria, browser keyboard/focus behavior, narrow layouts, and generic fallback; resolve findings on the pull request.
- [ ] T019 Complete an independent Secret/privacy review of metadata-only lookup, reference-versus-new-value submission, permission/eligibility behavior, and the absence of stored values from display; resolve findings on the pull request.
- [ ] T020 Complete an independent code/test/architecture review of boundaries, package dependency, serialization, failure recovery, test coverage, generated maps, and the full diff; resolve findings on the pull request.
- [ ] T021 Require all three independent review axes and exact-head required CI to be complete and green, with no failed check, open hold, unresolved blocking review thread, or missing audit evidence; post the test counts and evidence comment before merge, then merge only under the standing Oct 18 authorized gated-merge procedure and record its audit trail.
- [ ] T022 Verify resulting-main CI and Maps state, synchronize #3065, epic #3057, qualification #3075, and GitHub Project 61 status, record the merged outcome, and release the issue claim. Update another project only if live issue/board evidence confirms it is relevant.

## Dependencies and execution order

- Root approved the spec and private AdvancedSection recovery design on 2026-10-20. T001-T003 still gate production implementation.
- T001-T003 establish red regression evidence before T004-T007. T002 must be green after T005; T003 is root-owned and must be green after T004-T007.
- T005 depends on root approval and T002. T007 depends on importing the existing shared package and must be reviewed for lockfile scope.
- T008-T016 depend on the implementation and regressions being complete. Root serializes cross-package, architecture, and map checks on the shared machine.
- T017 depends on all local gates and the ownership recheck. T018-T020 are separate review axes. T021 depends on all three axes, complete exact-head checks, and posted evidence. T022 follows merge.

## Parallel opportunities

- T001, T002, and root-owned T003 touch separate test files and can be authored independently before implementation.
- After test authorship and approval, T004 and T005 affect separate production files, but T005 remains dependent on the approved disclosure design and T002.
- The full Connections, Studio UI, and Studio Workflows suite/build gates are individually bounded, but root should serialize them as shared-machine load permits.

## Notes

- All items remain unchecked; no implementation, test, build, architecture, map, GitHub delivery, review, or merge result is claimed here.
- The existing Studio main map job is already red at base f7c417d for the two stale generated files named in T015. This pre-feature finding must remain separate from any finding caused by this work.
- The browser proof reuses the existing Connections page fixture. Unit coverage is direct dialog coverage in the existing Connections client test harness; it is not a pre-existing dedicated picker test.
- Use the direct Vitest and Playwright exec commands in quickstart.md; do not forward a file argument through the package test script.

## Execution evidence

2026-10-20 root confirmed pre-implementation regression evidence: Connections focused 4 intended failures / 19 passed (23 total); shared disclosure 3 intended recovery failures / 27 passed (30 total); browser API-key flow 4 intended missing-disclosure failures across 320/1440 px and light/dark. External logs `3065-dialog-red.log`, `3065-disclosure-red.log`, `3065-browser-red.log`. No green execution result follows from these red checkpoints.

Root integration review found a valid Workflows regression (2058 pass / 1 fail): a new activity inherited a previous error reveal. A narrow identity-key correction and current-node regression check are authorized under T005; full Workflows rerun remains required. Final corrected Connections RED expanded to 27 cases with 7 intended failures, including the pure placement seam and required-name relevance (initial 23-case snapshot above preceded those added boundaries).

Root local gates: Connections44/44; sharedWeb564/564; Workflows2059/2059 after two fixture identity corrections; architecture578/578, zero skips; 35/35 related browser cases with actualhoststyles, no retries/force clicks; canonicaltypes/lint/builds passed (48 preexisting ESLint warnings, zero errors). Geometry debug reproduced and qualified native-radio sizing fix. Shared error latch and activityidentity safeguard covered before/after. Final screenshots inspected at320Dark and1440Light; scoped axe reports zero violations in the new complete flow. Hosted/fullbrowser and main gates still pending.

Root maps/diff review: the base’s spec248 had already been added to the status map but omitted from manifest/report counts. The narrow dependency-map/maps refresh changes only manifest count249→251 (existing248 plusnew254), report count249→251, and the Spec254 status row. No project/dependency/feature graph changed. Findings reviewed; manifest will be staged explicitly. Full diff/DRY/credential-boundary review complete. Direct Connections tsc --noEmit also passes; simplified the placement test to a static typed import and reran all44 Connections tests. Current App claim6285876529 remains unique, no assignee/readiness/competingPR.
