# Tasks: Module-owned Weaver contributions

**Input**: [spec](spec.md), [plan](plan.md), [research](research.md), [data model](data-model.md), [candidate contract](contracts/contributions.md).

Work unit #3437 owns the candidate and two thin connected journeys. #3438 independently consumes real packages before freeze; its proof is not claimed by in-tree fixtures. Existing application code is evolved with risk-based tests and causal red/green checks; no new greenfield application cadence or benchmark is introduced. One implementation leaf and one integration/review lane remain active.

## Phase 1 — Setup

- [x] T001 Refresh the claim, competing PRs, main health and source inputs; record candidate scope and artifact reviews on #3437/PR #3448, with `specs/1018-weaver-module-contributions/plan.md` as the reviewed plan.

## Phase 2 — Foundational contracts and composition

All stories require this phase; implementation files have explicit owners to avoid concurrent edits.

- [x] T002 Move the active defaults/feature/registration into `Extensions/Agent/src/Elsa.Agent/Elsa.Agent.csproj`, move `DeterministicAgentProvider` into `Extensions/Agent/tests/Tests`, update leaf features/references/Workbench/`Elsa.Server.slnx`, remove all 16 Agent entries from `Essentials/tests/Architecture/Tests/ArchitectureGuardTests.CoreImplementationShape.cs`, and preserve configured IDs with idempotent base wiring.
- [x] T003 Define the minimal candidate target, context origin/schema/availability, prepared invocation, typed payload, client request/ack and effect-receipt contracts in `Extensions/Agent/src/Core/Models` and `Extensions/Agent/src/Core/Contracts`; record exact JSON field agreement in `specs/1018-weaver-module-contributions/contracts/contributions.md` before frontend transport changes.
- [x] T004 [P] Evolve matching public SDK types/collections in `Studio/src/essentials/Elsa.Studio.Hosting/Client/src/sdk/index.ts` and `app/registry.ts`, keeping `api.agent` canonical and `api.ai` at most an actively consumed prompt-dispatch alias.
- [x] T005 Verify leaf/default/Events discovery, missing dependency diagnosis, stable IDs, replacement/aggregator counts and scoped resolution in `Extensions/Agent/tests/Tests/AgentFeatureRegistrationTests.cs`; retain domain-owned test visibility and document public API/package migration.

## Phase 3 — US1: Ask about the exact module context (P1)

**Independent check**: workflow and unrelated contexts retain admitted attachments/source/request identity and exact target/revision; arbitrary route text cannot invent a resource.

- [x] T006 [US1] Add named context/capability/tool collecting events in `Extensions/Agent/src/Core` and one aggregator each in `Extensions/Agent/src/Elsa.Agent`; evolve the existing source interfaces and feed the active registry with awaited Sequential outcomes and duplicate/version diagnostics.
- [x] T007 [US1] Connect module-owned caller-context admission/redaction and final host policy in `Extensions/Agent/src/Api/AgentApi.cs` and `Extensions/Agent/src/Elsa.Agent/Services`; stamp client origin server-side, reject unknown kinds/versions and remove workflow route inference/default kind knowledge.
- [x] T008 [P] [US1] Carry complete prompt requests through `Studio/src/essentials/Elsa.Studio.Hosting/Client/src/app/App.tsx` and `app/weaver/useWeaverSession.ts`; preserve busy/enqueue/steer correlation or explicitly refuse unsupported input.
- [x] T009 [US1] Implement typed required/optional collection/admission diagnostics in `Studio/src/essentials/Elsa.Studio.Hosting/Client/src/app/agent/agentContext.ts`, with cancellation and no silent duplicate dropping.
- [x] T010 [US1] Cover source fan-in, installed-only admission, explicit attachment handoff, route refusal, provenance and optional/required outcomes in `Extensions/Agent/tests/Tests` and the affected `Studio/src/essentials/Elsa.Studio.Hosting/Client/src/app/agent/__tests__`/`app/weaver/__tests__` suites, including causal baseline failures.

## Phase 4 — US2: Review and observe a module-owned change (P1)

**Independent check**: a reviewed exact native workflow layout edit changes visible working state and produces an observed local receipt/guarded undo; an unrelated server fixture changes its owning resource and returns read-back evidence. Approval/pending is never applied success.

- [x] T011 [US2] Evolve the actual invoker and module tools to prepare/normalize/validate before review in `Extensions/Agent/src/Elsa.Agent/Services` and `Extensions/Agent/src/Workflows/Tools`, binding exact target, arguments, revision, risk, permissions and tool version.
- [x] T012 [US2] Split data-only singleton proposal state from scoped execution in `Extensions/Agent/src/Elsa.Agent/Services`; replace the success-reporting no-op with real registered effects or explicit unavailable, reserve Executing rather than Executed, and retain actual typed outcomes.
- [x] T013 [US2] Add authenticated pending-client HTTP 202 and acknowledgement handling in `Extensions/Agent/src/Api/AgentApi.cs`, `Models/AgentApiModels.cs` and `AgentJsonContext.cs`; check session/actor/tenant, correlation, exact envelope, current authority, generation and expiry without executing from the acknowledgement.
- [x] T014 [P] [US2] Implement the generic client invocation runner and pending/receipt UI in `Studio/src/essentials/Elsa.Studio.Hosting/Client/src/app/agent` and `app/weaver`, using registered tool callbacks with identity/deduplication and generic review controls/renderers.
- [x] T015 [US2] Replace workflow window snapshot/apply broadcasts with module-private live bindings in `Studio/src/essentials/Elsa.Studio.Workflows/Client/src/module.tsx` and `workflow-editor`; enforce exact ID/revision/generation at the current-state transaction boundary, observe committed editor state before acknowledgement, preserve atomic validation/dirty state, guard whole-batch undo against intervening edits, and hold the autosave debounce for Weaver working-state changes until explicit save/publish or valid undo without changing the user's preference.
- [x] T016 [US2] Return stored receipts through paused-turn resumption in `Extensions/Agent/src/Elsa.Agent/Services` and through `Studio/src/essentials/Elsa.Studio.Hosting/Client/src/app/agent/agentClient.ts`; enforce explicit review for candidate client mutations even in FullAuto and state unattended continuation limits explicitly.
- [x] T017 [US2] Add backend/API tests for preparation/refusal, scoped service resolution, HTTP 202/ack, actual server readback and pending-versus-executed state in `Extensions/Agent/tests/Tests`; add module-owned revisioned resource fixtures there, without shipping the fixture as a default domain.
- [x] T018 [US2] Add frontend/native transaction regressions for observed receipt timing, exact stale target, dirty/validation/undo and preserved save/publish in `Studio/src/essentials/Elsa.Studio.Workflows/Client/src` and generic runner/review tests under Hosting; demonstrate red/green behavior.

## Phase 5 — US3: Extend Weaver with an independent module (P1)

**Independent check**: the unrelated candidate fixture uses the same contracts and typed transport as workflow participation; no shared domain switch is needed. Actual external package proof remains #3438.

- [x] T019 [US3] Migrate current workflow context/capability/prompt/tool/result producers into `Extensions/Weaver/studio/Elsa.Studio.Weaver.Workflows/Client/src/module.tsx` and the owning Workflows package, removing workflow registrations/presentation/application knowledge from shared `Studio/src/essentials/Elsa.Studio.Hosting/Client/src/app`.
- [x] T020 [US3] Remove workflow instructions/decoding and the Workflows project reference from `Extensions/Agent/src/GitHubCopilot`; preserve generic type/version/JSON results in Core MEAI bridges and `Extensions/Agent/src/Anthropic`, moving domain meaning into `Extensions/Agent/src/Workflows`.
- [x] T021 [US3] Prove equivalent admitted tool/result meaning through Copilot delegated and Anthropic host-driven protocol fixtures in `Extensions/Agent/tests/Tests/GitHubCopilotHarnessTests.cs` and `AnthropicAgentProviderTests.cs`; separately label deterministic orchestration and any opt-in live evidence.
- [x] T022 [P] [US3] Prove module load/removal, duplicate identity, incompatible version and missing renderer/counterpart behavior in `Studio/src/essentials/Elsa.Studio.Hosting/Client/src/app/agent/__tests__` and `Extensions/Agent/tests/Tests`, with explicit availability rather than fallback domain guesses.

## Phase 6 — US4: Preserve authority and incomplete-context visibility (P2)

**Independent check**: denied/stale/missing-before-dispatch cases have zero effects; after dispatch the real receipt or uncertainty survives without replay or a false rollback claim.

- [x] T023 [US4] Recheck actual current actor/tenant/module/host permissions at preparation/application using an API adapter to owning authorization contracts in `Extensions/Agent/src/Api/Authorization`; ensure denied tools never reach effects and client metadata cannot grant authority.
- [x] T024 [US4] Implement process-lifetime duplicate/conflict, cancellation/expiry, module/context loss and post-dispatch uncertainty rules in `Extensions/Agent/src/Elsa.Agent/Services` and the Hosting generic runner; invalidate API/identity/backend/registry replacement bindings without silently retaining an old local session target.
- [x] T025 [US4] Cover denial/revocation, secret/instruction-shaped content, late/conflicting/duplicate acknowledgements, navigation/removal and lost post-effect acknowledgement in `Extensions/Agent/tests/Tests` and affected Studio suites. Confirm scope-specific readback, no duplicate effect and no inferred restart recovery.

## Phase 7 — Integrated qualification and records

- [ ] T026 Build an owned fresh Workbench/Studio composition and prove both candidate journeys through real HTTP/SSE and rendered native UI, recording evidence with `Studio/tests/browser/weaver-contributions.spec.ts` or the existing owned-host harness; do not replace this with mocked browser routes.
- [ ] T027 Run affected complete backend/frontend projects and compare executed/discovered counts; run architecture restore/guards, maps/tier checks, locked solution restore, relevant builds/lint and package dependency closure per `specs/1018-weaver-module-contributions/quickstart.md`. Reconcile the recorded baseline Studio inotify failure rather than relabel it green.
- [x] T028 Update `Extensions/Agent/src/Elsa.Agent/EXTENSION_POINTS.md`, Workflow Agent/integration guidance and root `EXTENSION_POINTS.md` alongside final candidate signatures; record API/SemVer migration and the actual supported subset in `specs/1018-weaver-module-contributions/contracts/contributions.md` without claiming #3438 freeze.
- [ ] T029 Set `specs/1018-weaver-module-contributions/spec.md` to Implemented in the finishing PR and remove its temporary quality checklist only when this candidate is actually complete; complete issue acceptance/evidence, independent draft review rounds, exact-head required checks and authorized merge for PR #3448, then verify resulting main and reconcile Program/Project status.
- [x] T030 Record the #3438 handoff prerequisites and still-open package/freeze gate on the owning issue and `specs/1018-weaver-module-contributions` records. This task records the handoff only; performing and accepting that proof belongs to #3438 after verified #3437 completion.

## Dependencies and parallel work

T001 → T002/T003 → T004/T005 establishes the foundation. US1 establishes context/admission before US2 effects. US3 moves domain meaning and verifies both transports over those same seams; US4 qualifies the authority/lifecycle failures throughout. T026–T029 require all candidate stories. T030 records prerequisites for the next single implementation leaf; it is a Program handoff checkpoint, not a reason to close #3437 without its own evidence.

Parallel examples after prerequisite completion: backend context aggregation (T006–T007) alongside frontend full-request handoff (T008); backend pending-client transport (T013) alongside frontend runner (T014), after the JSON agreement; provider protocol tests (T021) alongside module availability tests (T022). Do not edit shared files concurrently; root integrates backend/API/provider behavior, a packaging worker completes relocation before root edits moved services, and a frontend worker owns Studio/module integration. Shared-machine builds stay coordinated.

## Incremental validation strategy

First validate exact context (US1); then validate one observed local and one observed server effect (US2), preserve equivalent transport/extension behavior (US3), and exercise negative authority/lifecycle cases (US4). These are internal checkpoints, not early Program completion. Continue through composed runs, reviews, merged-main evidence and #3438 without routine permission prompts. M2 durable recovery, M3/M4 inventory coverage and M5 qualification remain open until their own acceptance evidence exists.

Candidate checkpoint: Agent203/203 (203unique discovered), Studio .NET640/640 with theory expansion reconciled, frontend3838/3838 across19 workspaces after final Hosting606 rerun, and two real deterministic source-composed journeys passed. Nine second-review regressions failed before their fixes and passed after restoration. Architecture592/592, locked restore and maps/tier/dependency checks passed; the generated filter ordering was refreshed after the implementation-project move. Final matching-ABI owned-host replay, exact-head CI/review and resulting-main verification remain open under T026–T029. #3438 remains the independent package/freeze gate.

Exact-identity follow-up: seven storage/lease/pause cases failed against3cdf7664f; the mapped PostMessage handler independently selected two contexts for one case-sensitive ID before its selector fix. All eight now pass; opaque IDs use ordinal comparison, while tool/capability name policy is unchanged. The final matching-source host replay follows this last engine correction.
