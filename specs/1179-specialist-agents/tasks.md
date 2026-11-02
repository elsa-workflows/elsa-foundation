# Tasks: Specialist agents

**Input**: [spec.md](spec.md), [plan.md](plan.md), [research.md](research.md), [data-model.md](data-model.md),
[contracts](contracts/) and [qualification guide](quickstart.md).

**Status**: Implementation in progress. Checked boxes record completed implementation and local
qualification; they do not assert product acceptance. M2 is complete through
[PR #3573](https://github.com/elsa-workflows/elsa-foundation/pull/3573), with exact-head CI and
resulting-main qualification on `0d6db68a3`. M3 is complete; M4 implementation is active under
[#3524](https://github.com/elsa-workflows/elsa-foundation/issues/3524). The product spec remains nonterminal.

**Cadence**: ADR 0080 D1 risk-based behavior tests; no invented TDD rule or coverage quota. Tests
accompany their owning implementation slice and complete before its review/merge. Use meaningful
causal negatives, entire affected suites and discovery/count reconciliation. Generated composition
proof covers plain registration; add a feature-specific test only where wiring has decisions.

## Phase 1 — Setup and shared contract foundations (M2)

- [x] T001 Establish actual specialist Core/Design/runtime project envelopes and stable provider/descriptor/feature identities in `Extensions/Agent/src/Specialists/`, with `.slnx`, central package/API/lock/catalog registration as each real feature appears.
- [x] T002 Define justified immutable manifest/runtime/request/receipt/capability/authority shapes in `Extensions/Agent/src/Specialists/Core/Models/` and genuine store/execution extension ports in `Extensions/Agent/src/Specialists/Core/Contracts/`; verify independent package-author needs before making types public.
- [x] T003 Implement shared bounded JsonSchema.Net validation without a Design dependency in `Extensions/Agent/src/Specialists/JsonSchema/`, plus typed contract-to-schema compilation in `Extensions/Agent/src/Specialists/Design/Services/SpecialistSchemaCompiler.cs`; test malformed/remote references, required/null/unknown members and compatible field constraints.
- [x] T004 Qualify the normal central MEAI 10.10.1/MAF 1.27.0 graph in `Directory.Packages.props` and owned `packages.lock.json` files, including existing `Extensions/Agent/tests/` suites, actual CShells/shared-assembly/package closure and reviewed public/nuspec changes.

The central dependency enabler [#3544](https://github.com/elsa-workflows/elsa-foundation/issues/3544)
promotes the normal MEAI pin/locks and current Agent compatibility. T004's bounded production
runtime/package closure is now qualified through actual CShells activation and independent model,
authoring and runtime consumers. Dependency/PR CI and merge remain gates; this establishes no live
provider, durable journal or complete support-matrix acceptance.

## Phase 2 — Author, publish and execute the first fresh specialist (US1; M2)

**Goal**: A real published contract with the final facade/worker shape, not a throwaway inline path.
**Independent test**: Save/test/publish/select an exact specialist using an independent deterministic
model package; disclose disposable state until M3 durability/provider delivery. Final product A1–A3
also requires M3/M5/M7 evidence.

- [x] T005 [US1] Implement provider/schema authoring capabilities, contract proposals and validation in `Extensions/Agent/src/Specialists/Design/Services/SpecialistActivityProvider.cs`, using generic activity storage/authority and one public I/O contract.
- [x] T006 [US1] Compile exact schemas, descriptor, public result projections and final facade/background child into immutable fresh-invocation material in `Extensions/Agent/src/Specialists/Design/Services/SpecialistActivityProvider.cs`; test prompt-only/schema/model hashing and compatibility floor. Explicit continuation controls and their collision checks are delivered with T020.
- [x] T007 [US1] Add snapshot-bound active-draft provider membership in `Essentials/src/Activities/Design/Persistence/EntityFrameworkCore/Stores/EfActivityDesignStores.cs`; preserve exact owner scope, pre-count/page filtering and existing projection retention.
- [x] T008 [US1] Prove unpublished/multiple/mixed-provider drafts, discard/publish transitions, same-ID global/tenant isolation, stale cursors and all four SQL translations in `Essentials/tests/Activities/Design/Persistence/EntityFrameworkCore/Tests/`.
- [x] T009 [US1] Implement the native facade/typed completion checkpoint participant and fixed background-safe MAF leaf in `Extensions/Agent/src/Specialists/Elsa.Agent.Specialists/Activities/`, with actual aliases, Background placement and opaque encrypted-journal request/receipt references; explicitly refuse unsupported native encryption/secret-input policies.
- [x] T010 [US1] Implement real disposable journal behavior and the admitted MAF client path in `Extensions/Agent/src/Specialists/Elsa.Agent.Specialists/Services/`, enforcing fresh dispatch counts/deadline from the start; preserve final schemas and forbid production durability claims from memory stores.
- [x] T011 [US1] Prove actual reviewed publication, preflight capability refusal, invalid output, exact result projection and old-artifact isolation with a separately packaged deterministic client/consumer in `tools/agents/conformance/` and `Extensions/Agent/tests/Specialists/Tests/Publication/`.
- [x] T012 [US1] Integrate normal create/save/review/publish/recommendation/lifecycle permissions and diagnostics through `Extensions/Agent/src/Specialists/Design/` and existing Activity Design/Publishing APIs; no duplicate CRUD/catalog.
- [x] T013 [US1] Update provider/runtime extension catalogs and module-author configuration in `Extensions/Agent/src/Specialists/Design/EXTENSION_POINTS.md`, `Extensions/Agent/src/Specialists/Elsa.Agent.Specialists/EXTENSION_POINTS.md` and root `EXTENSION_POINTS.md`.

## Phase 3 — Durable model access, continuity and recovery (US3; M3)

**Goal**: Explicit scoped continuation and accepted-result replay under real persistence.
**Independent test**: Restart between turns/acceptance/delivery, force scope/revision/fence/uncertainty
and cancellation races, and verify journal and process independently.

T014–T015 are complete through prerequisite [#3578](https://github.com/elsa-workflows/elsa-foundation/issues/3578)
under existing Connections owner #2854, independently reviewed PR #3591 and source/package/core/Studio
qualification on resulting main `5be59cebc`. They do not establish M3 specialist execution or later product
acceptance. M3 source and local qualification are implemented in PR #3594, including the shared draft
Test Run application service, concrete SDK/socket profiles, optional streaming, protected continuity,
retention and packaged recovery. T016–T023 implementation is complete; independent review converged.
Required reviewed-head CI, Code Review merge and fresh resulting-main source/package/affected-browser verification completed through PR #3594 on `8dcbf2345`; broader hosted main jobs remain separately monitored.
The complete host/provider/store and live/Studio product acceptance remains M4–M7.

- [x] T014 [US3] Implement durable existing `IConnectionRepository` semantics in `Essentials/src/Connections/Persistence/EntityFrameworkCore/`, including four provider mappings/migrations, consumer-owned DbContext, TryAdd/version-CAS/conditional-delete and secret-reference behavior.
- [x] T015 [US3] Prove restart/concurrent save/revoke/create rollback/secret-reference isolation and HTTP connection regressions in `Essentials/tests/Connections/Persistence/EntityFrameworkCore/`; publish real durable backend capability through Connections Core.
- [x] T016 [US3] Implement concrete model connection/client/capability contributors in `Extensions/Agent/src/Specialists/Anthropic/` and `Extensions/Agent/src/Specialists/AzureOpenAI/`, with point-of-use auth, allowed hosts, provider setting/schema support and no Weaver harness activation.
- [x] T017 [US3] Test actual provider SDK request/options/response loss/disabled opaque retries and capability drift using protocol servers in `Extensions/Agent/tests/Specialists/Tests/Providers/`; count physical requests rather than only outer agent runs.
- [x] T018 [US3] Implement conversation/turn EF records (evaluation records follow T037), CQS/CAS/unexpired fencing and four provider migrations in `Extensions/Agent/src/Specialists/Persistence/EntityFrameworkCore/`; preserve the model in `data-model.md`.
- [x] T019 [US3] Implement exact operation/fingerprint admission, atomic accepted session/result/usage and old-operation replay in `Extensions/Agent/src/Specialists/Elsa.Agent.Specialists/Journal/SpecialistTurnJournal.cs`, with persisted single-specialist model-call/time/output-token reservations and deadline/unknown-usage settlement in `SpecialistTurnBudgetAllocator.cs` before real provider dispatch.
- [x] T020 [US3] Implement versioned minimal session codec, scoped protection envelope, key retention/rotation and fresh/explicit-continuation policy in `Extensions/Agent/src/Specialists/Elsa.Agent.Specialists/Services/SpecialistSessionCodec.cs`; add optional invocation controls with collision checks, and exclude private reasoning/credentials.
- [x] T021 [US3] Implement exact-operation cancel/applied intents, pre-admission tombstones and durable idempotent settlement through existing native checkpoint/outbox seams in `Extensions/Agent/src/Specialists/Elsa.Agent.Specialists/Services/SpecialistCheckpointEnricher.cs` and `SpecialistSettlementHandler.cs`.
- [x] T022 [US3] Prove sensitive/redacted native flow and explicit encryption-required/secret-input refusal, journal winners, delayed cancellation, lease loss, uncertainty and crash/ack boundaries in `Extensions/Agent/tests/Specialists/Tests/Recovery/`; include pre-dispatch budget exhaustion/deadline, unknown usage/restart and SDK physical-request controls, with no accepted model replay/cancelled parent continuation.
- [x] T023 [US3] Deliver retention/purge/disabled-new-admission and old codec/key reader compatibility in `Extensions/Agent/src/Specialists/Elsa.Agent.Specialists/Services/SpecialistRetentionPruner.cs` and `Extensions/Agent/tests/Specialists/Tests/Recovery/`; retain necessary pins/tombstones.

M3 admits no tool execution, so its tool budget is zero by capability refusal; T043/T047 add and
reserve actual tool/team effects through the owning #2912 contracts. Evaluation records belong to
M5/T037 with concrete case/run authorization and retention; T018 owns only the conversation/turn
persistence needed by specialist execution. This clarifies sequencing and preserves the complete
Program acceptance rather than adding unused evaluation rows to M3.

## Phase 4 — Native and BPMN editorial composition (US2; M4)

**Goal**: Actual authoring/publishing/bindings and equivalent process results, including Design-free BPMN.
**Independent test**: Save/reload/publish/run the same Copywriter/Proofreader references natively and
through BPMN; duplicate/late results cannot repeat parent continuation.

- [ ] T024 [US2] Extract neutral BPMN semantic models/identities into `Essentials/src/Activities/Bpmn/Core/`, preserving JSON/aliases/schema and justified neutral type forwarders.
- [ ] T025 [US2] Move authored structure/handler to `Essentials/src/Activities/Bpmn/Design/`, remove Design references from runtime, specialize Interchange and update `Essentials/apps/Elsa.Workbench/` shell manifests without losing authoring registration.
- [ ] T026 [US2] Preserve compiled-state readers, runtime/interchange/authoring tests and explicit pre-GA authored-type API migration in `Essentials/tests/Activities/Bpmn/` and `Essentials/src/Activities/Bpmn/` catalogs/docs.
- [ ] T027 [US2] Deliver the generic exact authorized native `HumanReview` wait/decision endpoint (agents are user-defined; the editorial process is a sample, not product code) and pre-claim `ActivityResumeValidating` guard with claimed-replay validation in `Extensions/Agent/src/Specialists/` per `contracts/editorial.md`; prove Sequence/Flowchart ports, rejected-review article/comments feedback, three-round exhaustion, approve/deny, restart and duplicate/foreign/changed/late-decision refusal in `Extensions/Agent/tests/Specialists/Composition/` using real publication/bookmark APIs.
- [ ] T028 [US2] Deliver the generic exact review page under `Extensions/Agent/studio/Elsa.Studio.Agent.Specialists/` and prove the same revision loop/human approve/deny/restart/duplicate scenarios through BPMN bound children, save/reload/export/import and explicit unsupported refusal in `Studio/tests/browser/` and `Extensions/Agent/tests/Specialists/Composition/`.
- [ ] T029 [US2] Replace the research BPMN gap only after a fresh normal package consumer proves zero Design/Publishing assemblies and no mutable lookup in `tools/agents/conformance/`; retain authoring-only/combined profiles.

## Phase 5 — Studio author/test/publish/use/inspect (US7; M5)

**Goal**: Complete ordinary product journeys with exact identity and consistent state/permission behavior.
**Independent test**: Real Studio mouse/keyboard and 320px flows, saved/reloaded in a normal qualified host.

- [ ] T030 [US7] Register the specialist feature area and module-owned provider-filtered collection in `Extensions/Agent/studio/Elsa.Studio.Agent.Specialists/Client/src/module.tsx` and `AgentsPage.tsx`; handle unpublished/mixed-provider/read-only/denied rows without client-side paging filters.
- [ ] T031 [US7] Implement provider settings and public-interface constraint contributions in `Extensions/Agent/studio/Elsa.Studio.Agent.Specialists/Client/src/SpecialistEditor.tsx`, honoring exact revision/readOnly/historyResetKey and generated schema preview.
- [ ] T032 [US7] Deliver production-runtime candidate/playground/continuity/evidence endpoints in `Extensions/Agent/src/Specialists/Api/` by reusing generic draft test-run dispatch/reconcile/cancel, exact saved candidate artifacts and scoped journal reads.
- [ ] T033 [US7] Implement module-owned playground with fresh/continue, capability diagnostics, partial-vs-accepted output and truthful stop/uncertainty in `Extensions/Agent/studio/Elsa.Studio.Agent.Specialists/Client/src/SpecialistPlayground.tsx`.
- [ ] T034 [US7] Reuse generic publication/history/lifecycle in `Extensions/Agent/studio/Elsa.Studio.Agent.Specialists/Client/src/SpecialistHistory.tsx`; add exact occurrence links through the owning Studio SDK/shared inspector contribution with no hardcoded specialist branch.
- [ ] T035 [US7] Implement permitted turn/process timeline/result/usage/approval/incident views in `Extensions/Agent/studio/Elsa.Studio.Agent.Specialists/Client/src/SpecialistInvocationPage.tsx`, including completed/not-applied and unknown outcome.
- [ ] T036 [US7] Prove real create/edit/test/publish/picker/use/inspect/history, conflict/save/undo, denied/read-only/source-owned, keyboard/narrow and runtime occurrence behavior in `Extensions/Agent/studio/Elsa.Studio.Agent.Specialists/Client/src/__tests__/` and `Studio/tests/browser/specialist-agents.spec.ts`.

## Phase 6 — Saved evaluations and comparison (US6; M5)

**Goal**: Versioned cases and exact candidate comparison, preserving evidence status distinctions.
**Independent test**: Save/reload a set, compare two exact versions and inspect original result/status/usage.

- [ ] T037 [US6] Implement revisioned evaluation set/case operations and exact run/case identity in `Extensions/Agent/src/Specialists/Api/` and `Extensions/Agent/src/Specialists/Persistence/EntityFrameworkCore/`, with permission/retention and production-runtime dispatch.
- [ ] T038 [US6] Implement deterministic assertions and separate versioned quality-rubric assessment in `Extensions/Agent/src/Specialists/Elsa.Agent.Specialists/Services/SpecialistEvaluator.cs`, with declared independent evaluator budget and no private reasoning storage.
- [ ] T039 [US6] Implement cases/save-from-test/compare/results views in `Extensions/Agent/studio/Elsa.Studio.Agent.Specialists/Client/src/SpecialistEvaluationsPage.tsx`, preserving failed/skipped/cancelled/unknown/pending statuses.
- [ ] T040 [US6] Prove candidate/set revision isolation, contract-vs-quality distinction, cancellation/replay, redaction and real browser persistence in `Extensions/Agent/tests/Specialists/Evaluations/` and `Studio/tests/browser/specialist-evaluations.spec.ts`.

## Phase 7 — Scoped resources and exact approved tools (US4; M6)

**Goal**: Actual workflow/MCP tools and knowledge with durable exact authorization/effect recovery.
**Independent test**: Denied/revoked/changed approval causes zero effect; supported crash-after-effect
recovery reconciles the real connector outcome. Requires the owning #2912 production integration.

- [ ] T041 [US4] Reconcile current #2912 decision/approval/effect/resource contracts before implementation and record exact dependencies in `specs/1179-specialist-agents/research.md`; do not fork/reparent its generic work.
- [ ] T042 [US4] Implement scoped/versioned knowledge and tool capability bindings through those owning contracts in `Extensions/Agent/src/Specialists/Core/` and `Extensions/Agent/src/Specialists/Elsa.Agent.Specialists/Services/`.
- [ ] T043 [US4] Implement native proposal/approval/wait/effect/result continuation outside the background model leaf in `Extensions/Agent/src/Specialists/Elsa.Agent.Specialists/Activities/`, binding canonical arguments, scope, authorization revision and exact operation.
- [ ] T044 [US4] Deliver one supported workflow tool and one MCP transport/client contribution in `Extensions/Agent/src/Specialists/Tools/`, with explicit idempotency/reconciliation capabilities and no SDK callback bypass.
- [ ] T045 [US4] Expose exact tool/resource policy, proposal/approval and observable receipts through `Extensions/Agent/studio/Elsa.Studio.Agent.Specialists/Client/src/` while reusing native approval/incident surfaces.
- [ ] T046 [US4] Prove denial/revocation/changed arguments, scoped observations, approval restart, effect-before-ack and duplicate/conflicting/late receipts in `Extensions/Agent/tests/Specialists/Tools/` and real approval browser journeys.

## Phase 8 — Budgeted reusable team patterns (US5; M6)

**Goal**: Sequential/concurrent-cohort/bounded-refinement team activities under native ownership.
**Independent test**: Team inside a native/BPMN parent preserves barriers, resource visibility and one
logical result under late/duplicate messages, exhaustion, cancellation and restart.

- [ ] T047 [US5] Extend M3's existing per-turn reservations to team-wide/cohort/round limits through #2912 in `Extensions/Agent/src/Specialists/Elsa.Agent.Specialists/Services/SpecialistTeamBudgetAllocator.cs`, aggregating without a second dispatch ledger or duplicate charging.
- [ ] T048 [US5] Implement the exact participant pins, typed causal input/result mappings, deterministic aggregation, completion predicate and fail-whole-team policy in `contracts/teams.md` through existing native structural contracts in `Extensions/Agent/src/Specialists/Design/` and `Extensions/Agent/src/Specialists/Elsa.Agent.Specialists/Activities/`; keep participant/cohort/message/resource identities distinct.
- [ ] T049 [US5] Implement the team configuration wireframe and `contracts/teams.md` validation in `Extensions/Agent/studio/Elsa.Studio.Agent.Specialists/Client/src/SpecialistTeamEditor.tsx`, including pattern, participant pins, mappings, aggregation/predicate, finite limits and failure policy with normal reusable activity publication.
- [ ] T050 [US5] Prove whole-cohort barriers, bounded feedback/budgets, private/shared visibility, cancellation/restart and one parent result in `Extensions/Agent/tests/Specialists/Teams/` and representative real Studio/native/BPMN journeys.

## Phase 9 — Supported deployments, real providers and release (US8; M7)

**Goal**: Observed complete matrix and operational product delivery, not merged plans/fixtures.
**Independent test**: Execute the support contract's named24 protocol cells, deployment shapes and
four authorized live journeys, retaining exact artifacts/readiness/original failure states.

- [ ] T051 [US8] Produce immutable packages/Studio distribution and independent module/consumer projects with isolated locks/caches/archive and loaded-byte checks in `tools/agents/conformance/`.
- [ ] T052 [US8] Execute four-store × three-host × two-provider protocol matrix and independently retained authoring-only/runtime-only/combined/Studio shapes through `tools/agents/conformance/`; use normal public readiness/shell lifecycle and private state.
- [ ] T053 [US8] Qualify disabled admission, retained in-flight work/readers, key/codec/schema upgrades, interrupted migration and explicitly supported rollback across actual providers in `Extensions/Agent/tests/Specialists/Tests/Recovery/` and `tools/agents/conformance/`.
- [ ] T054 [US8] Run bounded authorized live Anthropic/Azure OpenAI Copywriter/Proofreader examples through native and BPMN workflows, recording exact access/deployment/model identity and observed evidence in `specs/1179-specialist-agents/evidence/`; do not publish secrets/raw private state.
- [ ] T055 [US8] Ship user guide `website/docs/extensions/specialist-agents.md` with `website/docs/SUMMARY.md` navigation, operator guide `Extensions/Agent/src/Specialists/Elsa.Agent.Specialists/README.md`, and module-author `Extensions/Agent/src/Specialists/Core/README.md`, covering examples/configuration, actual support, migration/rollback and uncertainty/cancellation.
- [ ] T056 [US8] Reconcile real host startup asset budgets, missing/incompatible/restored module controls, native resource isolation and owned-process cleanup in `tools/agents/conformance/`; do not substitute static closure or source-tree hosts.
- [ ] T057 [US8] Close every A1–A15 row only with reviewed product receipts in `specs/1179-specialist-agents/evidence/acceptance.md`, preserving all failed/skipped/cancelled/quarantined/unknown statuses and exact source/fixture/host/provider identities.

## Final cross-cutting gate

- [ ] T058 Complete affected full suites/discovery reconciliation, API/lock/catalog/tier/architecture/maps gates and causal controls for every implementation leaf; update owning catalogs and `docs/program-goals/elsa-agents.md` in the same units.
- [ ] T059 Complete independent review, exact-head required CI and resulting descendant-main qualification, then reconcile Program/M0–M7/native subissues and Project66 status with actual evidence on the owning PRs/issues.
- [ ] T060 Set this product specification to Implemented only in the final completing PR and remove terminal checklist residue per `docs/reference/spec-lifecycle.md`; ship the final acceptance/support/docs links with Program closure.

## Dependencies and execution strategy

T001–T004 enable the first US1 slice; T005–T008 can land as complete authoring/query work while real
runtime capability admission still refuses publication without its interpreter. T009–T013 finish the
M2 published fresh-runtime vertical. M3 T014–T023 is required for durable/real-provider/continuity claims;
M2's disposable fixture does not satisfy those. M4 T024–T029 resolves the recorded BPMN gap before
claiming a Design-free BPMN product. M5 follows stable runtime/authoring contracts; M6 waits for #2912's
actual prerequisite contracts. M7 qualifies all earlier work and does not invent support by inference.

User-story order follows concrete infrastructure dependencies, not a promise that P2 value is needed
before every P1 operator test. Each story's independent test can use seeded earlier capabilities.
Deliver complete narrow leaves under the owning epics, one active implementation leaf plus review lane;
do not open60 issues or promote the entire backlog to Ready.

Independent-file opportunities within an authorized work unit: schema validation vs provider query
tests (US1); durable Connections vs journal tests after Core settles (US3); native vs interchange
qualification after BPMN split (US2); module UI vs independent browser fixture (US7); assertion engine
vs comparison view (US6); workflow vs MCP connector proof (US4); budget guards vs team pattern tests
(US5); isolated matrix consumers after one immutable producer (US8). These are planning opportunities,
not permission to exceed Program WIP or run simultaneous solution builds.

## Requirement and Program traceability

| Requirement | Tasks | Program acceptance |
|---|---|---|
| FR-001–FR-002 | T005–T008,T012,T030–T031 | A1 |
| FR-003 | T003–T004,T014–T017,T031–T033 | A1,A2,A13 |
| FR-004 | T009–T011,T032–T033 | A2 |
| FR-005–FR-006 | T006,T011–T013,T027,T034 | A3 |
| FR-007–FR-008 | T024–T029,T036 | A4,A5 |
| FR-009 | T003,T006,T011,T017,T022 | A2,A4 |
| FR-010–FR-014 | T018–T023,T032–T035 | A6,A8,A9 |
| FR-015–FR-017 | T041–T046 | A7,A8 |
| FR-018 | T010,T017–T019,T022,T043,T047 | A9 |
| FR-019 | T048–T050 | A11 |
| FR-020 | T037–T040 | A10 |
| FR-021–FR-022 | T030–T036,T039–T040,T045 | A12 |
| FR-023–FR-024 | T014–T015,T018,T023–T026,T029,T051–T053,T056 | A13 |
| FR-025 | T054 | A14 |
| FR-026 | T013,T055,T058–T060 | A15 |
| SC-001–SC-007 | All story proofs plus T051–T060 | Complete observed A1–A15 |

### M3 review follow-up owned by M7

[M7 #3527](https://github.com/elsa-workflows/elsa-foundation/issues/3527) must resolve indefinite
protected-content retention for terminal-owner OutcomeUnknown and abandoned Claimed/Dispatching
turns with a bounded resolution or retention ceiling. Preserve unknown/no-inference-retry semantics,
conversation pins, diagnostic metadata and replay refusal. The current M3 sweep deliberately pins
these states; documenting this limitation does not satisfy that M7 acceptance item.
