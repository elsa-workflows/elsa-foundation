# Tasks: Standalone triggers and workflow subscriptions

**Input**: [spec](spec.md), [plan](plan.md), [data model](data-model.md), [runtime](contracts/runtime.md), [targets](contracts/targets.md), [API](contracts/api.md), [transports](contracts/transports.md), [UX acceptance](ux-acceptance.md).

**State**: M0 reviewed for implementation. Unchecked tasks are not implemented/qualified. One canonical issue/branch/PR; this checklist is the staged critical path, not separate feature delivery claims.

**Tests**: ADR0080 risk-based tests apply. The derived constitution deliberately prescribes no TDD cadence; tests may accompany implementation, with meaningful causal failure controls before acceptance. Existing valid behavior remains covered. Final checks run affected projects whole and compare listed/executed counts. Do not request a new cadence approval or add documentation-text tests.

## Phase 1 — Setup and design (M0)

- [x] T001 Verify #3582 scope/claim/open PRs, current org/repo protocol, actual initiative boundary and current base; record scoped owner authorization and spec allocation on #3582.
- [x] T002 [P] Research actual runtime source/queue/retention, publication/model/input and Studio seams in `research.md`, `architecture.md` and `ux-acceptance.md`.
- [x] T003 Resolve confirmed owner choices in `spec.md` and contracts: slot-following, retained accepted work, per-trigger async admission policy and resource-owned authority; coordinate existing #3583/#3590/#2892 records.
- [x] T004 Draft `plan.md`, `data-model.md`, `contracts/`, `quickstart.md` and bounded proposed amendment in `docs/adr/0043-publication-slots-define-start-authority.md`.
- [x] T005 Independently review correctness, repository standards and #3582 acceptance across these artifacts; fix concrete findings and complete `checklists/requirements.md` before production implementation.
- [x] T006 Record reviewed design status/authority amendment and pre-code acceptance on #3582, commit the M0 candidate and open the one draft PR when pushing its branch in the same session (f064e47b7; PR #3912).

## Phase 2 — Foundation shared by all stories (M1/M2)

- [ ] T007 Add public thin resource/subscription/grant/configuration/mapping/capture models and purpose-specific source/type/model/store contracts under `Essentials/src/Workflows/Runtime/Core/`, with explicit single-replacement/contribution kinds and unknown-version refusal.
- [ ] T008 Add optional standalone feature registration and descriptor/adapter registry events in `Essentials/src/Workflows/Runtime/Elsa.Workflows.Runtime/`; diagnose duplicate keys and unsupported selected composition without adding a Runtime→Design reference.
- [ ] T009 Implement scoped revisioned resources/subscriptions/resource grants in the existing in-memory store family and EF entities/configuration/store registration under `Essentials/src/Workflows/Runtime/Persistence/EntityFrameworkCore/`; preserve source ownership, failed-update state and idempotent mutation receipts.
- [ ] T010 Add compatible true ingress arm and typed standalone recipient selection/progress to `Core/Models/RuntimePostCommitOutbox.cs`; preserve `RuntimePostCommitIntent`, old checkpoint/wire/fingerprint/key formats and original workflow source semantics.
- [ ] T011 Extend the existing outbox backend family/processor/dispatcher/source reader/retention in `Elsa.Workflows.Runtime/Services/Checkpoints/` and EF stores for actual source kinds, provider readiness and ordinary resumption; no second queue/pump.
- [ ] T012 Implement provider-atomic source admission with exact source-key replay/conflict, resource/grant/association/publication compare and material-root acceptance in the selected in-memory/EF family; reject unknown/truncated evaluation rather than call it partial success.
- [ ] T013 Implement immutable `DispatchCommitted` seal, cancel CAS and per-recipient progress/target receipt transitions in the same physical source store; retain owed commands and source tombstones across failure and deletion.
- [ ] T014 Extend all four existing Runtime provider migration/schema chains under `Persistence/EntityFrameworkCore/Migrations/Runtime/` for new resource facts and source-neutral outbox projections; preserve old readers/bytes and refuse unsupported writers/providers.
- [ ] T015 Add meaningful Runtime/EF behavior and logic-bearing registration tests for T007–T014 in the existing owning test projects, including old wire vectors, split-provider refusal, atomic mutation/replay and source/seal/cancel crash controls.

## Phase 3 — US1: shared live resource management (M1/M4)

**Goal**: Create/reload resources and edit one association from either direction without changing a business graph. Source firing acceptance completes with US5; a management milestone alone is incomplete product delivery.

- [ ] T016 [US1] Implement internal endpoint/DTO/capability/actor/action composition in `Essentials/src/Workflows/Runtime/Api/StandaloneTriggers/` using the seven scoped permission/action roles in `contracts/api.md`.
- [ ] T017 [US1] Implement shared subscription queries plus typed preview/apply commands, impact review, conditional revisions and idempotent results; allow disabled never-published targets and preserve prior effective configuration on refusal.
- [ ] T018 [US1] Implement grant/revoke, enable/pause/delete and no/zero-eligible behavior under server-owned scope; creator departure leaves resource authority intact, explicit resource revocation stops fresh intake.
- [ ] T019 [P] [US1] Add normal Triggers routes/navigation/collection/create/detail screens in `Studio/src/essentials/Elsa.Studio.Workflows/Client/src/`, using studio-ui and Workflow Definitions/Connections references.
- [ ] T020 [US1] Add one shared association editor and workflow designer Triggers panel through the existing panels slot; guard capability/permissions and keep live associations distinct from open draft/historical graph state.
- [ ] T021 [US1] Add complete Runtime API and affected Studio tests for both-direction persisted associations, conflict/reload/focus recovery, no graph mutation, origin/read-only and target/grant/permission states.

## Phase 4 — US2: both HTTP modes and async policies (M3/M4)

**Goal**: One exclusive HTTP owner exposes intentional workflow response or truthful durable fan-out admission.

- [ ] T022 [US2] Extend common endpoint reservation/conflict admission and route generation/fingerprint under `Workflows/Runtime/Http/` and `Activities/Http/` for first activation, pause reservation, atomic route changes/deletion and embedded conflicts both ways.
- [ ] T023 [US2] Implement authorized HTTP source intake into the common admission service with body limits, optional scoped idempotency key, exact replay/uncertain commit lookup and no payload-owned authority.
- [ ] T024 [US2] Implement RequireAll/AcceptAvailable policy results in async fan-out: explicit complete accepted/refused set, no false empty 202 and no original exchange provided to downstream roots.
- [ ] T025 [US2] Implement bounded workflow-owned response observation/writing using committed instructions, including peer-worker execution, missing response, deadline/disconnect/write failure and idempotent response replay without another root.
- [ ] T026 [US2] Add visible mode/policy/response-capability/route state and impact review to Studio trigger creation/details and both association contexts.
- [ ] T027 [US2] Add affected complete HTTP/Runtime HTTP/API tests and actual composed request cases for both modes/policies, mixed/zero admission, conflict, malformed/unavailable input/store, replay, cancellation and separate execution/transport outcomes.

## Phase 5 — US3: serving targets and declared inputs (M1/M3/M4)

**Goal**: Unchanged native and genuine supported BPMN graphs receive declared inputs and follow the selected serving slot safely.

- [ ] T028 [US3] Implement trusted serving-slot/model target discovery/preparation in Runtime, with actual publication/source/logical identity and separate deduplicated material identity; default-refuse unsupported model/response profiles.
- [ ] T029 [US3] Implement trigger-owned literal/source-field/portable-expression mapping via existing `IPortableExpressionEvaluator` and input validator; preserve absence/null/default/numeric semantics without a new workflow expression binding discriminator.
- [ ] T030 [US3] Add shared safe mapping-context/preview and compatibility findings to the API and Studio mapping component; detect actual serving contract changes and expose blocked/unavailable targets without silently selecting an old version.
- [ ] T031 [US3] Add material-neutral activation requests/coordinator and Publishing bridge in existing Runtime/Publishing Core/implementation packages, reusing empty projection states and one existing slot/switch/ref lifecycle.
- [ ] T032 [US3] Extend genuine authored BPMN publication, direct start and standalone target adapter in `Activities/Bpmn/Interchange/` and `Elsa.Activities.Bpmn/Internal/`; protect new authored direct slot/source comparison through the actual deterministic queue commit, reuse existing material leases/retention, default-refuse unsupported composition and preserve legacy raw identities/profile formats.
- [ ] T033 [US3] Implement the reviewed input-capable BPMN profile/state/input-source/native-work versions and typed literal/model-input worker mapping, with committed input consumption and service continuation through ordinary ExecuteModel.
- [ ] T034 [US3] Add authored BPMN literal/model-input worker mapping in `Studio/src/essentials/Elsa.Studio.Workflows/Client/src/bpmn/BpmnServiceBindingEditor.tsx` and `bpmnDirectBinding.ts`, plus target/serving/model readiness display; do not report native wrappers as direct-model evidence.
- [ ] T035 [US3] Add whole affected Runtime/Publishing/BPMN/Interchange/Hosting/Studio tests for native and genuine model inputs, exact pins, direct-start versus slot replacement/unpublication in both race orders, lost-ack replay, shared material, restore, unsupported profiles and unchanged legacy bytes/semantics.

## Phase 6 — US4: independent durable delivery and recovery (M2/M4)

**Goal**: Repeat/crash/partial recovery converges per subscription and distinguishes planned commitment, actual target acceptance and execution outcome.

- [ ] T036 [US4] Add the exact accepted-ingress authority and ordinary native root preparation/dispatch operation in Runtime Core/`Services/Executions/`; validate the immutable physical source seal independent of expired handler claims.
- [ ] T037 [US4] Preserve deterministic root identity/time/initial-checkpoint successor lineage for ingress in ordinary native Start handling; reconcile physical queue/successor/state evidence and remove dependence on memory-only Duplicate results.
- [ ] T038 [US4] Implement exact native/model target receipt lookup and source handoff/retry/poison reconciliation through normal actor/queue/checkpoint boundaries; never infer target acceptance from a planned ID or generic Deferred result.
- [ ] T039 [US4] Expose safe source/recipient/attempt/transport/linked execution projections and retry/cancel preview/apply under the canonical delivery API; map Pending/DispatchCommitted/TargetAccepted truthfully.
- [ ] T040 [US4] Build Studio delivery list/detail/recipient recovery/cancel views with current-versus-captured versions, actionable failures and separate authority/configuration/read-only state.
- [ ] T041 [US4] Qualify real EF restart/multi-worker source/queue/first-checkpoint interruption points, cancellation/seal races, expired claims, partial success, publication change and source/material retention/collection. Add causal whole-suite regressions in owning Runtime/EF/model projects.

## Phase 7 — US5: Event and schedule operation (M3/M4)

**Goal**: Real Events and due schedule occurrences start independent subscribers while embedded starts and waits retain their contracts.

- [ ] T042 [US5] Add standalone Event type/source and typed recipient selection to actual committed Event cohorts in `Activities/Primitives/`; retain original source/wait snapshot/local resume-only semantics and do not rematch on retry.
- [ ] T043 [US5] Add resource-owned schedule arm, generation/claim/occurrence key and common-source admission to existing Runtime scheduling stores/pump, with safe settlement after admission and partial delivery recovery.
- [ ] T044 [US5] Add explicit Cron timezone/next-firing calculation and existing dialect, bounded catch-up, intentional pause/resume/update generation and DST/clock behavior in `Workflows/Runtime/Scheduling/`; do not alter old embedded schedule keys.
- [ ] T045 [US5] Add source configuration/next firing/impact and schedule/Event delivery state to the normal Studio trigger screens using server descriptors/calculation.
- [ ] T046 [US5] Add whole affected Primitives/Scheduling/Runtime/EF suites and real Event/interval/Cron host witnesses for two subscribers, one workflow/two triggers, embedded/local/wait controls, update/due/clock/lease/restart and tenant isolation.

## Phase 8 — US6: module, identity and deployment composition (M3/M4)

**Goal**: Installed source/model extensions and normal resource permissions behave consistently across supported host shapes.

- [ ] T047 [US6] Qualify public source/target/type contracts with an independently compiled consumer through normal registry/capability/physical admission; test duplicate keys, absent editor/provider/profile and default unsupported before effects.
- [ ] T048 [US6] Qualify actual Design-only/Runtime-only/combined composition with the bounded management feature and no implicit runtime listeners in Design-only; API/Studio degrade to their genuinely installed operations.
- [ ] T049 [US6] Qualify real reader/operator/denied identities, resource grant versus creator-departure controls, source-owned resources/provider removal and tenant/access isolation across APIs, source intake, recovery and both Studio surfaces.
- [ ] T050 [US6] Update affected PublicAPI files, feature READMEs and owning `EXTENSION_POINTS.md` catalogs/root links in the same implementation; document normal provider composition and unsupported topology honestly.

## Phase 9 — Integrated delivery (M5)

- [ ] T051 Extend `Studio/tests/acceptance/` and `tools/ux-acceptance/` finite journey/capture inventory for the seven feature journeys in `ux-acceptance.md`; use actual HTTP/Event/due-schedule, genuine BPMN worker-input editing and persistence from both surfaces.
- [ ] T052 Run affected complete suites and causal controls, architecture graph/test, maps/test-tiers and locked restore when required; record listed/executed counts and passed/failed/skipped/unavailable states in the existing PR evidence.
- [ ] T053 Commit the candidate and run the actual owned composed Studio/backend matrix in `quickstart.md`, including restart/multi-node/provider/model witnesses and theme/narrow/keyboard/focus states.
- [ ] T054 Obtain independent visual UX inspection of current-commit downloadable screenshots/traces plus independent correctness/standards/acceptance review; fix findings and repeat affected suites/host journeys on the new exact head.
- [ ] T055 Finish practical operator/module documentation/examples for four families, both HTTP policies/modes, mapping, grants, slots, missed firing and recovery/retention limits in their existing canonical documentation homes.
- [ ] T056 Mark the converged draft ready, request normal review, satisfy exact-head checks and merge under current org rules without bypassing human holds/protection; record the actual merged commit and gate evidence on #3582/PR.
- [ ] T057 Qualify resulting main and the composed feature, set the spec lifecycle in the finishing implementation PR, remove terminal checklists and close #3582 only after all agreed acceptance is complete.

## Dependencies and parallel work

M0 review precedes production. T007–T009 establish resource contracts/persistence; T010–T015 establish common durable responsibility. US1 management uses those contracts and US3's serving/input selection. US2 requires common admission plus US3/US4 target handoff. US5 provides real Event/schedule firing; US6 qualifies all claimed extension/host shapes. M5 depends on every accepted feature criterion, not only the first vertical slice.

After foundational contracts converge, different file owners can implement Studio screens, source adapters and pure mapping/target preparation concurrently. Runtime Core/wire, EF context/migrations, activation coordinator and ordinary actor/checkpoint files have one writer at a time. All heavy builds/tests remain serial through build slots.

The first demonstrable slice is one Event resource → two native roots with declared inputs plus persisted association editing from both surfaces. It is an intermediate milestone; continue through genuine models, HTTP, scheduling, failure/recovery and final delivery. No task or PR title may present that slice as completing #3582.
