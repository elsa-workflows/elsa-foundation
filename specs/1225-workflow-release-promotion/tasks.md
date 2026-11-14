# Tasks: Workflow releases and environment promotion

Input: [spec](spec.md), [plan](plan.md), [model](data-model.md), [contracts](contracts/), [qualification guide](quickstart.md).
Status: in progress; checked design tasks are historical design work, not production acceptance.
Production tasks are checked only after their resubmitted scope is implemented and qualified; broader program gates remain T036–T037.
See [delivery slices](delivery-slices.md) for the reviewable sequence and [historical evidence](historical-evidence.md) for prior results.
All native/BPMN/ETL criteria remain required.
Risk-based test cadence follows ADR 0080/framework §2.23; no blanket test-first order or coverage quota is imposed.
Every behavioral delivery needs a meaningful red/green mutation or before/after control in its owning PR evidence.

## Phase 1 — Setup and source-grounded design

- [x] T001 Claim #3590, isolate branch, inspect live protocol/Initiative boundary/current scope and record authorization on the canonical issue; files: `AGENTS.md`, `docs/program-goals/workflow-release-promotion.md`.
- [x] T002 Inspect native/reusable/BPMN/ETL identity, owner/store/start/resource seams and record actual limitations in `specs/1225-workflow-release-promotion/research.md` and `contracts/model-artifacts.md`.
- [x] T003 Resolve consequential scope/activation/mapping/recovery/direct-BPMN compatibility choices and record them in `specs/1225-workflow-release-promotion/spec.md` and #3590.
- [x] T004 Independently review requirements and draft contracts; fix subset evidence, root roles, trusted partition, owner compensation, scoping and Runtime-only split in `specs/1225-workflow-release-promotion/contracts/` and `plan.md`.

## Phase 2 — Shared foundations

- [x] T005 Create lightweight release target Core/engine projects under `Essentials/src/Workflows/Publishing/Releases/`, excluding Design-bearing Publishing dependencies; prepare the packed consumer witness under `tools/releases/` (execution belongs to T008).
- [x] T006 Implement compiler-free immutable manifest/member/root/requirement identities and bounded transport/hash/declared-graph validation in `Essentials/src/Workflows/Publishing/Releases/Core/` and `Elsa.Workflows.Publishing.Releases/`.
- [x] T007 Prove canonical vectors, original blob preservation, duplicate/conflicting keys, malformed/oversized transport, cycles, missing/unrelated members and tamper rejection in `Essentials/tests/Workflows/Publishing/Releases/`; re-establish canonical, tamper and bounded I/O controls on the resubmitted head.
- [x] T008 Add optional feature composition, public API/extension catalogs and solution/tier/PR-shard registration in `EXTENSION_POINTS.md`, `Elsa.Server.slnx` and `tools/ci/test-tiers.json`; re-establish structural checks and a clean exact-package consumer for this slice. Full program gates remain T036.

Transport T005–T008 landed in [#4220](https://github.com/elsa-workflows/elsa-foundation/pull/4220).
The [resulting-main receipt](https://github.com/elsa-workflows/elsa-foundation/pull/4379#issuecomment-6859366563)
qualifies shared 143/143 and the exact packed consumer at `a9e6bca372`; pending full CI is recorded separately.

Transport validity is distinct from owner semantic validity/readiness; no bundle may activate without owner verification.

## Phase 3 — US1: Frozen exact release content (P1; M1)

Independent test: create/export/import a native/BPMN/ETL release with a shared transitive reusable dependency and preserve every typed identity/byte/hash after source GC.

- [ ] T009 [US1] Add source capture/owner codec ports, durable bundle store and source retention through complete capture in `Essentials/src/Workflows/Publishing/Releases/` and the existing Publishing source bridge.
- [ ] T010 [US1] Export/import native authored-version associations, artifacts and reusable catalog/templates without recompilation in `Essentials/src/Workflows/Publishing/` and `Activities/Design/Persistence/`.
- [ ] T011 [US1] Export/import exact BPMN typed model/worker closure, separating physical target locators from immutable source lineage in `Essentials/src/Activities/Bpmn/`.
- [ ] T012 [US1] Add ETL-owner historical exact v1/v2 import and read-only catalog discovery without drafts/sequential replay in `Extensions/Etl/src/Design/` and `Persistence/`.
- [ ] T013 [US1] Discover actual native/reusable/model/ETL edges and scoped requirements, enforce complete owner-verified closure and provider prerequisites in the model-owned adapters under `Essentials/src/Activities/` and `Extensions/Etl/src/`.
- [ ] T014 [US1] Add release creation/export/import/inspection API and source Studio selection/download, including all owner root roles, in release API and `Studio/src/essentials/Elsa.Studio.Workflows/`.
- [ ] T015 [US1] Qualify exact owner round-trip, arbitrary retained ETL version, same-identity/different-content, source capture/GC races and dependency-only independent-start refusal using existing domain suites and `tools/releases/`.

Historical archive and capture-framework tests do not complete T009–T015; source acquisition, owner import and complete closure still require current-head proof.

## Phase 4 — US2: Target mappings and preview/apply (P1; M2)

Independent test: actual source plus two owned target hosts expose equivalent previews, different bindings and all refusal cases for the same immutable release.

- [ ] T016 [US2] Add stable target identity/capability/limits and trusted tenant/partition scope to release API/store under `Essentials/src/Workflows/Publishing/Releases/`.
- [ ] T017 [US2] Add metadata-only resource revision/incarnation observation and same-read identity validation through `Essentials/src/Connections/`, `Secrets/` and ETL binding owners.
- [ ] T018 [US2] Implement immutable scoped Runtime deployment/resource/model-locator context and owner verification without Design lookup in `Essentials/src/Workflows/Runtime/Core/` and model/resource bridges.
- [ ] T019 [US2] Implement server-owned preview records, current state/evidence readiness and stale/fresh apply admission in release target services/API; add tenant/partition/permission/drift controls in release and resource tests.
- [ ] T020 [US2] Add shared prepared target-session lifetime and mapping/change-preview Studio pages using `Studio/src/essentials/Elsa.Studio.Backends/`, `Hosting/` and Workflows, preserving source context and separate target authentication.
- [ ] T021 [US2] Add packaged CLI import/inspect/preview calls, JSON/problem output and target-auth options in `Essentials/src/Cli/Elsa.Cli/`, testing the packed tool against actual target APIs.

The historical Connections identity prerequisite must be resubmitted and qualified before mapping/context consumers rely on it.

## Phase 5 — US3: Durable activation, recovery and rollback (P1; M3)

Independent test: interrupt real owner commits/callbacks, resume, rebind and roll back while native/BPMN/ETL executions continue on original pins/captures.

- [ ] T022 [US3] Implement owner-transaction command guard/receipt/fence/revocation plus guarded compensation settlement in `Essentials/src/Workflows/Runtime/` and owning catalog stores; qualify delayed workers and unknown commits.
- [ ] T023 [US3] Extend Runtime typed native/model activation with trusted stable subjects and atomic model-root admission in `Workflows/Runtime/`; adapt direct BPMN publication/start/adoption and document legacy behavior in `Activities/Bpmn/`.
- [ ] T024 [US3] Propagate immutable capture through native/saved-stimulus/start/checkpoint/child dispatch/outbox/model work and BPMN embedding in `Workflows/Runtime/`, `Activities/DispatchWorkflow/` and `Activities/Bpmn/`.
- [ ] T025 [US3] Propagate captured ETL occurrence/alias mapping through RunEtl checkpoint/outbox/admission while retaining existing revision/credential semantics in `Extensions/Etl/src/Workflows/Runtime/` and `Runtime/`.
- [ ] T026 [US3] Implement fixed-plan idempotency, staged dependency owners, deterministic root coordination, authenticated resume/stop and accurate member/attempt outcomes in release engine/API/persistence.
- [ ] T027 [US3] Add release/attempt/rollback roots and fresh validated rollback through existing owners; prove concurrent GC and old-run retention in runtime reference GC and ETL/BPMN consumer suites.
- [ ] T028 [US3] Qualify binding-only no-op distinction, same-key changed payload, overlap/external publisher, cross-slot trigger claims, owner compensation, superseded/partial/unknown and restart through `tools/releases/` and relevant `e2e-tests/`.
- [ ] T029 [US3] Qualify real long-running native/BPMN/ETL starts, delayed dependency/model/ETL admission, mapping rotation/revocation, explicit rollback and subsequent serving versions on two actual targets.

## Phase 6 — US4: Studio, pipelines and integrated acceptance (P1; M4)

Independent test: complete Studio and packaged CLI operate the same retained release on real APIs/targets, with equivalent machine/user outcomes and inspected current-commit artifacts.

- [ ] T030 [US4] Complete release/evidence/attempt/history/retention/rollback Studio journeys in Workflows and model modules, following `contracts/studio.md` and shared controls.
- [ ] T031 [US4] Complete API/CLI apply/wait/resume/stop/rollback/history and documented exit/status behavior in release API and `Essentials/src/Cli/Elsa.Cli/`; prove actual packed-tool parity and uncertain response recovery.
- [ ] T032 [US4] Add exact-candidate/subset evidence association and optional target policies, coordinating #3588/#3892 through their existing result boundary; keep status/mode/trust/coverage explicit.
- [ ] T033 [US4] Extend `tools/ux-acceptance/` with actual model/root/two-target journeys, persisted reopen, denied/read-only/stale/recovery states and keyboard/focus/themes/narrow captures; retain downloadable exact-commit screenshots/traces.
- [ ] T034 [US4] Obtain independent screenshot/trace/interaction acceptance and fix/retest actual findings; preserve failed/incomplete evidence in the existing PR review loop.

## Phase 7 — Documentation, gates and closeout

- [ ] T035 Replace planned commands in `specs/1225-workflow-release-promotion/quickstart.md` with executable owned-environment examples under `tools/releases/` and user/integrator docs under `website/docs/`; explain direct-BPMN adoption, evidence limits and recovery.
- [ ] T036 Run affected domain/provider/consumer/architecture/maps/CI-tier gates, meaningful bite controls and current-head review; record exact commands/counts/outcomes on the PR and #3590.
- [ ] T037 Merge only after all required current-head checks and UX acceptance pass under current rules; qualify resulting main, update spec lifecycle/program milestones and close #3590 only after all agreed criteria are complete.

## Dependencies and execution

T001–T004 establish the reviewed design. T005–T008 establish the common transport/package foundation. US1 precedes target effects;
US2 provides the context/preview used by US3. US4 UI design starts early and its actual journeys accompany those slices.
Native, ordinary/direct BPMN and ETL are all required: a native-only foundation or one happy path is not an MVP completion claim.

One integration/implementation lane owns shared contracts. After contracts are committed, disjoint owner adapter work (native,
BPMN, ETL) and separate Studio/CLI files may be delegated in parallel; no task is marked [P] before that shared-contract boundary.
Tests run at the cheapest level that proves their risk; composed/process/packaged acceptance cannot be replaced by mocked stores.
