# Implementation Plan: Execution Evidence foundation vertical slice

**Branch**: `779-execution-evidence-foundation` | **Date**: 2026-08-05 | **Spec**: [spec.md](spec.md)

**Status:** T029 construction-time logical-inspection-projection plan, tasks, and RED packet approved (2026-08-06). T029a produced 24 tests: 14 protective PASS / 10 intentional RED; materialized-source lifecycle is 8/8 PASS, and the unchanged guardrails are 1/7 PASS with six canonical `FirstCheckpointId` REDs. Independent RED review passed with no P0–P3 finding and the control room approved it. T029a/T029b are complete; T029c is authorized in exactly the three planned production files, while implementation and T029d remain pending. The 2026-08-05 plan, Path A recovery-authority amendment, and qualifying Immediate-boundary amendment also passed their applicable reviews. The constitution remains Draft and the referenced ADRs remain proposed until their separate governance review.

## Summary

Implement issue #1133 as an explicitly composed, process-local Execution Evidence foundation. Four projects separate contracts, provider-neutral capture/Runtime adapters, the InMemory provider leaf, and HTTP transport. Runtime adds only generic provenance/order, ownership-fenced attach, terminal-cutoff, and paged outbox-status contracts. Every baseline checkpoint proposal presented to enrichers receives replay-stable generic provenance and a monotonic order. Coalescing retains that guarantee with a minimal durable logical-checkpoint ledger and high-watermark while preserving accepted source-domain redrive: a D1/D2 fused prefix remains owned by normal redelivery of its original durable `ScheduleActivity`, whereas only work originally prepared with no declared source authority may use source-independent provider recovery. Context-bearing, context-mutating, or post-commit-work proposals are still forced to an immediate physical commit. When such a successful Immediate commit is context-free and contains only scheduler-continuation outbox rows, those exact durable Pending rows may continue through the active overlay under delayed acknowledgement only while the overlay is live; after session loss, ordinary durable redrive may acknowledge immediately after exact idempotent durable queue enqueue. Evidence derives canonical v1 workflow/activity facts from committed checkpoints, persists one opaque intent atomically, and materializes it idempotently after commit.

The constitution is Draft and the Execution Evidence ADRs are proposed. Renumbering the colliding durability ADR and reviewed governance amendments are implementation deliverables before this boundary is described as ratified.

## Technical Context

| Concern | Decision |
|---|---|
| Language/version | C# on the repository’s .NET SDK; nullable-aware records, `System.Text.Json`, and existing DI/CShells conventions. |
| Primary dependencies | Existing Runtime Core checkpoint/outbox contracts and ownership fences, CShells features, FastEndpoints, Elsa API Capabilities, Tasks Core. |
| Storage | Process-local InMemory Evidence store only. Runtime checkpoint/outbox storage remains host-selected. No Groundwork Evidence provider in #1133. |
| Testing | xUnit project tests, TestServer/FastEndpoints API integration, backend PowerShell e2e, deterministic CAS/replay/coalescing/concurrency tests, generic Immediate-boundary crash/reconciliation RED, construction-time inspection-projection RED/protective tests, separately owned unchanged spec 123 D1/D2 reconciliation gates, source-independent current/older-fence recovery tests, and benchmark observations. |
| Target | Server and host-agnostic .NET composition. API is an optional server feature. |
| Performance | Record reproducible absent/enabled-unscoped/enabled-scoped metadata-only throughput, allocation, and reservation-storage observations. A reservation may approach the canonical logical checkpoint payload size; correctness takes precedence over the coalescing benefit and no numerical budget is invented here. |
| Hard constraints | No Evidence-, D1-, D2-, or fusion-specific Runtime/provider branch, identifier, setting, or model; preserve accepted ADR 0047/spec 123 source redrive; preserve the generic after-enrichment Immediate override; no direct handler invocation, duplicate continuation intent/work item, or acknowledgement of memory-only overlay delivery; deterministic intent identity; checkpoint-atomic recording; at-least-once idempotent materialization; no synthetic order, inferred terminal disposition, or premature nonterminal compaction; no #1134 barriers/gap-free claims, #1136 values, #1137 Evidence durability, UI, shared fixtures, or J-Test. |

## Constitution and governance check

| Gate | Design result |
|---|---|
| Domain naming and Core envelope | Four domain-named projects, contracts-only Core; prove with project-reference tests. |
| Framework §2.20 provider decomposition | Explicit base + concrete InMemory leaf; API is isolated from InMemory. |
| Framework §§2.5–2.6 composition | Public non-sealed feature classes, contract registrations, and generic Runtime seam contributions. |
| Elsa Runtime → Design direction | No new Runtime-to-Design reference. |
| Source-domain dependency and accepted decisions | Runtime owns generic recovery routing and providers own only opaque prepared-ledger CAS. ADR 0047/spec 123 remains authoritative for D1/D2: the original durable `ScheduleActivity` is redelivered normally. No Evidence adapter or persistence provider selects or replaces source-domain progression. |
| Refactoring continuity | Preserve spec 123's byte-identical and crash-convergence objectives, including D1, D2, join/External/suspend fallback, fusion counters, and the existing idempotency ladder. The generic qualifying-Immediate handoff reuses durable outbox authority and does not replace source authority or rewrite the fusion driver. The current `ReplaySafeFusion` result is 5 PASS / 6 RED: the six unchanged guardrails isolate durable `ActivityExecutionInspection.FirstCheckpointId` drift, not transient outbox transport. The construction-time projection amendment is the reviewed generic reconciliation path; the authority split is not accepted as a reduction in guardrail coverage. |
| ADR status and collision | Execution Evidence ADR series is **0052–0061 plus 0063**. Before amendment review, rename the still-proposed Evidence durability ADR from `0062-...` to `0063-...`; preserve the JavaScript ADR as `0062-...`; update the Evidence PRD, ADR links/paths, this dossier, and generated maps. |
| Maps | The planning baseline’s generator check is stale for package/spec-status/findings. No stale map is relied on; a narrow authorized refresh and findings review are implementation deliverables. |

No technical choice remains marked `NEEDS CLARIFICATION`. The scheduler-continuation amendment review gate is satisfied. Separate governance review remains required and is not permission to defer the specified architecture.

### Path A plan-review disposition (2026-08-06)

Independent review: **PASS**, with no unresolved finding. The approved amendment closes the concrete scheduler-work
authority/accessor/router contract, exact-set fence adoption and rollback semantics for both recovery routes, and the full
spec 123 continuity gate. This disposition approves implementation planning only; it does not ratify the Draft constitution
or proposed ADRs.

### Durable scheduler-continuation amendment disposition (2026-08-06)

Independent review: **PASS**, with no unresolved finding; control-room disposition: **APPROVED**. The amendment preserves
the generic Immediate override and adds only a generic post-success handoff for exact committed `EnqueueSchedulerWork`
rows. T024/T025 retain proof ownership for actual outer D1 authority capture and stack-restoring ambient nesting only.
T028/T029 own the generic Immediate-boundary crash and delayed-reconciliation proof/implementation. The D2→D1 fusion/fold
proof is separately blocked for spec-123 reconciliation. This approval authorizes the amended task contract; it does not approve a RED artifact or production
implementation and does not ratify the Draft constitution or proposed ADRs.

### T029 construction-time logical-inspection-projection amendment (RED approved; implementation pending, 2026-08-06)

The generic scheduler-continuation handoff is complete and remains separate from this work. The current
`ReplaySafeFusion` inventory is **11 total: 5 PASS / 6 RED**. The five PASS are the isolated D2-pump authority proof,
resumption-barrier authority isolation, provider-fold harness prerequisite, ReplaySafe contract probe, and deterministic
fingerprint. The six RED are the canonical byte-identity guardrails, including External. Their matching logical commits
first diverge at durable `ActivityExecutionInspection.FirstCheckpointId`; the guardrails deliberately exclude only
transient post-commit intent/outbox transport bookkeeping under FR-023–FR-025 and SC-012.

The cause is construction-time, generic coalescing state: the existing inspection store treats a session-local read as a
durable-baseline memo, so a Deferred logical contribution remains invisible to the next inspection build whereas an
equivalent Immediate contribution has already become durable and is observed. That cadence selects different
`FromState`/merge inputs and later drifts `FirstCheckpointId`. Successful durable finalization already invalidates its
baseline memo; retaining that FR-024 invariant is protective but is not the root-cause fix. This amendment deliberately
and only supersedes the prior spec-131 **control-read semantics**: it retains the tests' byte-identity,
durable-pass-through, and read-observability objectives, but no longer treats an active session's logical projection as
ineligible for `FindAsync`.

T029a added the reviewed RED/protective packet and T029b independently reviewed it: 24 tests = 14 protective PASS /
10 intentional RED; materialized-source lifecycle 8/8 PASS; unchanged guardrails 1/7 PASS with six canonical
`FirstCheckpointId` REDs. Independent review passed with no P0–P3 finding and the control room approved it. T029c is
now authorized to change only
`src/Elsa/Workflows/Runtime/Services/Coalescing/{CoalescingRuntimeCheckpointCommitStore.cs,RuntimeCoalescingSession.cs,CoalescingRuntimeStateStores.cs}`.
The implementation scope adds the state-store adapter solely to apply the generic construction-time precedence below.
Fusion driver, committer, policy, providers, Evidence, public contracts, recovery authority, and checkpoint-name/source
branches remain excluded. T029d reruns the unchanged six guardrails and then the unfiltered spec-123 gate. No Evidence,
provider, Fusion, D1/D2, source-authority, or checkpoint-name branch is an acceptable reconciliation mechanism.

**T029a/T029b RED disposition (2026-08-06, approved):** Later source review proved the single authority scenario still ran a
queued `RunSchedulerWork` barrier ahead of the durable continuation. The test-only split now uses the existing scoped
pending-consume claim to bind exact dispatch ownership: in the isolated real-orchestrator path the nested item differs
from the outer claim and inherits its authority; in the sweep path `RunSchedulerWork` is first and the later drainer item
equals its own claim and receives a distinct authority. The provider-fold observer remains on the captured durable
provider and the outer boundary-crash behavior is unchanged. The completed amended packet is **24 total: 14 protective
PASS / 10 intentional RED**; materialized-source lifecycle is **8/8 PASS**, and the unchanged guardrail lane is **1/7
PASS** with six canonical `FirstCheckpointId` REDs. Independent review passed with no P0–P3 finding and the control room
approved T029c in exactly the three planned production files.

### T029e live-carrier resolution disposition (complete, 2026-08-06)

Source-owner review: **PASS**; independent RED review: **PASS**; control-room approval: **APPROVED**. An initial
implementation review found one P2 allocation issue; the corrected two-file implementation passed independent final
review with no open finding. The committer captures a raw `EnqueueSchedulerWork` conduit only for the owning live drain
and only publishes it after the prepared finalization returns a new `Committed` result with exactly one corresponding
durable intent. Identity/association plus candidate-intent payload and independently serialized candidate work-item
payload must structurally match the final durable payload. The durable payload remains authoritative; replay, skip,
failure, exception, mismatch, coalescing ownership, and unrelated commits do not publish. Verification: focused
`RuntimeInProcessHopFastPathTests` **19/19**; preparation/committer subset **50/50**; root full Runtime **1838/1838**.
T029a/T029b are complete. T029c is authorized only in the three planned production files; T030 remains blocked until
T029c–T029d complete.

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
| `RuntimeCheckpointRecoveryAuthority` | Version `1`, kind `runtime.scheduler-work`, workflow execution ID, durable scheduler `WorkItemId`, and the bounded canonical `sha256:` work-item fingerprint defined in [research.md](research.md). No other version/kind is accepted in #1133. |
| Dispatch authority accessor | `WorkflowSchedulerDrainer` opens a stack-restoring scope from the actually acquired durable item around handler/pipeline dispatch. Nested D2/D1 execution inherits it. Only `RuntimeCheckpointCommitter` consumes it; checkpoint callers and providers cannot supply or infer it. |
| `RuntimeCheckpointPrepareRequest` | Raw logical `CommitId`, workflow execution ID, stable source/operation identity, committer-copied optional recovery authority, raw `RuntimeCheckpoint`, pre-enrichment `RuntimeCheckpointStateChangeSet`, requested generic context mutation, and current execution ownership/fence token. |
| `RuntimeCheckpointPreparationResult.Replay` | Persisted provenance/order, full commit fingerprint, and terminal receipt for an existing marker or logical-ledger entry; no new order is allocated. |
| `RuntimeCheckpointPreparationToken` | Immutable preparation identity: `CommitId`, stable `LedgerToken`, workflow/order/provenance, original preparation fence and order/context revisions, source authority, canonical input reference/digest/fingerprint, and initial persistence disposition. Mutable authority binding is separate: current authority fence plus provider CAS revision. |
| `RuntimeCheckpointCommitStore.CommitPreparedAsync` | The token plus enriched commit and final disposition. It returns `Committed`, `Skipped`, `Replay`, `Conflict`, or ownership loss with the persisted receipt where one exists. |
| Recovery router | Before adoption/replay/dispatch, returns exactly `Absent`, `Exact`, `Missing`, `FingerprintMismatch`, `UnsupportedVersionOrKind`, or `Ambiguous`. It uses exact durable lookup or bounded stable keyset pages, includes claimed-but-durable work, and never reclassifies a declared source as absent. |
| `RuntimeCheckpointPreparedAdoptionRequest` | One generic provider-atomic exact-set request used for source-bound and source-free routes. It carries route, workflow, inclusive `ThroughWorkflowCheckpointOrder`, strictly newer target fence, and full ordered members with immutable identities, original bindings, expected current fence/CAS revision, fingerprints, and exact authority. |
| Qualifying durable scheduler continuation | An internal coalescing handoff after a successful new `Committed` Immediate CAS whose context snapshot is empty, context mutation is absent, and nonempty committed outbox consists only of `EnqueueSchedulerWork`. It imports the exact committed Pending rows/IDs into the active overlay, never creates a second row or work item, and delays `Delivered` only while the inline effect remains live-overlay-only. Eligibility remains generic: no durable recovery input may depend on memory-only scheduler work. The exact materialization/consume transition is a T029 implementation-and-review proof. After session loss ordinary durable redrive may acknowledge after its exact idempotent durable queue enqueue. |
| Active coalesced inspection projection | An internal per-activity logical projection assembled from ordered logical checkpoint contributions owned by the active session. Accepted Deferred contributions update it immediately so the next in-session build observes the same logical state that an Immediate path would observe durably. Immediate/fold trailing contributions update it only after a new successful `Committed` finalization. `IActivityExecutionInspectionStore.FindAsync` returns it before a durable baseline while it exists. It is neither a public model nor a new persistence record; session loss/deactivation discards uncommitted contributions. |

1. **Preflight.** The store reads the persisted commit marker and coalesced logical-checkpoint ledger before allocating an order. A matching marker/ledger entry returns its stored provenance; a different canonical input/fingerprint conflicts.
2. **Prepare.** For a new proposal, the store CAS-reserves a durable logical-ledger entry in `Prepared` state. It assigns `durableCommittedHighWatermark + bufferedOrdinal + 1`, snapshots (but does not mutate) generic context, and records expected context/order revisions and execution fence. The reservation persists a bounded canonical serialization of stable source/operation identity, optional `RuntimeCheckpointRecoveryAuthority`, raw logical `RuntimeCheckpoint`, pre-enrichment `RuntimeCheckpointStateChangeSet`, requested context mutation, provenance/order, and its input fingerprint. The authority is v1/`runtime.scheduler-work` with workflow ID, durable work-item ID, and the domain-separated canonical fingerprint and explicit bounds in research. `WorkflowSchedulerDrainer` opens it only from the item actually dispatched; the committer alone reads the dispatch-scoped accessor, including through nested D2/D1 pumping. It is never caller-supplied or provider-inferred. Thus every proposal sent to enrichers has durable, replay-stable provenance even while full state/outbox persistence is coalesced. This is not a checkpoint commit, context attachment, or progression authority, but it may approach a logical checkpoint payload in storage and is benchmarked accordingly.
3. **Enrich.** `RuntimeCheckpointCommitter` attaches generic provenance first, then runs enrichers in deterministic registration order; `ExecutionEvidenceCheckpointEnricher` is registered after that generic step and sees no mutable session index. Deferred enriched state/outbox remains in the coalescing buffer: it need not be durably duplicated because recovery loads reservation input, verifies its fingerprint, reattaches stored provenance/order, and deterministically reruns enrichers.
4. **Choose persistence.** The configured policy may suggest `Deferred` or `Immediate` only after enrichment. The committer overrides `Deferred` to `Immediate` when the enriched commit has any generic post-commit outbox work, a non-empty context snapshot, or a context mutation. This inspection is generic and occurs after outbox folding, not in an Evidence branch or a pre-enrichment policy shortcut. `Skip` after post-commit work is rejected as `SkipHasPostCommitWork`; no checkpoint/outbox/evidence is exposed, and the reservation remains `Prepared` unless a separate explicit trusted terminal disposition is supplied.
5. **Persist/fold.** An immediate commit, or a coalescing fold, performs one provider CAS over the expected fence, context revision, and order revision. It atomically writes the committed high-watermark, context, commit marker, logical-ledger records, runtime state, and post-commit outbox items. A fold persists every buffered logical entry in its allocated order and preserves each entry’s provenance/fingerprint; it never replaces them with a synthetic order.
5a. **Continue only across a qualifying durable scheduler boundary.** After a new Immediate CAS returns `Committed`, a still-active session examines only generic committed state. If context is empty/unmutated and every nonempty outbox row is `EnqueueSchedulerWork`, it imports those exact committed Pending rows/IDs, marks them durably persisted, and applies the durable boundary state to the overlay. The exact materialization/consume transition remains a T029 implementation-and-review proof: eligibility is generic and no durable recovery input may depend on memory-only scheduler work. `Replay` follows ordinary deactivation/advancement. While the inline effect is live-overlay-only, durable rows remain Pending until a later successful checkpoint/fold incorporates it and reconciliation records `Delivered`. A crash before or after inline dispatch starts ordinary recovery from the same durable row: the ordinary processor's exact idempotent durable queue enqueue may record `Delivered`, while a crash before that mark repeats without a duplicate. Mixed/arbitrary/external outbox, context-only/context-mutating, delivery-failure, and terminal/no-continuation boundaries deactivate normally. Whether a shipped fusion pump re-enters D1 remains the separately blocked spec 123 reconciliation, not this generic contract.
5b. **Construct inspection state from the session's logical projection.** The active session owns one logical
`ActivityExecutionInspectionProjection` per activity execution, composed by the existing projection merge semantics
from ordered logical checkpoint contributions. `CoalescingActivityExecutionInspectionStore.FindAsync` first returns
that projection whenever it exists, before consulting any durable baseline. This is a construction-time read rule, not
a change to durable visibility: outside an active matching session and after deactivation, reads remain exact durable
pass-through. When no logical projection exists, `CoalesceInspectionReads=true` may memoize the durable baseline for
the current segment and `false` reads that baseline per call. When a logical projection exists, both settings return it;
the `false` control may still perform its durable read for diagnostic/read-count meaning, but must not substitute its
result for the logical projection.

An accepted Deferred contribution updates the session projection immediately, after it is accepted into the active
buffer, so the next logical build uses the same merge input as an equivalent Immediate path. A new Immediate commit or
the trailing contribution of a fold updates it only after the provider returns successful `Committed`; a fold's
already-buffered contributions are already present and are not re-applied. Every successful durable finalization
invalidates any durable-baseline memo, preserving FR-024's no-stale-baseline invariant. A deferred contribution remains
visible only to its live session; a process loss drops it and replay begins from durable state before deterministically
reconstructing later logical contributions. `Conflict`, ownership loss, failure, exception, `Replay`, rejected/explicit
`Skip`, and a nonqualifying boundary never apply the candidate contribution. A successful cap flush may add only its
trailing contribution after persistence; terminal/quiescent deactivation clears session-local projection and baselines.
Thus later composition sees the active logical projection, while outside-session and post-deactivation readers see
durable truth only.
6. **Route recovery before mutation or dispatch.** Under the current workflow ownership lease, Runtime pages and validates the complete `Prepared` set, then classifies each reservation through the six-result router. Exact lookup addresses `(WorkflowExecutionId, WorkItemId)` directly; a fallback uses stable pages of at most 250 whose opaque cursor binds workflow and last queue order key. Both read durable entries regardless of active claim/visibility. `Absent` means authority was null at original preparation. `Exact` means one matching durable item and fingerprint. Zero, differing fingerprint, unsupported version/kind, or conflicting duplicates yield `Missing`, `FingerprintMismatch`, `UnsupportedVersionOrKind`, or `Ambiguous` and fail with zero mutation. No declared authority can later become `Absent`.
7. **Adopt the selected exact set.** Both `Exact` source-bound reservations and an exact contiguous `Absent` prefix use one provider-atomic adoption request before dispatch/replay. Members are fully ordered and include `CommitId`, `LedgerToken`, checkpoint order, canonical digest/fingerprint, original fence/revisions, expected current authority fence/CAS revision, and exact authority. The inclusive order bound defines the comparison scope: all same-authority `Prepared` members through it for source-bound, or every `Prepared` member from the first nonterminal order through it for source-free. The provider re-reads that scope and rejects missing, extra, duplicate, partial, mixed authority/current fence, stale, downgrade, or unauthorized requests atomically. A valid target is strictly newer; exact replay at the already-current target is idempotent. Success changes only current authority fence and CAS revision; original preparation identity and all state/context/outbox/marker/high-watermark/receipt/compaction data are immutable.
8. **Recover through the selected authority.** After source-bound adoption, Runtime performs no replay/fold and dispatches nothing synthetic; normal source redelivery recreates the same canonical proposals and prepare reuses every identity before deterministic enrichment. For D1/D2, all inline proposals stay tied to the original durable `ScheduleActivity`. After source-free adoption, the shared replayer reconstructs the exact prefix and one exact-membership fold may commit it. Recovery never crosses/omits a source-bound reservation, synthesizes an order/checkpoint, infers `Skipped`/`Failed`, or changes canonical input.
9. **Conflict/retry and compaction.** Any routing, adoption, CAS, fence, digest, token, context, or revision conflict discards the non-persisted attempt and re-enters validation. Injected failure during either route's provider adoption rolls back the whole set and permits no source dispatch, replayer call, or fold. A retry never invents a new order or rotates a durable token. A post-fold duplicate recovers its persisted provenance and exact receipt. Only a successful terminal fold may compact canonical input to an immutable receipt/marker.

The ledger is a Runtime correctness mechanism, not an Evidence concept and not a universal progression authority. Ordinary context-free work may remain coalesced, but each logical proposal pays one durable canonical-input reservation write before enrichment and retains its own replay identity and, when applicable, its source-domain authority. The later fold is the only **full checkpoint state/outbox** write for that buffered segment; it terminalizes exact explicit members and may compact only terminal payloads. A qualifying Immediate boundary is already a full durable commit: its exact Pending scheduler rows are merely borrowed into the overlay, not persisted twice or acknowledged while their effect is memory-only. Reserved orders are monotonic, not promised contiguous: explicitly skipped/failed reservations consume an internal order but expose no committed checkpoint, outbox, association, or evidence. #1134 owns gap semantics.

## Implementation sequence

### 1. Governance, skeleton, and project-reference proof

1. First resolve the ADR collision: rename the still-proposed Execution Evidence durability ADR to `0063-execution-evidence-starts-in-memory-and-adds-groundwork-durability.md`; update its links, the Execution Evidence PRD, dossier references, and generated maps. Keep the JavaScript ADR at 0062.
2. Submit the approved E2.1 module-row amendment and ADR amendments 0052, 0053, 0057, 0060, and 0063; submit #1133-scoped corrections for 0054, 0055, and 0061. Do not call the module boundary ratified until architecture review accepts them.
3. Create the four projects and tests above; follow existing CShells feature/public-registration conventions. Add solution placement and server references/catalog entries. Verify Core has contracts-only references and API has no transitive/direct InMemory dependency through project-reference architecture tests.
4. Add explicit enabled and absent host compositions. The absent in-process harness must prove no Evidence registration, setting, type branch, serializer, persistence item, or Runtime allocation path exists.

### 2. Generic Runtime provenance, order, attach, and status reads

1. Add the immutable bounded/versioned opaque execution-context snapshot and positive `WorkflowCheckpointOrder` to `RuntimeCheckpoint` as generic provenance.
2. Implement the prepare/replay/CAS protocol and durable coalesced logical-checkpoint ledger above in Runtime Core, InMemory, coalescing, and Groundwork checkpoint paths. Include the concrete v1 `runtime.scheduler-work` authority/fingerprint bounds; the drainer-opened, committer-only dispatch accessor; claimed-inclusive exact lookup/bounded router; immutable original preparation identity separated from current authority fence/CAS revision; one exact-set adoption operation for both routes; D1/D2 normal source redelivery; source-independent recovery only for exact contiguous no-source prefixes; high-watermark metadata writes; deterministic re-enrichment; exact provenance replay; terminal-only compaction; non-contiguous internal orders; and benchmarked reservation overhead. Stage T024/T025 proves an actual outer D1 dispatch plus ambient nesting semantics only. T028 proves the qualifying generic durable scheduler-continuation handoff and its crash/reconciliation behavior; it does not change the committer override or preparation replayer. After the T029 task and RED reviews, implement the construction-time inspection projection in exactly `CoalescingRuntimeCheckpointCommitStore`, `RuntimeCoalescingSession`, and `CoalescingRuntimeStateStores`: preserve existing merge semantics, publish accepted Deferred contributions immediately into the live projection, add Immediate/fold trailing contributions only after successful durable finalization, and give the active logical projection precedence over a durable baseline. T029 retains full Runtime/T027 recovery non-regression; T029d runs spec-123 verification before T030.
3. Inventory the **current 27 production direct `RuntimeCheckpointCommitter` callers in 21 files** as `docs/reports/runtime-checkpoint-committer-callers.md` during implementation. Treat it as a no-bypass gate: parameterized coverage must exercise every direct caller through preparation. Preserve T015's historical 28-caller/22-file audit at `bd94b3c8d`; the synthetic coalescing flush removed from the direct inventory is covered separately by the provider-atomic prepared-fold gate.
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
| Prepare, enrich, policy, cancellation, or persistence CAS failure | No committed checkpoint/outbox/evidence and no inferred terminal disposition. A durable reservation remains `Prepared` unless an explicit trusted `Skipped`/`Failed` outcome is supplied. |
| Source-bound deferred checkpoint | Router `Exact`: atomically adopt the full homogeneous set, leave it `Prepared`, then normally redeliver the source. `Missing`, fingerprint mismatch, unsupported version/kind, ambiguity, partial/mixed set, or adoption failure leaves every member unchanged and dispatches/replays/folds nothing; never switch to provider progression. |
| Source-independent deferred checkpoint | Router `Absent` only: an originally no-source exact contiguous homogeneous prefix may be atomically adopted by the current owner, then deterministically rehydrated/folded. Hidden source-bound gap, mixed/current-fence mismatch, or adoption failure rolls back the whole set and invokes neither replayer nor fold. |
| D1/D2 fused-span crash | The original durable `ScheduleActivity` remains queued until convergence; its redelivery reuses every matching reservation identity and progresses through the existing enqueue/status/fold-forward ladder. |
| Fence handoff | Exact-set adoption under a verified strictly newer current fence changes only current authority fence + CAS revision; original preparation fence/revisions remain immutable. Exact same-current replay is idempotent. Durable token, provenance/order, source authority, canonical bytes/reference, fingerprint, state, context, outbox, markers, high-watermarks, receipts, and compaction are unchanged. |
| Coalescing sees non-empty/mutating context or enriched post-commit work | Generic committer overrides deferral to immediate physical commit; provenance/order/outbox share one CAS receipt. |
| New `Committed` Immediate commit with empty/unmutated context and only `EnqueueSchedulerWork` rows | Import exactly the committed Pending rows/IDs into the active overlay, mark them durably persisted, and apply the boundary state. The exact materialization/consume transition requires T029 implementation-and-review evidence; eligibility remains generic and no durable recovery input may depend on memory-only scheduler work. Keep the session active. No direct dispatch, duplicate intent, or duplicate work item. A `Replay` uses ordinary deactivation/advancement. |
| Crash before or after inline scheduler-continuation dispatch | Both cases begin from the original durable Pending row. Normal outbox redrive reuses the same intent/work-item identity and Path A source authority; its exact idempotent durable queue enqueue may mark Delivered immediately, while a crash before the mark repeats without a duplicate. |
| Mixed/arbitrary/external outbox, context-only/context mutation, delivery failure, or terminal/no continuation | Deactivate the session and use ordinary durable processing/redrive; never widen the continuation eligibility rule. |
| Active inspection read after an accepted Deferred contribution | Publish its ordered logical inspection projection immediately into the live session and return it before a durable baseline, so the next build has the same merge input as the equivalent Immediate path. This applies with either `CoalesceInspectionReads` setting. |
| Active inspection read after a successful Immediate/fold boundary | Return the active session's ordered logical inspection projection before a durable baseline. Add a new Immediate/fold trailing contribution only after successful durable finalization; fold members already represented in the projection are not re-applied. Invalidate the baseline memo after success, preserving the no-stale-baseline invariant. |
| Active inspection read with no logical projection | `CoalesceInspectionReads=true` may memoize the durable baseline for the live segment; `false` reads it per call. Both remain byte-identical to durable pass-through until a logical projection exists. |
| Inspection finalization failure, conflict, ownership loss, replay, skip, crash, cap, or quiescence | Do not apply an unfinalized/replayed/skipped Immediate/fold candidate projection. A successful cap flush may add its trailing contribution only after persistence; deactivation/quiescence/crash drops volatile Deferred state, and subsequent outside-session/replay reads start from durable truth. |
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
| Runtime seams | `dotnet test tests/Elsa/Workflows/Runtime/Tests/Elsa.Workflows.Runtime.Tests.csproj` and Groundwork tests — v1 authority canonicalization/bounds; actual outer D1 dispatch authority plus ambient nesting; all six router results including claimed-but-durable exactness; immutable original versus mutable current fence/revision; exact-set adoption success, idempotent replay, every rejection, and injected all-member rollback/no-dispatch for source-bound and source-free routes; exact three-member D1 Prepared identity/token/provenance/order/source assertions; current 27-caller/21-file matrix plus separate provider-atomic prepared-fold gate; fingerprint/fold/compaction; explicit skipped/failed orders; unchanged after-enrichment Immediate override; qualifying scheduler-only import/continued session; nonqualifying deactivation; live-overlay delayed acknowledgement plus crash-before/after-inline ordinary durable-redrive convergence; skip-with-work; six-status paging; full T027 recovery non-regression. `CoalescingInspectionReadTests` preserves its original durable pass-through and byte-identity objectives while adding direct RED/protective coverage for active logical-projection precedence, successful Immediate/fold update ordering, stale-baseline invalidation, both control settings, failed/conflict/ownership-lost/replay/skip behavior, cap/quiescence/deactivation, and crash/replay reconstruction. T029e remains a separate live-carrier resolution. |
| Fusion continuity (T029c–T029d pending) | Preserve the original spec-123 tests as unchanged guardrails, not rewritten substitutes: all five ON/OFF byte-identical shapes (straight line, multi-outcome branch, fan-in join fallback, suspend/resume, External); non-vacuous D1 `FusedSpans` and D2 `InlineCascadeDispatches` counters plus join-fallback counter; D1-only counter separation; and all eight original Groundwork kill ordinals `2,3,4,7,9,10,11,12`, including D2→D1 recursion. The current 5 PASS / 6 RED Activity result is reconciled through reviewed RED → implementation → verification gates. The six guardrails continue to compare durable checkpoint/state documents while excluding only transient intent/outbox transport. The two authority PASS cases prove exact pump ownership and resumption-barrier isolation; they do not replace original convergence, join, External-parent, suspend, or no-fusion coverage. |
| API integration | `dotnet test tests/Elsa/Workflows/ExecutionEvidence/Api/Tests/Elsa.Workflows.ExecutionEvidence.Api.Tests.csproj` — scopes, disjoint typed/unknown records, correlation-pair/range validation, bounded scan/timeout cursor advancement, cursor reuse/access/filter/page-size binding, lifecycle/integrity outcomes. |
| Architecture/reference | `dotnet test tests/Elsa/Architecture/Elsa.Architecture.Tests.csproj` — Core/API/InMemory direction, server composition, and absence assertions. |
| Server e2e | Rebuild and deploy a fresh enabled server composition, wait for readiness, then run `e2e-tests/execution-evidence/Test-ExecutionEvidenceFoundation.ps1` against the ordinary REST/persistence/runtime path; see [quickstart.md](quickstart.md). |
| Benchmark | `dotnet test benchmarks/Elsa/Workflows/ExecutionEvidence/Benchmarks/Elsa.Workflows.ExecutionEvidence.Benchmarks.csproj --filter "FullyQualifiedName~ExecutionEvidence" --logger "console;verbosity=detailed"` — absent, enabled-unscoped, scoped metadata-only results plus reservation throughput/allocation and canonical-input storage size before/after safe compaction. Record inspection-store reads separately for active/no-projection, active/logical-projection, and outside-session paths. The existing fixed 7-versus-4 test-fixture count is superseded by explicit per-fixture read-count assertions: no-projection ON memoizes and OFF reads per call; logical-projection reads return identical projections in both modes, while OFF may retain a diagnostic durable read without changing the returned projection. No numeric production budget is invented. |
| Plan/OpenAPI | `git diff --check`; YAML parse; Redocly/OpenAPI lint or the repository-provided equivalent; `.specify/scripts/bash/check-prerequisites.sh --json --paths-only`. |

After T029a–T029d complete, the final regression gate also runs every project named by spec 123 SC-008, without filters or substitutions. T030 may not start before those prerequisites are separately authorized and complete. The known full Groundwork baseline remains 844/853 with nine pre-T029 REDs until T029d records the unfiltered/kill-gate result; it must not be described as green:

```bash
dotnet test tests/Elsa/Workflows/Runtime/Tests/Elsa.Workflows.Runtime.Tests.csproj
dotnet test tests/Elsa/Activities/Runtime/Tests/Elsa.Activities.Runtime.Tests.csproj
dotnet test tests/Elsa/Activities/Flowchart/Tests/Elsa.Activities.Flowchart.Tests.csproj
dotnet test tests/Elsa/Activities/Sequence/Tests/Elsa.Activities.Sequence.Tests.csproj
dotnet test tests/Elsa/Activities/ControlFlow/Tests/Elsa.Activities.ControlFlow.Tests.csproj
dotnet test tests/Elsa/Activities/Bpmn/Tests/Elsa.Activities.Bpmn.Tests.csproj
dotnet test tests/Elsa/Persistence/Groundwork/Tests/Elsa.Persistence.Groundwork.Tests.csproj
dotnet test tests/Elsa/Workflows/Publishing/Api/Tests/Elsa.Workflows.Publishing.Api.Tests.csproj
dotnet build Elsa.Server.slnx
```

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
| Source-aware routing and explicit fence adoption | Accepted source redrive and genuinely source-independent recovery require different progression authorities while sharing one replayer/provider CAS. | Global pre-dispatch fold strands D1; silent token/fence refresh loses ownership truth; D1/provider-specific branching reverses dependency direction. |
| Generic coalescing override | Context and post-commit work need a physical atomic boundary while remaining Evidence-agnostic. | An Evidence-specific Runtime branch or inspecting pre-enrichment state fails isolation/correctness. |
| Durable scheduler-continuation handoff | A generic post-commit scheduler boundary must remain an honest Immediate commit without ending safe same-drain locality. Exact Pending rows plus live-overlay delayed acknowledgement retain crash authority; after loss, durable redrive enqueues idempotently then acknowledges. | Direct handler invocation, duplicate intent/work, memory-only early `Delivered`, or disabling the override loses durability or changes accepted source semantics. |
| Reservation/freeze reconciliation | Association admission must remain truthful across owner drains, retries, and freeze timing. | Mutating a session before/after an untracked Runtime call creates ghosts or drops committed winners. |
| Frozen terminal-cutoff lifecycle | A session cannot safely report completed range while a frozen workflow can still commit. | Idle/quiescent/suspended is temporary and explicitly insufficient. |
