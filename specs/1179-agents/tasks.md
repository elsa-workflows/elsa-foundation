# Tasks: User-defined Agents

**Input**: [spec.md](spec.md), [plan.md](plan.md), [research.md](research.md), [data-model.md](data-model.md),
[contracts](contracts/) and [qualification guide](quickstart.md).

**Status**: Implementation in progress. Checked boxes record completed implementation and local
qualification; they do not assert product acceptance. M2 is complete through
[PR #3573](https://github.com/elsa-workflows/elsa-foundation/pull/3573), with exact-head CI and
resulting-main qualification on `0d6db68a3`. M3 is complete; M4 remains open under
[#3524](https://github.com/elsa-workflows/elsa-foundation/issues/3524), and M5 implementation is active under
[#3525](https://github.com/elsa-workflows/elsa-foundation/issues/3525). The product spec remains nonterminal.

**Shared revision delivery (4 November 2026)**: Owner agreement is recorded by merged PR #3783 (`4043cbfb1`); the owner separately authorized [#3790](https://github.com/elsa-workflows/elsa-foundation/issues/3790) on 4 November 2026. This delivery implements the source-storage slice of T065/T069. It does not ratify provisional constitution sections or infer additional human approvals. W1–W8 remain open until their complete witnesses pass. T001–T063 retain their historical meaning. #3790 owns source storage/adapters and storage tests; T066–T071 remain pending. The owner permits pre-GA clean-store recreation, but this leaf performs no live reset.

**Cadence**: ADR 0080 D1 risk-based behavior tests; no invented TDD rule or coverage quota. Tests
accompany their owning implementation slice and complete before its review/merge. Use meaningful
causal negatives, entire affected suites and discovery/count reconciliation. Generated composition
proof covers plain registration; add a feature-specific test only where wiring has decisions.

## Phase 1 — Setup and shared contract foundations (M2)

- [x] T001 Establish actual agent Core/Design/runtime project envelopes and stable provider/descriptor/feature identities in `Extensions/Agent/src/`, with `.slnx`, central package/API/lock/catalog registration as each real feature appears.
- [x] T002 Define justified immutable manifest/runtime/request/receipt/capability/authority shapes in `Extensions/Agent/src/Core/Models/` and genuine store/execution extension ports in `Extensions/Agent/src/Core/Contracts/`; verify independent package-author needs before making types public.
- [x] T003 Implement shared bounded JsonSchema.Net validation without a Design dependency in `Extensions/Agent/src/JsonSchema/`, plus typed contract-to-schema compilation in `Extensions/Agent/src/Design/Services/AgentSchemaCompiler.cs`; test malformed/remote references, required/null/unknown members and compatible field constraints.
- [x] T004 Qualify the normal central MEAI 10.10.1/MAF 1.27.0 graph in `Directory.Packages.props` and owned `packages.lock.json` files, including existing `Extensions/Agent/tests/` suites, actual CShells/shared-assembly/package closure and reviewed public/nuspec changes.

The central dependency enabler [#3544](https://github.com/elsa-workflows/elsa-foundation/issues/3544)
promotes the normal MEAI pin/locks and current Agent compatibility. T004's bounded production
runtime/package closure is now qualified through actual CShells activation and independent model,
authoring and runtime consumers. Dependency/PR CI and merge remain gates; this establishes no live
provider, durable journal or complete support-matrix acceptance.

## Phase 2 — Author, publish and execute the first fresh agent (US1; M2)

**Goal**: A real published contract with the final facade/worker shape, not a throwaway inline path.
**Independent test**: Save/test/publish/select an exact agent using an independent deterministic
model package; disclose disposable state until M3 durability/provider delivery. Final product A1–A3
also requires M3/M5/M7 evidence.

- [x] T005 [US1] Implement provider/schema authoring capabilities, contract proposals and validation in `Extensions/Agent/src/Design/Services/AgentActivityProvider.cs`, using generic activity storage/authority and one public I/O contract.
- [x] T006 [US1] Compile exact schemas, descriptor, public result projections and final facade/background child into immutable fresh-invocation material in `Extensions/Agent/src/Design/Services/AgentActivityProvider.cs`; test prompt-only/schema/model hashing and compatibility floor. Explicit continuation controls and their collision checks are delivered with T020.
- [x] T007 [US1] Add snapshot-bound active-draft provider membership in `Essentials/src/Activities/Design/Persistence/EntityFrameworkCore/Stores/EfActivityDesignStores.cs`; preserve exact owner scope, pre-count/page filtering and existing projection retention.
- [x] T008 [US1] Prove unpublished/multiple/mixed-provider drafts, discard/publish transitions, same-ID global/tenant isolation, stale cursors and all four SQL translations in `Essentials/tests/Activities/Design/Persistence/EntityFrameworkCore/Tests/`.
- [x] T009 [US1] Implement the native facade/typed completion checkpoint participant and fixed background-safe MAF leaf in `Extensions/Agent/src/Elsa.Agent/Activities/`, with actual aliases, Background placement and opaque encrypted-journal request/receipt references; explicitly refuse unsupported native encryption/secret-input policies.
- [x] T010 [US1] Implement real disposable journal behavior and the admitted MAF client path in `Extensions/Agent/src/Elsa.Agent/Services/`, enforcing fresh dispatch counts/deadline from the start; preserve final schemas and forbid production durability claims from memory stores.
- [x] T011 [US1] Prove actual reviewed publication, preflight capability refusal, invalid output, exact result projection and old-artifact isolation with a separately packaged deterministic client/consumer in `tools/agents/conformance/` and `Extensions/Agent/tests/Tests/Publication/`.
- [x] T012 [US1] Integrate normal create/save/review/publish/recommendation/lifecycle permissions and diagnostics through `Extensions/Agent/src/Design/` and existing Activity Design/Publishing APIs; no duplicate CRUD/catalog.
- [x] T013 [US1] Update provider/runtime extension catalogs and module-author configuration in `Extensions/Agent/src/Design/EXTENSION_POINTS.md`, `Extensions/Agent/src/Elsa.Agent/EXTENSION_POINTS.md` and root `EXTENSION_POINTS.md`.

## Phase 3 — Durable model access, continuity and recovery (US3; M3)

**Goal**: Explicit scoped continuation and accepted-result replay under real persistence.
**Independent test**: Restart between turns/acceptance/delivery, force scope/revision/fence/uncertainty
and cancellation races, and verify journal and process independently.

T014–T015 are complete through prerequisite [#3578](https://github.com/elsa-workflows/elsa-foundation/issues/3578)
under existing Connections owner #2854, independently reviewed PR #3591 and source/package/core/Studio
qualification on resulting main `5be59cebc`. They do not establish M3 agent execution or later product
acceptance. M3 source and local qualification are implemented in PR #3594, including the shared draft
Test Run application service, concrete SDK/socket profiles, optional streaming, protected continuity,
retention and packaged recovery. T016–T023 implementation is complete; independent review converged.
Required reviewed-head CI, Code Review merge and fresh resulting-main source/package/affected-browser verification completed through PR #3594 on `8dcbf2345`; broader hosted main jobs remain separately monitored.
The complete host/provider/store and live/Studio product acceptance remains M4–M7.

- [x] T014 [US3] Implement durable existing `IConnectionRepository` semantics in `Essentials/src/Connections/Persistence/EntityFrameworkCore/`, including four provider mappings/migrations, consumer-owned DbContext, TryAdd/version-CAS/conditional-delete and secret-reference behavior.
- [x] T015 [US3] Prove restart/concurrent save/revoke/create rollback/secret-reference isolation and HTTP connection regressions in `Essentials/tests/Connections/Persistence/EntityFrameworkCore/`; publish real durable backend capability through Connections Core.
- [x] T016 [US3] Implement concrete model connection/client/capability contributors in `Extensions/Agent/src/Anthropic/` and `Extensions/Agent/src/AzureOpenAI/`, with point-of-use auth, allowed hosts, provider setting/schema support and no Weaver harness activation.
- [x] T017 [US3] Test actual provider SDK request/options/response loss/disabled opaque retries and capability drift using protocol servers in `Extensions/Agent/tests/Tests/Providers/`; count physical requests rather than only outer agent runs.
- [x] T018 [US3] Implement conversation/turn EF records (evaluation records follow T037), CQS/CAS/unexpired fencing and four provider migrations in `Extensions/Agent/src/Persistence/EntityFrameworkCore/`; preserve the model in `data-model.md`.
- [x] T019 [US3] Implement exact operation/fingerprint admission, atomic accepted session/result/usage and old-operation replay in `Extensions/Agent/src/Elsa.Agent/Journal/AgentTurnJournal.cs`, with persisted single-agent model-call/time/output-token reservations and deadline/unknown-usage settlement in `AgentTurnBudgetAllocator.cs` before real provider dispatch.
- [x] T020 [US3] Implement versioned minimal session codec, scoped protection envelope, key retention/rotation and fresh/explicit-continuation policy in `Extensions/Agent/src/Elsa.Agent/Services/AgentSessionCodec.cs`; add optional invocation controls with collision checks, and exclude private reasoning/credentials.
- [x] T021 [US3] Implement exact-operation cancel/applied intents, pre-admission tombstones and durable idempotent settlement through existing native checkpoint/outbox seams in `Extensions/Agent/src/Elsa.Agent/Services/AgentCheckpointEnricher.cs` and `AgentSettlementHandler.cs`.
- [x] T022 [US3] Prove sensitive/redacted native flow and explicit encryption-required/secret-input refusal, journal winners, delayed cancellation, lease loss, uncertainty and crash/ack boundaries in `Extensions/Agent/tests/Tests/Recovery/`; include pre-dispatch budget exhaustion/deadline, unknown usage/restart and SDK physical-request controls, with no accepted model replay/cancelled parent continuation.
- [x] T023 [US3] Deliver retention/purge/disabled-new-admission and old codec/key reader compatibility in `Extensions/Agent/src/Elsa.Agent/Services/AgentRetentionPruner.cs` and `Extensions/Agent/tests/Tests/Recovery/`; retain necessary pins/tombstones.

M3 admits no tool execution, so its tool budget is zero by capability refusal; T043/T047 add and
reserve actual tool/team effects through the owning #2912 contracts. Evaluation records belong to
M5/T037 with concrete case/run authorization and retention; T018 owns only the conversation/turn
persistence needed by agent execution. This clarifies sequencing and preserves the complete
Program acceptance rather than adding unused evaluation rows to M3.

## Phase 4 — Native and BPMN editorial composition (US2; M4)

**Goal**: Actual authoring/publishing/bindings and equivalent process results, including Design-free BPMN.
**Independent test**: Save/reload/publish/run the same Copywriter/Proofreader references natively and
through BPMN; duplicate/late results cannot repeat parent continuation.

- [ ] T024 [US2] Extract neutral BPMN semantic models/identities into `Essentials/src/Activities/Bpmn/Core/`, preserving JSON/aliases/schema and justified neutral type forwarders.
- [ ] T025 [US2] Move authored structure/handler to `Essentials/src/Activities/Bpmn/Design/`, remove Design references from runtime, specialize Interchange and update `Essentials/apps/Elsa.Workbench/` shell manifests without losing authoring registration.
- [ ] T026 [US2] Qualify current compiled-state interpretation and runtime/interchange/authoring tests, and document the coordinated pre-GA authored-type API/package/schema clean break in `Essentials/tests/Activities/Bpmn/` and `Essentials/src/Activities/Bpmn/` catalogs/docs.
- [ ] T027 [US2] Deliver the generic exact authorized native `HumanReview` wait/decision endpoint (agents are user-defined; the editorial process is a sample, not product code) and pre-claim `ActivityResumeValidating` guard with claimed-replay validation in `Extensions/Agent/src/` per `contracts/human-review.md` (sample: `contracts/editorial.md`); prove Sequence/Flowchart ports, rejected-review article/comments feedback, three-round exhaustion, approve/deny, restart and duplicate/foreign/changed/late-decision refusal in `Extensions/Agent/tests/Tests/Composition/` using real publication/bookmark APIs.
- [ ] T028 [US2] Deliver the generic exact review page under `Extensions/Agent/studio/Elsa.Studio.Agent/` and prove the same revision loop/human approve/deny/restart/duplicate scenarios through BPMN bound children, save/reload/export/import and explicit unsupported refusal in `Studio/tests/browser/` and `Extensions/Agent/tests/Tests/Composition/`. Delivered in M4: the page, its browser cases, native BPMN bound-children journeys, and an export → import journey in which bound agent and review work never travels in shared XML, import refuses execution with every bound task reported as requiring an explicit binding, and the author's exact rebinding compiles and remaps through the BPMN structure handler and runs the loop (`EditorialBpmnTests`, and `EditorialInterchangeTests` as of M4; T063 has since replaced that journey with a binding-preserving one), per `contracts/human-review.md`. M4 runs from composed executables over really published agents; saving, reloading and publishing the process as a workflow definition and the browser import refusal move to T036, and binding-preserving interchange to T063. Deferred to M5: Studio-driven authoring (T030), the occurrence link to the page (T034).
- [ ] T029 [US2] Replace the research BPMN gap only after a fresh normal package consumer proves zero Design/Publishing assemblies and no mutable lookup in `tools/agents/conformance/`; retain authoring-only/combined profiles.

## Phase 5 — Studio author/test/publish/use/inspect (US7; M5)

**Goal**: Complete ordinary product journeys with exact identity and consistent state/permission behavior.
**Independent test**: Real Studio mouse/keyboard and 320px flows, saved/reloaded in a normal qualified host.

- [x] T030 [US7] Register the agent feature area and module-owned provider-filtered collection in `Extensions/Agent/studio/Elsa.Studio.Agent/Client/src/module.tsx` and `AgentsPage.tsx`; handle unpublished/mixed-provider/read-only/denied rows without client-side paging filters. Delivered in M5: the Agents area lists definitions through the generic API's server-side `providerKey` filter, search, count and cursor, creates an agent draft through the generic create endpoint, and opens a new draft, or a row's own resource (its matching published version, else its only active agent draft, else the definition's drafts to choose among, also when the draft lookup cannot answer; that generic Drafts section lists every authorized draft of the definition, each labelled with its provider and status), in the generic activity editor. Rows show the matching publication with its lifecycle, disclose another provider's recommended or latest version, label the server's all-provider draft and version counts as being of any kind, and report an expired or changed snapshot instead of refreshing it. The Workbench registers `AgentDesign`, `AgentApi` and the Anthropic and Azure OpenAI connection types.
- [ ] T031 [US7] Implement provider settings and public-interface constraint contributions in `Extensions/Agent/studio/Elsa.Studio.Agent/Client/src/AgentEditor.tsx`, honoring exact revision/readOnly/historyResetKey and generated schema preview. Delivered in M5 so far: the implementation form for manifest schemas 1 and 2. It edits instructions, one model connection chosen from every registered provider's connections (the provider follows from the connection), settings and limits offered from that connection's current capabilities via `Extensions/Agent/src/Api/`, and explicit continuity on schema 2. It patches only the members it edits, keeps saved values it cannot use (an unavailable connection, a stale model, an unsupported setting, an unknown member) visible with an explicit repair, and distinguishes permission, unusable-model and failure answers. Two limits remain: the editor props cannot block the generic editor's save, so number text the form cannot accept is explained and never written but does not stop a save of the last valid value; and choosing a connection needs `workflows/design:read` or `connections:manage` for the Connections picker in addition to the authoring permission. Remaining: the public-interface constraint contribution (`inputConstraints`/`resultConstraints`) and the generated schema preview.
- [ ] T032 [US7] Deliver production-runtime candidate/playground/continuity/evidence endpoints in `Extensions/Agent/src/Inspection/Api/` (kept apart from the authoring reads in `Extensions/Agent/src/Api/`) by reusing generic draft test-run dispatch/reconcile/cancel, exact saved candidate artifacts and scoped journal reads. Delivered in M5 so far: the Workbench's stock shell composes `AgentRuntime` (the runtime consumer that publication preflight requires), `BackgroundActivities` for its model-turn workers and the durable `AgentEntityFrameworkCore` journal on the host's `Elsa` connection, so publication preflight finds the agent runtime consumer and an agent draft becomes publishable. A Production deployment validates migrations at start, so it must apply the journal's migrations (`dotnet elsa persistence apply`) first; the CLI acceptance legs script and apply them before a validated Workbench start. Playground runs start, poll and cancel through the generic draft Test Run routes, which already compile the exact saved candidate (`activity-test-version-…`, `draft-{revision}`) and run it on the production agent worker. The new `AgentInspectionApi` (`Extensions/Agent/src/Inspection/Api/`, composed by the Workbench) adds `GET agent/test-runs/{testRunId}/evidence`. It authorizes through the generic run read and returns that run's root invocation evidence:
  - the exact candidate version and artifact, the model, the limits and the native status;
  - the fault code, plus the message only for an agent refusal;
  - the result as the host's payload capture policy admits it, never a sensitive or encryption-required value;
  - the journal turn's status, model requests, usage and conversation;
  - the application status of this invocation's own delivery: not accepted, pending, settlement pending, applied, or not applied (faulted, cancelled or superseded);
  - and never protected request or session material, instructions, fences or fingerprints, all read through the runtime-owned `IAgentInvocationInspector`.
  - Playground continuity across runs: the root agent of an activity draft Test Run (its runtime-pinned source kind `ActivityDraftTestRun`) converses in the exact candidate's workspace, so a later run of the same saved revision continues explicitly through `$agent.options` `{conversationId, revision}` (a fresh run passes `{}`; see #3768), while an edited or stale revision, a published run and a nested agent cannot. The workspace is tenant-shared like the generic Test Run, which records no requester; `data-model.md` records that decision, and a caller-bound playground would first need the generic Test Run to bind its requester.
  - Runtime occurrence evidence: `GET agent/invocations/{workflowExecutionId}/{activityExecutionId}/evidence` reads any agent occurrence the caller may inspect. It requires `workflows/runtime:read` and the run's `workflows/activity-executions:inspect`. On both evidence reads, the result's value is re-authorized per run with `inspect-values`, otherwise left out. The stock Workbench test drives a real invocation against a closed local endpoint and reads it both ways.
  - Remaining: an observable partial-output surface for T033's partial-vs-accepted view: the runner collapses a streamed response before acceptance, so no read returns partial output yet.
- [ ] T033 [US7] Implement module-owned playground with fresh/continue, capability diagnostics, partial-vs-accepted output and truthful stop/uncertainty in `Extensions/Agent/studio/Elsa.Studio.Agent/Client/src/AgentPlayground.tsx`. Delivered in M5 so far: the playground route `/agents/drafts/{draftId}/playground`, linked from the agent editor. It runs the saved revision through the generic draft Test Run routes and polls the agent evidence. It shows:
  - the exact candidate, model, request count and usage (unknown counts stay unknown);
  - the application status, and the accepted output when the host's capture policy admits it, otherwise why it is withheld;
  - a fault code and message, a refused start's own reason and code, and an explicit outcome-unknown warning that offers no automatic retry;
  - Stop, which says a request already sent is not recalled, and says so plainly when the stop was not applied.

  Fresh is the default. A continuity-enabled agent can continue a conversation accepted by a run of the same saved revision, always at its latest accepted revision. Reloading another revision clears those choices. Resubmitting the same values after a lost start reuses its idempotency key. Failed status reads back off and then stop until the author retries.
  - Remaining: capability diagnostics beyond the backend's own refusals; partial streamed output (no backend surface yet, see T032); leaving an optional input out (#3768) and typed input editors richer than JSON text; browser proof with T036.
- [ ] T034 [US7] Reuse generic publication/history/lifecycle in `Extensions/Agent/studio/Elsa.Studio.Agent/Client/src/AgentHistory.tsx`; add exact occurrence links through the owning Studio SDK/shared inspector contribution with no hardcoded agent branch. Delivered in M5 so far, through two generic extension points and no agent branch in either host:
  - The runtime's `IActivityStateViewProjector` can match an executable node's descriptor kind, for a family of user-defined activities whose type keys are not known in advance.
  - Studio's `StudioWorkflowRunRelatedLinkContribution` can set `anyActivityType`, so the view kind alone decides.
  - The agent runtime projects every `elsa.agent` node as view `elsa.agent.invocation`, and the agent module links any such occurrence in run inspection to its invocation page (T035). The stock Workbench test reads that view for a real invocation.
  - Remaining: the generic publication history and lifecycle view (`AgentHistory.tsx`).
- [ ] T035 [US7] Implement permitted turn/process timeline/result/usage/approval/incident views in `Extensions/Agent/studio/Elsa.Studio.Agent/Client/src/AgentInvocationPage.tsx`, including completed/not-applied and unknown outcome. Delivered in M5 so far: the invocation page `/agents/invocations/{workflowExecutionId}/{activityExecutionId}` over the runtime occurrence evidence (T032), shared with the playground's evidence view. It shows:
  - the exact published or candidate version, model, model requests and usage;
  - the application status, including accepted-but-not-applied;
  - a fault code, and an explicit unknown-outcome warning;
  - the result only with the per-run value permission.

  It refreshes while the occurrence is unsettled, keeps the last evidence on a failed refresh, and opens the native run where incidents and parent activities are inspected. The playground links each run's invocation here.
  - Remaining: a turn/process timeline beyond the one journal turn; linking an accepted-but-not-applied turn to both decisions and its settlement evidence; approval views (human review has its own page); and the occurrence link from run inspection (T034).
- [ ] T036 [US7] Prove real create/edit/test/publish/picker/use/inspect/history, conflict/save/undo, denied/read-only/source-owned, keyboard/narrow and runtime occurrence behavior, plus the native and BPMN editorial process saved, reloaded and published as a workflow definition (including an imported BPMN process rebound and resolved through publication) and the browser-level BPMN import refusal and confirmation of retained task bindings (T063: the proposals, `confirmedBindings` and readiness fields in the Studio import UI), in `Extensions/Agent/studio/Elsa.Studio.Agent/Client/src/__tests__/` and `Studio/tests/browser/agents.spec.ts`.
- [ ] T061 [US7] Deliver configuration-defined agents (FR-027, plan D10):
  - a configuration source in `Extensions/Agent/src/Configuration/` that reads operator-versioned manifests, validates each definition (schema; connection name only; a content change requires a version bump, checked against the persisted hash for the same `(DefinitionId, Version)` computed as `IActivityDefinitionVersionFactory` does), and maps declared outcomes to the `elsa.outcomes` design facet that `SourceOwnedActivityVersionPublisher.ToContract` reads. A definition whose outcomes need an emitted-outcome mapping the publication contract does not yet support is refused. Invalid definitions are left out with per-definition diagnostics, which are logged and visible as validation diagnostics; only valid definitions are contributed through `ActivityVersionsReconciling` as source-owned content;
  - proof in `Extensions/Agent/tests/Tests/Configuration/`: declared outcomes published through the facet, and an unsupported outcome refused for that definition only; one same-version changed-content definition refused while a valid one still reconciles; restart idempotency, read-only Studio presentation with copy and fork (with T036), and native and BPMN use.
- [x] T062 [US7] Extend source-owned publication so that provider-backed source content is compiled like Studio content. This is generic Essentials work, with no agent-specific branch:
  - generalize the request of `ActivityTemplateCompiler` (`Essentials/src/Workflows/Publishing/Elsa.Workflows.Publishing/Services/ActivityTemplateCompiler.cs`) beyond `ActivityDefinitionDraft`, so that a source-owned candidate is compiled through the registered `IActivityTemplateProviderCompiler`. The candidate carries the provider key, schema and payload, plus the contract built from the version's inputs, outputs and outcomes, as `ToContract` does;
  - generalize the preflight beyond drafts in the same way:
    - `ActivityPublicationReviewPolicy.ReadinessDiagnostics` takes a subject built from the source-owned version;
    - schema validation of source content goes through `IActivityDraftValidator` or a named equivalent;
    - the candidate evaluation is extracted from `ActivityDefinitionPublisher.EvaluateCandidateAsync` into a seam that `Essentials/src/Workflows/Publishing/Api/Services/SourceOwnedActivityVersionPublisher.cs` calls;
  - keep `ExecutableNodeCompiler.CompileSourceOwnedRoot` for CLR and JSON sources that have no provider;
  - include the model-capability gate in source-owned preflight. Studio's `ReadinessDiagnostics` covers storage and runtime only, so add the selected connection's verified capability check (the one Studio publication runs through the provider compiler and model capabilities source) for source-owned candidates;
  - catch a compile or preflight failure of one source-owned version inside `SourceOwnedActivityVersionPublisher` (not only today's concurrent-publication race). Return a per-version outcome from `PublishAsync` (today it returns `Task` and the reconciler reads nothing): either published, or the `ActivityDiagnostic`s for that version. `ActivityVersionReconciler` records each diagnostic in its reconciliation-state sibling and logs, skips that version and continues with the remaining candidates;
  - prove, in `Essentials/tests/Workflows/Publishing/`: one compile-failing, one capability-failing and one valid source-owned version in a single pass. Only the valid one publishes, and each failure leaves a diagnostic for its version.
  - Delivered in M5. `ActivityTemplateCompiler` compiles an `ActivityTemplateCandidate` (a Studio draft, or other saved content with its own subject kind), and `ActivityPublicationCandidateEvaluator` is the one publication verdict both publishers call. `SourceOwnedActivityVersionPublisher` sends content whose provider key a registered provider compiler owns through `IActivityDraftValidator`, that compiler and storage/runtime readiness under the subject kind `ActivitySourceVersion`, and publishes it as a reusable template boundary; content whose provider key no compiler owns (such as `elsa.clr-activity`, from CLR scanning or JSON) keeps `CompileSourceOwnedRoot`. The model-capability gate is the provider compiler's own (for agents, `AgentActivityProvider` asks the connection's `IAgentModelCapabilitiesSource`), so source-owned candidates pass it exactly as Studio publication does. Content with activity dependencies is refused, because a source-owned commit records no dependency edges. A validator or compiler that throws refuses that version only, and a refusal without an error diagnostic gets `activity.source.publication-refused`. `PublishAsync` returns `ActivitySourceVersionPublicationResult`; the reconciler logs each refusal's diagnostics, records them in the new replaceable `IActivityReconciliationDiagnosticStore` (in memory and per node by default; an entry is cleared when its version publishes or when a complete pass no longer refuses it; a bridge's refusal without an error diagnostic also records `activity.source.publication-refused`), skips the version and continues. Provider diagnostics carry the sanitized provider subject, as in Studio, so the store keys them by version id. Proof: `SourceOwnedProviderPublicationPassTests` (Publishing EF), `SourceOwnedActivityVersionPublisherTests` (Publishing API) and `SourceVersionRefusalTests` (Activities Design). Under the accepted ADR 0094 this is the source-owned bridge part of T066 ("Extend the source-owned bridge to registered provider compilation/preflight"); T066's exact revision references, shared operation/review/reservation and receipt recovery remain open.
- [x] T063 [US2] (scheduled in M5) Carry exact task bindings through BPMN export/import fail closed in `Essentials/src/Activities/Bpmn/Interchange/`: export resolves each bound version's input definitions through the catalog and omits any binding with a definition-sensitive, credential, non-literal or unresolvable input (reporting the omission per element); import proposes a retained binding with its activity version and input keys and keeps `CanExecute` false until the author confirms it; prove both in Interchange tests and the editorial interchange journey. Delivered in M5. Export (`IBpmnDocumentExporter.ExportAsync`) carries each task's exact activity version and literal inputs in the vendor attribute `elsa:taskBinding` when the visible catalog resolves the version and declares every bound input a portable literal. It omits the whole binding and reports one `BpmnExportOmission` per element for a sensitive, credential, connection or password input, an expression, an unresolvable version or input, an output capture or a storage/evaluation/conversion setting; synchronous `Export`, which has no catalog, carries none. Import proposes each retained binding on its capability (`RetainedBinding`: version and input keys) and keeps the task `RequiresBinding`; the author confirms elements through `BpmnImportOptions.ConfirmedBindings` (`confirmedBindings` on analyze and import), and only then is the child bound, still refused as Unsupported unless it names an exact version (never a binding-profile placeholder) that the importing host's visible catalog can read and that declares every carried input as portable. Proof: `BpmnTaskBindingInterchangeTests` proves each omission per element and each import refusal; the binding-preserving `EditorialInterchangeTests` journey proves carry, proposal and confirmation, and runs the confirmed import without a manual rebinding. The Studio import UI's confirmation step and its readiness fields belong with T036's browser journey.

## Phase 6 — Saved evaluations and comparison (US6; M5)

**Goal**: Versioned cases and exact candidate comparison, preserving evidence status distinctions.
**Independent test**: Save/reload a set, compare two exact versions and inspect original result/status/usage.

- [ ] T037 [US6] Implement revisioned evaluation set/case operations and exact run/case identity in `Extensions/Agent/src/Api/` and `Extensions/Agent/src/Persistence/EntityFrameworkCore/`, with permission/retention and production-runtime dispatch.
- [ ] T038 [US6] Implement deterministic assertions and separate versioned quality-rubric assessment in `Extensions/Agent/src/Elsa.Agent/Services/AgentEvaluator.cs`, with declared independent evaluator budget and no private reasoning storage.
- [ ] T039 [US6] Implement cases/save-from-test/compare/results views in `Extensions/Agent/studio/Elsa.Studio.Agent/Client/src/AgentEvaluationsPage.tsx`, preserving failed/skipped/cancelled/unknown/pending statuses.
- [ ] T040 [US6] Prove candidate/set revision isolation, contract-vs-quality distinction, cancellation/replay, redaction and real browser persistence in `Extensions/Agent/tests/Evaluations/` and `Studio/tests/browser/agent-evaluations.spec.ts`.

## Phase 7 — Scoped resources and exact approved tools (US4; M6)

**Goal**: Actual workflow/MCP tools and knowledge with durable exact authorization/effect recovery.
**Independent test**: Denied/revoked/changed approval causes zero effect; supported crash-after-effect
recovery reconciles the real connector outcome. Requires the owning #2912 production integration.

- [x] T041 [US4] Reconcile current #2912 decision/approval/effect/resource contracts before implementation and record exact dependencies in `specs/1179-agents/research.md`; do not fork/reparent its generic work. Delivered as [R11](research.md#r11--2912-prerequisite-reconciliation-for-m6-t041-4-november-2026). #2912 has no production leaf for its carry-forward obligations. T042–T048 and T050 wait for obligations 1–5 as R11 maps them, and implementation can begin after obligations 1–3. MCP (T044) has no owner in #2912 and needs one. T049 falls under the ADR 0094 hold and is to be re-planned after agreement. #2912 is not forked or reparented.
- [ ] T042 [US4] Implement scoped/versioned knowledge and tool capability bindings through those owning contracts in `Extensions/Agent/src/Core/` and `Extensions/Agent/src/Elsa.Agent/Services/`.
- [ ] T043 [US4] Implement native proposal/approval/wait/effect/result continuation outside the background model leaf in `Extensions/Agent/src/Elsa.Agent/Activities/`, binding canonical arguments, scope, authorization revision and exact operation.
- [ ] T044 [US4] Deliver one supported workflow tool and one MCP transport/client contribution in `Extensions/Agent/src/Tools/`, with explicit idempotency/reconciliation capabilities and no SDK callback bypass.
- [ ] T045 [US4] Expose exact tool/resource policy, proposal/approval and observable receipts through `Extensions/Agent/studio/Elsa.Studio.Agent/Client/src/` while reusing native approval/incident surfaces.
- [ ] T046 [US4] Prove denial/revocation/changed arguments, scoped observations, approval restart, effect-before-ack and duplicate/conflicting/late receipts in `Extensions/Agent/tests/Tools/` and real approval browser journeys.

## Phase 8 — Budgeted reusable team patterns (US5; M6)

**Goal**: Sequential/concurrent-cohort/bounded-refinement team activities under native ownership.
**Independent test**: Team inside a native/BPMN parent preserves barriers, resource visibility and one
logical result under late/duplicate messages, exhaustion, cancellation and restart.

- [ ] T047 [US5] Extend M3's existing per-turn reservations to team-wide/cohort/round limits through #2912 in `Extensions/Agent/src/Elsa.Agent/Services/AgentTeamBudgetAllocator.cs`, aggregating without a second dispatch ledger or duplicate charging.
- [ ] T048 [US5] Implement the exact participant pins, typed causal input/result mappings, deterministic aggregation, completion predicate and fail-whole-team policy in `contracts/teams.md` through existing native structural contracts in `Extensions/Agent/src/Design/` and `Extensions/Agent/src/Elsa.Agent/Activities/`; keep participant/cohort/message/resource identities distinct.
- [ ] T049 [US5] Implement the team configuration wireframe and `contracts/teams.md` validation in `Extensions/Agent/studio/Elsa.Studio.Agent/Client/src/AgentTeamEditor.tsx`, including pattern, participant pins, mappings, aggregation/predicate, finite limits and failure policy with normal reusable activity publication.
- [ ] T050 [US5] Prove whole-cohort barriers, bounded feedback/budgets, private/shared visibility, cancellation/restart and one parent result in `Extensions/Agent/tests/Teams/` and representative real Studio/native/BPMN journeys.

## Phase 9 — Supported deployments, real providers and release (US8; M7)

**Goal**: Observed complete matrix and operational product delivery, not merged plans/fixtures.
**Independent test**: Execute the support contract's named24 protocol cells, deployment shapes and
four authorized live journeys, retaining exact artifacts/readiness/original failure states.

- [ ] T051 [US8] Produce immutable packages/Studio distribution and independent module/consumer projects with isolated locks/caches/archive and loaded-byte checks in `tools/agents/conformance/`.
- [ ] T052 [US8] Execute four-store × three-host × two-provider protocol matrix and independently retained authoring-only/runtime-only/combined/Studio shapes through `tools/agents/conformance/`; use normal public readiness/shell lifecycle and private state.
- [ ] T053 [US8] Qualify disabled admission, within-deployment in-flight work/readers and key rotation/replay, incompatible-profile refusal, plus declared pre-GA clean initialization/recreation/republish across actual providers in `Extensions/Agent/tests/Tests/Recovery/` and `tools/agents/conformance/`.
- [ ] T054 [US8] Run bounded authorized live Anthropic/Azure OpenAI Copywriter/Proofreader examples through native and BPMN workflows, recording exact access/deployment/model identity and observed evidence in `specs/1179-agents/evidence/`; do not publish secrets/raw private state.
- [ ] T055 [US8] Ship user guide `website/docs/extensions/agents.md` with `website/docs/SUMMARY.md` navigation, operator guide `Extensions/Agent/src/Elsa.Agent/README.md`, and module-author `Extensions/Agent/src/Core/README.md`, covering examples/configuration, actual support, declared pre-GA reset/recreation/republish and uncertainty/cancellation.
- [ ] T056 [US8] Reconcile real host startup asset budgets, missing/incompatible/restored module controls, native resource isolation and owned-process cleanup in `tools/agents/conformance/`; do not substitute static closure or source-tree hosts.
- [ ] T057 [US8] Close every A1–A15 row only with reviewed product receipts in `specs/1179-agents/evidence/acceptance.md`, preserving all failed/skipped/cancelled/quarantined/unknown statuses and exact source/fixture/host/provider identities.

## Shared revision amendment — staged delivery

The following dependency-ordered tasks apply FR-028–FR-035 and W1–W8. #3790 authorizes the first source-storage slice; later publication/product tasks remain pending. Existing runtime tests remain applicable within the new supported deployment; legacy-format fixture acceptance is not a cross-break requirement.

- [x] T064 Reconcile merged owner agreement (#3783) and explicit #3790 delivery authorization, without inferring other human approvals. Align owning Workflow/activity specs and record concrete placement, temporary publication boundary, reset scope and remaining W1–W8 in plan.md.
- [x] T065 (#3790 source slice) Deliver shared source identity/working-copy CAS/immutable snapshot/history/provenance/pin mechanics used by real typed Workflow and Agent adapters, preserving Workflow mutation/layout semantics and Agent source permissions/I/O authority. Reusable-activity fit and the temporary publication boundary are recorded in plan.md; an independent typed Notes consumer verifies the seam. Qualify the storage portions of W1/W2/W5 with one implementation. Full W1–W8, including derived contracts and publication, remain T066/T069/T070 obligations; this check does not certify review/CI/resulting-main completion.
- [ ] T066 After T065, adapt shared publishing/provider compilation for exact revision references, review, durable operation/reservation and receipt-authoritative release/index repair. Use Workflow and Agent adapters with explicit domain compiler/commit differences. Extend the source-owned bridge to registered provider compilation/preflight. Prove W2/W3/W5 and the failed-publication example; do not add an Agent candidate store or publisher.
- [ ] T067 After T065/T066, define and qualify the pre-GA clean break: affected stores/configuration/packages, coordinated host stop/drain and incompatible-profile refusal, fresh schema initialization, source recreation and explicit republish. The Specialist-to-Agent naming change is not part of this task; it was delivered under [#4027](https://github.com/elsa-workflows/elsa-foundation/issues/4027). No legacy import/backfill, aliases, mixed-schema migration or old-data rollback is required. Qualify W7 on owned disposable stores; do not reset a live demo from this design task.
- [ ] T068 After T065/T066 and the declared T067 deployment contract, adapt Agent Studio/API discovery/editor/test/history to shared revision callbacks through typed Agent authority. Keep generated activity views read-only, show source revision/release and truthful pending/unknown results. Preserve domain permissions and local conflict/undo semantics; use canonical Agent terminology.
- [ ] T069 With T065–T068, implement meaningful tests in actual revision, Workflow, Agent and publishing/persistence suites. Prove W1–W7: shared CAS/capture/pin races, rejected snapshots, release/activation distinction, stale review/authority, changed-operation refusal, independent-store failure and receipt recovery for both adapters. Retain within-deployment native/BPMN artifact and accepted-turn replay guarantees. Qualify four-provider scope/concurrency, test counts and causal red/green controls.
- [ ] T070 After T068/T069, qualify W4/W6–W8 in real Studio and normal source/package authoring-only/runtime-only/combined hosts, configuration-defined Agents and native/BPMN journeys. Include clean-store initialization/recreation/republish; document reset boundaries without promising cross-break compatibility. Update support/guide/wireframes and owning docs with fresh receipts, distinct from historical evidence.
- [ ] T071 After separately authorized delivery and required review/CI/resulting-main checks, reconcile W1–W8 and actual shared-engine evidence, prune obsolete domain-specific versioning code/tests and compatibility scaffolding rather than retain parallel implementations. Complete only with proven behavior and declared clean-break support; this source leaf does not close complete product acceptance.

## Final cross-cutting gate

- [ ] T058 Complete affected full suites/discovery reconciliation, API/lock/catalog/tier/architecture/maps gates and causal controls for every implementation leaf; update owning catalogs and `docs/program-goals/elsa-agents.md` in the same units.
- [ ] T059 Complete independent review, exact-head required CI and resulting descendant-main qualification, then reconcile Program/M0–M7/native subissues and Project66 status with actual evidence on the owning PRs/issues.
- [ ] T060 Set this product specification to Implemented only in the final completing PR and remove terminal checklist residue per `docs/reference/spec-lifecycle.md`; ship the final acceptance/support/docs links with Program closure.

## Dependencies and execution strategy

**Ownership amendment**: T064 → T065 → T066 → T067, with T068 following the source/publication and clean-deployment contracts; T069 accompanies each behavior change, T070 qualifies it and T071 closes delivery. Merged design #3783 and the owner-authorized #3790 source leaf replace the former design hold; later leaves still follow their dependency order. Configuration source T061 and shared publishing T062 consume T066 rather than introducing another source/publisher. Historical completed tasks are not reopened or erased by this amendment.


T001–T004 enable the first US1 slice; T005–T008 can land as complete authoring/query work while real
runtime capability admission still refuses publication without its interpreter. T009–T013 finish the
M2 published fresh-runtime vertical. M3 T014–T023 is required for durable/real-provider/continuity claims;
M2's disposable fixture does not satisfy those. M4 T024–T029 resolves the recorded BPMN gap before
claiming a Design-free BPMN product. M5 follows stable runtime/authoring contracts; M6 waits for #2912's
actual prerequisite contracts. M7 qualifies all earlier work and does not invent support by inference.

User-story order follows concrete infrastructure dependencies, not a promise that P2 value is needed
before every P1 operator test. Each story's independent test can use seeded earlier capabilities.
Deliver complete narrow leaves under the owning epics, one active implementation leaf plus review lane;
do not open 63 issues or promote the entire backlog to Ready.

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
| FR-007–FR-008 | T024–T029,T036,T063 | A4,A5 |
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
| FR-027 | T036,T061–T062 | A1,A3,A4,A5,A12,A13 |
| SC-001–SC-007 | All story proofs plus T051–T060 | Complete observed A1–A15 |

### M3 review follow-up owned by M7

[M7 #3527](https://github.com/elsa-workflows/elsa-foundation/issues/3527) must resolve indefinite
protected-content retention for terminal-owner OutcomeUnknown and abandoned Claimed/Dispatching
turns with a bounded resolution or retention ceiling. Preserve unknown/no-inference-retry semantics,
conversation pins, diagnostic metadata and replay refusal. The current M3 sweep deliberately pins
these states; documenting this limitation does not satisfy that M7 acceptance item.

### M4 review follow-up owned by M7

A cookie request from an externally signed-in user decides as its canonical Elsa user (`elsa.identity.user_id`), but
a first-party bearer minted from that session names the provider subject and carries no user ID claim, so such a
caller is refused (`Forbidden`) rather than misattributed. No production external sign-in path is wired today. M7
#3527 must make the issued token, the session service and every subject-scoped store agree on one subject for
external sign-ins before external deciders are supported. Under the revised pre-GA policy, delivery may recreate affected scoped records instead of migrating old provider-subject records; it must never silently reattribute a retained record.

### Shared revision amendment traceability

| Requirement | Planned tasks | Witness / Program acceptance |
|---|---|---|
| FR-028–FR-029 | T064–T066,T068–T070 | W1,W2,W8; A1,A3,A12 |
| FR-030 | T066,T069–T071 | W3; A3,A13 |
| FR-031–FR-032 | T065,T066,T069–T071 | W2,W3,W5,W7; A1,A3,A13 |
| FR-033–FR-035 | T066–T071 | W4,W6,W7,W8; A4,A5,A6,A8,A9,A12,A13 |
| SC-008–SC-010 | T064–T071 | W1–W8; amendment acceptance remains unproven |
