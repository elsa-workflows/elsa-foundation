# Tasks: Finite CSV-to-JSONL ETL

**State:** Approved execution checklist for [#3109](https://github.com/elsa-workflows/elsa-foundation/issues/3109). Domain admission/design review and existing-runtime coverage are complete; component/design-persistence implementation is in progress. The [foundation checkpoint](evidence/finite-file-foundations.md) records executed evidence and the remaining product gate. This is the one active program leaf; do not turn its checkboxes into competing issue claims.

## Phase 1 — Admission and source verification

- [x] T001 Adopt reviewed ADR 0087 under the owner's reaffirmed self-review/approval delegation; update the Elsa domain pin/purpose/version report in `.specify/memory/constitution.md` and `docs/reference/elsa-domain-tree.md`, retaining existing framework rules.
- [x] T002 Prove the existing wait boundary in `Essentials/tests/Workflows/Runtime/Tests/RuntimeCreateBookmarkSchedulerWorkHandlerTests.cs`: typed state/bookmark co-commit, legacy behavior and cancelled/stale delayed work. Root-reviewed focused run: 19 passed, 0 failed/skipped on 2026-10-20; no ETL production proof.
- [x] T003 Reconcile independent review of `specs/255-finite-file-etl/{spec,plan,research,data-model}.md`, `contracts/journey.md` and `quickstart.md`; pass both constitution gate indexes before implementation.

## Phase 2 — Foundations

- [ ] T004 Create the real package/test layout in `Extensions/Etl/{src,tests,studio}`, register its path bucket in `Essentials/tests/Architecture/Tests/ArchitectureGuardTests.cs`, and add solution/host composition entries only for actual features.
- [ ] T005 Implement version-1 immutable artifact/schema/topology and scoped design/runtime contracts in `Extensions/Etl/src/Core`; add tracked public API and strict unsupported-version/identity validation tests in `Extensions/Etl/tests/Tests`.
- [ ] T006 Define bounded host file binding options/revision resolution in `Extensions/Etl/src/Runtime` and test safe aliases, role/scope checks, required immutable input digests, pinned output revisions and refusal of authored paths.
- [ ] T007 Add separate design/runtime EF contexts/stores/schema modules in `Extensions/Etl/src/Persistence/EntityFrameworkCore`, reviewed EF admission, provider/migration inventory and lock files. Validate actual fresh-schema/reopen behavior without design-store execution dependencies.

## Phase 3 — US1: Author and publish

**Goal:** Save, validate and publish a supported definition whose artifact is independently runnable.
**Independent test:** Publish a fixture, modify its draft, and load the old immutable artifact from runtime-only composition.

- [x] T008 [US1] Add behavior tests in `Extensions/Etl/tests/Tests` for valid topology, duplicate/missing headers/keys, invalid types/filters/mapping, finite limits, draft concurrency and deterministic artifact hashing.
- [ ] T009 [US1] Implement definition commands/queries and validation in `Extensions/Etl/src/Design`; retain schema/operation order, separate draft and published state, and issue safe stable diagnostics.
- [ ] T010 [US1] Implement immutable artifact publication/retention through `Extensions/Etl/src/Design` and its EF store; prove replay/conflicting version refusal and unavailable dependency diagnostics.
- [ ] T011 [US1] Add authorized binding/definition/draft/validate/publish endpoints in `Extensions/Etl/src/Api`, capability/permission contributions and scope/concurrency behavior tests.

## Phase 4 — US2: Execute and return a native outcome

**Goal:** One native invocation executes one finite run; rows are transformed/routed correctly and the surrounding composite continues normally.
**Independent test:** Valid/filtered/rejected fixture through Sequence and Flowchart, with typed JSONL and one accepted continuation.

- [x] T012 [US2] Write bounded parser/transform tests in `Extensions/Etl/tests/Tests` before implementation: quoting/multiline/BOM/CRLF/strict UTF-8, record/field/lookup/output byte bounds, typed mapping/filtering and deterministic row diagnostics.
- [x] T013 [US2] Implement streaming CSV preparation, verified snapshots, logical-record index/ranges, exact-key lookup and transforms in `Extensions/Etl/src/Runtime`; avoid whole-file materialization.
- [ ] T014 [US2] Implement short idempotent run admission and separately recoverable preparation in `Extensions/Etl/src/Runtime` and its EF store; persist chosen input/output revisions before acknowledging admission.
- [ ] T015 [US2] Implement independently leased partitions and durable record/byte batch reservations in `Extensions/Etl/src/Runtime` and the EF store. Both quotas precede dispatch; only one owed batch per partition; retries retain identity/reservation.
- [ ] T016 [US2] Implement immutable bounded JSONL chunks, conflict checks, fenced acceptance/cursor/quota transaction and deterministic accepted manifest/materialization in `Extensions/Etl/src/Runtime` and its EF store.
- [ ] T017 [US2] Implement `RunEtl` and immutable typed invocation/result state in `Extensions/Etl/src/Workflows/Runtime`; derive admission from real `BookmarkCreated` commits using the existing enricher/outbox seam, with no kernel switch or mutable staging buffer.
- [ ] T018 [US2] Implement publication pinning/picker contribution in `Extensions/Etl/src/Workflows/Design`; retain the exact ETL artifact/hash and expose the activity only through the existing catalog.
- [ ] T019 [US2] Implement durable terminal compare-and-set/outbox and idempotent bookmark result delivery in the ETL runtime/native adapter; acknowledge consumption or demonstrated parent termination, not merely queueing.
- [ ] T020 [US2] Prove native Sequence sibling continuation and Flowchart completion plus business-error/fault/cancellation behavior in `Extensions/Etl/tests/Tests`, using real durable state and normal composite semantics.

## Phase 5 — US3: Recover and fence work

**Goal:** Worker loss, retries and cancellation preserve accepted state and owed work.
**Independent test:** Two real worker processes over a shared fixture filesystem/database, deterministic interruption barriers and durable-state assertions.

- [ ] T021 [US3] Implement live worker record/byte permits and cancellation/unwind cleanup in `Extensions/Etl/src/Runtime`; test that a replaced lease does not release a paused task's capacity and a successor acquires its own permit.
- [ ] T022 [US3] Implement private-state-derived cancellation intents and pre-admission tombstones in `Extensions/Etl/src/Workflows/Runtime`/ETL store; test whole-parent and subtree cancellation, reversed delivery and no resurrection.
- [ ] T023 [US3] Add shared two-process fixture support in `Extensions/Etl/tests/Tests`, following existing cluster process lifecycle/teardown. Stop one owned fixture worker and recover on restart/survivor.
- [ ] T024 [US3] Cover interruptions at read/admission/mid-staged-write/published-effect/cursor/result boundaries in `Extensions/Etl/tests/Tests`; verify no partial final chunk, stable replay, conflicting chunks, stale claims/cursors, duplicate/late results and retained reservations.
- [ ] T025 [US3] Cover cancellation/completion races, frozen bookmark/resume/stimulus/type address validation, mismatched or unexpectedly retired registrations versus legitimate consumption/parent termination, changed/corrupt snapshots/output revisions and unknown saved formats in `Extensions/Etl/tests/Tests`.
- [ ] T026 [US3] Remove a real acceptance/cancellation safeguard temporarily, observe the precise regression fail, restore it and rerun the affected test; record source hashes/commands/results in `specs/255-finite-file-etl/evidence/`.
- [ ] T027 [US3] Validate ETL schema/module upgrade/reopen on the existing EF migration/provider fixtures and record executed versus skipped provider evidence in `specs/255-finite-file-etl/evidence/`.

## Phase 6 — US4: Studio authoring and inspection

**Goal:** Authors publish the supported journey and operators understand run/partition recovery without database access.
**Independent test:** Browser author/publish/invoke/inspect a fixture and distinguish rejected data, fault, cancellation and outstanding work.

- [ ] T028 [US4] Add scope-filtered paginated run/partition inspection and idempotent cancel endpoints to `Extensions/Etl/src/Api`; test permission failures, cross-tenant non-disclosure, missing features and runtime-only composition.
- [ ] T029 [P] [US4] Implement the structured stage/binding/schema editor and publish/version views in `Extensions/Etl/studio/Elsa.Studio.Etl`, with focused frontend tests for validation and state preservation.
- [ ] T030 [P] [US4] Implement run list/detail/partition progress/recovery views in `Extensions/Etl/studio/Elsa.Studio.Etl`, including loading/empty/forbidden/error/missing-feature states and partial outputs.
- [ ] T031 [US4] Add and execute rebuilt Workbench HTTP acceptance in `e2e-tests/etl/`, using fresh fixture storage, both native composites and the declared feature composition.
- [ ] T032 [US4] Verify the integrated journey with an authorized browser harness, including keyboard, narrow viewport and useful diagnostics; save screenshots and exact fixture evidence under `specs/255-finite-file-etl/evidence/`.

## Phase 7 — Integrated review and delivery

- [ ] T033 Document ETL feature contracts/tasks and retention/recovery/rollback limitations in `Extensions/Etl` README/catalogs, root `EXTENSION_POINTS.md` and the canonical glossary; keep parent connector/BPMN obligations explicit in `docs/program-goals/unified-execution.md`.
- [ ] T034 Run focused build/tests, architecture guards, affected migration/provider checks and solution-filter validation; regenerate/check actual changed maps and lock files. Stage explicit paths including `docs/maps/manifest.json` when changed.
- [ ] T035 Review the integrated diff independently and through a bounded self-review loop, fix actionable findings and post exact-head validation/limits on the single #3109 PR; keep issue/project review state synchronized.
- [ ] T036 Merge only through the normal green gate, verify resulting-main CI/maps/Studio and update #3109/#2908/#2892 acceptance and claim disposition. Mark spec implemented in the implementation PR; never claim parent/program complete from this slice.

## Dependencies and parallel execution

T001/T003 gate T004 onward. T002 is independent and complete. Foundations precede US1 publication and US2 execution; US2's parser, state and sink work has separate testable seams but root integrates them before native composition. US3 exercises the same state machine, not a competing runtime. US4 consumes reviewed API contracts; its two UI views can proceed in parallel once those contracts exist. Final acceptance requires all four stories.

Use bounded workers for parser/transform tests, EF behavior, native integration and Studio views only after their input contracts are reviewed. Root owns scope, shared files, integration and QA. Builds stay focused and respect the shared-machine build slots. No new actor-provider or research prerequisite is introduced.
