# Tasks: Finite HTTP ETL

Issue [#3151](https://github.com/elsa-workflows/elsa-foundation/issues/3151). One implementation branch/PR; phases are checkpoints, not separate worker claims. Paths below are relative to the repository. Root integrates and verifies bounded delegated work in isolated worktrees. Versioned foundation acceptance is checked below; full HTTP execution and product acceptance remain outstanding.

## Phase 1 — Setup and reviewed scope

- [x] T001 Inspect current source, claims, program dependencies and reserve spec996; record the supported profile in `specs/996-http-etl/spec.md` and `research.md`.
- [x] T002 Resolve source/design review findings and approve `specs/996-http-etl/plan.md`, `data-model.md` and `contracts/http-profile.md`; record current-main CI limitations separately and require their reconciliation before final delivery.

## Phase 2 — Compatibility and versioned foundation

- [ ] T003 Capture an authentic pre-change v1 published artifact/native workflow and persisted in-flight run fixture with a reserved/owed batch in `Extensions/Etl/tests/Shared/` with replay tests in `Extensions/Etl/tests/Tests/`; migrate and resume with the successor; preserve canonical bytes/hash/output and accounting.
  - Partial retained-artifact/store proof and outstanding successor-worker limitation: [checkpoint](evidence/v1-compatibility-checkpoint.md).
- [x] T004 Add connector-capable v2 model/codec/validation/compiler in `Extensions/Etl/src/Core/` and `src/Design/Elsa.Etl.Design/`, and dual-reader/native pin support without changing v1 canonical semantics; cover formats and strict parsing in ETL tests.
  - Partial connector model/codec/compiler proof and outstanding runtime integration: [checkpoint](evidence/v2-model-compiler-checkpoint.md).
  - Additive definition persistence, retained-v1 migration and native v2 pins: [checkpoint](evidence/v2-persistence-pin-checkpoint.md). Runtime execution remains owed.
  - Completed versioned foundation with runtime-only validation, typed transformation and retained terminal envelopes: [checkpoint](evidence/v2-runtime-reader-checkpoint.md). Connector requests still receive an explicit unavailable refusal until T005/T009 are integrated.
- [ ] T005 Add typed HTTP page/position/effect/retry store contracts in `Extensions/Etl/src/Core/Runtime/`, EF records/migrations in `src/Persistence/EntityFrameworkCore/` and provider upgrade/claim-shape tests; share current fence/quota/terminal invariants while retaining v1 provider composition.
  - Partial typed admission/preparation/ownership implementation: [checkpoint](evidence/connector-preparation-checkpoint.md). Page, reservation, effect and retry transitions remain owed.
  - Retained page attachment, request history and empty-page progression: [checkpoint](evidence/http-page-store-checkpoint.md). Nonempty batch/effect/retry transitions remain owed.
  - Indexed request ledger, transactional run-wide metadata budget and full recovery/terminal audits: [checkpoint](evidence/http-metadata-ledger-checkpoint.md). This addresses the preceding resource findings without closing T005.
  - Typed nonempty batch reservations, replay, quota accounting and replacement-worker recovery: [checkpoint](evidence/connector-reservation-checkpoint.md). Route effects, durable attempts/retries and atomic acceptance remain owed; T005 stays open.

## Phase 3 — US1: Author and execute HTTP integrations

Independent proof: real publication/selection plus HTTP->HTTP, HTTP->JSONL and CSV->HTTP, two independently progressing HTTP shards, and native Sequence/Flowchart continuation.

- [ ] T006 [US1] Implement scoped revisioned HTTP binding pins and existing publication-permission and approved-alias validation in `Extensions/Etl/src/Runtime/Elsa.Etl.Runtime/Bindings/` and `src/Design/Api/`; compose existing Connections Core ports and test anonymous/authenticated scope, null-tenant authentication refusal without fallback, current-revision changes and unsupported composition.
- [ ] T007 [US1] Implement bounded JSON Pointer/record decoding and token/next-link pagination in `Extensions/Etl/src/Runtime/Elsa.Etl.Runtime/Http/`; add strict pointer, numeric/null/missing, duplicate-property, depth, byte, record, empty-page and loop tests in ETL tests.
  - Partial one-page decoding and local transform composition: [checkpoint](evidence/json-page-decoder-checkpoint.md). Durable request progression and loop detection remain owed.
- [ ] T008 [US1] Extend typed transform evaluation in `Extensions/Etl/src/Runtime/Elsa.Etl.Runtime/Transformation/` for JSON records and optional CSV lookup without CSV round-tripping; retain v1 behavior and prove fully filtered zero-effect acceptance.
- [ ] T009 [US1] Compose durable page acquisition, reservation and retained route bodies with HTTP/file delivery in `Extensions/Etl/src/Runtime/Elsa.Etl.Runtime/Execution/` and `Output/`; test all three connector combinations through real local HTTP fixtures.
- [ ] T010 [US1] Extend immutable v2 native publication pinning and actual persisted Sequence/Flowchart continuation in `Extensions/Etl/src/Workflows/` and `Extensions/Etl/tests/Tests/`; test runtime-only execution with design storage unavailable and retained revision/configuration behavior.
- [ ] T011 [US1] Extend scoped binding/definition APIs and connector-aware Studio authoring in `Extensions/Etl/src/Design/Api/` and `Extensions/Etl/studio/Elsa.Studio.Etl/Client/`; run actual create/validate/publish/select tests.

## Phase 4 — US2: Retain responsibility across interruption

Independent proof: two worker processes with shared SQLite/object storage and a real receiver recover at every named interruption boundary with stable request identity, no lost owed work and no double accounting.

- [ ] T012 [US2] Implement fenced page attachment, body/effect identity and route acknowledgement/acceptance in `Extensions/Etl/src/Persistence/EntityFrameworkCore/Stores/` and runtime execution; prove stale/conflicting/duplicate writes and unchanged budget accounting.
- [ ] T013 [US2] Extend shared fixtures and `Extensions/Etl/tests/WorkerHost/` for two-process HTTP recovery after page publication, reservation, first/second-route acknowledgement, lost response and before acceptance; cover deduplicating and ordinary receivers honestly.
- [ ] T014 [US2] Prove cache corruption/missing objects, changed uncommitted response, exhausted partitions, all-filtered batches and physical-attempt replacement in `Extensions/Etl/tests/Tests/`; run targeted safeguard-removal and byte-exact restoration controls.
- [ ] T015 [US2] Test cancellation/late acknowledgement/terminal races through ETL stores and actual native adapters in `Extensions/Etl/tests/Tests/`; no physical rollback or stopped-worker assertion.

## Phase 5 — US3: Durable delay and safe inspection

Independent proof: controlled time/endpoints show persistent Retry-After, released worker capacity and another partition progressing; Studio/HTTP inspection exposes safe state and correct terminal outcomes.

- [ ] T016 [US3] Implement durable attempt/deadline/not-before state and eligible claim selection in `Extensions/Etl/src/Runtime/Elsa.Etl.Runtime/Http/`, `Hosting/` and the EF store; test 408/429/selected5xx/network uncertainty, dates/deltas, exhaustion and restart without early dispatch.
- [ ] T017 [US3] Enforce synchronous acknowledgement, redirect/endpoint exact-path allowlist policy (including canonicalization, dot-segment and encoded-separator cases) and request-local auth in runtime HTTP execution; test 202/permanent rejection, connection drift/revocation/rotation, denied ETL publication/alias, header collisions and safe diagnostics in ETL tests, including observed ETL logger/trace/error output for query credentials and opaque cursors; distinguish platform HTTP activities and verify/document their host-owned default redaction boundary. Clarify the broad masking rule in `Essentials/src/Connections/Core/EXTENSION_POINTS.md` without changing its runtime or peer hardening.
- [ ] T018 [US3] Extend safe partition/effect/retry API views and Studio inspection in `Extensions/Etl/src/Runtime/Api/` and ETL Studio Client; test pagination/scope boundaries and absence of sensitive values.
- [ ] T019 [US3] Extend `e2e-tests/etl/` and `Studio/tests/live/etl-journey.spec.ts`; run rebuilt Workbench REST and actual browser journeys for authoring/continuation, retries and cancellation.

## Phase 6 — Final integration and evidence

- [ ] T020 Update affected public API baselines, extension catalogs, migration/provider inventory and current program/spec docs; run affected full suites, architecture/core and map/filter/diff checks. Record exact commands/results in `specs/996-http-etl/evidence/`.
- [ ] T021 Run independent correctness/standards/acceptance review of the integrated exact diff, fix concrete findings and rerun affected proof. Open/update the single #3151 draft PR with per-criterion evidence; require green exact-head checks before normal merge.
- [ ] T022 Verify resulting-main CI/Studio/CodeQL/maps and runtime delivery evidence, reconcile issue/parent/program/project state and release the exact claim. Preserve parent continuous/SQL/RabbitMQ/BPMN and task-runner boundaries in `docs/program-goals/unified-execution.md`.

## Dependencies and execution strategy

T001 -> T002 -> T003/T004 -> T005 -> US1 runtime composition -> US2/US3 recovery and operator behavior -> T020–T022. T012/T016 shape the storage transitions during T005; do not postpone durability until after shipping a happy path. Studio authoring can be developed in a separate worktree after v2 DTO/API contracts stabilize; root integrates it before live proof. Parser and store tests may be delegated separately once their contracts are reviewed. Heavy tests/builds run serially on this shared machine.

Use focused tests for each behavioral change, then each full affected suite once on the final candidate. This unit chooses test-first for codec/transition/retry logic under the owner's design delegation; it does not impose repository-wide TDD. US1 is an incremental checkpoint, not the issue's complete delivery. All three stories and final gates are required; no reduced MVP substitutes for them.
