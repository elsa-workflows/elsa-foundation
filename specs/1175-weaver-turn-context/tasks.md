# Tasks: Authorized module context on every Weaver turn

**Input**: [spec](spec.md), [plan](plan.md), [research](research.md), [data model](data-model.md), [contract](contracts/context-lifecycle.md), [qualification guide](quickstart.md).

Only #3439 is the active implementation leaf. Parallel work below is investigation or disjoint work inside that leaf, with a single integration lane. Risk-based behavior tests cover changed decisions, failures and concurrency. Reproduce suspected regressions before fixing them; do not invent a blanket greenfield TDD requirement.

## Phase 1: Setup

- [x] T001 Refresh main, Program hierarchy/dependencies/claims and Project65; reserve spec1175 and branch in `specs/1175-weaver-turn-context/spec.md` and owning issue records.
- [x] T002 Review specification, both constitution gates and scoped design in `specs/1175-weaver-turn-context/plan.md`; verify existing ignore rules for private evidence/build outputs.

## Phase 2: Reproduction and shared prerequisites

- [ ] T003 [P] Capture fresh package-consumer acceptance-to-dispatch and accepted-effect-to-continuation native authority probes using `tools/studio/Weaver.Conformance.Probe/run.py` and existing native IAM fixture; record actual adapter input, current owner denial and receipt truth on #3439.
- [ ] T004 [P] Reproduce admitted-display, per-request surface, granted permission filtering and same-tick request behavior in `Studio/src/essentials/Elsa.Studio.Hosting/Client/src/app/weaver/__tests__/WeaverSurface.test.tsx` and `src/__tests__/app-weaver-intent.test.tsx`.
- [ ] T005 [P] Reproduce required/optional failure, uncooperative contributor, Stop/late completion and budget behavior in `Studio/src/essentials/Elsa.Studio.Hosting/Client/src/app/agent/__tests__/agentContext.test.ts` and `Extensions/Agent/tests/Tests/AgentContextContributionTests.cs`.
- [ ] T006 Update demonstrated findings and smallest actual contract decisions in `specs/1175-weaver-turn-context/research.md` and `contracts/context-lifecycle.md`; reevaluate constitution gates before contract edits.

## Phase 3: US1 - Correct context for this request

**Independent test**: Explicit correlated request on A followed by fresh intent on B displays the admitted owner/revision/limitations; an older operation stays on A or refuses.

- [ ] T007 [US1] Preserve and refresh request surface/source/correlation and return actual admitted context in `Extensions/Agent/src/Api/AgentApi.cs`, `src/Api/Models/AgentApiModels.cs` and `Studio/src/essentials/Elsa.Studio.Hosting/Client/src/app/agent/agentClient.ts` as demonstrated necessary.
- [ ] T008 [US1] Display backend-admitted evidence and opaque metadata in `Studio/src/essentials/Elsa.Studio.Hosting/Client/src/app/weaver/useWeaverSession.ts` and `src/app/agent/AgentContextChips.tsx`, removing domain payload interpretation.
- [ ] T009 [US1] Prove provenance/revision, explicit attachments, redacted/partial/unavailable display and A-to-B selection/editor transitions in `Studio/src/essentials/Elsa.Studio.Hosting/Client/src/app/weaver/__tests__/WeaverSurface.test.tsx` and owning workflow bridge tests.

## Phase 4: US2 - Current authority at every disclosure

**Independent test**: Fresh normal identity after native grant withdrawal denies owner reads and prevents forbidden context/results/history reaching either actual adapter, while one accepted effect remains true and occurs once.

- [ ] T010 [US2] Re-admit or explicitly withhold retained resource evidence before provider use in `Extensions/Agent/src/Elsa.Agent/Services/AgentTurnServices.cs`, reusing installed module admission/current host policy without retargeting or replay.
- [ ] T011 [US2] Apply matching current tool admission/policy/scope semantics in `Extensions/Agent/src/Elsa.Agent/Services/AgentTurnServices.cs` and `src/Elsa.Agent/Services/AgentContributedToolServices.cs` as warranted by repro; preserve native owning authorization.
- [ ] T012 [US2] Prove ordinary/continuation disclosure, truthful receipts, original target binding, authority and host-policy changes in `Extensions/Agent/tests/Tests/AgentTurnOrchestratorTests.cs` and related existing Agent tests.
- [ ] T013 [US2] Prove normal remount and retained custom editor identity/module lifetimes through `Studio/src/essentials/Elsa.Studio.Workflows/Client/src/workflow-editor/useWorkflowContextBridge.ts` tests and the native independent fixture; change owning lifetime code only for demonstrated gaps.

## Phase 5: US3 - Bounded contributor and delivery outcomes

**Independent test**: Required/optional faults, noncompletion, Stop, late completion, budgets and two same-tick requests each produce the specified explicit disposition without stale state or hidden missing required evidence.

- [ ] T014 [US3] Implement bounded isolated contribution outcomes in `Studio/src/essentials/Elsa.Studio.Hosting/Client/src/app/agent/agentContext.ts` and `Extensions/Agent/src/Elsa.Agent/Handlers/CollectAgentContributions.cs`, preserving named Sequential event semantics.
- [ ] T015 [US3] Preserve cancellation/generation isolation and explicit simultaneous-request disposition in `Studio/src/essentials/Elsa.Studio.Hosting/Client/src/app/weaver/useWeaverSession.ts` and `src/app/App.tsx` as demonstrated necessary.
- [ ] T016 [US3] Prove complete/partial/required/fault/deadline/cancellation/late-mutation/volume outcomes in `Studio/src/essentials/Elsa.Studio.Hosting/Client/src/app/agent/__tests__/agentContext.test.ts`, `src/__tests__/app-weaver-intent.test.tsx` and `Extensions/Agent/tests/Tests/AgentContextContributionTests.cs`.

## Phase 6: US4 - Provider parity and module extensibility

**Independent test**: Both actual adapters receive equivalent admitted tools and denial causes, installed guidance stays distinct from untrusted data, forbidden values are absent, and the independent resource completes the full journey.

- [ ] T017 [US4] Prove installed-guidance trust, instruction-shaped data, secret redaction, unrelated exclusion and bounded authorized detail reads in `Extensions/Agent/tests/Tests/AgentContextContributionTests.cs` and existing provider tests.
- [ ] T018 [US4] Qualify same canonical tools and refusals under current permissions/scopes/module/host policy using `tools/studio/Weaver.Conformance.Probe/` and both actual adapters; inspect actual provider inputs/results.
- [ ] T019 [US4] Extend the existing native conformance fixture in `tools/studio/Weaver.Conformance.Probe/` for the demonstrated #3439 controls with bounded execution and owner evidence; preserve all accepted M1 cases and immutable old roots.
- [ ] T020 [US4] Rebuild supported packages and complete the independent resource/workflow witness through supported consumers using `tools/studio/Weaver.Conformance.Probe/README.md`, preserving normal workflow validation, atomic edit/whole undo and explicit save.

## Phase 7: Integration, review and verified closure

- [ ] T021 Update actual public API/SDK contract baselines and owning extension catalogs in `Extensions/Agent/` and `Studio/src/essentials/Elsa.Studio.Hosting/`; document module-author failure/deadline/admission rules and update `docs/program-goals/weaver-across-studio.md` with bounded M1/current M2 state.
- [ ] T022 Run full affected Agent/Hosting/Workflows/Studio tests and architecture/maps gates; reconcile discovery/execution/skips and retain gate evidence on #3439 and its scoped PR.
- [ ] T023 Review the integrated design against every #3439 criterion, update `specs/1175-weaver-turn-context/` to its accurate terminal implementation snapshot and remove its temporary checklist only when implementation is actually complete.
- [ ] T024 Open/attach a scoped draft PR for #3439, converge independent nonauthor review, publish required gate evidence and request normal Code Review through the authorized repository process; preserve claim/Project transitions.
- [ ] T025 Verify required checks and reviewed head, obtain authorized merge and verify resulting descendant main through affected tests/native consumers; record evidence on the owning issue/PR, without substituting issue closure for validation.
- [ ] T026 Reconcile verified acceptance/task checklists, dependency and claim release, Program/M2 and Project65 statuses; mark #3439 Done only after T025, then continue the next eligible leaf.

## Dependencies and parallel work

T001–T002 precede implementation. T003–T005 are independent causal investigations; T006 records their conclusions before contract changes. US1 and US3 client work can be developed in disjoint files with coordinated SDK/API edits. US2 depends on native evidence and shares backend collection contracts with US3; integrate those sequentially. US4 validates all integrated stories. T021–T026 follow passing integrated qualification and use one review lane.

Parallel examples: native authority baseline versus controlled client collection experiment; client display work versus backend retained-admission work; independent nonauthor review versus immutable evidence/artifact audit. Do not concurrently edit shared SDK/API contracts or run redundant builds.

## Implementation strategy

Deliver observable behavior in the order above, reusing existing generic seams and module ownership. An independently tested story is an integration increment, not completion of #3439 or the Program. No milestone continuation permission is needed within the recorded full delivery authorization.
