# Implementation Plan: Durable Runtime Alterations

**Branch**: `codex/1016-runtime-alterations` | **Date**: 2026-07-26 | **Spec**:
[spec.md](spec.md) | **Issue**:
[#1016](https://github.com/elsa-workflows/elsa-foundation/issues/1016)

**Input**: Approved feature specification from `/specs/141-runtime-alterations/spec.md`

## Summary

Add a durable Runtime-owned alteration subsystem that captures explicit or query-selected workflow
targets before execution, seals the cohort, and drives one atomically checkpointed job through each
workflow actor. The initial schema-versioned handlers cancel workflows, modify root variables,
schedule direct authored children, supersede and reschedule eligible activity executions, and
migrate quiescent suspended executions between exact compatible artifacts. Plan/job storage,
at-least-once work claims, protected payloads, result paging, cancellation, authorization, and
Groundwork persistence make the surface operationally durable. One authenticated REST submission
path returns `202`; inspection and cancellation use the same plan identity, with no immediate
server-side run path.

## Technical Context

**Language/Version**: C# 14 on .NET 10 (`net10.0`)
**Primary Dependencies**: Elsa Runtime actor/checkpoint/outbox contracts, Elsa Tasks, Elsa Mediator,
FastEndpoints, API Capabilities, Groundwork document persistence
**Storage**: In-memory development stores plus the unified Groundwork runtime store used by SQLite,
PostgreSQL, SQL Server, and MongoDB shell adapters
**Testing**: xUnit unit, contract, architecture, feature-composition, persistence/conformance, replay,
and REST-driven PowerShell backend e2e tests
**Target Platform**: Cross-platform ASP.NET Core host and Elsa runtime libraries
**Project Type**: Modular .NET library and web API monorepo
**Performance Goals**: Bounded-memory target capture and job/result paging; configurable bounded job
parallelism; no all-target materialization; one mandatory atomic checkpoint per applied target job
**Constraints**: Runtime never references Design; target scan uses immutable key ordering; handler
effects and terminal job evidence commit together; payloads remain protected/redacted; no fixed API
target cap; no implicit workflow recovery or incident resolution
**Scale/Scope**: New Runtime Core contracts/models, default Runtime orchestration and built-ins,
Runtime API endpoints/capability, InMemory and Groundwork storage, reference-host composition, tests,
maps, docs, and backend e2e evidence

## Constitution Check

*GATE: Pass with documented draft/provisional status. Re-checked after Phase 1 design.*

The framework and Elsa constitutions remain draft and provisionally ratified. This work applies them
as review gates without amending or ratifying them.

| Gate | Result | Design response |
|---|---|---|
| Domain layering and `.Core` dependency envelope (framework §2.1; Elsa §E2.2) | Pass | Public alteration contracts and durable models live in Runtime Core; orchestration, cryptography adapter, pumps, and built-ins live in Runtime; HTTP types live in Runtime API; Groundwork stays under Elsa Persistence. No Runtime-to-Design reference is added. |
| Artifact-only runtime (Elsa §E2.6.2) | Pass | Schedule and migration validation read only `WorkflowExecutable`, retained source references, and Runtime state. They never load authored definitions. |
| Activity-owned behavior | Pass planned | Generic Runtime requires an exact operator-scheduling capability pinned on a direct executable child relation; the parent activity module owns policy and completion integration. Runtime does not infer module behavior from topology alone. |
| Cross-feature contribution (framework §2.6.1) | Pass by startup registry | `AddWorkflowAlterationHandler<T>` contributes immutable descriptor/service registrations to a startup-built registry. Runtime dispatch is exact by kind/version; it does not resolve an unordered `IEnumerable<T>` per job or persist service types. |
| Replacement contracts (framework §2.6.2) | Pass | Plan store, payload protector, registry, capture coordinator, job coordinator, compatibility validator, and checkpoint collaborator each have one selected implementation. |
| Strategy-pattern naming (framework §2.24) | Pass | Alteration handlers are externally selected by a stable envelope and therefore form a genuine algorithm family. Staging planners remain internal services, not public strategies. |
| Service lifetimes (framework §2.5.1) | Pass | Immutable registrations/options may be singleton; handlers, validators, coordinators, API handlers, and checkpoint work execute scoped. |
| Command/query separation (framework §2.10) | Pass | Submission/cancellation are commands; plan/job reads are queries. Target capture is a background command workflow, not a query endpoint with side effects. |
| Atomic runtime state (Elsa Runtime checkpoint gates; ADRs 0020/0031/0032) | Pass planned | `AlterationJobState` terminal changes join `RuntimeCheckpointStateChangeSet`; alteration checkpoints are mandatory, fenced, and excluded from deferred coalescing. |
| Persistence-provider neutrality (framework §2.9) | Pass planned | InMemory and unified Groundwork implementations share contracts and conformance tests. The four Groundwork database adapters reuse one manifest/query/writer implementation. |
| Sensitive information (§2.23.5 and domain policy) | Pass planned | Deferred envelopes are protected with a tenant-bound protector and never returned by read DTOs. Results contain only bounded safe evidence. Durable composition requires a restart-stable protection key. |
| Extension point documentation (framework §2.22) | Pass planned | Update Runtime and Runtime API extension-point catalogs and capability declarations in the same work unit. |
| Refactor/test preservation (framework §2.21.1/§2.23; Elsa §E1) | Pass planned | Existing cancellation, scheduling, retry, migration-adjacent publication, persistence, and API tests remain; no test deletion is planned. |

## Project Structure

### Documentation (this feature)

```text
specs/141-runtime-alterations/
├── spec.md
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   ├── runtime-alteration-api.md
│   └── runtime-alterations.openapi.yaml
├── checklists/requirements.md
└── tasks.md
```

### Source Code (repository root)

```text
src/Elsa/Workflows/Runtime/
├── Core/
│   ├── Constants/
│   ├── Contracts/
│   ├── Events/
│   └── Models/
├── Extensions/RuntimeCoreServiceCollectionExtensions.cs
└── Services/
    ├── Alterations/
    └── Coalescing/

src/Elsa/Activities/Runtime/Core/
├── Contracts/
└── Models/

src/Elsa/Workflows/Publishing/Api/Services/

src/Elsa/Workflows/Runtime/Api/
├── Capabilities/
├── Constants/
├── Endpoints/Alterations/
├── Handlers/Alterations/
├── Models/Alterations/
└── Requests/Alterations/

src/Elsa/Persistence/Groundwork/
├── DependencyInjection/GroundworkRuntimeStoreRegistration.cs
├── Querying/ElsaGroundworkQueryRoutes.cs
├── Serialization/
├── Stores/
└── ElsaRuntimeStorageManifest.cs

tests/Elsa/Workflows/Runtime/Tests/
tests/Elsa/Workflows/Runtime/Api/Tests/
tests/Elsa/Persistence/Groundwork/Tests/
tests/Elsa/Architecture/
e2e-tests/runtime-alterations/
```

**Structure Decision**: Extend the existing Runtime Core, default Runtime, Runtime API, and unified
Groundwork projects. Do not create an alterations package family: the models are Runtime state, the
handlers execute through Runtime's existing actor/checkpoint boundary, and the API is a Runtime API
capability. The four database adapters remain thin Groundwork compositions over the same storage
manifest and stores.

## Runtime Integration

1. Submission validates permission, tenant/authority scope, idempotency, envelope descriptors, and
   composition rules; canonical request content is hashed, the execution payload is protected, and a
   durable plan enters `CapturingTargets`.
2. The capture pump scans workflow executions in immutable `(tenantPartition,
   workflowExecutionId)` order. It evaluates the frozen query vocabulary, persists deduplicated
   captured-target records and cursor progress in batches, then seals the plan. No alteration job is
   claimable before sealing.
3. The job pump leases claimable targets with bounded concurrency and dispatches one deterministic
   `AlterWorkflow` command through the workflow actor. Missing/inaccessible explicit targets receive
   a safe failed job record without leaking whether another tenant owns them.
4. The actor loads current Runtime state and the exact handler sequence, checks captured concurrency
   facts, preflights every alteration against one mutable staged projection, and either stages all
   effects or produces one failure plus later skipped outcomes.
5. A mandatory `RuntimeAlterationJob` checkpoint atomically persists workflow/activity/bookmark/
   durable-value/scheduler/outbox changes and the terminal `AlterationJobState`. An acknowledgement
   loss is reconciled from that job/checkpoint evidence before redelivery.
6. The plan reconciler derives running/final counts from authoritative job records, handles
   cooperative cancellation, and marks a sealed plan terminal only when no pending/running job
   remains.
7. API reads project redacted plan/job views and stable cursors. They never decrypt or return the
   submitted payload.

## Built-in Handler Integration

1. **CancelWorkflow** extracts reusable cancellation staging from
   `WorkflowCancelSchedulerWorkHandler`; terminal targets remain successful no-ops. The job is
   successful only with the cancellation checkpoint.
2. **ModifyVariable** resolves a `WorkflowExecutable.WorkflowVariables` declaration, validates the
   replacement value, and uses `VariableFrameState.Set` against the captured root-frame revision.
3. **ScheduleActivity** requires the selected parent execution to represent the direct executable
   parent of the requested child node, expose the exact compiled operator-scheduling capability for
   that relation, satisfy the activity module's state/scope/completion policy, and have no conflicting
   live child in that slot. A shared scheduling planner stages a fresh deterministic execution and
   Start intent while normal input materialization evaluates authored inputs.
4. **RescheduleActivity** adds the `Superseded` activity state and successor lineage. A new
   supersession planner reclaims only the source-owned subtree/resources, clones immutable pinned
   inputs, and stages the successor. Blocking incidents and terminal workflows reject preflight.
5. **Migrate** runs a strict compatibility validator while the workflow is suspended and quiescent,
   updates every retained artifact-bound Runtime projection plus artifact reference leases, and
   exposes the staged target artifact to later handlers in the same job.

## Persistence Integration

- `AlterationPlanState` and `AlterationTargetState` are standalone Runtime documents managed by the
  alteration store. Batch capture atomically inserts/deduplicates targets and advances the cursor.
- A sealed `AlterationTargetState` becomes the authoritative job record. Claim state is lease-based;
  terminal state and ordered outcomes join the workflow checkpoint change set.
- `RuntimeCheckpointStateChangeSet` and its fingerprint/fold/writer paths gain alteration job
  changes. Alteration checkpoints force an immediate flush and are never folded across jobs.
- Groundwork adds document kinds, versions, primary indexes, plan/job paging and claim queries,
  serializer projections, schema manifests, query routes, golden fixtures, and writer support.
- Provider acceptance covers InMemory plus unified Groundwork. SQLite, PostgreSQL, SQL Server, and
  MongoDB reuse that implementation and must continue passing their existing admission smoke lanes.

## Delivery Slices

1. **Contracts and state machine**: plan/target/job/outcome models, envelopes, exact statuses,
   composition validator, canonicalization, protected payload contract, registration/registry.
2. **Stores and atomic checkpoint**: InMemory stores, capture/claim/cancel/reconcile operations,
   checkpoint job changes/fingerprints/coalescing boundary, Groundwork manifest/store/writer/query
   routes and conformance.
3. **Durable orchestration**: submission service, capture pump, job pump, actor command/handler,
   cancellation, acknowledgement reconciliation, aggregate terminalization.
4. **Cancel and variable handlers**: cancellation staging extraction, terminal no-op, root variable
   validation/revision/protection, focused tests.
5. **Schedule and reschedule handlers**: activity-owned operator-scheduling capability, compiled
   structural predicate, shared schedule staging, supersession state/lineage/cleanup/pinned inputs,
   focused tests.
6. **Migration handler**: quiescence proof, exact compatibility report, artifact references, staged
   post-migration validation, focused tests.
7. **REST and composition**: routes, requests/views, permission/tenant projection, capability links,
   reference-host activation, API tests.
8. **Verification and delivery**: architecture/provider/replay/full runtime tests, rebuilt-server
   backend e2e, maps refresh/review, self-review, local commits, push, draft PR, CI inspection.

## Complexity Tracking

| Tension | Why Needed | Simpler Alternative Rejected Because |
|---|---|---|
| Durable plan/claim orchestration plus checkpoint-integrated terminal job state | Bulk operations must survive restart and acknowledgement loss without false success or duplicate mutation | A request-scoped loop or separate post-checkpoint result write reintroduces timeout ambiguity and an unreconcilable commit/result gap |
| New `Superseded` activity lifecycle and targeted cleanup | Scheduled/waiting/suspended/faulted rescheduling must stop old continuation ownership while preserving history and fresh identity | Reusing an activity ID or calling ordinary retry/resume contradicts the accepted semantics and risks duplicate stimuli |
| Strict migration compatibility engine | Foundation state is split across artifact-bound workflow, activity, bookmark, scope, variable, inspection, scheduler, and reference records | Elsa 3's graph swap or root pin replacement alone can leave retained state pointing at incompatible behavior |
| Startup handler registry | Multiple trusted modules can add stable alteration kinds while dispatch remains deterministic | A framework-owned enum closes the surface; persisting CLR types couples storage to deployment; per-job `IEnumerable<T>` selection violates the contribution and determinism gates |
