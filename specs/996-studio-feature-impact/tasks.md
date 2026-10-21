# Tasks: Studio Feature Impact Review

**Input**: Approved design artifacts under `specs/996-studio-feature-impact/` (`spec.md`, `plan.md`, `research.md`, `data-model.md`, `contracts/impact-review.md`, and `quickstart.md`).

**Scope boundary**: Product edits are limited to `Extensions/FeatureManagement/studio/Elsa.Studio.FeatureManagement/Client/src/module.tsx`, `Extensions/FeatureManagement/studio/Elsa.Studio.FeatureManagement/Client/src/__tests__/module.test.ts`, and `Extensions/FeatureManagement/studio/Elsa.Studio.FeatureManagement/Client/src/styles.css` only when scoped review styling is needed. Do not change backend/API, SDK, dependency resolver, permission policy, package pins/manifests, host configuration, provider composition, or shared configuration.

**Test cadence**: Studio's derived constitution requires focused tests and browser verification but does not declare TDD as the general cadence. This work has an explicit issue-specific gate: add and run the meaningful test-only RED, then stop for root review before any product edit.

## Phase 1: Setup

**Purpose**: Establish the offline workspace and untouched module baseline before editing tests.

- [x] T001 In `Studio/`, run `pnpm install --offline --frozen-lockfile` followed by the untouched focused Vitest command from `specs/996-studio-feature-impact/quickstart.md`; record the actual test count, source revision, command, exit status, and full safe output in `/Users/sipke/Documents/Codex/2026-10-19/studio-ux-audit/execution/3074-baseline-install.log` and `/Users/sipke/Documents/Codex/2026-10-19/studio-ux-audit/execution/3074-baseline-module.log`; stop if install requires a lock/pin change or the untouched suite fails.

## Phase 2: Foundational Test-Only RED

**Purpose**: Prove the requested behavior is missing on the approved source baseline, with the product module unchanged.

- [x] T002 Add shared rendered regressions only in `Extensions/FeatureManagement/studio/Elsa.Studio.FeatureManagement/Client/src/__tests__/module.test.ts`, retaining meaningful existing tests and using the existing page contract and public Studio SDK auth providers. Cover single/bulk required cascades, optional-edge exclusion, filtered impacts, overlapping roots and cycle-safe explanation paths, the A-off/B-off enable-A-then-disable-A retained-effect case (B remains automatic and is explained historically, A is omitted and no direct B/current pending A claim is made; Reset clears it), the `A=off`/`B=on`/`A requires B` disable-B-then-enable-A supersession case (B is omitted and `(B, off)` explains nothing), a companion case where B's still-changed setting remains independently visible, sensitive-setting/reload metadata, host isolation, cancel/reset, complete reviewed Apply payload/revision, every review invalidation trigger with zero Apply posts, distinct stale-revision and activation-guard 409 reasons, direct and transitioned authenticated read-only controls, and anonymous/demo write behavior through reviewed Apply. Do not change `module.tsx`, `styles.css`, SDK/backend, or manifests; do not duplicate the cascade implementation or assert source text.
- [x] T003 With `module.tsx` still unchanged, run the focused module suite and save its full safe output, exit status, and failing scenario summary to `/Users/sipke/Documents/Codex/2026-10-19/studio-ux-audit/execution/3074-contract-red.log`; distinguish expected behavior-level failures from provider, fixture, or harness failures. The corrected run had 43 tests: 26 expected behavior failures (22 cannot open the required review, 2 do not preserve distinct backend 409 reasons, and 2 expose editable controls to authenticated read-only users), with 17 passing; command exit status 1. The review-open failures prevent the new invalidation post-count assertions and reviewed anonymous Apply payload assertion from reaching their later steps. No setup/provider/fixture/teardown failures occurred. Preserve the earlier 42-test receipt at `/Users/sipke/Documents/Codex/2026-10-19/studio-ux-audit/execution/3074-contract-red-r2-pre-T004-review.log`.
- [x] T004 Stop for root review and approval of T001–T003, including the untouched baseline, test-only diff, and meaningful RED evidence, before beginning any product change in `Extensions/FeatureManagement/studio/Elsa.Studio.FeatureManagement/Client/src/module.tsx` or `styles.css`.

## Phase 3: User Story 1 - Stage feature changes (Priority: P1)

**Goal**: Preserve host-local single and bulk staging with the existing required cascade and no automatic changes from optional edges.

**Independent Test**: Stage single and bulk edits on Studio and Server, including an affected feature hidden by a filter; confirm only the selected local draft changes and the existing cascade result remains unchanged.

- [ ] T005 [US1] In `Extensions/FeatureManagement/studio/Elsa.Studio.FeatureManagement/Client/src/module.tsx`, retain the current required-dependency/dependant resolver and host-local draft behavior while recording effective enabled-state roots separately from still-pending setting edits; discard or mark inapplicable a state root returned to baseline or overwritten by a later cascade. Retain earlier automatic cause/path information only as needed to explain a remaining net difference after its initiating root is reverted; clear it on return to baseline or Reset. Preserve optional-edge exclusion and add no second cascade. Run the focused US1 tests in `Extensions/FeatureManagement/studio/Elsa.Studio.FeatureManagement/Client/src/__tests__/module.test.ts` and record the GREEN result.

## Phase 4: User Story 2 - Review the complete impact (Priority: P1)

**Goal**: Show every current net change and truthful direct/automatic explanation for the selected host, regardless of feature-list filtering.

**Independent Test**: Review overlapping edits and a changed setting; verify effective roots and required paths, filtered-out changes, setting-field handling, and omitted net-zero/superseded roots.

- [ ] T006 [US2] In `Extensions/FeatureManagement/studio/Elsa.Studio.FeatureManagement/Client/src/module.tsx` and, only if needed, `Extensions/FeatureManagement/studio/Elsa.Studio.FeatureManagement/Client/src/styles.css`, render the selected host's complete baseline-to-draft impact with direct versus automatic classification, all effective roots and cycle-safe transitive required explanation paths, truthful historical reasons for retained automatic effects without active roots, independent setting-field changes under existing sensitive-value handling, filter-independent rows, optional-edge exclusion, and only known reload implications (otherwise explicitly unknown). Ensure superseded state roots cannot classify or explain surviving rows, while still-changed settings remain represented. Add no fabricated summary, new resolver, API, or backend behavior. Run the focused US2 tests in `Extensions/FeatureManagement/studio/Elsa.Studio.FeatureManagement/Client/src/__tests__/module.test.ts` and record the GREEN result.

## Phase 5: User Story 3 - Apply only what was reviewed (Priority: P1)

**Goal**: Bind review, cancellation, reset, and Apply to the exact selected-host snapshot and preserve backend failure meaning.

**Independent Test**: Review Studio and Server drafts, invalidate after every specified change, cancel without saving, reset only the selected host draft, and verify Apply submits the exact reviewed host/state/revision; distinct 409 reasons remain failures.

- [ ] T007 [US3] In `Extensions/FeatureManagement/studio/Elsa.Studio.FeatureManagement/Client/src/module.tsx`, bind the open review to the selected host, complete desired state, loaded catalog snapshot, and existing configuration revision; invalidate it on draft mutation, host change, refresh, or catalog replacement; make Cancel preserve the draft without Apply and Reset affect only the selected host. Apply only the still-current reviewed target, complete state, and revision. Preserve backend-provided reasons for stale-revision and activation-guard 409 responses without false success or a blanket stale label. Keep the revision guarantee bounded to the loaded catalog and existing feature-configuration revision; do not imply atomic detection of out-of-band package/dependency metadata changes or add a graph token. Extend only necessary rendered behavior tests in `Extensions/FeatureManagement/studio/Elsa.Studio.FeatureManagement/Client/src/__tests__/module.test.ts` and run the focused US3 GREEN suite.

## Phase 6: User Story 4 - Inspect with read-only access (Priority: P2)

**Goal**: Let authenticated readers inspect while preventing management actions, without changing anonymous/auth-disabled demo behavior.

**Independent Test**: Mount the page under the public SDK authenticated read-only, manage, and anonymous providers; verify inspect visibility and all stage, setting, reset, and Apply controls for each case.

- [ ] T008 [US4] In `Extensions/FeatureManagement/studio/Elsa.Studio.FeatureManagement/Client/src/module.tsx`, use only the public `@elsa-workflows/studio-sdk` auth/session and permission hooks to keep authenticated users with inspect but no existing management permission read-only, while preserving current writable behavior for anonymous/auth-disabled demos and manage users. Do not import the app-private host helper or alter backend authorization. Extend `Extensions/FeatureManagement/studio/Elsa.Studio.FeatureManagement/Client/src/__tests__/module.test.ts` with provider-backed rendered cases and run the focused US4 GREEN suite.

## Phase 7: Polish and Root-Owned Qualification

**Purpose**: Complete affected-project, host, repository, review, lifecycle, and resulting-main gates in the approved serial order.

- [ ] T009 Run the complete focused module suite, module TypeScript check, and feature-management client build from `Studio/`; then run the existing backend preservation test command from `specs/996-studio-feature-impact/quickstart.md` through the normal dotnet build-slot wrapper. Save command, source revision, exit status, test counts, and safe output under `/Users/sipke/Documents/Codex/2026-10-19/studio-ux-audit/execution/`; make no backend edits.
- [ ] T010 Root-owned: run the serial Studio workspace `typecheck`, `lint`, and recursive `build` gates from `Studio/`; leave recursive workspace tests to hosted CI and record each local command/result independently in `/Users/sipke/Documents/Codex/2026-10-19/studio-ux-audit/execution/`.
- [ ] T011 Root-owned: build `Essentials/apps/Elsa.Workbench/Elsa.Workbench.csproj` and `Studio/src/apps/Elsa.Studio.Web/Elsa.Studio.Web.csproj` serially through the normal repository wrapper; save exact commands and results under `/Users/sipke/Documents/Codex/2026-10-19/studio-ux-audit/execution/`.
- [ ] T012 Root-owned: use the canonical private-host procedure in `specs/993-studio-diagnostics-index/quickstart.md#composed-workbench-comparison` with fresh guarded roots, databases, keyrings, and browser contexts for the unchanged Feature Management baseline and the implementation revision. Root may reuse an already-owned baseline host build only after proving its Feature Management module/styles and relevant host/API sources byte-identical to `7fb745f8604b6ed67a2eae60266976c98bce949e`, retaining the actual whole-host source and explicit limits. Sign in through the normal source-provided Development UI identity on the supported `/settings/feature-management` route (legacy `/features` may redirect), without recording credential values. On baseline, record existing staging/cascade and host selection for Studio and Server, the available Apply action and absence of review, then Reset and verify neither copied host file changed. Baseline Apply is optional; if exercised, restrict it to a root-selected safe reversible change and verify/restore the intended private file. Do not attempt future review controls on baseline. On the implementation revision, prove review and Cancel for Studio and Server, then review and Apply a root-selected safe reversible already-loaded change for each target; verify only the intended copied `shells.json` changes and the other host file remains byte-identical. Never disable Feature Management, authentication, or the API, activate a provider, change shared config, or record credentials/configuration values. If no safe change is available, stop for root review. Clean up each owned root/process/browser/port and save only secret-safe receipts in `/Users/sipke/Documents/Codex/2026-10-19/studio-ux-audit/execution/3074-private-host-proof.md`.
- [ ] T013 Root-owned: run `bash tools/architecture/restore-ci-project-graph.sh --locked-mode -p:WarningsNotAsErrors=NU1603`, then `dotnet test Essentials/tests/Architecture/Tests/Elsa.Architecture.Tests.csproj -c Release --no-restore -p:WarningsNotAsErrors=NU1603`, then `dotnet run --project tools/maps/Elsa.Maps.Generator -- check`; preserve this order and record separate outcomes under `/Users/sipke/Documents/Codex/2026-10-19/studio-ux-audit/execution/`.
- [ ] T014 Before the implementation PR merges, set `**Status**: Implemented` in `specs/996-studio-feature-impact/spec.md` and delete terminal process residue `specs/996-studio-feature-impact/checklists/requirements.md` per `docs/reference/spec-lifecycle.md`; keep `plan.md`, `research.md`, `data-model.md`, `contracts/`, `quickstart.md`, and `tasks.md`.
- [ ] T015 Root-owned: immediately before commit/PR, recheck issue #3074 and open PRs for competing ownership, preserving winning claim comment `6304861123`; commit the reviewed scope with the Elsa Workflows Codex identity, open a ready PR with `Fixes #3074`, and post safe evidence mapping each acceptance criterion to actual results without secrets in the PR evidence comment and `/Users/sipke/Documents/Codex/2026-10-19/studio-ux-audit/execution/3074-pr-evidence.md`.
- [ ] T016 Root-owned: obtain the independent exact-head review axes and required hosted CI on the PR candidate head; record each result separately in `/Users/sipke/Documents/Codex/2026-10-19/studio-ux-audit/execution/3074-pr-evidence.md`, do not represent unavailable or stale evidence as passed, resolve findings, and merge only after all required current-head gates are green.
- [ ] T017 Root-owned: qualify the actual resulting `main` with its required CI/maps and any affected source gates that must be repeated after integration; do not reuse PR-head evidence as resulting-main proof. Record outcomes in `/Users/sipke/Documents/Codex/2026-10-19/studio-ux-audit/execution/3074-resulting-main.log`, reconcile any failure before reporting completion, then update issue/board state and release the specific winning claim `6304861123` with the verified shipped outcome as Elsa Workflows Codex.

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: T001 must pass before fixtures or tests are changed.
- **Foundational Test-Only RED (Phase 2)**: T002 and T003 depend on T001; T004 blocks all product tasks until root accepts the test-only diff and meaningful RED.
- **User stories**: T005 → T006 → T007 → T008 are intentionally sequential because they extend the same `module.tsx` and `module.test.ts` state flow; each focused GREEN gates the next story.
- **Qualification (Phase 7)**: T009 → T010 → T011 → T012 → T013; T014 completes lifecycle edits before PR merge; T015 → T016 → T017 covers claim recheck, PR/current-head gates, and resulting-main qualification.

### User Story Dependencies

- **US1 (P1)**: First product increment after the root-approved RED; it preserves stage/cascade semantics and supplies effective provenance.
- **US2 (P1)**: Depends on US1 provenance to explain the current draft; together US1 + US2 are the first useful vertical slice.
- **US3 (P1)**: Depends on the impact snapshot from US2 to bind Cancel, invalidation, and Apply to what was reviewed.
- **US4 (P2)**: Depends on the same page controls and should follow the P1 staging/review/apply flows; no server permission contract changes.

## Parallel Opportunities

None are identified. The story tasks share the same module and test file, and the repository's heavy local gates share a constrained machine. Keep story work and all build/host/repository checks serial; do not add `[P]` markers to these tasks.

## Implementation Strategy

### MVP First

The useful MVP is the US1 + US2 vertical slice: preserve the existing staging behavior, then let an operator inspect a complete, truthful impact review. US1 alone is a regression-preservation checkpoint and does not deliver the requested review experience.

1. Complete T001–T004 and stop for root approval of the test-only RED.
2. Implement US1, then US2; run each focused GREEN before continuing.
3. Stop at the MVP boundary for root review; only then proceed with US3 and US4.

### Incremental Delivery

1. Add the host-local effective provenance without changing the resolver.
2. Add the complete impact view and confirm the A/B superseded-root cases.
3. Bind the review lifecycle and exact-state Apply behavior.
4. Add authenticated read-only controls while preserving demo behavior.
5. Run the root-owned actual-host, serial repository, exact-head CI/review, and resulting-main gates.

## Coverage Map

| User story / requirement group | Tasks |
|---|---|
| US1: FR-001–003, FR-017 (staging, cascade, optional edges, host isolation) | T002, T005, T012 |
| US2: FR-004–007 (complete impact, roots/paths, settings, reload truth) | T002, T006, T012 |
| US3: FR-008–013, FR-016 (snapshot, invalidation, Cancel/Reset, exact Apply, errors, revision boundary) | T002, T007, T009, T012, T013 |
| US4: FR-014–015 (authenticated read-only, demo behavior) | T002, T008, T012 |
| Lifecycle and delivery gates | T010–T017 |

## Root Task Review

Root read the complete task list against the approved specification and plan. T012 now preserves the plan's optional baseline Apply instead of adding an unnecessary pre-change mutation. The accepted baseline reuse is limited to source-proven unchanged Feature Management and relevant host/API source, with actual whole-host provenance retained; it does not waive the new implementation's actual Studio and Server reviewed Apply/correct-file proof. All other acceptance, review and resulting-main gates remain mandatory.

Approved to execute T001–T003 only, then stop at T004 for root review of the baseline, full test-only diff and meaningful RED before any product edit. No task is completed by this approval.
