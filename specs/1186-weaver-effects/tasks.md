# Tasks: Weaver effects and effective session autonomy

**Input**: Approved [spec.md](spec.md), reviewed [plan.md](plan.md), [research.md](research.md),
[data-model.md](data-model.md), [contracts](contracts/session-policy.md) and [quickstart](quickstart.md).
**Status**: Approved for implementation after independent correctness and acceptance/standards task reviews on 2026-11-01. Both qualification-inventory and freeze-order findings were corrected and independently rechecked; no implementation criterion is complete at this transition.

Use risk-based tests alongside each increment and causal red/green controls for changed behavior,
as recorded in the plan under the owner's delegated technical authority. No global TDD cadence is
invented. Keep original valid behavior/tests. An expected-failing causal baseline is evidence of a
gap, never completed qualification. A task is checked only after its actual outcome is verified.

## Phase 1: Setup

- [x] T001 Refresh source/main health, #3440/parents/dependencies/claims/PRs/Project/readiness and record the winning sole-leaf ownership on the issues; reconcile `docs/program-goals/weaver-across-studio.md` without adding Ready work at the cap.
- [x] T002 Complete independent specification, plan and task review; verify existing ignore/build boundaries and retain review hashes/decisions with `specs/1186-weaver-effects/checklists/requirements.md` and owning issue evidence.

## Phase 2: Foundational causal journey and ownership

**Goal**: Establish a real automatic-eligible module operation before choosing a shared update contract.
All subsequent story implementation depends on this phase. Experimental interfaces remain candidates.

- [x] T003 Add distinct strict S/M-size server tool/result/readback through `tools/studio/Weaver.Conformance.Probe/fixtures/Resource.Backend/WidgetSizeTool.cs.in`, existing `WidgetResource.cs.in`, `AcmeHomeFeature.cs.in` and `WidgetContext.cs.in`; reuse current native personal authority/CAS, preserve unrelated JSON and reviewed visibility, and qualify side-effect-free preparation, actual receipts and refusal/uncertainty with packed `Resource.Backend.Tests/WidgetSizeTests.cs.in` and `authors.py` inventories.
- [x] T004 [P] Add size action, bounded owner observation and separate result renderer in `tools/studio/Weaver.Conformance.Probe/fixtures/Resource.Studio/Client/src/module.tsx`; preserve existing visibility/context contracts and use only the retained public SDK.
- [x] T005 Extend generic inline native-SSE receipt correlation and wrong/missing/truncated/orphan/duplicate-evidence controls in `tools/studio/Weaver.Conformance.Probe/case_records.py` and `test_case_records.py`; define the exact mandatory per-adapter inventory in `contracts/session-policy.md` through new `autonomy_journeys.py`, `test_autonomy_journeys.py` and `browser.mjs`, initially enabling its four causal mode scenarios without resource knowledge in protocol factories or extra execute calls.
- [x] T006 Commit/freeze the module and baseline harness with unchanged production policy behavior; execute unchanged Manual/FullAuto controls and both existing-session transition directions through both actual adapters in owned package-composed hosts, retain actual owner/correlation and precise mismatch evidence via `autonomy_journeys.py`, and record it on #3440. Wrong-cause failure or missing-route observations do not satisfy this task. Verified on `39a1affef` ([#3440 record](https://github.com/elsa-workflows/elsa-foundation/issues/3440#issuecomment-6563444132)): 4 Passed controls and 4 `autonomy.mode_mismatch` failures on both adapters, with zero mode-selection requests and no effective-policy readback.
- [x] T007 Use the causal observations and controlled ownership experiments to select the smallest candidate session-owned admission/update surface in `Extensions/Agent/src/Core/Contracts/AgentContracts.cs`, related Core models and `specs/1186-weaver-effects/contracts/session-policy.md`; document exact identities, conditional update/readback and explicit unsupported replacement semantics without freezing the contract. Selected in `contracts/session-policy.md` (independently reviewed; four claim-leak findings corrected) and added as the experimental `IAgentSessionPolicyService` surface.
- [x] T008 Implement short reservation/commit/handoff ownership in existing `DefaultAgentServices.cs`, `AgentTurnServices.cs`, `AgentToolInvoker.cs`, `AgentProposalServices.cs` and registration as demanded by T007; cover direct invoker and all pending/foreground/pause/review/dispatch/ACK/continuation owners, preserve existing message lock/call-proposal ordering, and keep I/O/callbacks outside monitors with risk-based tests in `Extensions/Agent/tests/Tests/`. Verified: `AgentSessionPolicyTests` (29 cases) plus all 573 existing Agent tests, 602/602.

## Phase 3: US1 — Acknowledged autonomy in one conversation (P1)

**Independent test**: Both actual adapters change mode in each direction on established conversations;
unchanged controls, ceiling/policy preservation, lost response and accepted-result attribution agree
with authoritative state and native effect/readback. History/context/receipts retain their identity.

- [x] T009 [US1] Add authenticated autonomy-only conditional command and authoritative create/detail readback in `Extensions/Agent/src/Api/AgentApi.cs`, `Api/Models/AgentApiModels.cs` and actual JSON serialization context; preserve unrelated policy/current limits and accepted-delivery policy attribution in the owning session model, with malformed/foreign/stale/busy tests in `Extensions/Agent/tests/Tests/`.
- [x] T010 [US1] Implement acknowledged/pending/unresolved-mode transport/UI in `Studio/src/essentials/Elsa.Studio.Hosting/Client/src/app/agent/agentClient.ts`, `agentTypes.ts`, `weaver/useWeaverSession.ts` and `WeaverSurface.tsx`; retain backend/session/generation isolation, block unacknowledged dependent work, reconcile readback and attribute results to their accepted policy.
- [x] T011 [US1] Add behavioral regression tests in `agent/__tests__/agentClient.test.ts` and `weaver/__tests__/WeaverSurface.test.tsx` for both directions, effective clamps, unrelated restrictive policy, duplicate/conflicting/unchanged requests, old retry after newer update, lost response/readback, stale callbacks and no silent session rotation; preserve causal red/green controls.
- [x] T012 [US1] Repeat T006's semantic expectations successfully through `autonomy_journeys.py`/`browser.mjs` on both actual adapters with actual acknowledgement/readback, original IDs/history/provenance/receipts, exact accepted policy and native whole-layout preservation; record deterministic/provider-protocol categories distinctly on #3440.

## Phase 4: US2 — Exact reviewed operation and truthful effects (P1)

**Independent test**: Competing decisions/current authority cannot alter the original preparation or
apply twice. Unconfirmed effects/late receipt retain truthful outcomes; atomic Undo/save stays native.

- [x] T013 [US2] Preserve/recheck original prepared binding, module/host risk/authority and genuine human-review semantics in `Extensions/Agent/src/Elsa.Agent/Services/AgentPreparedInvocationResolver.cs` and proposal/invoker paths; test current-policy drift cannot reinterpret automatic approval as a new human decision in `Extensions/Agent/tests/Tests/AgentProposalTests.cs` and related behavioral fixtures.
- [x] T014 [US2] Add controlled approve/deny, execute/deny/cancel, duplicate decision, pre/post-effect cancellation and late/conflicting ACK orderings in `Extensions/Agent/tests/Tests/AgentProposalTests.cs` and affected turn tests; assert original correlations, one owner effect, accepted receipt retention and explicit losers, fixing only demonstrated lifecycle gaps. Ordering controls live in `AgentProposalOrderingTests.cs`; pre-dispatch cancellation is now `Cancelled` with zero effect.
- [x] T015 [US2] Implement and verify every remaining mandatory per-adapter lifecycle case in `contracts/session-policy.md` through `tools/studio/Weaver.Conformance.Probe/autonomy_journeys.py`, `browser.mjs` and existing `journeys.py` owners: genuine approved/denied/refused results return to the originating turn on both actual adapters, duplicate/cancellation/uncertainty controls observe native owners, and FullAuto visibility/workflow controls retain review, validation, atomic batch, dirty state, whole Undo and explicit Save/reload/Publish boundaries.

## Phase 5: US3 — Defined admission and ownership order (P1)

**Independent test**: Real-owner barriers establish one policy/work ordering, exact duplicate identity,
no extra provider/effect, identity-specific cleanup and supported/unsupported replacement behavior.

- [x] T016 [US3] Add collector-to-insertion races and direct message-owner controls in `Extensions/Agent/tests/Tests/AgentApiCurrentSurfaceTests.cs` and new `AgentSessionAdmissionTests.cs`; cover distinct/identical/conflicting delivery, policy-first/admission-first, preserved source/client/context/provenance, contributor cancellation/fault and accepted insertion followed by audit/cache/transport failure.
- [x] T017 [US3] Add lookup-to-registration, terminal-to-unregister and verified-pause handoff barriers in `Extensions/Agent/tests/Tests/AgentTurnOrchestratorTests.cs` and related boundary tests; prove terminal display alone never permits an incompatible update, no stale policy snapshot survives acquisition, and cleanup releases only its exact ownership.
- [x] T018 [US3] Qualify direct-invoker preparation-to-proposal, approved dispatch, unresolved client ACK and receipt-continuation handoffs with `Extensions/Agent/tests/Tests/AgentToolInvokerTests.cs`, proposal/disclosure tests and the real ownership implementation; no session monitor spans call/proposal waits, no accepted receipt is lost and no uncertainty authorizes replay.
- [x] T019 [US3] Qualify an actual participating session/lifecycle replacement and explicit unsupported replacement through `Extensions/Agent/tests/Tests/AgentCompositionTests.cs`, registration tests and the package-composed path; update only necessary replacement cardinality/lifetime validation and never select a shadow default when installed owners cannot participate.

## Phase 6: US4 — Correlated expiry and owner-scoped audit (P2)

**Independent test**: Fake-clock repeated expiry has zero pre-dispatch effect and correlated records;
post-dispatch remains uncertain. A mixed-owner/tenant take1 query filters/orders inside its reader.

- [x] T020 [US4] Add serialized correlated approval/execution expiry and distinct pending-client deadline audit in `Extensions/Agent/src/Elsa.Agent/Services/AgentProposalServices.cs`; retain session/proposal/invocation/turn/call identities and stable repeated outcomes, with fake-clock zero-effect/late-receipt/no-replay tests in `Extensions/Agent/tests/Tests/AgentProposalTests.cs`.
- [x] T021 [US4] Add the smallest owner-bounded query and authoritative ownership association/index in `Extensions/Agent/src/Core/Contracts/AgentContracts.cs`, Core models and `Elsa.Agent/Services/DefaultAgentServices.cs`; preserve trusted existing reads, missing-legacy fail-closed behavior, coherent association/append/query and no session-sink-session DI cycle, with mixed-owner/tenant/tie/conflicting-association tests.
- [x] T022 [US4] Route authenticated bounded audit reads through the owning query in `Extensions/Agent/src/Api/AgentApi.cs`; test normalized actor/tenant, foreign explicit session, take1 ordering, missing legacy ownership, current replacement ownership and unsupported reader refusal using recording-reader API tests without global read/per-event lookup fallback.

## Phase 7: Qualification and integration

- [x] T023 Document candidate wire signatures/replacement guidance in `specs/1186-weaver-effects/contracts/session-policy.md`, Agent `EXTENSION_POINTS.md`, public API baselines and fixture README/Cross-domain contributions after T012/T015–T022. Keep the contract explicitly experimental pending T024/T025; recheck both constitutions and API SemVer classification, and remove only source made obsolete by this leaf with evidence.
- [x] T024 Run affected complete backend/API/Hosting/driver suites, typecheck/lint/build and architecture/maps checks; execute real normal composed Studio validation from `specs/1186-weaver-effects/quickstart.md`, match discovery/execution and retain exact source/input/test identities and actual failures/skips/quarantines on #3440/PR.
- [x] T025 Wire exact mandatory autonomy completeness from `contracts/session-policy.md` into `tools/studio/Weaver.Conformance.Probe/journeys.py`, final evidence/export checks and reader/oracle tests; run a fresh complete immutable package witness retaining original38+4 plus every named new case on each actual adapter, owner/native evidence, consumer no-source/no-frontend/byte-equality and owned-host cleanup. Missing/duplicated cases or wrong-cause outcomes fail qualification. Do not impose the old135-stage count on the extended run.
- [ ] T026 Only after successful T024 normal composed Studio and T025 complete package proof, freeze the demonstrated minimum contract and reconcile `specs/1186-weaver-effects/contracts/session-policy.md`, `quickstart.md` and issue acceptance mapping with actual evidence; set spec Status Implemented in this same PR, preserving live review/merge/resulting-main gates. Revalidate changed inputs and obtain final exact-head proof after any later source change.
- [ ] T027 Open/update one scoped draft PR for #3440, attach it to this chat, post gate/causal evidence and `pr:` receipt on the issue, and maintain governing checkpoints in `docs/program-goals/weaver-across-studio.md`, Program/M2/Project65 without claiming Done.
- [ ] T028 Converge independent non-author correctness/standards/acceptance review against `specs/1186-weaver-effects/spec.md` on the entire current PR diff and all issue/PR threads; preserve cumulative rounds, causal regressions and resolved findings on the owning PR. Mark ready and request normal review after draft convergence.
- [ ] T029 Refresh actual reviewed head/main/org rules/claims/holds and required checks under `docs/contributing/merging.md`; merge through the repository's authorized process with exact reviewed head and no bypass. Retain merge identity and keep #3440 In review until resulting-main verification, reopening automatic issue closure if necessary.
- [ ] T030 Verify actual resulting main using `specs/1186-weaver-effects/quickstart.md`, required CI and fresh affected composed/native package proof; retain source/archive/owner/process evidence and distinguish deterministic, provider-protocol and opt-in live-provider outcomes. Requalify when main advances instead of assuming earlier evidence still proves it.
- [ ] T031 Reconcile all five #3440 criteria, tasks and actual evidence on the owning issue/PR; only then mark it completed/Project Done, release the exact claim and update Program/M2/dependencies/`docs/program-goals/weaver-across-studio.md`. Continue to fresh #3441 eligibility and the remaining M0–M5 roadmap.

## Dependencies and execution order

T001–T002 -> T003/T004 -> T005 -> T006 -> T007 -> T008 -> US1 -> US2 -> US3 -> US4 ->
T023–T031. The shared owner is foundational because US1 must not become an unsafe update-only API.
US2 reuses its ordering with independently observable reviewed-effect controls; US3 expands controlled
boundary/replacement qualification; US4 shares correlation but owns distinct expiry/query assertions.
T025 completeness/oracle implementation may be prepared earlier, but final passing qualification
depends on all stories and final committed inputs. T026 freeze requires both T024 and T025; T023 is
candidate documentation only. No public contract freeze precedes composed/package proof.

Only T003/T004 are explicitly parallel file-owner tasks; agree the module-owned size payload first.
Within US1, transport/UI authoring and backend test investigation can run independently after the
candidate contract is agreed, with separate files. Within US2/US3/US4, independent review/investigation
may overlap, but mutations of shared session/proposal/driver files stay sequential. At most root plus
two active subagents, one implementation leaf and one integration/review lane; respect build slots.

## Delivery strategy and live completion

The first useful internal increment is the causal module baseline and US1. It is not a shipped MVP or
permission to stop; all stories and all Program milestones remain required. Commit logical groups,
open a draft when pushing, retain expected-failing baselines separately and continue autonomously.

T028–T031 include live GitHub/merge/resulting-main events after the final source commit. Their verified
completion belongs on the owning issue/PR and Project; do not pre-check them in source or open a
separate status-only PR. Spec Implemented describes delivered source, not verified issue/Program Done.
