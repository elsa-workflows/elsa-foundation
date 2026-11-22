# Tasks: Activity-by-activity execution playback

**Input**: [plan.md](plan.md), [research.md](research.md), [data-model.md](data-model.md), [dependency/UI contract](contracts/playback-inspection.md), [quickstart.md](quickstart.md).

**Authority**: The owner authorizes end-to-end implementation. **Current execution status: T001–T019 are implemented and qualified; T020 remains open for the exact-head merge and actual resulting-main verification.** The [delivery receipt](evidence.md) binds the landed runtime prerequisites,54-case composed corpus, independently reviewed actual images and immutable downloaded evidence to their original sources. CI is explicitly skipped under owner authorization. All four user stories remain required.

**Tests/cadence**: Existing risk-based Foundation discipline applies; no greenfield TDD mandate is declared. Add meaningful behavior tests with each logic-bearing implementation; reproduce existing-behavior fixes red/green. Use controlled presentation time, and distinguish real backend evidence from fixture bodies. No new test asserts documentation text.

## Phase 1: Setup and planning

- [X] T001 Inspect current activity browsing/history/selection contracts and record source-backed gaps and research decisions in `specs/1226-activity-playback/research.md`.
- [X] T002 Complete independent review of `specs/1226-activity-playback/plan.md`, `data-model.md`, `contracts/playback-inspection.md`, `quickstart.md` and `tasks.md`; preserve owner authority, coverage and actual prerequisite status.

## Phase 2: Foundational prerequisites

**Delivered prerequisite**: T003 is integrated through the native history stack on main, including provider/migration and process-restart qualification. Its exact-order/frozen-bound/ordinal/gap/authorization evidence is linked in the delivery receipt. Timestamps, loaded-page arrays, fabricated signed offsets or test-only history responses are not substitutes.

- [X] T003 Coordinate and deliver the separately reviewed runtime-owned facet with #3849 under the explicit dependency instruction. Actual native/provider/API heads, admission/order/frozen-bound/ordinal/gap/authorization, migrations and restart evidence are bound by the [delivery receipt](evidence.md). The older6185 handoff and R6–R11 receipts remain historical technical-readiness evidence, not restamped current executions.
- [X] T004 Add the private, cancellable capability-discovered sequence adapter in `Studio/src/essentials/Elsa.Studio.Workflows/Client/src/api/activityPlayback.ts`, using only the qualified T003 facet and exact string/opaque ordering identities.
- [X] T005 Add adapter behavior tests for unsupported/denied/unknown totals, context binding, gap/retention changes, stale responses and safe positions in `Studio/src/essentials/Elsa.Studio.Workflows/Client/src/__tests__/activityPlaybackApi.test.ts`.

## Phase 3: US1 — Manual exact-occurrence traversal

**Goal/test**: Previous/Next and direct selection traverse the exact run/scope sequence, including nested/loop/retry identities, while Timeline/focus and explicit Activity details remain coherent.

- [X] T006 [US1] Implement the private presentation/cursor model and hook in `Studio/src/essentials/Elsa.Studio.Workflows/Client/src/workflow-editor/activityPlaybackModel.ts` and `useActivityPlayback.ts`; keep confirmed and pending selection distinct.
- [X] T007 [US1] Add labelled controls with previous/next, position/timestamp and truthful disabled/unavailable states in `Studio/src/essentials/Elsa.Studio.Workflows/Client/src/workflow-editor/ActivityPlaybackControls.tsx`, using Studio public controls/tokens and existing styles.
- [X] T008 [US1] Integrate confirmed cancellable occurrence/scope navigation and synchronous manual-navigation pause into `Studio/src/essentials/Elsa.Studio.Workflows/Client/src/workflow-editor/WorkflowInstances.tsx`; coordinate with spec1211 S06/S07 and preserve current timeline scope, pinned graph, ambiguous-occurrence choice and focus behavior.
- [X] T009 [US1] Add mounted selection/boundary/identity/Activity-return regressions in `Studio/src/essentials/Elsa.Studio.Workflows/Client/src/__tests__/activityPlayback.test.tsx` and keyboard/pointer states in `Studio/tests/browser/activity-playback.spec.ts`.

## Phase 4: US2 — Play/pause and presentation pace

**Goal/test**: Every eligible step appears once at the four defined cadences; pause, Activity inspection, manual navigation, backgrounding and end stop further ticks. Normal page buffering resumes only the same still-playing generation.

- [X] T010 [US2] Add cancellable timer ownership, speed selection, end/Replay and visibility pause in `Studio/src/essentials/Elsa.Studio.Workflows/Client/src/workflow-editor/useActivityPlayback.ts` and `ActivityPlaybackControls.tsx`; keep live-update/runtime controls separate.
- [X] T011 [US2] Verify four speeds, pause-before-queued-tick, no catch-up, manual inspection, delayed confirmation, buffering and reduced motion with controlled presentation time in `Studio/src/essentials/Elsa.Studio.Workflows/Client/src/__tests__/activityPlayback.test.tsx` and `Studio/tests/browser/activity-playback.spec.ts`. Historical supporting4007-client execution includes all46 playback hook cases; real composed transport passes4/4 at60a0 with reduced motion and all four paces (research R34).

## Phase 5: US3 — Bounded ordinal seeking

**Goal/test**: First/middle/final/older repeated occurrence in a200-occurrence run, across pages and ten rapid seeks, resolves exactly the final requested target without whole-history preload or stale details.

- [X] T012 [US3] Implement native labelled ordinal scrubbing and bounded target/page loading in `Studio/src/essentials/Elsa.Studio.Workflows/Client/src/workflow-editor/ActivityPlaybackControls.tsx`, `useActivityPlayback.ts` and `src/api/activityPlayback.ts`; manual seeks pause and loading never impersonates confirmation.
- [X] T013 [US3] Add random-position/page-boundary, latest-request-wins, unknown-total, failure/retry and no-preload regressions in `Studio/src/essentials/Elsa.Studio.Workflows/Client/src/__tests__/activityPlayback.test.tsx`, `activityPlaybackApi.test.ts` and `Studio/tests/browser/activity-playback.spec.ts`.

## Phase 6: US4 — Evidence, live bounds and access

**Goal/test**: Frozen live membership, true gaps/coverage, concurrency and protected contexts remain truthful. No implied causal path, historical state reconstruction or cross-context disclosure.

- [X] T014 [US4] Integrate admission/segment boundaries, explicit paused refresh, new-record indication and context/access invalidation in `Studio/src/essentials/Elsa.Studio.Workflows/Client/src/workflow-editor/useActivityPlayback.ts` and `WorkflowInstances.tsx`; reuse existing conservative `executionMotion.ts` predicates without inferring edges from adjacency.
- [X] T015 [US4] Cover concurrent/tied order, retained gaps, coarse/legacy/unavailable recording, missing pin, revocation and pending old-context reads in `Studio/src/essentials/Elsa.Studio.Workflows/Client/src/__tests__/activityPlayback.test.tsx`, `activityPlaybackApi.test.ts` and `Studio/tests/browser/activity-playback.spec.ts`.

## Phase 7: Composed acceptance and delivery

- [X] T016 Establish genuine owned-host held-retry, owner-recorded retention-gap,52-minute backend-clock and permission-revocation witnesses; record their source/seed/authority boundaries in `specs/1226-activity-playback/quickstart.md`. Do not change timestamps or substitute evidence JSON. Add only the necessary owned acceptance support beside `Studio/tests/acceptance/` after its source composition is reviewed. Historical60a0 transport/live/coverage cases pass all four configurations; synthetic coarse history, internal unlocated gaps, natural dangling pins and upgrades remain explicitly unclaimed (research R34).
- [X] T017 Add the four real composed journeys in `Studio/tests/acceptance/activityPlaybackJourney.ts` and register them in `journeys.spec.ts` and `tools/ux-acceptance/run.py`; retain the current24 playback project executions, original320px and native390px narrow cases, exact identities/coverage/request manifests, PNG/ARIA and traces without weakening inherited inventory. Historical16-case execution passed at60a0 with132 required PNG/ARIA pairs, preserving the original120 (research R34).
- [x] T018 Run the complete affected suites, owner API/provider controls, architecture/maps/typecheck/lint and the current54 feature cases, including the36-case playback/prerequisite subset from `specs/1226-activity-playback/quickstart.md`; reconcile discovery/execution, obtain current-head downloadable artifacts and independent actual screenshot/trace inspection. Record failures/not-run states on #4366/the PR.
- [x] T019 Update `specs/1226-activity-playback/spec.md`, `tasks.md` and applicable public/catalog documentation to the actual qualified delivery state; remove the requirements checklist only in the final implementation PR that reaches terminal status. No partial slice sets Implemented.
- [x] T020 Complete exact-head independent source/acceptance review and the owner-authorized admin self-merge with CI deliberately skipped, verify actual resulting main and close the feature only after its full acceptance; record the merge/main receipt on the existing issue/PR and link it from the delivery evidence.

## Dependencies and parallel opportunities

`T001 → T002 → T003 → T004–T005 → US1 → US2 → US3 → US4 → T016–T020`. All20 tasks are required; US1 is the first useful increment, not the final scope.

Runtime prerequisites and Studio/playback are landed and qualified, including exact-head merges and actual resulting-main verification. CI is deliberately waived by the owner. The following parallel-work guidance describes implementation sequencing: Within US1, model tests and control layout can be prepared in separate files after the adapter/selection contract settles; US2 cadence assertions can be prepared separately from control markup; US3 adapter read tests and native range keyboard tests can be prepared separately; US4 owner access/coverage controls and client retirement tests can be prepared separately. These are conditional opportunities, not permission to race edits to the same hook/workbench. The conservative task list leaves those shared-source changes sequential and marks no `[P]` task before its prerequisites are complete.

## Current execution boundary

T001–T020 are complete on their recorded sources. Actual merge/main verification and downloaded evidence close T020, with earlier failed/held attempts preserved separately. The source-bound corpus and actual downloaded-byte/hash/render receipts supersede earlier local-only preparation. Research R6–R36 retain the old technical handoffs, failures and separate source heads as history. The owner instruction permits corrective review/test continuation and skips CI; actual human holds, product acceptance and source/main verification remain required.

All required tasks are complete. The [resulting-main receipt](../1211-run-history-timeline/evidence/2026-11-22-resulting-main.md) records the actual merges, source-bound verification, immutable downloads and issue closeout. CI was deliberately waived by the owner.
