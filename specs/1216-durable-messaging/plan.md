# Implementation Plan: Durable Messaging

**Branch:** `agent/codex/3583-durable-messaging` | **Date:** 2026-11-06 | **Spec:** [spec.md](spec.md)
**State:** Independently reviewed plan; owner Q1–Q4 recorded. T004 submission rules and the independent Core identity/submission/source-contract slice are implemented. Resolve T003 concrete shared source/participant mapping before dependent ABI edits; no takeover of another claim is implied.
**Delivery:** [#3583](https://github.com/elsa-workflows/elsa-foundation/issues/3583), draft [#3904](https://github.com/elsa-workflows/elsa-foundation/pull/3904).

## Summary

Add Runtime-owned acceptance and one exact recipient per logical definition, preserving Event broadcast and publication/execution ownership. Freeze eligible definitions at acceptance using Runtime-owned interests, including receive-only definitions and active pinned material. Prefer ready waits, exclusively reserve one-shot subscriptions and enforce active message-started admission by tenant/definition/key across versions. Keep buffer lifetime, producer deduplication, delivery/admission and audit retention independent.

Use the existing Runtime persistence family, fenced delivery, checkpoint, scheduler/mailbox and resumption pump. Applications require a real standalone ingress source; native sends remain committed outbox sources with a sticky source-to-acceptance binding. No fake publishing workflow, reopened Event cohort, parallel broker/pump or execution authority.

## Technical Context

- **Language/runtime:** .NET 10/C#. SDK policy is `global.json` 10.0.300 with latest-feature roll-forward; inspected installed SDK 10.0.402.
- **Studio:** React 19/TypeScript/Vite/pnpm 11.9.0; current shell/module/capability/permission conventions.
- **Storage:** Existing RuntimeDbContext/EF schema and source-materialization family, with SQLite, PostgreSQL, SQL Server and MySQL migrations. In-memory is explicit volatile development, not restart durability.
- **Dependencies:** Existing Runtime Core/implementation/API/resumption, Activities/Primitives, model-owned BPMN, Identity, optional EF and Studio shared UI. No external broker/service or new third-party package is required.
- **Testing:** Existing Runtime/Activities/API/EF behavioral/provider projects, architecture/registration/API guards, owned HTTP/process hosts, actual package consumers and Studio frontend/browser/composed UX.
- **Platforms/shapes:** Runtime-only and combined hosts; preserve Design-only. Native and genuine direct BPMN are first release scope. Each adapter owns its exact subscription, consumption and terminal evidence.
- **Constraints:** Complete bounded cohorts and quotas before acknowledgement; deterministic per-lane selection, independent-key progress, no global completion order or unmeasured throughput promise. Evaluate interest/transaction hot-path cost during implementation.
- **Shared machine:** Use the PATH dotnet build-slot wrapper. Before any build this session observed load averages above 130; queue/coordinate heavy phases and check load before interpreting timing failures. Never reuse/stop existing hosts or stores.

## Constitution Check

Read both [framework](../../.specify/memory/constitution-framework.md) and [Elsa](../../.specify/memory/constitution.md) constitutions and [review checklist](../../docs/contributing/review-checklist.md). No known planning contradiction; no implementation gate has passed.

| Gate | Application |
|---|---|
| Domain/providers; framework §§2.1–2.7, 2.18–2.20 | Core owns actual integrator/provider extension contracts; helpers default internal/scoped. Extend existing EF/backend family; no new pinned domain/eager tiny package. |
| Atomicity/composition; §§2.6, 2.9–2.11 | Provider transactions stage all message/source/checkpoint participants together. No independently saving callback. Qualify actual physical tuples and diagnose mixed/duplicate backend replacement. |
| Tests/catalog/API; §§2.21–2.23 | Preserve Event cohorts/correlation/frozen keys/native/model authority and old tests. Add causal failures, actual extension catalogs and reviewed public API manifests. |
| Elsa §E2.2.3, §E2.6 | Runtime interests/provenance come from executable/publication/active material, not Design drafts. Preserve deployment shapes and artifact-only execution. |
| Persisted compatibility | Null participation preserves old checkpoint fingerprints. Version new sources/participants; unsupported writers/compositions fail before effects. Qualify real upgraded/restarted data and retained roots. |
| Studio | Follow the [before-code record](acceptance-matrix.md) and current-commit composed downloadable evidence plus independent inspection. |
| API magnitude/CI tiers | Reuse cohesive test projects; register new consumer/provider tests in correct tiers. Review API delta/computed magnitude without manual package versions. |

Framework §2.24 and Elsa provisional sections remain unratified. This plan changes no #3052-owned serializer/binding/outcome decision; a concrete need to change one follows that initiative's agreement path.

## Engineering decisions

These are implementation choices for review, not already delivered behavior. Details/invariants are in [data-model.md](data-model.md), [delivery design](contracts/delivery-design.md) and [submission/configuration contract](contracts/submission-api.md).

| Decision | Rationale |
|---|---|
| Model-neutral authored definition identity | Native/BPMN versions of one WorkflowDefinitionId share one recipient/admission lane. Carry separate logical provenance for direct BPMN; preserve executable hashes. Standalone models require stable owner identity, not a material/protocol-derived identity. |
| Static or finite declared message names | Infer literal interest before a wait exists; a dynamic name requires a finite declared set checked at registration. No implicit wildcard interest. Business keys remain runtime expressions. |
| Frozen complete interest cohort | Owner Q2. Capture coherently against Runtime publication/active roots before acknowledgement; no later definition/new-name membership. Old active pins retain late-wait interest after unpublication. |
| Waits first; address/order before compatibility | Owner Q3. Choose the oldest ready exact wait with stable tie-breaker; persist an incompatible selected winner as failed without fallback. Start only without a ready wait and with free admission. |
| FIFO eligible definition/key selection | Trusted acceptance order plus immutable tie-breaker. No global completion/external-effect order; bounded recovery cannot let an unavailable lane monopolize a worker. |
| Exact nonblank identity | Ordinal name/key/ID, no trimming/coercion; reject blank keys rather than inheriting Event wildcard/unkeyed behavior. |
| Fixed independent clocks and identity layers | Snapshot accepted policy; duplicates do not slide deadlines. Same ID/content returns original receipt; changed content conflicts. An authored source binding stays idempotent beyond producer dedup expiry and without a producer ID. |
| Native terminal/model settled release | Native Completed/Faulted/Cancelled are terminal and terminal retry is refused. Models release after authoritative settlement. Incidents/requested cancellation do not unlock; stale release cannot clear a successor generation. |
| Immutable winner plus exclusive reservation | Select/reserve/dispatch atomically. Target admission AND consumption check Message owner/fence. Failed/abandoned unconsumed work durably closes old target authority before reuse of a still-live wait. |
| Send activity waits for acceptance | Commit send intent and private receipt bookmark together. Outbox accepts once, persists source binding/callback, resumes exact private wait; completion means accepted, not business delivered. |
| Cancellation competes with acceptance | Owner Q4. The actual sender cancellation checkpoint and source finalization serialize through one retained sender control. Cancellation-first seals it; current source replay derives Cancelled; acceptance-first preserves message/recipient work and retires only the sender continuation with proof. |
| Capability-gated operator module | Follow User Tasks discovery and shared collection/detail controls, separate current metadata/payload/recovery authorization, no new shell/manual-send console. |

## Project Structure

Use existing cohesive projects and owner families:

```text
Essentials/src/Workflows/Runtime/
  Core/Messaging/                       domain/physical extension contracts where public is justified
  Elsa.Workflows.Runtime/Messaging/     validation, selection, interests, guards and recovery
  Persistence/EntityFrameworkCore/      source/message entities, participant staging and migrations
  Api/Messaging/                       capabilities, access and receipt/payload/recovery endpoints
  Resumption/                          existing sweep/pump integration
Essentials/src/Activities/Primitives/   send/receive/start activities and catalog metadata
Essentials/src/Activities/Bpmn/         native/direct-model messaging and logical provenance
Studio/src/essentials/Elsa.Studio.Messages/  proposed operator contribution/package
tools/messages/Consumer/               planned public consumer/example
e2e-tests/messages/                    planned owned HTTP/process qualification
specs/1216-durable-messaging/           contracts, tasks and evidence pointers
```

Finalize Studio package placement through current module packaging/discovery before creation. A folder does not introduce another domain or independently selected store.

## Implementation Sequence

Keep one implementation leaf active under #3583; file-level subtasks within it may run independently. Milestones sequence implementation, never narrow first-release scope.

1. **M0 concrete contracts:** Reconcile standalone source/claim/completion and checkpoint participation with the [shared-owner request](https://github.com/elsa-workflows/elsa-foundation/issues/2892#issuecomment-6671646081) and #3820. Publish exact signatures/physical mapping for co-review, preserving existing work. Finish data/API/configuration/consumer contracts. Independent domain validation, material-only interest extraction and model-neutral provenance preparation can progress without changing the disputed shared ABI.
2. **M1 acceptance/physical family:** Implement validation/policy/fingerprint and Runtime interests; complete source/cohort/dedup state within the existing backend/DI rollback family. Add coherent provider-clock/epoch/capacity transactions and immediate-only selection, all migrations and physical capability checks. API must not advertise durability before those boundaries pass.
3. **M2 delivery/admission:** Add optional typed checkpoint participation, exact source/target guards and transactionally selected wait/start work. Integrate native/model consumption, retained roots, admission/release and the existing processor/resumption path for pending discovery/retry/cleanup. Prove the matrix's adverse cases; no second pump.
4. **M3 authoring/models:** Deliver Send Message/Receive Message/message start catalog/editor behavior, declared names and receipt references. Commit source/private receipt continuation together. Adapt native BPMN message constructs and genuine direct-model subscriptions using canonical logical identity; preserve signals/Events and reject unsupported shapes explicitly.
5. **M4 operations/examples:** Deliver scoped Messages collection/detail, mixed outcomes, protected payload reveal and authorized retry/abandon. Supply public module/API/activity configuration and package-backed executable examples, not source-internal fixtures.
6. **M5 qualification/closeout:** Execute real providers/two-worker modes, process death/uncertainty and public-consumer/HTTP journeys. Rebuild composed Studio on a committed candidate, inspect evidence independently, run correctness/standards/acceptance reviews and exact-head required checks, merge under current rules and qualify resulting main. Reconcile canonical records only to actual evidence.

## Concrete Shared Boundaries

- Add an additive genuine Runtime standalone-source interface implemented by the existing backend. Preserve old WorkflowExecutionId-required outbox intent/context rather than making it optional. A verified source context carries actual family/address/scope/fence/authority.
- Extend RuntimePostCommitOutboxProcessor orchestration and normal resumption with the source family. Source completion, domain progress and callback work share provider authority. Preserve old constructors, workflow-owned path, source fingerprints and Event snapshot rules.
- Native Message/finalization requires existing outbox family v3 and a checkpoint producer dormancy guard. Replace the current write-version shortcut with explicit row minimums: legacy Event source v2, Message/finalization v3. Guarded target delivery uses distinct DeliverMessage kind 22 plus scheduler v4; optional fields alone cannot stop an older decoder. Current code with the Message feature disabled refuses before claim. Validate the actual older binary's unknown-kind/stamp refusal and every relevant state/checkpoint/model version; default upgrades are quiesced homogeneous fleets, not in-process membership presented as rolling-fleet proof.
- Add versioned Message participation to RuntimeCheckpointCommit/validator/fingerprint, EF staging, coherent in-memory application and coalescing forwarding. Null preserves existing fingerprints; tracked selection/consumption/release forces a physical boundary while Balanced remains default.
- Maintain interests coherently in activation switching and initial/terminal native/model checkpoints. Notifications only wake reconciliation. Current in-memory checkpoints reject model state, so native support cannot imply direct-model capability.
- Extend direct BPMN's existing publication provenance with canonical authored logical identity. Preserve executable hashes, explicitly validate/upcast persisted provenance and qualify old data; do not infer definition from content hash.
- API producer mapping uses current normalized authorized identity. There is no general canonical application-ID claim: a narrow trusted host/Identity resolver supports genuine apps without making #2852/SSO whole products prerequisites.

## Verification and Evidence

Elsa currently declares no blanket greenfield test cadence (§2.21.2). Record that application-level gap; this feature uses risk-based behavior tests plus the causal controls required by current org review, without inventing a mandatory repository TDD sequence.

The [acceptance matrix](acceptance-matrix.md) is the coverage source: all B01–B32, U01–U07 and provider/model/consumer modes remain **NOT RUN**. Findings add causal cases rather than weaken old objectives. Configuration/support values are in the proposed API contract and need provider/workload validation.

Run scoped builds/whole affected projects after coherent changes, through build slots. Preserve causal red/green for risky behavior, then real provider/process failure proof. Refresh actual extension catalogs/maps/architecture checks; generated maps stay uncommitted. Broaden to relevant backend/Studio/architecture suites as integration requires; leave whole-solution runs to required CI.

Composed Studio proof uses owned disposable ports/stores/children and committed source/head/dirty-state receipts. Retain downloadable screenshots/traces; independent reviewers open them. Fixtures and green build summaries cannot establish acceptance. Separate passed, failed, skipped, cancelled, unavailable and incomplete results.

No deployment, release, publisher activation, permission change or external purchase is included. Work continues until all agreed #3583 criteria and resulting-main acceptance are met.
