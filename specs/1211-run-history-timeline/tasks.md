# Tasks: Timeline workspace and durable run history

**Input**: [spec.md](spec.md), [plan.md](plan.md), [research.md](research.md), [data-model.md](data-model.md), [contracts](contracts/), [quickstart.md](quickstart.md).

**Authority**: #3849 end-to-end; existing draft #3850. All tasks below belong to this one delivery. Checkmarks mean completed with recorded evidence, not merely planned. Risk-based tests/causal controls follow ADR 0080 D1; no mandatory TDD cadence is inferred.

## Phase 1 — Setup and reviewed boundary

- [x] T001 Record owner delivery authority and preserve the selected reference in specs/1211-run-history-timeline/spec.md and design/timeline-workspace.md.
- [x] T002 Inspect actual capture/coalescing, provider/query/access and Studio/acceptance seams and consolidate decisions in specs/1211-run-history-timeline/research.md.
- [x] T003 Review and freeze cross-layer candidate/record/policy/query contracts and dependency map in specs/1211-run-history-timeline/contracts/ and plan.md, resolving replay-after-enable, required-policy refusal, deletion races, empty search windows and unknown creation history.

## Phase 2 — Shared foundation

**Goal**: One stable provider-independent participant/query boundary with working atomic in-memory behavior. Complete before production story integration.

- [x] T004 Compatibly extend RuntimeHistoryEvent and add policy/batch/candidate/head/bookmark/query contracts and public API records in Essentials/src/Workflows/Runtime/Core/Models/RuntimeHistoryEvent.cs, Models/RuntimeHistory.cs, Contracts/IRuntimeHistoryStore.cs and PublicAPI.Unshipped.txt.
- [x] T005 Add an optional ordered history participant, preserve it through every clone/fold, and conditionally fingerprint it without changing history-free replay shapes in Essentials/src/Workflows/Runtime/Core/Models/RuntimeCheckpointCommit.cs, RuntimeCheckpointCommitFingerprint.cs and Elsa.Workflows.Runtime/Services/Coalescing/RuntimeCheckpointFold.cs.
- [x] T006 Pin capture policy at actual new admission/start and carry it through run/queued work/retry in Essentials/src/Workflows/Runtime/Core/Models/WorkflowExecutionState.cs and the existing start/dispatch payload and admission handlers; keep legacy absent-policy replays uncaptured.
- [x] T007 Add exact history writer/read-store qualification and decorator forwarding in Essentials/src/Workflows/Runtime/Core/Contracts/IRuntimeCheckpointCommitStore.cs and Elsa.Workflows.Runtime/Services/{Checkpoints,Coalescing}; required-policy capture must fail closed without forcing extra persistence boundaries.
- [x] T008 Implement deterministic safe capture, explicit composition and atomic in-memory head/events/bookmark staging in Essentials/src/Workflows/Runtime/Elsa.Workflows.Runtime/Services/History/, Services/Checkpoints/InMemoryRuntimeCheckpointCommitStore.cs and existing Runtime registration/options; add focused contracts/coalescing/replay/registration tests in Essentials/tests/Workflows/Runtime/Tests/.

**Checkpoint**: Source-bound tests prove intermediate transitions survive folding, old fingerprints stay compatible, no phantom facts survive rollback, unsupported/wrong readers refuse and Ephemeral no-record cadence remains unchanged.

T005 and T007 were reopened after published-head CI exposed continuation separation and eager service-resolution errors. The corrected top-level write sidecar, independently buffered coalescing and lazy qualification passed 212 normal Runtime controls (including the unchanged invariant) and both failing Architecture checks; API passed 314/314. These tasks are closed again for this slice; final whole-feature/current-head gates remain open.

## Phase 3 — US1: run chronology

**Independent test**: Real approval wait/resume shows linked chronological milestones in Timeline and preserves exact selection when switching Diagram.

- [x] T009 [US1] Add authorized history page/exact-event handlers, requests/views/endpoints and optional capability relations in Essentials/src/Workflows/Runtime/Api/{Handlers,Requests,Models,Endpoints,Capabilities} using existing structure authority and missing/denied behavior.
- [x] T010 [US1] Add source-bound run/wait/incident history API behavior tests in Essentials/tests/Workflows/Runtime/Api/Tests/ including unknown kinds and explicit uncaptured/disabled coverage.
- [x] T011 [P] [US1] Add capability-aware client contracts and bounded stale-preserving history observation in Studio/src/essentials/Elsa.Studio.Workflows/Client/src/api/, workflowTypes.ts and workflow-editor/run-history/.
- [x] T012 [US1] Integrate primary Timeline/Diagram, explicit scope/filter/search and event/causation inspection in Studio/src/essentials/Elsa.Studio.Workflows/Client/src/workflow-editor/WorkflowInstances.tsx and workflow-editor/run-history/ with public Studio tokens.
- [x] T013 [US1] Preserve relevant pinned/current diagram labels, persistence/granularity help and resolved-incident history in Studio/src/essentials/Elsa.Studio.Workflows/Client/src/workflow-editor/ and workflowInstanceInspection.ts.
- [x] T014 [US1] Add full run-workbench/history observation regressions and update deliberate default-view assumptions in Studio/src/essentials/Elsa.Studio.Workflows/Client/src/__tests__/ and Studio/tests/browser/workflow-run-detail-layout.spec.ts.

## Phase 4 — US2: concrete activity journal

**Independent test**: Earlier/parallel/nested/retried execution remains exact through journal/data navigation and refresh.

- [x] T015 [US2] Add exact execution/descendant history scoping through the existing hierarchy/read authority in Essentials/src/Workflows/Runtime/Api/Handlers/ and Runtime history stores, with isolation tests in Essentials/tests/Workflows/Runtime/Api/Tests/.
- [x] T016 [US2] Add activity summary plus Journal/Bookmarks/Data tabs while preserving existing execution browser and phase-specific value controls in Studio/src/essentials/Elsa.Studio.Workflows/Client/src/workflow-editor/WorkflowActivityExecutionDetails.tsx and run-history/.
- [x] T017 [US2] Preserve exact event/bookmark/execution view links and back context in Studio/src/essentials/Elsa.Studio.Workflows/Client/src/runSelectionUrl.ts, workflow-editor/pages.tsx and existing attached selection/preparation paths.
- [x] T018 [US2] Test A→B→A, older exact links outside initial pages, descendant/attempt boundaries and no implicit payload reads in Studio/src/essentials/Elsa.Studio.Workflows/Client/src/__tests__/workflowActivityExecutionDetails.test.tsx and related exact-selection tests.

## Phase 5 — US3: bookmark lifecycle

**Independent test**: Multiple/rearmed handles retain exact historical creation and disposition; expiry remains eligibility only.

- [ ] T019 [US3] Implement selected initial-claim consumption, sibling retirement, stale cleanup, redelivery-without-consumption and unknown-prior lifecycle projection in Essentials/src/Workflows/Runtime/Elsa.Workflows.Runtime/Services/History/ and existing bookmark claim/cancellation paths.
- [x] T020 [US3] Add bounded activity bookmark inventory/exact lifecycle API and server observation-time eligibility in Essentials/src/Workflows/Runtime/Api/{Handlers,Models,Endpoints} and history stores.
- [x] T021 [P] [US3] Add historical bookmark list/lifecycle/causal links with active-versus-lifetime counts in Studio/src/essentials/Elsa.Studio.Workflows/Client/src/workflow-editor/run-history/.
- [ ] T022 [US3] Add real claim/rearm/cancel/expiry/unknown-creation and UI lifecycle behavior coverage in Essentials/tests/Activities/Runtime/Tests/WorkflowResumeBookmarkSchedulerWorkHandlerTests.cs, Runtime history tests and Studio Workflows __tests__/.

## Phase 6 — US4: actual delivery and causation

**Independent test**: Valid/duplicate/competing/unmatched/faulting deliveries expose actual observations and effects without false continuation.

- [x] T023 [US4] Define and implement an independently authorized durable observation append with shared sequence CAS, stable identity and run-deletion guard in Essentials/src/Workflows/Runtime/Core/Contracts/ and runtime history providers; never advance runtime capture coverage from this append.
- [ ] T024 [US4] Record actual attributable receipt/admission/disposition in Essentials/src/Workflows/Runtime/Elsa.Workflows.Runtime/Services/Bookmarks/BookmarkResumeDispatcher.cs and existing owned delivery/admission paths, preserving source authority and no raw payload/capability data.
- [ ] T025 [US4] Complete supported timer, child/broadcast, incident resolution, retry/recovery, control/alteration, compensation and metadata-only value links through their actual semantic owners and the catalog in Essentials/src/Workflows/Runtime/Elsa.Workflows.Runtime/Services/History/.
- [ ] T026 [US4] Prove duplicate/competing delivery, consumption followed by fault, unmatched targeted receipt and legitimate multiple recipients in Essentials/tests/Workflows/Runtime/Tests/, Essentials/tests/Activities/Runtime/Tests/ and relevant API tests, with causal failing controls.
- [x] T027 [US4] Complete event provenance/causation/related-run details in Studio/src/essentials/Elsa.Studio.Workflows/Client/src/workflow-editor/run-history/ and test permission-safe links/fault distinctions in Workflows __tests__/.

## Phase 7 — US5: durable storage and trustworthy coverage

**Independent test**: New processes/providers preserve original committed identities/order and fail closed on capture failure; gaps/legacy/no-record are honest.

- [x] T028 [P] [US5] Add scoped head/event/bookmark entities, indexes/configuration, provider-neutral content mapping and schema-family declarations in Essentials/src/Workflows/Runtime/Persistence/EntityFrameworkCore/{Entities,Configuration,RuntimeDbContext.cs,RuntimeProviderContexts.cs,AssemblyInfo.cs}.
- [x] T029 [US5] Stage head allocation/events/bookmarks in the existing transaction/marker with bounded head-contention handling and actual selected reader qualification in Essentials/src/Workflows/Runtime/Persistence/EntityFrameworkCore/Stores/EfRuntimeCheckpointCommitStore.cs and DependencyInjection/.
- [x] T030 [US5] Add policy/coverage/late-or-disabled/purge state handling and owned run deletion in Essentials/src/Workflows/Runtime/Persistence/EntityFrameworkCore/Stores/, the in-memory deletion path and Runtime history contracts; required missing batches refuse, unsupported active policy changes remain explicit.
- [x] T031 [US5] Generate additive RunHistory migrations for all four providers through tools/ef/generate-module-migrations.sh --runtime-incremental and retain the existing Runtime Initial/history in Essentials/src/Workflows/Runtime/Persistence/EntityFrameworkCore/Migrations/.
- [x] T032 [US5] Prove staging rollback, ambiguous post-commit acknowledgement, replay, first-head/CAS races, stale fences, reader mismatch, scope drift and deletion-observation races in Essentials/tests/Workflows/Runtime/Persistence/EntityFrameworkCore/Tests/ with actual failure interceptors and fresh contexts.
- [x] T033 [US5] Qualify fresh install and frozen pre-change upgrades, schema validation and supported provider concurrency in existing Runtime EF ProviderTests and Essentials/tests/Persistence/EntityFrameworkCore/Migrations/{Tests,ProviderTests}/.
- [x] T034 [US5] Prove all persistence modes/physical cadence and legacy-marker replay after history enablement in Essentials/tests/Workflows/Runtime/Tests/RuntimeCheckpointCoalescingTests.cs and EF contract tests.
- [x] T035 [US5] Render uncaptured/disabled/late/purged/invalid/stale coverage, atomic group and exact instant details in Studio/src/essentials/Elsa.Studio.Workflows/Client/src/workflow-editor/run-history/ and meaningful state regressions.

## Phase 8 — US6: bounded histories and live context

**Independent test**: 10,000 events, pages ≤100, exact deep links, empty text windows and concurrent appends preserve bounded retrieval and selection.

- [x] T036 [US6] Implement purpose-bound authenticated snapshot/catch-up continuations, indexed exact queries and bounded safe text-search windows in Runtime history stores and Essentials/src/Workflows/Runtime/Api/Handlers/; do not reuse the shared empty-page invariant.
- [x] T037 [US6] Implement bounded bookmark inventory continuation and honest counts/search coverage in runtime history providers and API views, avoiding unbounded grouping/count scans.
- [x] T038 [US6] Add 10,000-row keyset/deep-link/append/reconnect/empty-window/cursor-tamper and deterministic query-work tests in Essentials/tests/Workflows/Runtime/{Tests,Api/Tests,Persistence/EntityFrameworkCore/Tests}/.
- [x] T039 [US6] Complete live/pause/follow/new-events, stale retention, scoped caching and back/scroll restoration in Studio/src/essentials/Elsa.Studio.Workflows/Client/src/workflow-editor/run-history/ with observer/URL regressions.

## Phase 9 — US7: access and composed UX acceptance

**Independent test**: Real normal composition, fresh durable restart, metadata/payload/cross-tenant identities, keyboard and both themes at desktop/320px.

- [x] T040 [US7] Prove pre-persistence safe capture and independent structure/value/payload grants, subject/tenant/scope-bound cursors and no protected identifiers via search/counts/links in existing Runtime/API/EF test projects.
- [ ] T041 [US7] Complete public-token styling, focus/tab semantics, narrow stacked inspector and all required loading/empty/error/denied states in Studio Workflows run-history components and selected browser regressions.
- [x] T042 [US7] Add the real User Tasks approval/history/restart journey and exact screenshot/trace inventory to Studio/tests/acceptance/ and tools/ux-acceptance/run.py, including 320px and light/dark; preserve owned process/store lifecycle.
- [ ] T043 [US7] Commit the complete candidate, run the added composed journey and inspect source-bound captures for waiting/completed/fault/duplicate/expiry/partial/denied/stale cases under specs/1211-run-history-timeline/evidence/ and the normal downloadable CI artifact.
- [ ] T044 [US7] Obtain independent current-head UX acceptance using the actual owned host and downloaded screenshots/traces; fix/retest findings and retain the receipt on PR #3850 and feature evidence.

## Phase 10 — Integration, review and closeout

- [x] T045 Update touched public API baselines, Runtime extension-point catalogs, root index if needed and capability/module documentation under Essentials/src/Workflows/Runtime/ and EXTENSION_POINTS.md; no generated maps on main.
- [ ] T046 Reconcile current main and live overlapping #3828/#3847 source changes without wholesale branch copying; run affected whole .NET/Workflows/frontend/browser/provider suites plus required architecture/maps/locked restore gates, recording exact source and test counts in specs/1211-run-history-timeline/evidence/.
- [ ] T047 Run separate independent correctness, repository-standards and acceptance/spec-and-UX lenses on the exact draft #3850 head without sharing reviewer output; fix actionable findings in additive commits, repeat affected qualification and record numbered rounds (maximum six) under the organization standing self-merge protocol and repo evidence rules.
- [ ] T048 Update final PR title/body to delivered behavior with Fixes #3849, source-bound acceptance evidence and actual limitations; mark spec Implemented in specs/1211-run-history-timeline/spec.md only for the final fully delivered merge candidate.
- [ ] T049 Mark PR ready only after review converges; obtain green required exact-head checks, inspect independent actual UI evidence, post the standing audit comment/self-merged label and perform the normal protected merge through the author App. Do not bypass holds/protection or incomplete acceptance; no external Code Review/personal approval wait is required under standing authorization.
- [ ] T050 Qualify resulting main/post-merge CI, update #3849 criteria and final evidence, close completed work with landed commit/PR and mark the delivery goal complete only when all required work above is done.

## Component qualification notes

The completed Studio implementation tasks are supported by Workflows whole/focused tests, typecheck/build/lint/budget checks, strict owned browser support and the early clean358native12/12 receipt. They do not close T043–T044: settled visible captures, expanded diagnostics/access/long-history scenes, final clean candidate and downloaded independent visual acceptance remain open. Finalv3 owner provenance is still integrating; exact labels/data will be requalified.

The completed EF component tasks are supported by40 focused cases (39+actualold-writer replay), three real server-provider contracts and four canonical migration model/script controls, zero skips. Source/discharge race tests use both causal orders under actual default isolation. The actual oldinspection/value export carries prefeature writer/hash provenance; retained marker/native/inspection bytes replay without history allocation. The original286row upgrade archive is unchanged. Final v3 integration/current-head whole gates remain open. T030 also has the corrected memory marker-content purge/replay controls in the passing 3,417-test Runtime suite. T034 now includes the actual published custom ReplaySafe schedule/retire/no-record journey in the passing 27-test Scheduling suite. Final current-head gates remain open.

## Dependencies and parallel work

T001–T003 precede shared foundation T004–T008. Capture-policy pinning and preserved fingerprints precede enabling any production history. Every story requires the shared foundation. US1 exposes the common read/selection boundary; US2/US3/US4 then add their exact scopes/semantics. US5 EF implementation can proceed beside Studio/API work after shared contracts freeze, with no overlapping source edits. US6 extends those read paths; US7 acceptance integrates all required journeys. Closeout depends on every feature requirement and acceptance gate, not just an MVP.

Parallel opportunities: after T004–T008, one worker owns EF/provider files (T028 onward), another owns Studio run-history components (T011 onward), and the root owns API integration/semantic reconciliation. Shared Core, public API baselines and WorkflowInstances.tsx edits are serialized and coordinated. [P] tasks only run when their listed prerequisites are complete. No independent solution builds compete outside the build-slot wrapper.

Implementation strategy: prove the approval wait/resume slice first, then exact scopes/lifecycles, delivery/failure semantics, provider recovery, long-history/access and composed acceptance. A partial slice remains In progress and cannot close #3849. This task list creates no child issues or new program.
