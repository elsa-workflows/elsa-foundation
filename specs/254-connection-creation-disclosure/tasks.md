# Tasks: Connection Creation Disclosure

**Input**: Design documents from specs/254-connection-creation-disclosure/

**Status**: Root approved 2026-10-20; implementation and PR setup are complete. T001-T017 are checked against recorded evidence. T018-T022 remain pending. Regression tests and browser proof were added and shown failing before the corresponding implementation, as explicitly required for this work unit. This is a task-specific order, not a claim that the application constitution declares a general TDD cadence.

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, quickstart.md. No contracts/ directory applies because no public contract changes.

## Phase 1: User Story 1 - Create a connection with relevant options (Priority: P1)

**Goal**: Establish failing evidence for the credential-first API-key path and safe disclosure recovery before implementation.

**Independent Test**: Use the Connections dialog tests, shared disclosure regression, and actual Connections browser fixture to prove the three issue acceptance criteria without loading stored Secret values.

### Regression tests - add before implementation

- [x] T001 [P] [US1] Add a separate built-in HTTP/API-key descriptor fixture and failing coverage for credential-before-Base-URL order, the optional-address one-of rule and its copy, descriptor-required address controls, placement/name preservation, authored versus untouched defaults, checkbox on→off omission, Basic/custom ordering, unfamiliar contributed fields, and unchanged Secret-source submission in Extensions/Connections/studio/Elsa.Studio.Connections/Client/src/__tests__/connectionCreateDialog.test.tsx; record the initial focused red result before implementation.
- [x] T002 [P] [US1] Add a failing AdvancedSection regression for real error reveal→focused correction→recovery→manual collapse, asserting child/value/focus identity and unchanged persisted disclosure preference in Studio/src/apps/Elsa.Studio.Web/Client/src/app/ui/disclosure/__tests__/disclosure.test.tsx; record the expected red result before implementation.
- [x] T003 [P] [US1] Add a root-owned failing create-flow browser case using metadata-only route stubs in Studio/tests/browser/connections-management.spec.ts and Studio/tests/browser/connectionsFixture.tsx; cover 320px and desktop, light/dark, keyboard Enter/Space and ARIA relationships, credential-first ordering, initial collapsed disclosure, address guidance, placement switching and retained values, and record the expected red result before implementation.

### Implementation

- [x] T004 [US1] Implement exact built-in HTTP/API-key credential-before-Base-URL presentation, keeping Base URL direct; offer address one-of validation when both recognized fields are present and guidance only when both address fields are optional, keep descriptor-required addresses direct and mandatory without either/or copy, preserve Basic Username→Password and custom ordering, and implement runtime placement, authored-name submission, safe-default serialization, and generic fallback in Extensions/Connections/studio/Elsa.Studio.Connections/Client/src/ConnectionCreateDialog.tsx; preserve ConnectionCredentialField behavior.
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

- [x] T017 Create the local commit as elsa-workflows-codex[bot], open a ready pull request with Fixes #3065, map all three acceptance criteria to evidence, post the issue PR transition, and attach the pull request to the Codex task.
- [ ] T018 Complete an independent acceptance and accessibility review of the three issue criteria, browser keyboard/focus behavior, narrow layouts, and generic fallback; resolve findings on the pull request.
- [ ] T019 Complete an independent Secret/privacy review of metadata-only lookup, reference-versus-new-value submission, permission/eligibility behavior, and the absence of stored values from display; resolve findings on the pull request.
- [ ] T020 Complete an independent code/test/architecture review of boundaries, package dependency, serialization, failure recovery, test coverage, generated maps, and the full diff; resolve findings on the pull request.
- [ ] T021 Require all three independent review axes and exact-head required CI to be complete and green, with no failed check, open hold, unresolved blocking review thread, or missing audit evidence; post the test counts and evidence comment before merge, then merge only under the standing Oct 18 authorized gated-merge procedure and record its audit trail.
- [ ] T022 Verify resulting-main CI and Maps state, synchronize #3065, epic #3057, qualification #3075, and GitHub Project 61 status, record the merged outcome, and release the issue claim. Update another project only if live issue/board evidence confirms it is relevant.

## Dependencies and execution order

- Root approved the spec and private AdvancedSection recovery design on 2026-10-20. T001-T003 captured the red checkpoints before implementation and are complete.
- T001-T003 establish red regression evidence before T004-T007. T002 must be green after T005; T003 is root-owned and must be green after T004-T007.
- T005 depends on root approval and T002. T007 depends on importing the existing shared package and must be reviewed for lockfile scope.
- T008-T016 are checked against the local verification results recorded below. Root serializes cross-package, architecture, and map checks on the shared machine.
- T017 is complete: commit, ready PR, issue transition, and attachment are recorded. T018-T020 are separate round-two review axes. T021 depends on all three axes, revised-head exact checks, and posted evidence. T022 follows merge.

## Parallel opportunities

- T001, T002, and root-owned T003 touch separate test files and can be authored independently before implementation.
- After test authorship and approval, T004 and T005 affect separate production files, but T005 remains dependent on the approved disclosure design and T002.
- The full Connections, Studio UI, and Studio Workflows suite/build gates are individually bounded, but root should serialize them as shared-machine load permits.

## Notes

- T001-T017 are checked against the recorded work. T018-T022 remain unchecked because round-one review is not clean and exact-head hosted CI, merge, and resulting-main proof are incomplete.
- Base f7c417d initially had stale manifest/findings output. Root refreshed and reviewed the narrow map output; the local map check passed in the verification snapshot. Root will regenerate/check again after this documentation cleanup.
- The browser proof reuses the existing Connections page fixture. Unit coverage is direct dialog coverage in the existing Connections client test harness; it is not a pre-existing dedicated picker test.
- Use the direct Vitest and Playwright exec commands in quickstart.md; do not forward a file argument through the package test script.

## Execution evidence

### Initial red checkpoints before implementation

Root recorded intentional failing regressions before changing production behavior:

- The initial focused Connections dialog run had 4 intended failures and 19 passes, 23 tests total.
- The initial shared disclosure run had 3 intended recovery failures and 27 passes, 30 tests total.
- The initial browser API-key flow had 4 intended missing-disclosure failures across 320px and 1440px in light and dark themes.
- After the API-key unit boundary cases were expanded, the red snapshot was 7 intended failures across 27 cases.

These were expected red checkpoints, not unresolved failures in the subsequent green runs. Root retained the logs 3065-dialog-red.log, 3065-disclosure-red.log, and 3065-browser-red.log.

### Local green verification

Root recorded the following local verification results on the implementation and review-fix source:

- The Connections client passed 46/46 tests, including 30/30 in the focused dialog test file. Direct Connections TypeScript checking and its build passed.
- Studio Web passed 564/564 tests. Studio Workflows passed 2059/2059 after two fixture-identity corrections.
- The architecture suite passed 578/578 with zero skipped tests.
- The revised source passed all 35 related browser cases in one execution, including 9 Connections management cases with credential-before-Base-URL order, 8 existing Secret creation cases, 2 dialog-token cases, 4 picker cases, and 12 HTTP disclosure cases. Required-address behavior has focused unit coverage.
- Canonical type and lint checks, affected builds, and the local maps check passed in the recorded snapshot. Lint reported zero errors and 48 existing warnings.
- Root inspected the 320px dark and 1440px light screenshots; scoped axe reported zero violations in the new flow. The prior base map finding was reviewed and narrowly refreshed; the map check passed in that local snapshot.

These are local results, not hosted CI. Canonical type/lint passed on the revised source. Root checks generated maps and the final diff after documentation updates.

### Delivery and remaining gates

PR #3105 is open and ready. The commit, public issue transition, and Codex pull-request attachment are complete (T017). Round-one fixes have the local evidence above; each pushed head requires fresh independent review and hosted checks.

Round-one independent review is not clean, so T018-T020 remain pending for the revised PR. Exact-head hosted CI is not complete; there is no merge or resulting-main proof, so T021-T022 remain pending. No gate was waived.
