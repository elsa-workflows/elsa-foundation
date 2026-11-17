# Tasks: Activity-by-activity execution playback

**Input**: [plan.md](plan.md), [research.md](research.md), [data-model.md](data-model.md), [dependency/UI contract](contracts/playback-inspection.md), [quickstart.md](quickstart.md).

**Authority**: Owner's2026-11-13 instruction authorizes plan/tasks and end-to-end implementation. **Current execution status: T001–T017 are locally complete. T003 technical readiness follows the explicit6185 handoff (research R11); implementation and genuine owned-host witnesses pass all16 playback and8 prerequisite cases at60a0 (research R34). T018–T020 remain open for final independent complete UX acceptance, coherent prerequisite publication, implementation PR/downloadable shared evidence, CI or authorized disposition, exact-head merge and resulting-main verification.** All four user stories remain required. Local assertion qualification does not establish feature delivery.

**Tests/cadence**: Existing risk-based Foundation discipline applies; no greenfield TDD mandate is declared. Add meaningful behavior tests with each logic-bearing implementation; reproduce existing-behavior fixes red/green. Use controlled presentation time, and distinguish real backend evidence from fixture bodies. No new test asserts documentation text.

## Phase 1: Setup and planning

- [X] T001 Inspect current activity browsing/history/selection contracts and record source-backed gaps and research decisions in `specs/1226-activity-playback/research.md`.
- [X] T002 Complete independent review of `specs/1226-activity-playback/plan.md`, `data-model.md`, `contracts/playback-inspection.md`, `quickstart.md` and `tasks.md`; preserve owner authority, coverage and actual prerequisite status.

## Phase 2: Foundational prerequisites

**Technical prerequisite**: T003 is now satisfied for local authoring on the qualified source. Its runtime-owned evidence cannot be replaced by timestamps, loaded-page arrays, signed-offset fabrication, another capture store or a test-only history response. Formal dependency publication/current-head shared CI remains required for delivery; the local handoff is no waiver.

- [X] T003 Coordinate and implement the separately reviewed runtime-owned facet with #3849 under the explicit dependency instruction. The qualified local product base is `6185f61d4439f956ea98905f0a60f974ab842617`; research R6–R11 preserves source ownership, exact backend/provider/API heads, admission/order/frozen-bound/ordinal/gap/authorization, migrations and restart controls. Complete corrected backend536 passes9,109 owning cases/622 architecture; current6185 passes8 real composed prerequisite cases and three independent local actual-image lenses. The [explicit handoff](https://github.com/elsa-workflows/elsa-foundation/issues/4366#issuecomment-6873192940) releases only `WorkflowInstances.tsx`, `run-history/RunHistoryTimeline.tsx`, `HistoryInspection.tsx` and `useHistoryResource.ts`, plus private playback source/tests/spec1226. T003 technical readiness permits local T004–T020 authoring. Formal delivered-publication/current-head CI receipt remains open under T018/T020; no deployment/production acceptance is implied.
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
- [X] T011 [US2] Verify four speeds, pause-before-queued-tick, no catch-up, manual inspection, delayed confirmation, buffering and reduced motion with controlled presentation time in `Studio/src/essentials/Elsa.Studio.Workflows/Client/src/__tests__/activityPlayback.test.tsx` and `Studio/tests/browser/activity-playback.spec.ts`. Supporting4007-client execution includes all46 playback hook cases; real composed transport passes4/4 at60a0 with reduced motion and all four paces (research R34).

## Phase 5: US3 — Bounded ordinal seeking

**Goal/test**: First/middle/final/older repeated occurrence in a200-occurrence run, across pages and ten rapid seeks, resolves exactly the final requested target without whole-history preload or stale details.

- [X] T012 [US3] Implement native labelled ordinal scrubbing and bounded target/page loading in `Studio/src/essentials/Elsa.Studio.Workflows/Client/src/workflow-editor/ActivityPlaybackControls.tsx`, `useActivityPlayback.ts` and `src/api/activityPlayback.ts`; manual seeks pause and loading never impersonates confirmation.
- [X] T013 [US3] Add random-position/page-boundary, latest-request-wins, unknown-total, failure/retry and no-preload regressions in `Studio/src/essentials/Elsa.Studio.Workflows/Client/src/__tests__/activityPlayback.test.tsx`, `activityPlaybackApi.test.ts` and `Studio/tests/browser/activity-playback.spec.ts`.

## Phase 6: US4 — Evidence, live bounds and access

**Goal/test**: Frozen live membership, true gaps/coverage, concurrency and protected contexts remain truthful. No implied causal path, historical state reconstruction or cross-context disclosure.

- [X] T014 [US4] Integrate admission/segment boundaries, explicit paused refresh, new-record indication and context/access invalidation in `Studio/src/essentials/Elsa.Studio.Workflows/Client/src/workflow-editor/useActivityPlayback.ts` and `WorkflowInstances.tsx`; reuse existing conservative `executionMotion.ts` predicates without inferring edges from adjacency.
- [X] T015 [US4] Cover concurrent/tied order, retained gaps, coarse/legacy/unavailable recording, missing pin, revocation and pending old-context reads in `Studio/src/essentials/Elsa.Studio.Workflows/Client/src/__tests__/activityPlayback.test.tsx`, `activityPlaybackApi.test.ts` and `Studio/tests/browser/activity-playback.spec.ts`.

## Phase 7: Composed acceptance and delivery

- [X] T016 Establish genuine owned-host held-retry, owner-recorded retention-gap,52-minute backend-clock and permission-revocation witnesses; record their source/seed/authority boundaries in `specs/1226-activity-playback/quickstart.md`. Do not change timestamps or substitute evidence JSON. Add only the necessary owned acceptance support beside `Studio/tests/acceptance/` after its source composition is reviewed. Actual60a0 transport/live/coverage cases pass all four configurations; synthetic coarse history, internal unlocated gaps, natural dangling pins and upgrades remain explicitly unclaimed (research R34).
- [X] T017 Add the four real composed journeys in `Studio/tests/acceptance/activityPlaybackJourney.ts` and register them in `journeys.spec.ts` and `tools/ux-acceptance/run.py`; retain the current24 playback project executions, original320px and native390px narrow cases, exact identities/coverage/request manifests, PNG/ARIA and traces without weakening inherited inventory. All16 pass at60a0 with132 required PNG/ARIA pairs, preserving the original120 (research R34).
- [ ] T018 Run the complete affected suites, owner API/provider controls, architecture/maps/typecheck/lint and the current36 composed playback/prerequisite cases from `specs/1226-activity-playback/quickstart.md`; reconcile discovery/execution, obtain current-head downloadable artifacts and independent actual screenshot/trace inspection. Record failures/not-run states on #4366/the PR.
- [ ] T019 Update `specs/1226-activity-playback/spec.md`, `tasks.md` and applicable public/catalog documentation to the actual qualified delivery state; remove the requirements checklist only in the final implementation PR that reaches terminal status. No partial slice sets Implemented.
- [ ] T020 Complete the independent source/acceptance review loop and exact-head required CI for #4366, use the authorized protected merge path, verify resulting main and close the feature only when its full acceptance is established; record the receipt on the existing issue/PR and link it from `specs/1226-activity-playback/quickstart.md`.

## Dependencies and parallel opportunities

`T001 → T002 → T003 → T004–T005 → US1 → US2 → US3 → US4 → T016–T020`. All20 tasks are required; US1 is the first useful increment, not the final scope.

Final delivery remains gated by dependency publication/shared CI. Local implementation proceeds after the explicit qualified T003 handoff. Within US1, model tests and control layout can be prepared in separate files after the adapter/selection contract settles; US2 cadence assertions can be prepared separately from control markup; US3 adapter read tests and native range keyboard tests can be prepared separately; US4 owner access/coverage controls and client retirement tests can be prepared separately. These are conditional opportunities, not permission to race edits to the same hook/workbench. The conservative task list leaves those shared-source changes sequential and marks no `[P]` task before its prerequisites are complete.

## Current execution boundary

T003 is technically satisfied for local dependent authoring, with the explicit source/base handoff in research R11. Historical60a0 qualification and corrected8e77 R13 acceptance retain their exact source attribution; the current public-main integration earns fresh owning-suite and composed evidence. Research R36 records the finite integration and the direct owner grant removing retry-count permission stops for this delivery and required #3849 dependencies. Reasonable independent review continues with cumulative R1–R13 preserved. T018–T020 remain incomplete until a coherent released backend parent, current-source composed acceptance, downloadable shared evidence, PR/CI disposition, eligible merge and resulting-main verification are established. Runtime/provider source remains with #3849; no unrelated work or acceptance gate is waived.
