# Implementation Plan: Specialist agents

**Branch**: `agent/codex/3538-specialist-design` | **Date**: 2026-10-31 | **Spec**: [spec.md](spec.md)

**Input**: Owner-authorized [Program #3519](https://github.com/elsa-workflows/elsa-foundation/issues/3519), [SDK research](../../docs/reports/elsa-agents-research.md), [publication/lifecycle research](../../docs/reports/elsa-agents-publication-research.md) and [source reconciliation](research.md).

## Summary

Implement specialists inside the existing `Elsa.Agent` domain, separately from Weaver. Reuse activity authoring/publication and native/BPMN execution. MAF executes admitted model turns; existing Elsa background work owns delivery; a specialist journal owns scoped conversation acceptance. Module-owned Studio routes provide discovery, playground, evaluation and inspection. This packet delivers design, not the proposed production behavior.

## Technical Context

**Language/Version**: .NET 10/C#; TypeScript/React 19; pnpm 11.9.0, matching the workspace.

**Primary Dependencies**: Current activity Design/Publishing/Runtime Core contracts; Connections and Secrets; MAF 1.27.0 with a reviewed normal-graph MEAI abstractions upgrade to 10.10.1; existing JsonSchema.Net 9.2.0; Studio SDK contributions. MAF is the first concrete turn implementation, not a new workflow engine.

**Storage**: Existing activity stores remain sole definition/publication authority. A specialist journal owns conversations/turns/evaluations. Deliver a durable Connections repository through its existing contract. SQLite, PostgreSQL, SQL Server and MySQL are named first-party EF targets; in-memory is disposable support.

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

No constitution exception is requested. Recheck after design and each implementation slice. Missing internal runtime capability remains a bug; domain authorization/retention gates do not excuse losing artifact interpretation.

## Architecture decisions

### D1 — Generic publication and discovery

A specialist is an activity provider manifest/public contract. Reuse drafts/revisions/review tokens/versions/templates/recommendations/lifecycle; add no specialist definition database. Fix provider membership using existing temporal draft projections: matching published head/recommendation OR active read-visible matching draft, both at the same resolved snapshot and exact owner scope, before count/paging. Do not filter client-side. Mixed-provider rows select an exact matching recommendation/head/draft or present an explicit draft choice. [Publication contract](contracts/publication.md).

### D2 — One I/O contract

Generic public-interface members own stable typed input/result keys. The provider derives JSON schemas from that interface, with validated per-field JSON constraints where needed; no second independently edited competing contract. Preview exact schemas/fingerprints and validate inputs/output before acceptance. Reserved optional invocation controls are distinct from business JSON. [Execution contract](contracts/execution.md).

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

Build /agents over provider-filtered generic APIs, with existing api.activityEditors contributions. Keep generic contract/save/conflict/publish ownership. Add module-owned playground/evaluation/inspection routes and a narrow occurrence-link contribution where needed; no universal resource-lens/tab framework. [Studio contract](contracts/studio.md).

Detailed meanings live in the [glossary](../../docs/glossary/elsa.md); state/support decisions live in the linked documents. Proposed public signatures stabilize only after the first independent package consumer and owning implementation review.

## Project Structure

### Documentation (this feature)

- spec.md, plan.md, research.md, data-model.md, quickstart.md and tasks.md.
- contracts/publication.md, execution.md, editorial.md, teams.md, studio.md and support.md.
- design/studio-wireframes.html and the requirements checklist.

### Source Code (created only by owning implementation slices)

| Path | Responsibility |
|---|---|
| Extensions/Agent/src/Specialists/Core/ | Elsa.Agent.Specialists.Core contracts/descriptors |
| Extensions/Agent/src/Specialists/Design/ | Provider, schema/contract validation and compiler |
| Extensions/Agent/src/Specialists/Elsa.Agent.Specialists/ | Runtime facade/worker/journal orchestration and sole concrete MAF integration |
| Extensions/Agent/src/Specialists/Anthropic/ and AzureOpenAI/ | Actual model connection/client/capability contributors, not a new assistant harness |
| Extensions/Agent/src/Specialists/Persistence/EntityFrameworkCore/ | Real conversation/turn/evaluation persistence |
| Extensions/Agent/src/Specialists/Api/ | Actual playground/evaluation/inspection endpoints, with deliberate authoring/runtime composition |
| Extensions/Agent/studio/Elsa.Studio.Agent.Specialists/ | Module-owned Studio surfaces |
| Extensions/Agent/tests/Specialists/ | Behavior/provider/host/recovery tests |
| Essentials/src/Connections/Persistence/EntityFrameworkCore/ | Existing repository's durable implementation |
| Essentials/src/Activities/Bpmn/Core/ and Design/ | Actual runtime/authoring package separation |
| tools/agents/conformance/ | Independent packaged qualification |

No empty adapter hierarchy: extract a MAF provider package only when optional composition or another real implementation warrants it. A focused JsonSchema helper may earn a lightweight identity if Design and Runtime cannot share validation without an unlawful feature reference; name its actual consumers/dependency boundary. EF/API projects appear with real logic, not placeholders.

## Delivery order and review checkpoints

M1 closes on reviewed spec/research/state/support/UX/tasks, not an Implemented product spec. M2 delivers catalog, draft membership and immutable publication, including the minimum real fresh-runtime facade/background leaf required to execute its published contract. Bring the central MAF dependency upgrade into that enabling slice. Compile the final facade/worker shape from the start; do not publish a throwaway inline interpreter and change its meaning later. M2 may use a qualified deterministic external model connection and disposable journal for its bounded publication proof, without claiming durable conversation or production-provider acceptance. M3 then delivers durable Connections/journal, explicit continuity/recovery and concrete model adapters. M4 resolves BPMN packaging and actual native/BPMN bindings. M5 delivers Studio/evaluations. M6 integrates tools/knowledge/teams with #2912 prerequisites. M7 executes the named matrix, live examples, docs and exact-head/resulting-main gates.

Each leaf owns code, meaningful tests, catalogs/APIs/locks, host proof and review. One implementation leaf plus review lane; stacked design can progress on a reviewed base, but cannot merge before it or claim downstream acceptance. [Tasks](tasks.md) map all requirements and A1–A15 to implementation/evidence.

## Complexity Tracking

| Choice | Why needed | Alternative rejected |
|---|---|---|
| Specialist Core/Design/runtime | Real package boundary and runtime-only deployment | Mixed package repeats BPMN coupling |
| Facade + background leaf | Protected result transport and approval suspension are unsupported in one background leaf | Blocking actor or inventing another scheduler |
| Conversation journal + native receipts | Cross-workflow CAS and accepted session/result are absent today | Process-local Weaver state or false cross-store atomicity |
| BPMN Core + Design | Shared graph vocabulary needs independent deployment | Design coupled to concrete runtime |
| Durable Connections provider | Metadata currently disappears on restart | Secrets EF does not retain connection documents |
