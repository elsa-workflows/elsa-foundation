# Tasks: Durable Rules

Scope: complete [specification](spec.md), not a prototype-only or SQLite-only substitute. Branch `agent/codex/3181-durable-rules`, sole active leaf [#3181](https://github.com/elsa-workflows/elsa-foundation/issues/3181). Root owns integration/review and shared registrations. `[P]` permits bounded delegation only after dependencies are satisfied and files do not overlap. Tasks are complete only with reviewed evidence, never merely generated files.

Elsa has no imposed greenfield TDD cadence (constitution 6.0.0 D9). Implement and validate each checkpoint together, with required behavior/registration tests and meaningful removal controls. No unrelated tests for planning prose.

## Phase 1: Reviewed setup

- [x] T001 Consolidate and review approved scope and requirements checklist in `specs/999-durable-rules/spec.md` and `checklists/requirements.md`; reserve unique spec number on #3181.
- [x] T002 Complete integrated review of `specs/999-durable-rules/plan.md`, `research.md`, `data-model.md`, `contracts/`, `quickstart.md` and this task decomposition; fix all concrete blocking findings and commit/push the planning checkpoint.
- [x] T003 Adopt narrow optional Rules-domain admission in `docs/adr/0088-rules-owns-its-execution-domain.md`, `.specify/memory/constitution.md` and `docs/reference/elsa-domain-tree.md`; refresh #2891/#1785 and claims before any shared registration edits.

## Phase 2: Foundational contracts and deterministic semantics

- [x] T004 Implement immutable finite-language, artifact, identity/pin, result and bounds contracts in `Extensions/Rules/src/Core/`; create the actual Core project, tracked API and local documentation without implementation dependencies.
- [x] T005 Implement CQS store/reader, work/delivery token, receipt validator and confirmation contracts in `Extensions/Rules/src/Persistence/Core/`; snapshot mutable input buffers/collections and keep semantic revision separate from leases.
- [x] T006 Implement canonical artifact/fact/receipt encoding, finite round evaluator and complete receipt validator in `Extensions/Rules/src/Runtime/`; frozen input per round, unique IDs, complete firings, semantic hashes excluding attempt metadata.
- [ ] T007 Validate semantic witness, budget/default capture, malformed/duplicate/noncanonical receipts, vocabulary membership and byte/count boundaries in `Extensions/Rules/tests/Tests/`; include omission-of-applicable-rule and fixed-point-vs-budget removal controls.
- [ ] T008 Register real projects in `Elsa.Server.slnx` and affected `tools/solution-filters/profiles.json` selections; verify scoped build, public API tracking and feature composition with actual services. Root serializes shared registrations.

Checkpoint status: T004–T006 and T009 implemented. The migrated SQLite admission/refusal/cancellation and status/terminal subset passes 92 focused tests, with a concurrent-winner reread removal control; 86 selected architecture/API/EF closure checks and local map/filter checks pass. T010 remains partial until snapshot-stable progress inspection exists; T016 remains partial until races against the real worker transitions are proven. T007 durable fixed-point/budget controls and T008 composition remain pending. See [transaction checkpoint](evidence/transaction-checkpoint.md) and prior checkpoint evidence. No full durable/package/native/provider qualification claimed yet.

## Phase 3: US1 — Direct durable invocation and actual packages (P1)

Independent acceptance: direct finite witness from a clean PackageReference consumer, exact package/asset evidence, stable terminal result. SQLite-only or source-only proof is insufficient for full leaf closure.

- [x] T009 [US1] Implement Rules-owned EF entities/context and SQLite migration in `Extensions/Rules/src/Persistence/EntityFrameworkCore/`; exact nullable scope, bounded indexed keys with full identity collision comparison, immutable invocation and separately paged receipt history.
- [ ] T010 [US1] Implement atomic admission/refusal and reader projections in `Extensions/Rules/src/Persistence/EntityFrameworkCore/`; identity-first replay, unique-key race reread, full terminal result read and snapshot-stable inspection.
- [ ] T011 [US1] Implement claim/read/accept/consume in `Extensions/Rules/src/Persistence/EntityFrameworkCore/`; complete validator plus atomic snapshot/fence/revision recheck, accepted receipt separate from consumption, next round or terminal exactly once.
- [ ] T012 [US1] Implement direct facade/worker and trusted deployment identity in `Extensions/Rules/src/Runtime/`; freeze omitted limits before fingerprint, distinguish Invalid/Refused/Unavailable, use existing feature/task composition without a second scheduler authority.
- [ ] T013 [US1] Validate real SQLite direct success, budget fault, refusal/unavailability, terminal facts/diagnostic and immutable replay in `Extensions/Rules/tests/Tests/`; registration resolves exact defaults and overrides as documented.
- [ ] T014 [US1] Implement `tools/rules/verify-packaged-rules.py` and its clean consumer generation/receipt support; compute versions from immutable refs, pack actual dependency closure, isolated cache/outside repo, verify no transitive implementation ProjectReference, compare NuGet content/archive/selected loaded assets separately.
- [ ] T015 [US1] Execute clean package direct witness and incompatible-pin cases; retain manifests/commands/results under `specs/999-durable-rules/evidence/` with large/raw artifacts referenced from the evidence receipt. Include a package-identity removal control, restore and rerun. No invented package versions or success-shaped fallback.

## Phase 4: US2 — Durable recovery and cancellation (P1)

Independent acceptance: separate packaged PostgreSQL processes recover accepted and consumed boundaries with design storage unavailable; late/stale results cannot advance state.

- [ ] T016 [US2] Implement cancellation/tombstone races and immutable terminal transitions in `Extensions/Rules/src/Persistence/EntityFrameworkCore/`; pre-admission empty facts, post-admission committed facts, no unconsumed additions after cancellation.
- [ ] T017 [US2] Implement lease renewal/release and lost-response replay in `Extensions/Rules/src/Persistence/EntityFrameworkCore/`; trusted expiry, no semantic revision changes on renewal, same-owner refreshed reads without mutation, no successor payload to stale workers.
- [ ] T018 [US2] Test accepted-response loss, consume-response loss, exact receipt semantic replay with distinct attempt attribution, cancellation/consumption races and stale owner rejection in `Extensions/Rules/tests/Tests/`; shared fixtures keep arrange/cleanup concise.
- [ ] T019 [US2] Extend `tools/rules/verify-packaged-rules.py` with two independent PostgreSQL process runs at accepted-unconsumed and consumed-round boundaries, old-worker late response and absent design storage. Depends on PostgreSQL provider task T021.
- [ ] T020 [US2] Execute process replacement and cancellation proof; record exact identities/counts/outcomes and topology in `specs/999-durable-rules/evidence/`; prove cancellation identity, worker fencing and one-time consumption with separate meaningful removal controls, restore and rerun.

## Phase 5: US4 — Four-provider durability and upgrade/rollback (P2, prerequisite for complete US2/US3 acceptance)

Independent acceptance: identical invariants actually run on all four engines, old native and Rules state survive additive schema/deployment changes under original pins.

- [ ] T021 [US4] Implement PostgreSQL, SQL Server and MySQL provider contexts/migrations alongside SQLite in `Extensions/Rules/src/Persistence/EntityFrameworkCore/`; use provider-specific registration and trusted clock/conditional-write mechanics, not SQLite compatibility claims.
- [ ] T022 [US4] Build shared real-engine contract suite in `Extensions/Rules/tests/Persistence/EntityFrameworkCore/ProviderTests/`; cover null/case/Unicode-normalization scope, exact/one-over UTF-8 scope limits, simulated index-hash collision, key-size/collation, admission races, lease/model revision, receipts, delivery and bounded read consistency.
- [ ] T023 [US4] Implement `tools/rules/verify-providers.py` and register the suite in `tools/ci/ef-suites.json`; require-all mode fails for missing engines, avoids credentials in evidence and cleans up only owned disposable resources. No workflow changes.
- [ ] T024 [US4] Execute all four provider suites and record engine versions/counts/no missing qualification in `specs/999-durable-rules/evidence/`; scheduled CI skips are not substitute evidence.
- [ ] T025 [US4] Extend package harness upgrade scenarios with retained original Rules pin, incompatible successor, capable recovery and admission-disabled rollback; preserve original artifact/receipt/state schemas without destructive migration or repinning.
- [ ] T026 [US4] Capture persisted native pre-Rules state, install additive Rules schema and prove identity/continuation unchanged in `tools/rules/` acceptance; run alongside Rules upgrade/rollback and retain evidence. Depends on native T030/T031.

## Phase 6: US3 — Persisted native composite integration (P1)

Independent acceptance: real Sequence following-activity and Flowchart sibling/join continuation, plus terminal/cancellation/refusal/unavailability settlement through rebuilt Workbench REST paths.

- [ ] T027 [US3] Implement immutable artifact/pin publication contribution in `Extensions/Rules/src/Workflows/Design/`; ordinary CLR catalog/compilation path, no shared execution-model switch or capability-key registration workaround.
- [ ] T028 [US3] Implement ordinary Rules activity, checkpoint admission/cancellation intent and durable dispatch in `Extensions/Rules/src/Workflows/Runtime/`; no Design runtime dependency or Rules state in parent checkpoint. Invalid input settles through proven existing native responsibility without fake Rules rows.
- [ ] T029 [US3] Implement terminal delivery leases/projections/acknowledgement in Rules EF and native bookmark dispatch/proof verification in `Extensions/Rules/src/Workflows/Runtime/`; no ack on enqueue, compare full canonical result content after deserialization, direct run has no delivery outbox.
- [ ] T030 [US3] Validate persisted Sequence and Flowchart continuation, sibling/join, fault/cancel/refusal, duplicate delivery, parent-terminal proof and unavailable retry in `Extensions/Rules/tests/Tests/`; assert authoritative parent/child state, not just method calls.
- [ ] T031 [US3] Add/register real Workbench REST journey under `e2e-tests/rules/` and required opt-in host composition; update `specs/999-durable-rules/quickstart.md` with exact runnable suite commands. Rebuild host on fresh isolated DB before running.
- [ ] T032 [US3] Execute native REST/package-host integration and proof-before-ack removal control; restore actual code and rerun affected checks; record commands and results in `specs/999-durable-rules/evidence/`.

## Phase 7: Integrated delivery

- [ ] T033 Complete module READMEs/local `EXTENSION_POINTS.md` and root index links; document actual override/registration/hosting/pin/provider behavior, deployment/recovery limits and future task-runner separation. No placeholder empty modules or duplicate canonical explanations.
- [ ] T034 Refresh claims/open PRs, review integrated diff and duplication, validate locks/API/solution profiles, affected builds/tests, architecture and current generated-map policy; record exact evidence in `specs/999-durable-rules/evidence/` and #3181.
- [ ] T035 Open/attach scoped PR with Fixes #3181, perform bounded self-review/fix loop and record every round with exact head/validation; merge only with green applicable checks and no owner/protection hold under standing authority.
- [ ] T036 Verify resulting-main CI/Studio/CodeQL/maps, reconcile authoritative issue/project/program status and release claim. Mark spec delivered only after every acceptance gate passes; keep broader #2892 program open.

## Dependencies and parallel examples

T001 → T002/T003 → T004/T005 → T006/T007/T008 → T009–T013. Package tooling T014 can proceed after public contracts/real build outputs exist; package execution T015 follows direct runtime. US2 T016–T018 follows direct durable transitions; US4 T021–T024 can proceed after EF base/store shape is stable and provides PostgreSQL for T019/T020. Native T027/T028 can proceed against reviewed stable direct/store contracts while root integrates providers; T029 shares EF files and must be serialized with store implementation. T030/T031 → T032 and T026; T025 requires package identities and durable recovery. Every story's full acceptance precedes T033–T036.

Useful parallel examples: semantic module tests vs root shared registrations after contract freeze; provider fixture implementation vs native publication contribution after EF contracts stabilize; package evidence review vs root REST integration after both are executable. Never assign two writers the same EF store, project file, task status or public API file. These are internal checkpoints of one owned leaf, not permission to create competing claims or shrink scope.

## Traceability

| Requirements | Tasks |
|---|---|
| FR001 finite semantics | T004, T006, T007, T013 |
| FR002 independent packaging | T008, T014, T015 |
| FR003 identity/artifact-only recovery | T004, T005, T010, T012, T014, T019, T025, T027 |
| FR004–FR008 durable progression/validation/replay/cancellation | T005–T007, T009–T013, T016–T024 |
| FR009 bounded inspection | T010, T013, T022 |
| FR010–FR011 native boundary/ownership | T016, T027–T032 |
| FR012 four providers | T021–T024 |
| FR013 upgrades | T025, T026 |
| FR014 guidance | T033 |
| FR015 integrated qualification | T034–T036 |
| SC001 package execution | T014, T015 |
| SC002 package identity | T014, T015 |
| SC003 provider matrix | T021–T024 |
| SC004 process recovery | T019, T020 |
| SC005 native continuation | T030–T032 |
| SC006 cancellation | T016, T018, T020 |
| SC007 refusal/invalid/unavailable | T012, T013, T015, T028, T030 |
| SC008 upgrade/rollback | T025, T026 |
| SC009 inspection | T010, T013, T022 |
| SC010 six removal controls and final gates | T007, T015, T020, T032, T034–T036 |
