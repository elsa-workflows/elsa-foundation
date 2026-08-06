# Implementation Plan: Execution Evidence foundation vertical slice

**Branch**: `779-execution-evidence-foundation` | **Date**: 2026-08-05 | **Spec**: [spec.md](spec.md)

**Status:** Approved (2026-08-05) — approved by control-room review and independent architecture/API review. This approves the implementation plan only; the constitution remains Draft and the referenced ADRs remain proposed until their separate governance review.

## Summary

Implement issue #1133 as an explicitly composed, process-local Execution Evidence foundation. Four projects separate contracts, provider-neutral capture/Runtime adapters, the InMemory provider leaf, and HTTP transport. Runtime adds only generic provenance/order, ownership-fenced attach, terminal-cutoff, and paged outbox-status contracts. Every baseline checkpoint proposal presented to enrichers receives replay-stable generic provenance and a monotonic order. Coalescing retains that guarantee with a minimal durable logical-checkpoint ledger and high-watermark; context-bearing, context-mutating, or post-commit-work proposals are forced to an immediate physical commit. Evidence derives canonical v1 workflow/activity facts from committed checkpoints, persists one opaque intent atomically, and materializes it idempotently after commit.

The constitution is Draft and the Execution Evidence ADRs are proposed. Renumbering the colliding durability ADR and reviewed governance amendments are implementation deliverables before this boundary is described as ratified.

## Technical Context

| Concern | Decision |
|---|---|
| Language/version | C# on the repository’s .NET SDK; nullable-aware records, `System.Text.Json`, and existing DI/CShells conventions. |
| Primary dependencies | Existing Runtime Core checkpoint/outbox contracts and ownership fences, CShells features, FastEndpoints, Elsa API Capabilities, Tasks Core. |
| Storage | Process-local InMemory Evidence store only. Runtime checkpoint/outbox storage remains host-selected. No Groundwork Evidence provider in #1133. |
| Testing | xUnit project tests, TestServer/FastEndpoints API integration, backend PowerShell e2e, deterministic CAS/replay/coalescing/concurrency tests, and benchmark observations. |
| Target | Server and host-agnostic .NET composition. API is an optional server feature. |
| Performance | Record reproducible absent/enabled-unscoped/enabled-scoped metadata-only throughput, allocation, and reservation-storage observations. A reservation may approach the canonical logical checkpoint payload size; correctness takes precedence over the coalescing benefit and no numerical budget is invented here. |
| Hard constraints | No Evidence Runtime branch/identifier/settings/models; deterministic intent identity; checkpoint-atomic recording; at-least-once idempotent materialization; no #1134 barriers/gap-free claims, #1136 values, #1137 Evidence durability, UI, shared fixtures, or J-Test. |

## Constitution and governance check

| Gate | Design result |
|---|---|
| Domain naming and Core envelope | Four domain-named projects, contracts-only Core; prove with project-reference tests. |
| Framework §2.20 provider decomposition | Explicit base + concrete InMemory leaf; API is isolated from InMemory. |
| Framework §§2.5–2.6 composition | Public non-sealed feature classes, contract registrations, and generic Runtime seam contributions. |
| Elsa Runtime → Design direction | No new Runtime-to-Design reference. |
| ADR status and collision | Execution Evidence ADR series is **0052–0061 plus 0063**. Before amendment review, rename the still-proposed Evidence durability ADR from `0062-...` to `0063-...`; preserve the JavaScript ADR as `0062-...`; update the Evidence PRD, ADR links/paths, this dossier, and generated maps. |
| Maps | The planning baseline’s generator check is stale for package/spec-status/findings. No stale map is relied on; a narrow authorized refresh and findings review are implementation deliverables. |

No implementation decision remains unresolved. Governance review is required work, not permission to defer the specified architecture.

## Project structure

```text
src/Elsa/Workflows/ExecutionEvidence/
├── Core/Elsa.Workflows.ExecutionEvidence.Core.csproj
├── Elsa.Workflows.ExecutionEvidence.csproj
├── InMemory/Elsa.Workflows.ExecutionEvidence.InMemory.csproj
└── Api/Elsa.Workflows.ExecutionEvidence.Api.csproj

tests/Elsa/Workflows/ExecutionEvidence/
├── Core/Tests/Elsa.Workflows.ExecutionEvidence.Core.Tests.csproj
├── Tests/Elsa.Workflows.ExecutionEvidence.Tests.csproj
├── InMemory/Tests/Elsa.Workflows.ExecutionEvidence.InMemory.Tests.csproj
└── Api/Tests/Elsa.Workflows.ExecutionEvidence.Api.Tests.csproj

benchmarks/Elsa/Workflows/ExecutionEvidence/Benchmarks/
└── Elsa.Workflows.ExecutionEvidence.Benchmarks.csproj

e2e-tests/execution-evidence/
└── Test-ExecutionEvidenceFoundation.ps1
```

Runtime modifications belong in `src/Elsa/Workflows/Runtime/Core/` and the generic committer/store/coalescing seams. Every `RuntimeCheckpointCommitter` caller is covered by that seam; the four baseline fact producers remain a separate capture concern. Add all new projects to `Elsa.Server.slnx`; add direct server references/assembly catalog entries only for the three feature-owning assemblies, and add the explicit server shell example only after feature IDs exist.

## Dependency and composition decision

```text
ExecutionEvidence.Core
        ↑              ↑
ExecutionEvidence ──→ Runtime.Core, Tasks.Core
        ↑
ExecutionEvidence.InMemory       ExecutionEvidence.Api ──→ FastEndpoints, Api.Capabilities
        ↑                                     ↑
        └──────── explicit server composition ┘
```

- `Core` publishes session, store, catalog, query/cursor, integrity, contribution, and wire contracts only.
- Base `ExecutionEvidence` installs catalog/session/capture/Runtime adapters and the evidence delivery pump.
- `InMemory` registers process-local stores and depends on base; it is the only #1133 provider.
- `Api` depends directly on Core and base, never InMemory. It maps domain outcomes to HTTP only.
- `WorkflowsExecutionEvidence`, `WorkflowsExecutionEvidenceInMemory`, and `WorkflowsExecutionEvidenceApi` are the exact feature IDs. The server explicitly enables all three; absent composition loads none.

## Closed Runtime preparation, replay, and coalescing protocol

The provider-neutral Runtime Core contract evolves the checkpoint store into a two-phase operation:

| Contract/result | Required contents |
|---|---|
| `RuntimeCheckpointPrepareRequest` | Raw logical `CommitId`, workflow execution ID, stable source/operation identity, raw `RuntimeCheckpoint`, pre-enrichment `RuntimeCheckpointStateChangeSet`, requested generic context mutation, and current execution ownership/fence token. |
| `RuntimeCheckpointPreparationResult.Replay` | Persisted provenance/order, full commit fingerprint, and terminal receipt for an existing marker or logical-ledger entry; no new order is allocated. |
| `RuntimeCheckpointPreparationToken` | `CommitId`, logical-ledger token, canonical provenance, expected order revision, expected context revision, expected execution fence, canonical input fingerprint, persisted canonical generic input reference, and initial persistence disposition. |
| `RuntimeCheckpointCommitStore.CommitPreparedAsync` | The token plus enriched commit and final disposition. It returns `Committed`, `Skipped`, `Replay`, `Conflict`, or ownership loss with the persisted receipt where one exists. |

1. **Preflight.** The store reads the persisted commit marker and coalesced logical-checkpoint ledger before allocating an order. A matching marker/ledger entry returns its stored provenance; a different canonical input/fingerprint conflicts.
2. **Prepare.** For a new proposal, the store CAS-reserves a durable logical-ledger entry in `Prepared` state. It assigns `durableCommittedHighWatermark + bufferedOrdinal + 1`, snapshots (but does not mutate) generic context, and records expected context/order revisions and execution fence. The reservation persists a bounded canonical serialization of stable source/operation identity, raw logical `RuntimeCheckpoint`, pre-enrichment `RuntimeCheckpointStateChangeSet`, requested context mutation, provenance/order, and its input fingerprint. Thus every proposal sent to enrichers has durable, replay-stable provenance even while full state/outbox persistence is coalesced. This is not a checkpoint commit or context attachment, but it may approach a logical checkpoint payload in storage and is benchmarked accordingly.
3. **Enrich.** `RuntimeCheckpointCommitter` attaches generic provenance first, then runs enrichers in deterministic registration order; `ExecutionEvidenceCheckpointEnricher` is registered after that generic step and sees no mutable session index. Deferred enriched state/outbox remains in the coalescing buffer: it need not be durably duplicated because recovery loads reservation input, verifies its fingerprint, reattaches stored provenance/order, and deterministically reruns enrichers.
4. **Choose persistence.** The configured policy may suggest `Deferred` or `Immediate` only after enrichment. The committer overrides `Deferred` to `Immediate` when the enriched commit has any generic post-commit outbox work, a non-empty context snapshot, or a context mutation. This inspection is generic and occurs after outbox folding, not in an Evidence branch or a pre-enrichment policy shortcut. `Skip` after post-commit work is rejected as `SkipHasPostCommitWork`; its reservation becomes non-committed `Skipped` (or is deterministically reconciled from `Prepared`) and no checkpoint/outbox/evidence is exposed.
5. **Persist/fold.** An immediate commit, or a coalescing fold, performs one provider CAS over the expected fence, context revision, and order revision. It atomically writes the committed high-watermark, context, commit marker, logical-ledger records, runtime state, and post-commit outbox items. A fold persists every buffered logical entry in its allocated order and preserves each entry’s provenance/fingerprint; it never replaces them with a synthetic order.
6. **Conflict/retry.** A CAS/fence/context revision conflict discards the non-persisted attempt and re-enters preflight. It may reuse only the matching durable ledger token; otherwise it rebuilds against current durable revisions. A retry never invents a new order for the same `CommitId`. Provider crash recovery loads the `Prepared` reservation input, verifies its canonical fingerprint, reattaches its stored provenance/order, and deterministically recomputes enrichment/decision/commit/fold; it does not assume a scheduler source can be re-driven. A post-fold duplicate `CommitId` recovers its persisted provenance and receipt. After a safe fold, the provider may compact the canonical input payload to an immutable receipt/marker.

The ledger is a Runtime correctness mechanism, not an Evidence concept. Ordinary context-free work may remain coalesced, but each logical proposal pays one durable canonical-input reservation write before enrichment and retains its own replay identity. The later fold is the only **full checkpoint state/outbox** write for that buffered segment; it marks reservations `Committed` and may compact safe committed payloads to immutable receipts. Reserved orders are monotonic, not promised contiguous: failed/skipped reservations consume an internal order but expose no committed checkpoint, outbox, association, or evidence. #1134 owns gap semantics.

## Implementation sequence

### 1. Governance, skeleton, and project-reference proof

1. First resolve the ADR collision: rename the still-proposed Execution Evidence durability ADR to `0063-execution-evidence-starts-in-memory-and-adds-groundwork-durability.md`; update its links, the Execution Evidence PRD, dossier references, and generated maps. Keep the JavaScript ADR at 0062.
2. Submit the approved E2.1 module-row amendment and ADR amendments 0052, 0053, 0057, 0060, and 0063; submit #1133-scoped corrections for 0054, 0055, and 0061. Do not call the module boundary ratified until architecture review accepts them.
3. Create the four projects and tests above; follow existing CShells feature/public-registration conventions. Add solution placement and server references/catalog entries. Verify Core has contracts-only references and API has no transitive/direct InMemory dependency through project-reference architecture tests.
4. Add explicit enabled and absent host compositions. The absent in-process harness must prove no Evidence registration, setting, type branch, serializer, persistence item, or Runtime allocation path exists.

### 2. Generic Runtime provenance, order, attach, and status reads

1. Add the immutable bounded/versioned opaque execution-context snapshot and positive `WorkflowCheckpointOrder` to `RuntimeCheckpoint` as generic provenance.
2. Implement the prepare/replay/CAS protocol and durable coalesced logical-checkpoint ledger above in Runtime Core, InMemory, coalescing, and Groundwork checkpoint paths. Include Prepared reservations carrying bounded canonical generic input, high-watermark metadata writes, post-crash input verification and deterministic re-enrichment, exact provenance replay, safe post-fold payload compaction, non-contiguous internal orders, and benchmarked reservation storage/throughput overhead.
3. Inventory the **28 production direct `RuntimeCheckpointCommitter` callers in 22 files** as `docs/reports/runtime-checkpoint-committer-callers.md` during implementation. Treat it as a no-bypass gate: parameterized coverage must exercise every caller through preparation. The inventory explicitly includes direct handlers, both checkpoint-pipeline middlewares, incident paths, bookmark paths, alteration paths, activity-parent paths, and a synthetic coalescing flush.
4. Keep the four baseline fact producers distinct from that plumbing inventory: `WorkflowCheckpointSchedulerWorkHandler`, `WorkflowScheduleActivitySchedulerWorkHandler`, `WorkflowStartActivitySchedulerWorkHandler`, and the direct mandatory path in `WorkflowStartSchedulerWorkHandler`. They provide the four v1 source facts; they must not allocate order/context themselves.
5. Add generic start-context propagation and generic checkpoint-boundary `AttachIfAbsent` through workflow execution ownership/scheduler fencing. Extend outbox items with checkpoint order and add generic terminal-checkpoint observation plus `IRuntimePostCommitOutboxStatusReader`, returning all six statuses in bounded pages with opaque filter-bound cursors.

### 3. Association, capture, provider, and lifecycle implementation

1. Reserve an Evidence association before Runtime start/attach dispatch. The generic attach command is enqueued and executed by the workflow owner behind its scheduler fence, not by a direct store mutation. Its provider CAS requires absent entry, expected context revision, expected fence, expected order revision, marker/ledger token, state, and outbox write together.
2. Model reservation resolution explicitly. Two session attempts for the same workflow compete on the generic absent-entry CAS; only one can commit. An uncertain client retry uses the same operation key and reads its durable Runtime/Evidence receipt rather than issuing a second attachment. A running owner drain serializes an attach behind accepted work; attach cannot leapfrog an active drain.
3. Completion freezes both resolved associations and pending reservations. A reservation created before the freeze is included and must resolve: a Runtime-committed winner is finalized into the frozen set even if freeze happened between Runtime commit and Evidence finalization; rejected, skipped, or failed start/attach resolves to no association. A post-freeze reservation is rejected.
4. Associate-and-start returns an admitted `Starting` association only after authoritative Runtime admission. The first checkpoint commit promotes it to `Active`; an authoritative start/checkpoint failure removes it. Until an unresolved admission is authoritatively resolved, session completion remains incomplete rather than fabricating an association or a negative result. Reconciliation removes no permanent ghost after a recorded failure.
5. Add typed descriptors, deterministic conflict validation, and v1 payload contracts. Reject arbitrary/unregistered payloads; preserve registered unknown records as common envelopes. Canonical checkpoint enrichment recognizes only committed `WorkflowStarted`, `WorkflowCompleted`, `ActivityStarted`, and activity-completion checkpoints; nonbaseline checkpoints still receive generic provenance/order but produce no evidence intent.
6. Derive batch/intent/record IDs from `CommitId`, immutable provenance, stable descriptor data, and ordinal. Canonicalize maps/arrays and copy checkpoint time only as diagnostic metadata. Register an explicit recurring evidence intent driver and idempotent handler; a handler failure cannot alter committed workflow state.
7. Reconcile every evidence-kind outbox page through each terminal cutoff before reporting completion-dependent success. Pending/delivering/retryable remain incomplete; final/cancelled are terminal integrity failures; only all-delivered permits `Completed`, completed-range-without-match, and delete.

### 4. API, documentation, maps, e2e, and benchmark handoff

1. Implement [execution-evidence.openapi.yaml](contracts/execution-evidence.openapi.yaml) with FastEndpoints permissions/access-context conventions. Use disjoint `recordShape` wire unions, bounded scan/page semantics, and exact opaque cursor binding.
2. Add domain README, per-domain `EXTENSION_POINTS.md`, root index entry, explicit host-composition reference, and process-local/no-restart claim.
3. Refresh maps only after explicit authorization: inspect the manifest, run the narrow generators, review generated findings, and run the generator check.
4. Add the enabled-composition REST e2e suite and its lifecycle setup/teardown documented below. Keep module-absence proof in-process unless a separate explicitly configured absent shell is launched.
5. Add benchmark observations with source revision, host, command, workload, throughput, and allocations for absent/enabled-unscoped/scoped cases.

## Failure and concurrency rules to implement

| Situation | Required outcome |
|---|---|
| Existing marker/ledger replay | Return the stored provenance/order/receipt; changed canonical input or enriched fingerprint conflicts. |
| Prepare, enrich, policy, or persistence CAS failure | No committed checkpoint/outbox/evidence; reservation is `Failed`/`Skipped` or deterministically reconciled from `Prepared`; retry starts from preflight under the current fence/revisions. |
| Deferred coalesced logical checkpoint | A durable `Prepared` reservation retains canonical raw checkpoint/state-change/context-mutation/source input plus order/provenance/fence; recovery verifies it, reruns deterministic enrichment, and fold commits full state/outbox in that order. |
| Coalescing sees non-empty/mutating context or enriched post-commit work | Generic committer overrides deferral to immediate physical commit; provenance/order/outbox share one CAS receipt. |
| Persistence policy skips enriched intent | Generic committer returns `SkipHasPostCommitWork`; no context update, checkpoint, outbox, or evidence. |
| Attach versus drain/two sessions/uncertain retry | Scheduler ownership serializes drain; absent-entry CAS gives one winner; the durable operation receipt resolves retry exactly once. |
| Freeze after Runtime commit before Evidence finalization | The pre-freeze reservation is included; reconciliation finalizes the committed winner into the frozen set. |
| Admitted start never reaches first checkpoint | Association remains truthful `Starting`/incomplete until authoritative Runtime outcome; an authoritative failure removes it. |
| Handler fails after commit | Workflow remains committed; generic status is retryable/final, evidence integrity reflects it. |
| Final/cancelled intent through cutoff | Terminal integrity failure, never completed-range-without-match. |

## Verification plan

| Scope | Concrete project/command and required proof |
|---|---|
| Core contracts | `dotnet test tests/Elsa/Workflows/ExecutionEvidence/Core/Tests/Elsa.Workflows.ExecutionEvidence.Core.Tests.csproj` — descriptor/ID/order/cursor/wire validation, disjoint typed/unknown envelope, unknown payload, and no values. |
| Base + InMemory | `dotnet test tests/Elsa/Workflows/ExecutionEvidence/Tests/Elsa.Workflows.ExecutionEvidence.Tests.csproj` and `dotnet test tests/Elsa/Workflows/ExecutionEvidence/InMemory/Tests/Elsa.Workflows.ExecutionEvidence.InMemory.Tests.csproj` — gating; reservation/attach/freeze races; active drain; two sessions; uncertain retry; start failure reconciliation; cutoff reconciliation; dedupe/wait/delete. |
| Runtime seams | `dotnet test tests/Elsa/Workflows/Runtime/Tests/Elsa.Workflows.Runtime.Tests.csproj` and relevant Groundwork checkpoint tests — parameterized no-bypass coverage for the 28 callers/22 files; both checkpoint middlewares; incidents/bookmarks/alterations/activity-parent paths; synthetic coalescing flush; Prepared canonical-input recovery without source redrive; fingerprint verification; post-fold duplicate/compaction; non-contiguous skipped/failed orders; immediate override; skip-with-work; six-status paging. |
| API integration | `dotnet test tests/Elsa/Workflows/ExecutionEvidence/Api/Tests/Elsa.Workflows.ExecutionEvidence.Api.Tests.csproj` — scopes, disjoint typed/unknown records, correlation-pair/range validation, bounded scan/timeout cursor advancement, cursor reuse/access/filter/page-size binding, lifecycle/integrity outcomes. |
| Architecture/reference | `dotnet test tests/Elsa/Architecture/Elsa.Architecture.Tests.csproj` — Core/API/InMemory direction, server composition, and absence assertions. |
| Server e2e | Rebuild and deploy a fresh enabled server composition, wait for readiness, then run `e2e-tests/execution-evidence/Test-ExecutionEvidenceFoundation.ps1` against the ordinary REST/persistence/runtime path; see [quickstart.md](quickstart.md). |
| Benchmark | `dotnet test benchmarks/Elsa/Workflows/ExecutionEvidence/Benchmarks/Elsa.Workflows.ExecutionEvidence.Benchmarks.csproj --filter "FullyQualifiedName~ExecutionEvidence" --logger "console;verbosity=detailed"` — absent, enabled-unscoped, scoped metadata-only results plus reservation throughput/allocation and canonical-input storage size before/after safe compaction. |
| Plan/OpenAPI | `git diff --check`; YAML parse; Redocly/OpenAPI lint or the repository-provided equivalent; `.specify/scripts/bash/check-prerequisites.sh --json --paths-only`. |

## Explicit deferrals

- #1134: full lifecycle catalog, settled barriers, gap detection, gap-free completeness, definitive negatives.
- #1135: stimuli, scheduling, child-workflow causation.
- #1136: values, sanitization, redaction, truncation, and actual disposition behavior.
- #1137: Groundwork Evidence store, recovery, distributed/failover proof, retention cleanup/provider conformance.
- #1138: shared protocol/conformance fixtures, J-Test DSL/lifecycle/retry/adapter.
- UI/dashboard work and any test-framework-specific surface.

## Complexity tracking

| Deliberate complexity | Why needed | Smaller alternative rejected |
|---|---|---|
| Four projects | Contracts, adapters, provider, and transport have different dependency envelopes. | Three projects leaves provider/transport coupling and contradicts the approved spec. |
| Durable generic provenance/order ledger | FR-002/SC-003 require every enriched baseline proposal to carry replay-stable order, including coalesced work. | Treating deferred proposals as unordered/non-durable violates the approved contract. |
| Generic coalescing override | Context and post-commit work need a physical atomic boundary while remaining Evidence-agnostic. | An Evidence-specific Runtime branch or inspecting pre-enrichment state fails isolation/correctness. |
| Reservation/freeze reconciliation | Association admission must remain truthful across owner drains, retries, and freeze timing. | Mutating a session before/after an untracked Runtime call creates ghosts or drops committed winners. |
| Frozen terminal-cutoff lifecycle | A session cannot safely report completed range while a frozen workflow can still commit. | Idle/quiescent/suspended is temporary and explicitly insufficient. |
