# Tasks: Activity-by-activity execution playback

**Input**: [plan.md](plan.md), [research.md](research.md), [data-model.md](data-model.md), [dependency/UI contract](contracts/playback-inspection.md), [quickstart.md](quickstart.md).

**Authority**: Owner's2026-11-13 instruction authorizes plan/tasks and end-to-end implementation. **Current execution status: T003 has passing Runtime/API and isolated EF component qualification, with installed-schema, native-provider, persisted-IAM, real runtime capture and process-restart proof added, while current-main production composition remains incomplete; dependent playback implementation has not begun.** All four user stories remain required. Do not mark a fixture prototype or an unavailable-only UI as feature completion.

**Tests/cadence**: Existing risk-based Foundation discipline applies; no greenfield TDD mandate is declared. Add meaningful behavior tests with each logic-bearing implementation; reproduce existing-behavior fixes red/green. Use controlled presentation time, and distinguish real backend evidence from fixture bodies. No new test asserts documentation text.

## Phase 1: Setup and planning

- [X] T001 Inspect current activity browsing/history/selection contracts and record source-backed gaps and research decisions in `specs/1226-activity-playback/research.md`.
- [X] T002 Complete independent review of `specs/1226-activity-playback/plan.md`, `data-model.md`, `contracts/playback-inspection.md`, `quickstart.md` and `tasks.md`; preserve owner authority, coverage and actual prerequisite status.

## Phase 2: Foundational prerequisites

**Blocking**: No user-story implementation begins until T003 is complete. Its missing source cannot be replaced by timestamps, loaded-page arrays, signed-offset fabrication, another capture store or a test-only history response.

- [ ] T003 Coordinate and implement the separately reviewed runtime-owned facet with #3849's S02–S05/S11–S14 owners under the owner's explicit dependency-implementation instruction. Agree the exact integration base and owned source/PR boundary before modifying active shared slices; implement and qualify the contract/provider/API additions against `specs/1226-activity-playback/contracts/playback-inspection.md`, including its source-derived membership/retention controls. Record actual base/head, paths and passing admission/order/frozen-bound/ordinal-seek/gap/authorization controls in `research.md`. The accepted S14a handoff fixes base `f2638c8ac8f23cb3f08071db4d68b793a84d9217`; source authoring is underway in `agent/codex/3849-history-occurrence-facet`. S14b private EF projection is independently source-reviewed in #4408 at `0f68c9ae6f9e0b5e490f30961ad5c84ed3d2a8f5` on owner base `6aaeb8187df504145a3c108ad4319e6cd83f912d`. Actual complete component results (Runtime4193/API399, S14b EF2086/native272, S14c historical clean EF2208/native275; owning deferred EF2211/native275; selected adapter clean EF2301/native278, no failures or skips after documented corrections) are recorded in research R6. S14c reader #4418 historically at `ccbc76998971d99184fc61302beb6bb729623619` on agreed `700f41ff6767f9b2dbfb0162221a8980b7ac96af` is independently reviewed and component-qualified. Owning deferred correction d79, selected reader #4442 at da6, owner E/F actual qualification and official G generation handoff are recorded in research R7. Research R8 records actual generated-schema installation, native provider publication/contention, public reader binding and persisted-IAM HTTP qualification on their separate exact heads. Research R9 records actual owner mutation/deletion, public pairing/model guards, strict native342 with real authored capture, and complete EF2411 including eight-process restart qualification. Research R10 records current-main official schema, genuine shell enrollment and complete integrated baseline Runtime4406/API431/EF2694/native342, plus later canonical Event integration and Activities881 qualification. Final current-source receiver/transport/capture-choice controls, architecture/graph and actual composed-host qualification remain open; no component or discovery result closes T003.
- [ ] T004 Add the private, cancellable capability-discovered sequence adapter in `Studio/src/essentials/Elsa.Studio.Workflows/Client/src/api/activityPlayback.ts`, using only the qualified T003 facet and exact string/opaque ordering identities.
- [ ] T005 Add adapter behavior tests for unsupported/denied/unknown totals, context binding, gap/retention changes, stale responses and safe positions in `Studio/src/essentials/Elsa.Studio.Workflows/Client/src/__tests__/activityPlaybackApi.test.ts`.

## Phase 3: US1 — Manual exact-occurrence traversal

**Goal/test**: Previous/Next and direct selection traverse the exact run/scope sequence, including nested/loop/retry identities, while Timeline/focus and explicit Activity details remain coherent.

- [ ] T006 [US1] Implement the private presentation/cursor model and hook in `Studio/src/essentials/Elsa.Studio.Workflows/Client/src/workflow-editor/activityPlaybackModel.ts` and `useActivityPlayback.ts`; keep confirmed and pending selection distinct.
- [ ] T007 [US1] Add labelled controls with previous/next, position/timestamp and truthful disabled/unavailable states in `Studio/src/essentials/Elsa.Studio.Workflows/Client/src/workflow-editor/ActivityPlaybackControls.tsx`, using Studio public controls/tokens and existing styles.
- [ ] T008 [US1] Integrate confirmed cancellable occurrence/scope navigation and synchronous manual-navigation pause into `Studio/src/essentials/Elsa.Studio.Workflows/Client/src/workflow-editor/WorkflowInstances.tsx`; coordinate with spec1211 S06/S07 and preserve current timeline scope, pinned graph, ambiguous-occurrence choice and focus behavior.
- [ ] T009 [US1] Add mounted selection/boundary/identity/Activity-return regressions in `Studio/src/essentials/Elsa.Studio.Workflows/Client/src/__tests__/activityPlayback.test.tsx` and keyboard/pointer states in `Studio/tests/browser/activity-playback.spec.ts`.

## Phase 4: US2 — Play/pause and presentation pace

**Goal/test**: Every eligible step appears once at the four defined cadences; pause, Activity inspection, manual navigation, backgrounding and end stop further ticks. Normal page buffering resumes only the same still-playing generation.

- [ ] T010 [US2] Add cancellable timer ownership, speed selection, end/Replay and visibility pause in `Studio/src/essentials/Elsa.Studio.Workflows/Client/src/workflow-editor/useActivityPlayback.ts` and `ActivityPlaybackControls.tsx`; keep live-update/runtime controls separate.
- [ ] T011 [US2] Verify four speeds, pause-before-queued-tick, no catch-up, manual inspection, delayed confirmation, buffering and reduced motion with controlled presentation time in `Studio/src/essentials/Elsa.Studio.Workflows/Client/src/__tests__/activityPlayback.test.tsx` and `Studio/tests/browser/activity-playback.spec.ts`.

## Phase 5: US3 — Bounded ordinal seeking

**Goal/test**: First/middle/final/older repeated occurrence in a200-occurrence run, across pages and ten rapid seeks, resolves exactly the final requested target without whole-history preload or stale details.

- [ ] T012 [US3] Implement native labelled ordinal scrubbing and bounded target/page loading in `Studio/src/essentials/Elsa.Studio.Workflows/Client/src/workflow-editor/ActivityPlaybackControls.tsx`, `useActivityPlayback.ts` and `src/api/activityPlayback.ts`; manual seeks pause and loading never impersonates confirmation.
- [ ] T013 [US3] Add random-position/page-boundary, latest-request-wins, unknown-total, failure/retry and no-preload regressions in `Studio/src/essentials/Elsa.Studio.Workflows/Client/src/__tests__/activityPlayback.test.tsx`, `activityPlaybackApi.test.ts` and `Studio/tests/browser/activity-playback.spec.ts`.

## Phase 6: US4 — Evidence, live bounds and access

**Goal/test**: Frozen live membership, true gaps/coverage, concurrency and protected contexts remain truthful. No implied causal path, historical state reconstruction or cross-context disclosure.

- [ ] T014 [US4] Integrate admission/segment boundaries, explicit paused refresh, new-record indication and context/access invalidation in `Studio/src/essentials/Elsa.Studio.Workflows/Client/src/workflow-editor/useActivityPlayback.ts` and `WorkflowInstances.tsx`; reuse existing conservative `executionMotion.ts` predicates without inferring edges from adjacency.
- [ ] T015 [US4] Cover concurrent/tied order, retained gaps, coarse/legacy/unavailable recording, missing pin, revocation and pending old-context reads in `Studio/src/essentials/Elsa.Studio.Workflows/Client/src/__tests__/activityPlayback.test.tsx`, `activityPlaybackApi.test.ts` and `Studio/tests/browser/activity-playback.spec.ts`.

## Phase 7: Composed acceptance and delivery

- [ ] T016 Establish genuine owned-host held-retry, owner-recorded retention-gap,52-minute backend-clock and permission-revocation witnesses; record their source/seed/authority boundaries in `specs/1226-activity-playback/quickstart.md`. Do not change timestamps or substitute evidence JSON. Add only the necessary owned acceptance support beside `Studio/tests/acceptance/` after its source composition is reviewed.
- [ ] T017 Add the four real composed journeys in `Studio/tests/acceptance/activityPlaybackJourney.ts` and register them in `journeys.spec.ts` and `tools/ux-acceptance/run.py`; retain16 project executions,320px playback narrow cases, exact identities/coverage/request manifests, PNG/ARIA and traces without weakening inherited inventory.
- [ ] T018 Run the complete affected suites, owner API/provider controls, architecture/maps/typecheck/lint and the16 composed cases from `specs/1226-activity-playback/quickstart.md`; reconcile discovery/execution, obtain current-head downloadable artifacts and independent actual screenshot/trace inspection. Record failures/not-run states on #4366/the PR.
- [ ] T019 Update `specs/1226-activity-playback/spec.md`, `tasks.md` and applicable public/catalog documentation to the actual qualified delivery state; remove the requirements checklist only in the final implementation PR that reaches terminal status. No partial slice sets Implemented.
- [ ] T020 Complete the independent source/acceptance review loop and exact-head required CI for #4366, use the authorized protected merge path, verify resulting main and close the feature only when its full acceptance is established; record the receipt on the existing issue/PR and link it from `specs/1226-activity-playback/quickstart.md`.

## Dependencies and parallel opportunities

`T001 → T002 → T003 → T004–T005 → US1 → US2 → US3 → US4 → T016–T020`. All20 tasks are required; US1 is the first useful increment, not the final scope.

No production parallel release is possible while T003 is blocked. Within US1, model tests and control layout can be prepared in separate files after the adapter/selection contract settles; US2 cadence assertions can be prepared separately from control markup; US3 adapter read tests and native range keyboard tests can be prepared separately; US4 owner access/coverage controls and client retirement tests can be prepared separately. These are conditional opportunities, not permission to race edits to the same hook/workbench. The conservative task list leaves those shared-source changes sequential and marks no `[P]` task before its prerequisites are complete.

## Current stop point

T003 is an actual missing delivered dependency, not a waiver-able test failure. The owner has explicitly requested coordinated implementation of that prerequisite, rather than passive waiting for it. The implementation handoff is accepted on #3849 and source authoring has begun; retain its ownership and qualify the agreed facet on a recorded integration base before advancing the dependent tasks. End-to-end implementation, real UX acceptance, CI/merge and closeout are all NOT DONE. Do not present coordination or plan/task generation as fulfillment of the owner's complete implementation request.
