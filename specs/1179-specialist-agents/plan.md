# Implementation Plan: User-defined Agents

**Branch**: `agent/codex/3538-specialist-design` | **Date**: 2026-10-31 | **Spec**: [spec.md](spec.md)

**Input**: Owner-authorized [Program #3519](https://github.com/elsa-workflows/elsa-foundation/issues/3519), [SDK research](../../docs/reports/elsa-agents-research.md), [publication/lifecycle research](../../docs/reports/elsa-agents-publication-research.md) and [source reconciliation](research.md).

**Amendment status**: Owner agreement is recorded by merged PR #3783 (`4043cbfb1`); the owner separately authorized [#3790](https://github.com/elsa-workflows/elsa-foundation/issues/3790) on 4 November 2026. This delivery implements the source-storage slice of T065/T069. It does not ratify provisional constitution sections or infer additional human approvals. W1–W8 remain open until their complete witnesses pass.

## Summary

Implement specialists inside the existing `Elsa.Agent` domain, separately from Weaver. Own typed Agent content over shared Workflow/Agent revision mechanics and derive read-only activity publications through the existing provider and shared publication boundaries. Reuse native/BPMN execution. MAF executes admitted model turns; existing Elsa background work owns delivery; a specialist journal owns scoped conversation acceptance. Module-owned Studio routes provide discovery, playground, evaluation and inspection. Implementation is staged; the source slice below does not deliver the later publication, Studio or runtime witnesses.

## Technical Context

**Language/Version**: .NET 10/C#; TypeScript/React 19; pnpm 11.9.0, matching the workspace.

**Primary Dependencies**: Current activity Design/Publishing/Runtime Core contracts; Connections and Secrets; MAF 1.27.0 with a reviewed normal-graph MEAI abstractions upgrade to 10.10.1; existing JsonSchema.Net 9.2.0; Studio SDK contributions. MAF is the first concrete turn implementation, not a new workflow engine.

**Storage**: Shared scoped source/working-copy/immutable-snapshot storage for Workflow and Agent typed adapters; shared publication operations reference snapshots and authoritative receipts. No separate Agent draft/candidate/version storage engine. Agent release indexes are repairable. Domain journal/conversation and Connections persistence remain distinct. No cross-store transaction is assumed. SQLite, PostgreSQL, SQL Server and MySQL remain named first-party EF targets; in-memory is disposable. Pre-GA coordinated store recreation replaces legacy migration/reader requirements.

**Testing**: Risk-based behavior tests under ADR 0080 D1, using xUnit/Vitest/Playwright appropriately. No new global TDD cadence. Preserve valid tests; demonstrate red/green controls at consequential guards. Process restart, normal hosts, package consumers and authorized live models supply evidence fixtures cannot.

**Target Platform**: Workbench/Studio, package-fed Foundation Host, independent custom authoring/runtime/combined hosts. Runtime-only native/BPMN closures must contain zero Design/Publishing assemblies.

**Project Type**: Modular library features and Studio module, with focused EF/API packages.

**Performance Goals**: No performance benchmark/throughput claim. Preserve applicable existing startup-asset and deterministic command-count budgets; measure the real Studio host.

**Constraints**: Artifact-only execution; one checkpoint/effect owner; exact trusted tenant/owner/version scope; no credentials/private reasoning in observable evidence; retained codecs/readers for admitted artifacts; one implementation leaf plus review lane.

**Scale/Scope**: Specialists and sequential/concurrent-cohort/bounded-refinement teams; finite A1–A15. No free-form team graph, mandatory cloud control plane, arbitrary SDK-object persistence or replacement of Weaver's single assistant harness.

## Constitution Check

| Gate | Design disposition |
|---|---|
| Framework §2.1/§2.2; Elsa §E2.1 | Use `Elsa.Agent.Specialists` beneath pinned `Elsa.Agent`, never a new `Elsa.Agents` root. Core has contracts/models/lightweight dependencies; SDK/EF logic stays in features |
| Framework §2.6/§2.7/§2.20 | Consume cross-domain Core ports and existing named contribution/outbox seams. No empty umbrella or unrelated concrete-feature dependency |
| Elsa §E2.2/§E2.6 | Execute immutable artifacts. Resolve BPMN's existing Design.Core coupling; preserve Design-only/runtime-only/combined shapes |
| Framework §2.9/§2.10 | CQS persistence, scoped CAS, consumer-owned DbContext and domain-wrapped infrastructure failures |
| Framework §2.16/§2.23.3 | Packages earn a real dependency/capability boundary. Internal sealed defaults; public contracts justified by independent consumers and tracked APIs |
| Framework §2.21–§2.23; ADR 0080 D1 | Risk-based behavior/composition/concurrency/recovery tests plus browser/provider/package evidence |
| Framework §2.22; Elsa §E5/§E7 | Same-unit catalogs, stable feature identities, package ranges/locks, solution filters and test tiers |
| Draft/provisional material | §E2.8 versioning/Model X, §E2.9 and framework §2.24 retain their recorded status; this design ratifies none |

The proposed Agent source/bridge split keeps Core dependencies and runtime-only closures intact. Agent Core must not depend on Activities Design DTOs; adapters consume owning Core contributions. Logical record names do not pre-admit a project hierarchy. No constitution exception is requested. Recheck after design and each implementation slice. Missing internal runtime capability remains a bug; domain authorization/retention gates do not excuse losing artifact interpretation.

## Architecture decisions

### D1 — Shared revisions and Agent-owned content

Agent owns typed content/permissions, while Workflow and Agent share scoped source identity, working-copy CAS, immutable snapshot capture, provenance/history and retention pins. Keep Workflow graph-aware diff mutation and layout separation. A candidate is an exact snapshot in a shared publication operation, not another Agent entity family. The Agent provider derives read-only activity publications; Agent discovery queries authorized sources, and the workflow picker retains one activity catalog. No generic activity write bypasses Agent authority. [Shared mapping and failed-publication recovery](contracts/revision-management.md), [Agent ownership](contracts/agent-ownership.md).

### D2 — One I/O contract

Agent source owns stable typed input/result keys, names, null/default/required behavior and compatible field constraints. The provider derives both public activity ports and model JSON schemas. Generated contracts are not independently editable; there are no two source schemas to synchronize. Schema preview and runtime fingerprints derive from the exact candidate. Reserved invocation controls remain separate from business JSON. Published contracts/bindings stay exact within a supported deployment; pre-GA schema reset need not preserve historical wire keys.

### D3 — Durable model connections

Deliver durable Connections metadata before relying on restart; Secrets EF alone is insufficient. Add model type/capability contributions, resolve grants/credentials at point of use and validate saved settings against an identified capability snapshot. Unknown options are not supported. First specialist provider: official Anthropic IChatClient; second: Azure OpenAI through MAF, separately qualified. Concrete provider leaves implement specialist Core contributions; the runtime never references them. A few duplicated SDK factory lines are preferable to depending on Weaver's concrete provider feature or enabling its harness. Weaver Anthropic/Copilot remain regression targets; no multiple assistant harnesses are enabled.

### D4 — Native facade and background turn leaf

A native facade stages the exact scoped turn and schedules an existing background-safe leaf. The leaf runs MAF and returns an opaque accepted-turn receipt handle. The facade revalidates handle/scope and emits typed JSON through IRuntimeActivityCheckpointParticipant.PrepareCompletionCheckpointAsync. Initial native values support sensitivity/redaction with RequiresEncryption=false; refuse encryption-required values/captures and secret-bound business inputs before dispatch. Journal payload encryption is separate and does not repair native Withheld semantics. Current background result codecs reject protected results, and background leaves cannot suspend for approval. This shape avoids both an actor blocked on model work and a second scheduler. Preserve the existing stable activity alias/capability registration, BackgroundExecutionSafe, SupportsBackgroundExecution and Background placement rules.

### D5 — Conversation journal

The specialist journal owns scoped turn CAS/lease fencing and atomic accepted session/result/usage/receipt. Stable OperationKey deduplicates logical work; matching replays return the original result without rewinding a newer conversation revision. Persist the provider-dispatch marker before the request. Expiry after dispatch does not authorize a new request: expose OutcomeUnknown unless an authoritative provider continuation/idempotency capability resolves it. Fresh is default; continuity is explicit and exact-version-bound. A different version creates a fresh conversation; history migration is not implicit. [Data model](data-model.md).

### D6 — Separate turn acceptance and process application

TurnAccepted and AppliedToProcess are separate commits. Journal cancellation winning first fences acceptance; acceptance winning first remains accepted. Native cancellation commits an exact-operation journal-cancellation outbox intent, including a tombstone if admission has not arrived. Delivery can lag: a turn accepted in that gap is **completed, not applied to process**. Durable idempotent delivery/repair settles the boundary; no cross-store atomicity claim. Next-turn admission follows the journal's terminal decision/revision, not unaccepted model history. A preflight worker-fence read cannot substitute for journal CAS.

### D7 — BPMN runtime/authoring separation

Extract shared semantic graph models/identities into Elsa.Activities.Bpmn.Core. Keep Elsa.Activities.Bpmn/ActivitiesBpmn as runtime. Move authored structure/handler into .Design; make Interchange specialize the Design feature. Preserve activity aliases, structure schema1.0.0, slots and compiled semantics. Neutral type forwarders are safe; forwarding authored types from Runtime would recreate the forbidden dependency. Document the authored-type pre-GA move and update every consumer/host registration. Keep runtime refusal constants needed by BpmnGraphAdapter in the neutral/runtime closure.

### D8 — Native tools, approval and teams

Separate model turns, human approval waits and protected effects into native operations. M4 delivers the generic [human review](contracts/human-review.md) wait and decision path, shown by the [editorial sample](contracts/editorial.md), before M6 reusable teams/tools. SDK approval objects are not durable permission. Coordinate #2912's decision/approval/effect/budget work; use native complete-cohort barriers, bounded feedback and cancellation. Do not adopt MAF Workflows checkpoints as a second owner for the initial patterns. [Execution contract](contracts/execution.md) and [team contract](contracts/teams.md).

### D9 — Module-owned Studio experience

The Agent module owns `/agents` discovery, authoring/public-contract editing, source revision/conflict/history and publication-intent APIs/permissions. Reuse existing UI controls and shared publication preflight/receipt mechanics through adapters without exposing a generic activity definition as the editable source. Generic activity pages recognize read-only Agent projections and navigate to the source. Playground/evaluation/inspection use exact candidate/version identity and the existing runtime path; internal model-turn handles remain diagnostic references. No compatibility-route transfer is required. Draft controls use shared revision callbacks through the Agent adapter. [Studio contract](contracts/studio.md).

### D10 — Configuration-defined specialists

Configuration and Studio authoring produce the same immutable Agent candidate/public contract, with explicit source ownership. Configuration versions are operator-declared and read-only in Studio; authorized copy/fork creates a new Agent source. One invalid source entry yields a scoped actionable diagnostic while other entries reconcile. Same-version/different-content remains a conflict.

The shared source-owned publication bridge must be extended beyond its current single-root compilation so provider-backed candidates use `IActivityTemplateProviderCompiler` and the same schema, capability, runtime/storage, compatibility and review checks as interactive publication. This is generic Essentials publishing work, not a second Agent publisher. Extract candidate evaluation only as required by these real consumers. Capture/pin exact shared snapshots, reserve operations before shared commit, and reconcile receipt/index loss without cross-store atomicity. Workflow and Agent adapters must exercise the common coordination; do not hide duplicate engines behind a generic interface. Proposed T064–T071 sequence this alongside the existing T061–T062 obligations. [Ownership contract](contracts/agent-ownership.md).

### D11 — Focused extraction, naming and pre-GA reset

Select mutable working copies plus immutable boundary snapshots, extracting a small reusable capability from current Elsa mechanics. Compare Orchard Core/Aster in [research R10](research.md#r10--shared-revision-management-orchard-core-and-aster); adopting their full content/resource model is outside this unit. Review real Workflow/Agent adapters and reusable-activity fit before API/package freeze. Reconcile owning Workflow/activity specs in the separately authorized delivery unit.

Agent is canonical; keep the pinned `Elsa.Agent` root. Pre-GA implementation may rename Specialist code/API/package/schema identifiers without shims, old readers or import/backfill. Planned store recreation needs a declared scope, stopped/drained hosts, incompatible-profile rejection, clean initialization and explicit source recreation/republish. Ordinary artifact, scope, retention-pin and crash/replay guarantees remain within the admitted deployment. W1–W8 qualify the new model; this draft changes no live stores.

## Project Structure

### Documentation (this feature)

- spec.md, plan.md, research.md, data-model.md, quickstart.md and tasks.md.
- contracts/revision-management.md, agent-ownership.md, publication.md, execution.md, editorial.md, teams.md, studio.md and support.md.
- design/studio-wireframes.html and the requirements checklist.

### Source Code (baseline paths; exact clean renames reviewed in delivery)

| Path | Responsibility |
|---|---|
| Extensions/Agent/src/Specialists/Core/ | Elsa.Agent.Specialists.Core contracts/descriptors |
| Extensions/Agent/src/Specialists/Design/ | Provider, schema/contract validation and compiler |
| Extensions/Agent/src/Specialists/Elsa.Agent.Specialists/ | Runtime facade/worker/journal orchestration and sole concrete MAF integration |
| Extensions/Agent/src/Specialists/Anthropic/ and AzureOpenAI/ | Actual model connection/client/capability contributors, not a new assistant harness |
| Extensions/Agent/src/Specialists/Persistence/EntityFrameworkCore/ | Real conversation/turn/evaluation persistence |
| Extensions/Agent/src/Specialists/Api/ | Authoring reads, kept composable without the runtime |
| Extensions/Agent/src/Specialists/Inspection/Api/ | Playground/evaluation/inspection evidence endpoints over the runtime and generic draft Test Runs, a separate project so authoring and runtime composition stay deliberate |
| Extensions/Agent/studio/Elsa.Studio.Agent.Specialists/ | Module-owned Studio surfaces |
| Extensions/Agent/tests/Specialists/ | Behavior/provider/host/recovery tests |
| Essentials/src/Connections/Persistence/EntityFrameworkCore/ | Existing repository's durable implementation |
| Essentials/src/Activities/Bpmn/Core/ and Design/ | Actual runtime/authoring package separation |
| tools/agents/conformance/ | Independent packaged qualification |

Shared revision storage and coordinator placement follow the owning Core/persistence boundary demonstrated by Workflow and Agent adapters, not a new eagerly created project tree. The paths above describe current implementation inputs; Specialist naming need not survive a pre-GA clean break.

No empty adapter hierarchy: extract a MAF provider package only when optional composition or another real implementation warrants it. A focused JsonSchema helper may earn a lightweight identity if Design and Runtime cannot share validation without an unlawful feature reference; name its actual consumers/dependency boundary. EF/API projects appear with real logic, not placeholders.

## Delivery order and review checkpoints

M1 closes on reviewed spec/research/state/support/UX/tasks, not an Implemented product spec. M2 delivers catalog, draft membership and immutable publication, including the minimum real fresh-runtime facade/background leaf required to execute its published contract. Bring the central MAF dependency upgrade into that enabling slice. Compile the final facade/worker shape from the start; do not publish a throwaway inline interpreter and change its meaning later. M2 may use a qualified deterministic external model connection and disposable journal for its bounded publication proof, without claiming durable conversation or production-provider acceptance. M3 then delivers durable Connections/journal, explicit continuity/recovery and concrete model adapters. M4 resolves BPMN packaging and actual native/BPMN bindings. M5 delivers Studio/evaluations. M6 integrates tools/knowledge/teams with #2912 prerequisites. M7 executes the named matrix, live examples, docs and exact-head/resulting-main gates.

Each leaf owns code, meaningful tests, catalogs/APIs/locks, host proof and review. One implementation leaf plus review lane; stacked design can progress on a reviewed base, but cannot merge before it or claim downstream acceptance. [Tasks](tasks.md) map all requirements and A1–A15 to implementation/evidence.

## Complexity Tracking

| Choice | Why needed | Alternative rejected |
|---|---|---|
| Shared revision capability | Workflow and Agent need the same CAS/snapshot/history/retention mechanics | A third independently versioned Agent subsystem |
| Specialist Core/Design/runtime | Real package boundary and runtime-only deployment | Mixed package repeats BPMN coupling |
| Facade + background leaf | Protected result transport and approval suspension are unsupported in one background leaf | Blocking actor or inventing another scheduler |
| Conversation journal + native receipts | Cross-workflow CAS and accepted session/result are absent today | Process-local Weaver state or false cross-store atomicity |
| BPMN Core + Design | Shared graph vocabulary needs independent deployment | Design coupled to concrete runtime |
| Durable Connections provider | Metadata currently disappears on restart | Secrets EF does not retain connection documents |


## T067 shared schema-family preparation

Shared revision rows retain the source family's schema stamp even when the consumer module's own schema advances independently. The common materialization/stamping interceptor selects the source chain for shared revision rows and the consumer chain for domain rows. Disposable tests use module version `7.0.0` and shared source version `1.0.0`, including refusal of a module-valid but source-incompatible stamp.

This correction changes no schema/profile version, stored source content or live store. It prepares [the pre-GA reset boundary](contracts/revision-management.md#pre-ga-reset-boundary); coordinated admission, stop/drain, fresh initialization and incompatible-binary proofs remain T067 obligations. The eventual break reuses existing module admission/finalization and contracting-migration refusal gates, with no automatic reset or second migration coordinator.

## #3790 source slice: placement and transition

The provider-neutral `Elsa.Persistence.Core` owns only source scope, working-copy, snapshot, pin and bounded reader/writer contracts. `Elsa.Persistence.EntityFramework` owns the single `EfSourceRevisions` implementation and composable `AddSourceRevisions` mapping. Both typed adapters use its CAS, capture, provenance and retention primitives. No EF, Activity Design DTO or runtime implementation enters the new Core project. Shared schema-family declaration covers the new document columns; physical tables remain in the composing module's migrations and finalization boundary.

Workflow keeps definition metadata, its domain commands, locks, operation replay, validation and separate layout rows. Draft source content now lives once in the shared row; `StateSource` is an unmapped domain view. Existing whole-draft last-writer-wins submission semantics in spec 003 remain: the command stages CAS against the revision read under its existing lock. This does not add a client expected-revision field to Workflow editing. Capture/history use exact shared snapshots and preserve case-insensitive Workflow definition lookup. Workflow discard retires its shared copy while retaining snapshots/pins. Initial collection requires a retired copy with no pins: active-copy snapshots retain operation replay identity, and a retired copy cannot recapture a collected ID. Activity-upgrade commands stage shared source CAS in their existing atomic transaction.

Agent source content/permissions remain Agent-owned. `EfAgentSources<TContext>` composes the same engine in an ordinary consumer-owned context. The `/agent/definitions` API exposes unpublished typed copies, bounded discovery/history, expected-revision save, capture, fork and discard. Trusted scope comes from the host accessor, never JSON. The initial Agent policy permits one copy per source; Workflow retains multiple copies. `AgentSourcesEntityFrameworkCore` provides authoring storage without `SpecialistsRuntime`; the existing journal persistence composition also installs the source ports.

Reusable-activity authoring was assessed as the third existing consumer: its typed state, `Revision` CAS, provider authority fence and atomic Design/publication mutations fit the shared save/capture responsibilities. Replacing that store now would also replace its publication state machine and violate this leaf's T066 boundary. It therefore remains the graph-backed Activity authoring owner; generated Agent publications remain read-only through its existing provider-source guard. An independent typed test consumer exercises the new neutral ports before their surface is frozen.

Temporary boundary: the existing activity-backed Agent editor/provider publication path and Workflow version/promotion rows remain until T066/T068 replace their publication/source selection together. New typed Agent source API content is unpublished and cannot be executed, tested through the old candidate API, or silently mirrored into an editable activity draft. No second Agent candidate/version engine is introduced. T066 owes source-to-provider compilation, reviewed reservation, release association and receipt reconciliation; T067 owes coordinated reset/rename deployment; T068 owes source-backed Studio; T069/T070 owe the remaining product/runtime proofs. Storage-only tests do not close W1–W8.

### Breaking deployment boundary

Affected stores are Workflow Design (`elsa_workflow_sources_*`, changed Workflow draft payload mapping) and Specialists (`elsa_agent_sources_*` beside unchanged turn/conversation/operation rows). Four-provider incremental migration artifacts preserve the existing chains, including #2898's retained Workflow history. Dropping the old draft payload column is intentionally breaking: no backfill or mixed-profile writer support is supplied. Do not apply this delivery to populated running stores. A coordinated T067 deployment must stop affected authoring/combined hosts, exclude old binaries, recreate the affected owned databases, initialize all composing modules, recreate source and explicitly republish before readiness. If stores share a database, inventory all modules in that database before recreation. Runtime/checkpoint/hosting/ETL schemas and #2892's lane are not changed by this slice. This delivery performs no live reset, host restart or migration execution against a demo store.


## T066 shared Publishing contracts (independent preparation)

The provider candidate compiler models, evaluator, runtime/storage readiness, catalog projection and release-choice policy are exposed through Publishing Core contracts. The endpoint-free Publishing feature supplies replaceable defaults, and existing draft/CLR/JSON publication consumes the same services. This permits typed domain adapters to depend on the Core seam without coupling their Design features to the default engine. Publication review tokens, provider diagnostics, compatibility choices and canonical commit boundaries retain their existing behavior.

This slice prepares T066 under [ADR 0094](../../docs/adr/0094-agent-definitions-own-authored-content-and-providers-publish-activities.md). It does not deliver source-operation coordination, typed adapters, changed schemas or the amended Studio journey. Those retain their [revision contract](contracts/revision-management.md) obligations. Prepared executable retention is a prerequisite owned by [#2892](https://github.com/elsa-workflows/elsa-foundation/issues/2892#issuecomment-6641754588); no Runtime retention or execution infrastructure is duplicated here.

### Independent source discovery and retained inspection (5 November 2026)

The source-reader increment exposes one metadata-only Agent directory entry per source identity, including identities whose draft was discarded. It prefers an active copy for navigation and uses a stable retained copy when none is active; this representative is neither a selected source revision nor a publication claim. Shared source-scoped history and exact snapshot reads span retained and replacement copies, return the original copy identity, and verify both source-copy and snapshot schema/content identity. Snapshot ordering is stable by identity; copy-local CAS revisions are not presented as a global chronological sequence. SQL Server's padded equality is handled by consistent non-space-terminated source comparisons before count, selection and pagination, preserving case and valid trailing-space identities.

The Agent-owned `elsa.api.agent-definitions` v1 capability declares directory and retained-revision reads. The existing specialist capability declares model discovery only. No release/publication link is advertised before its endpoint is delivered. Reads use Agent permissions and trusted selected source scope; generated activity authority, mutable source behavior and schema storage are unchanged. This isolated increment is extracted from the preserved local candidate without its operation/reservation/Runtime infrastructure. It adds no migration or live reset. Qualification and independent review are pending at this source checkpoint; historical candidate receipts do not certify this extracted head. T066–T071 and complete W1–W8 remain open, including Runtime-owned preparation retention and actual composed Studio/OpenAI acceptance.

Independent source-read review round 1 at `53a46ebf0` found a consequential SQL Server authority edge: the scope hash deliberately excludes authority, while raw padded SQL equality could admit a foreign authority before history/directory count and paging. The correction applies exact terminated authority comparison in the existing shared scoped query, without changing identity hashes or schema. The native witness includes a padded foreign authority with its own copy/snapshot, exact-read exclusion, owned count/paging and directory-search exclusion. Causal native execution and fresh whole-project qualification remain pending; no historical result is relabelled as proof of this correction.

### Studio source transport preparation

The unwired Agent client consumes the typed source endpoints and source-wide retained reads above. It preserves exact identities, copy provenance and caller-selected CAS/capture operations; an unknown write or missing snapshot is returned to the caller without retry or latest-draft fallback. Request bodies contain source fields rather than activity drafts, release labels or caller-selected authority. This is client preparation for T068, with no visible editor, publication transport or Runtime behavior. Current client tests/typecheck and the composed Studio journey remain unqualified; T066–T071 and W1–W8 stay open.

Prospective source-client review at `4b80ab06d` found that accepted dot-only identities become normalized URL path segments in a raw SDK context, while the selected-backend path guard refuses dot and encoded-slash segments. The source correction keeps accepted identities unchanged: reads select through query values, and writes carry identities in their existing typed JSON bodies. The client reuses SDK `resolveBackendPath` for selected-shell prefixes and guards; static shell-relative capability hrefs contain no query or URI template. No shared SDK exception, identity restriction, compatibility alias, schema change or live reset is introduced. The unversioned source URLs change before the new definition capability's first delivery.

Regressions exercise actual SDK URL resolution and the real HTTP source host with dot, slash, literal-percent and Unicode identities. Original baseline `141d814b8` and its main-reconciled descendant are retained for causal execution; the opaque-identity regression definitions are unchanged by the correction. Existing ordinary HTTP/mock-path cases were migrated to the canonical contract. No new regression, client typecheck or corrected-source whole suite has run, and the finding remains unqualified. The prototype is not ready for publication; current independent review, whole-project execution and causal proof remain pending.
