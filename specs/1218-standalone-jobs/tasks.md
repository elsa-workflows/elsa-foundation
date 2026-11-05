# Tasks: Standalone durable jobs

**Input**: [spec](spec.md), [provisional plan](plan.md), [research](research.md),
[data model](data-model.md), [lifecycle](contracts/job-lifecycle.md) and [acceptance matrix](acceptance-matrix.md).

**Status**: Provisional task breakdown. No production task has started. Domain agreement and the
reviewed spec/plan gate must pass first. Tests are risk-based; application TDD cadence is undeclared,
so the template does not invent it. Required behavioral red/green bite-proofs remain obligations.

## M0 — Setup and design gates

- [x] T001 Record scoped owner authorization, source/provider research and selected decisions in `docs/program-goals/standalone-jobs.md` and `specs/1218-standalone-jobs/research.md`.
- [ ] T002 Agree ADR0096 and apply its additive domain/version/sync-impact delta in `.specify/memory/constitution.md` and `docs/reference/elsa-domain-tree.md`; record actual human authority and concurrent ADR0095 reconciliation.
- [ ] T003 Review and settle continuation permits, authority/time/lease/race protocol and linked manual retry in `specs/1218-standalone-jobs/contracts/job-lifecycle.md`; coordinate shared paths through #3582/#3583/#2892/#3815.
- [ ] T004 Review `specs/1218-standalone-jobs/spec.md`, `plan.md`, `data-model.md`, `tasks.md` and `ux-acceptance.md` together; resolve Constitution Check and record agreement before production code.

## Foundation — concrete contracts and package closure

- [ ] T005 Define actual handler/client/result/store and inspection extension contracts under `Extensions/Jobs/src/Core/`, public API baselines and `EXTENSION_POINTS.md`; keep Core free of implementation/workflow/provider weight.
- [ ] T006 Create justified runtime/EF/test projects under `Extensions/Jobs/`, composition features and locked references; add actual projects to `Elsa.Server.slnx`, `EXTENSION_POINTS.md` and `tools/ci/test-tiers.json`.
- [ ] T007 Implement scoped canonical identities/serialization limits and guarded EF state transitions under `Extensions/Jobs/src/Persistence/EntityFrameworkCore/`, using existing retry/schema policy and host-selected engines.
- [ ] T008 Prove conditional registration/duplicates, trusted scope/cursors and package boundaries in `Extensions/Jobs/tests/Tests/` and existing `Essentials/tests/Architecture/Tests/`; preserve compatibility for any extracted renewal/recurrence mechanics.

## M1 — US1 standalone vertical slice

**Goal**: one actual packaged producer/worker and useful recoverable result without workflow packages.

- [ ] T009 [US1] Implement pinned handler registration, idempotent durable client and bounded request/result dispatch in `Extensions/Jobs/src/Elsa.Jobs/`.
- [ ] T010 [US1] Implement independent worker hosting, fenced lease renewal, physical receipts and terminal/result responsibility in `Extensions/Jobs/src/Elsa.Jobs/` and its job-owned EF transitions.
- [ ] T011 [US1] Cover equal/conflicting key replay, advertised recognition expiry/cleanup with pending-obligation extension, lost acknowledgements, missing handler/schema support and restart in `Extensions/Jobs/tests/Tests/` and `tests/Persistence/ProviderTests/`.
- [ ] T012 [US1] Add immutable clean PackageReference producer/worker witness in `tools/jobs/`; record dependency closure, actual versions/hashes and useful result/restart evidence in `specs/1218-standalone-jobs/evidence/`.

## M2 — US1 schedules and US2 outage controls

**Goal**: every attempt uses one durable pause/concurrency/retry authority; real nodes preserve work.

- [ ] T013 [US1] Implement delayed/UTC interval/cron schedules and atomic occurrence/cursor responsibility under `Extensions/Jobs/src/Elsa.Jobs/` and `src/Persistence/EntityFrameworkCore/`; reuse agreed arithmetic without workflow activation state.
- [ ] T014 [US1] Cover default catch-up, bounded explicit replay, pause/materialization/edit races and preservation of accepted jobs in `Extensions/Jobs/tests/Persistence/ProviderTests/`.
- [ ] T015 [US2] Implement revisioned four-scope holds, atomic start permissions and leased queue/type/instance/definition limits under `Extensions/Jobs/src/Persistence/EntityFrameworkCore/`; index eligible discovery to skip held shared-queue work.
- [ ] T016 [US2] Implement durable retry/backoff, cancellation/absolute timeouts, graceful shutdown and linked authorized manual retry in `Extensions/Jobs/src/Elsa.Jobs/`; retain late physical receipts and local slots until exit.
- [ ] T017 [US2] Prove scope overlaps, every retry path, future/version coverage, pause/start ordering, capacity and unrelated same-queue progress in `Extensions/Jobs/tests/Persistence/ProviderTests/` with independent PostgreSQL processes and SQLite restart profile.
- [ ] T018 [US2] Prove stale-owner/lease/ackloss/clock-skew/timeout/cancel/shutdown failure controls and downstream idempotency witness in `tools/jobs/` and `Extensions/Jobs/tests/Integration/`; label physical-overlap limits precisely.

## M3 — US3 model-owned integration

**Goal**: the same handler works through actual native and genuine BPMN models with exact retained results.

- [ ] T019 [US3] Implement optional native activity, accepted handler/schema pin contribution, saved-wait checkpoint/outbox admission and cancellation-before-admission under `Extensions/Jobs/src/Workflows/` using existing model contracts.
- [ ] T020 [US3] Implement exact ResultId/ContinuationId permission and consumption proof in the optional native adapter under `Extensions/Jobs/src/Workflows/Runtime/`; preserve current delivered-elsewhere/coalescing refresh.
- [ ] T021 [US3] Implement the real BPMN invocation/input adapter at the reviewed model-owned seam under `Extensions/Jobs/src/Bpmn/` or the smallest agreed consumer module; add no kernel model-name switch or duplicate retry authority.
- [ ] T022 [US3] Qualify same-handler native/BPMN success/failure, held result-driven continuation, permission replay, parent termination/cancel-first, nested/detached association and acknowledgement-loss restart in `Extensions/Jobs/tests/Integration/` and `tools/jobs/`.

## M4 — US4 operations and Studio

**Goal**: useful authorized queue/schedule/job/attempt/result/hold operations in actual API and Studio.

- [ ] T023 [US4] Implement bounded CQS operations endpoints, trusted tenant/permission checks and audit/idempotent mutations under `Extensions/Jobs/src/Api/`; update actual endpoint/public API baselines.
- [ ] T024 [US4] Cover cross-tenant IDs/cursors/associations, denied mutations/results, shared-worker tenant/secret-binding isolation across retry/restart, redaction, retention and linked-JobId manual-retry recovery under `Extensions/Jobs/tests/Tests/` and `tests/Integration/`.
- [ ] T025 [US4] Implement optional Jobs module collections/detail/schedule editor and pause/recovery flows under `Extensions/Jobs/src/Studio/` using current shared controls and `specs/1218-standalone-jobs/ux-acceptance.md`.
- [ ] T026 [US4] Add feature-specific actual composed journeys under `tools/ux-acceptance/`, including empty/populated/loading/failure/denied, all holds, result/parent links, keyboard/focus, themes and narrow states; retain downloadable current-head artifacts.
- [ ] T027 [US4] Independently inspect the composed screenshots/traces and risky live interactions; fix/retest findings and post the exact UX receipt in the existing PR review loop.

## M5 — integrated qualification and delivery

- [ ] T028 Complete practical code/API/worker/operator documentation and executable package commands in `specs/1218-standalone-jobs/quickstart.md` and the appropriate existing product reference docs; preserve provider/idempotency/retention limits.
- [ ] T029 Run every required `specs/1218-standalone-jobs/acceptance-matrix.md` row against immutable packages, supported providers and actual composed models/UI; record passed/failed/skipped/cancelled/unavailable/incomplete separately.
- [ ] T030 Run affected whole projects with discovery/execution counts, behavioral bite-proofs, locked restore, architecture/maps/tier checks; post exact-head evidence and independent correctness/standards/acceptance reviews on the PR.
- [ ] T031 Complete current-head required CI and approved merge, then resulting-main package/provider/native/BPMN/UX qualification; update spec lifecycle in the finishing PR and reconcile #3586/program acceptance without premature closure.

## Dependencies and parallel opportunities

T002–T004 gate T005 onward. T005–T008 establish the shared production boundary before the standalone
slice. T009–T012 prove US1 before widening M2; T013–T018 establish complete attempt semantics before
M3. T019–T022 must prove actual model ownership before UI claims that behavior. M4 uses proven API/
runtime state; M5 requires every original criterion. A usable first vertical slice is an integration
checkpoint, not a scope reduction or completion of this first-release product.

Within a milestone, independent tests/docs/review lenses may run in parallel once their contracts are
committed. Source edits to persistence gates and model checkpoints share one integration lane. Do not
mark a task parallel merely because another worker is available; shared authority changes stay ordered.
