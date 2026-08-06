# Research: Execution Evidence Foundation

**Status:** Review-pending design input; the Path A recovery-authority decision is approved, while the durable scheduler-continuation amendment below awaits plan review. These decisions implement the approved spec and are not ratification records.

## Decision: use four projects and explicit host composition

**Decision:** Add these projects under `src/Elsa/Workflows/ExecutionEvidence/`:

1. `Core/Elsa.Workflows.ExecutionEvidence.Core.csproj` — provider-neutral public contracts and typed wire/domain models only.
2. `Elsa.Workflows.ExecutionEvidence.csproj` — provider-neutral session lifecycle, catalog, capture, Runtime adapters, and delivery-pump feature.
3. `InMemory/Elsa.Workflows.ExecutionEvidence.InMemory.csproj` — the single #1133 process-local provider and registration leaf.
4. `Api/Elsa.Workflows.ExecutionEvidence.Api.csproj` — FastEndpoints transport only.

`ExecutionEvidence` references `Core`, `Elsa.Workflows.Runtime.Core`, and `Elsa.Tasks.Core`. `InMemory` references `Core` and the base implementation. `Api` references `Core`, the base implementation, `Elsa.Api.FastEndpoints`, and `Elsa.Api.Capabilities`; it has no `InMemory` reference. `Core` has no ASP.NET, Runtime implementation, provider, test-framework, or concrete-store dependency.

The three activatable feature IDs are `WorkflowsExecutionEvidence`, `WorkflowsExecutionEvidenceInMemory`, and `WorkflowsExecutionEvidenceApi`. The server adds direct references and `WithAssemblies` entries for each feature assembly because feature-name dependencies do not make an absent assembly discoverable. The normal host shape is base + InMemory + API. Omitting all three leaves Runtime unchanged; API must never imply an InMemory provider.

**Alternatives rejected:** the issue/PRD’s older three-project shape, a Core-owned default store, a provider umbrella, API-to-InMemory coupling, and silently enabling the provider from the API.

## Decision: every enriched checkpoint has generic durable provenance

**Decision:** Extend `Elsa.Workflows.Runtime.Core` with a generic `RuntimeCheckpointProvenance` on `RuntimeCheckpoint`:

- a bounded, versioned, canonical immutable `RuntimeExecutionContextSnapshot` of opaque entries; and
- a positive `WorkflowCheckpointOrder`.

Runtime validates generic entry bounds/version/order only. It does not reserve or parse an Execution Evidence entry, reference an evidence type, add an evidence setting, or branch on an Evidence value. Execution Evidence serializes its `EvidenceSessionId` and approved correlation data into one opaque domain entry and alone understands it.

All `RuntimeCheckpointCommitter` callers use one provider-neutral prepare/commit flow. Four baseline fact producers remain distinct (`WorkflowCheckpointSchedulerWorkHandler`, `WorkflowScheduleActivitySchedulerWorkHandler`, `WorkflowStartActivitySchedulerWorkHandler`, and the direct mandatory path in `WorkflowStartSchedulerWorkHandler`), but no caller allocates provenance/order itself.

| Phase | Provider-neutral behavior |
|---|---|
| Preflight | `PrepareAsync` reads a persisted `CommitId` marker and logical-checkpoint ledger before allocation. Matching replay returns stored provenance/order; mismatched canonical input or fingerprint conflicts. |
| Prepare | A new request obtains `RuntimeCheckpointPreparationToken`: logical ledger token, provenance/order, expected order/context revisions, execution ownership fence, canonical input fingerprint, and initial persistence mode. The store CAS-persists a `Prepared` ledger reservation before an enricher sees it. It stores bounded canonical generic input: stable source/operation identity, raw `RuntimeCheckpoint`, pre-enrichment `RuntimeCheckpointStateChangeSet`, requested context mutation, provenance/order, revisions/fence, and fingerprint. It does not write checkpoint state, outbox, or context. |
| Enrich | `RuntimeCheckpointCommitter` applies generic provenance, then invokes enrichers in deterministic registration order. Evidence reads only the immutable prepared commit after this generic step. Deferred enriched state/outbox stays in the coalescing buffer and is deterministically recomputed from the durable reservation input after a crash. |
| Decide | Persistence policy evaluates the enriched commit. The committer changes any `Deferred` decision to `Immediate` when the enriched commit has a non-empty generic snapshot, any context mutation, or post-commit outbox work. `Skip` with post-commit work becomes `SkipHasPostCommitWork`. |
| Commit/fold | `CommitPreparedAsync` CAS-checks fence/order/context revisions, then atomically writes high-watermark, context, marker, logical ledger, state, and outbox. It returns a committed/skipped/replay/conflict/ownership-loss receipt. |
| Retry | A revision/fence conflict returns to preflight. Only a matching ledger token can be reused; the same `CommitId` never gets a new order. |

`RuntimeCheckpointCommitFingerprint` includes provenance. A physical immediate commit advances committed order exactly once and writes state/outbox together. A matching replay returns the persisted receipt; a changed provenance conflicts. This is the required generic implementation across InMemory, coalescing, and Groundwork stores.

**Rationale:** `RuntimeSchedulerWorkItem.Sequence` is nullable dispatch metadata and `SchedulerState.Version` is unavailable at several checkpoints. Neither is a durable semantic checkpoint order. FR-002 and SC-003 instead require a generic store-owned order visible before enrichment.

**Alternatives rejected:** timestamps, hashes, checkpoint IDs, session counters, mutable association lookups, scheduler sequence/version, delivery order, and assigning a durable order only when a coalesced buffer folds.

## Decision: coalescing retains a logical-checkpoint ledger without replacing source recovery authority

**Decision:** `CoalescingRuntimeCheckpointCommitStore` gains a minimal durable workflow-local logical-checkpoint ledger plus committed/reserved high-watermarks. Before enrichment, each proposal writes a `Prepared` reservation containing immutable `(logical CommitId, stable LedgerToken, order, provenance/context fingerprint, bounded canonical stable source/operation identity, optional recovery authority, raw RuntimeCheckpoint, pre-enrichment state-change set, requested context mutation, original order/context revisions and fence, input fingerprint)` plus the separate mutable `(current authority fence, authority CAS revision)` binding and status. The order is computed from the durable base plus its buffered ordinal. This reservation is separate from the later full checkpoint state/outbox write and is therefore a stable order for the logical checkpoint, not a synthetic fold order, context attachment, or universal progression authority.

Recovery first preserves any accepted source-domain redrive contract. The concrete declaration is
`RuntimeCheckpointRecoveryAuthority` protocol version `1`, kind `runtime.scheduler-work`, the workflow execution ID,
the durable scheduler `WorkItemId`, and a `sha256:` lower-case hex fingerprint of the immutable work item. The hash is
domain-separated with `elsa.runtime.scheduler-work-authority:v1` and covers, in fixed property order, the work-item and
workflow IDs, command ID and numeric wire kind, envelope ID, idempotency key, UTC ticks for enqueued/recorded time,
nullable sequence, execution-scope ID, attempt lineage, canonical payload, and ordinal-keyed command/envelope metadata.
Identifiers and lineage strings are nonblank and at most 450 UTF-16 code units; each metadata map has at most 64 entries,
keys are at most 128 and values at most 4096 UTF-16 code units; canonical payload is at most 256 KiB UTF-8 and depth 64;
and the complete canonical fingerprint input is at most 512 KiB UTF-8. JSON objects are recursively property-sorted by
ordinal name, arrays retain order, numbers use their validated minimal JSON representation, strings are emitted without
Unicode normalization, and timestamps use UTC ticks. A value outside these bounds is rejected before preparation; it is
never silently converted to a source-free reservation.

`WorkflowSchedulerDrainer` opens a dispatch-scoped `IRuntimeCheckpointRecoveryAuthorityAccessor` from the actual durable
item returned by claim/list acquisition immediately around handler/pipeline dispatch. The scope is stack-restoring, so the
D2 inline pump and every nested D1 stage inherit the original outer `ScheduleActivity` authority. Only
`RuntimeCheckpointCommitter` reads the accessor and copies the authority into `RuntimeCheckpointPrepareRequest`; checkpoint
callers cannot set it on a commit/request, and providers cannot derive, replace, or interpret it. A checkpoint created
outside scheduler-work dispatch has no declared authority.

Before any adoption, replay, or scheduler dispatch, a Runtime-owned router classifies each durable reservation as exactly
`Absent`, `Exact`, `Missing`, `FingerprintMismatch`, `UnsupportedVersionOrKind`, or `Ambiguous`. `Absent` means the
authority was null at original preparation and is the only source-independent route. `Exact` requires one durable queue
item at `(WorkflowExecutionId, WorkItemId)` with an identical canonical fingerprint. Lookup uses a provider exact-key
inspection when available, otherwise stable bounded keyset pages (maximum 250) whose cursor binds workflow and last queue
order key. This inspection includes a work item that is currently claimed but still durable; delivery visibility is not
source existence. Zero rows is `Missing`, conflicting duplicates are `Ambiguous`, and a different fingerprint is
`FingerprintMismatch`. Every result except `Absent`/`Exact` fails closed. In particular, a declared authority never becomes
`Absent` or eligible for provider progression because its item later disappears, is hidden from ordinary claim selection,
or uses an unknown protocol.

For `Exact`, Runtime adopts the source-bound reservations and then performs no checkpoint replay/fold: normal redelivery of
the source re-enters the ordinary preparation path. Every D1/D2 fused checkpoint therefore remains tied to the original
durable `ScheduleActivity`, whose redelivery recreates the same logical proposals and reuses their immutable preparation
identities. For `Absent`, the current workflow owner may select an exact contiguous source-free prefix for the shared
replayer/fold. The two routes use one provider-atomic `RuntimeCheckpointPreparedAdoptionRequest`, not route-specific update
loops. It carries route, workflow, inclusive `ThroughWorkflowCheckpointOrder`, target current-owner fence, and every ordered
member's `CommitId`, `LedgerToken`, order, canonical digest/fingerprint, original preparation fence/revisions, expected
current authority fence, expected authority CAS revision, and exact recovery authority (null for source-free). For a
source-bound route, the exact scope is every `Prepared` member for that workflow and authority through the bound; for a
source-free route it is every `Prepared` member from the first nonterminal order through the bound. The provider re-reads
that whole selected durable set and rejects missing, extra, duplicate, partial, mixed-authority, mixed-current-fence,
stale, downgrade, or unauthorized input
with zero mutation. A target must be strictly newer than the shared current fence; an exact replay already at that target
is idempotent and returns the same adoption receipt. One successful CAS changes only every member's current authority fence
and authority revision. Original fence/revisions, `CommitId`, `LedgerToken`, provenance, `WorkflowCheckpointOrder`, source
authority, canonical bytes/reference, fingerprints, status, state, context, outbox, markers, high-watermarks, receipts, and
compaction remain unchanged.

Only the source-independent route reconstructs and folds before source dispatch. Its fold uses one provider CAS to persist every explicit terminal member, high-watermark, state, context, and outbox in allocated order. The source-bound route reaches that same terminal mechanism only through normal source redelivery. Only successful terminalization may compact canonical input to an immutable receipt/marker. A duplicate after the fold returns the same receipt. `Skipped`/`Failed` is recorded only from an explicit trusted disposition, never inferred from missing source, digest failure, enrichment failure, cancellation, transient failure, or ownership loss. It exposes neither checkpoint, outbox, association, nor evidence, but consumes an internal order. Orders are monotonic and deliberately not contiguous; #1134 owns gap semantics.

The committer must inspect the **enriched** generic commit, including outbox work folded from `PostCommitIntents`, before honoring `Deferred`. A non-empty snapshot, a context mutation, or any generic post-commit work forces an immediate physical commit. This rule remains unchanged: the continuation design below does not weaken, bypass, or move the generic after-enrichment override. Context-free checkpoints may still coalesce, but their own ledger provenance remains durable and replay-stable before enrichment.

An Immediate commit does not, however, have to terminate an already-active coalescing session when the committed boundary is itself a generic durable scheduler continuation. After a successful Immediate commit, the session may continue only when the committed proposal has an empty execution-context snapshot, no context mutation, and one or more outbox rows whose kind is exclusively `EnqueueSchedulerWork`. The physical commit has already atomically written the checkpoint/state and those exact rows as durable `Pending` authority. The coalescing store imports the exact committed row identities and values into the active session, marks those identities as durably persisted, applies the committed boundary state to the overlay, advances/consumes the current scheduler source with the same rule as a cap flush, and leaves the session active. The existing outbox processor, coalescing outbox/queue overlays, and shipped D2 pump then deliver and consume the continuation; there is no direct handler invocation and no second intent or scheduler work item.

Overlay delivery is deliberately not a durable acknowledgement. A qualifying row remains durably `Pending` while its inline effect exists only in the active session; only a later successful checkpoint or prepared fold that incorporates that effect may reconcile the original row to `Delivered`. A crash before inline dispatch and a crash after inline dispatch both therefore begin ordinary recovery from that same original `Pending` row. After session loss, ordinary durable redrive idempotently enqueues the exact work item to the durable scheduler queue; that durable enqueue incorporates the effect, so the processor may mark the row `Delivered` immediately. A crash between enqueue and that mark leaves `Pending`; the next sweep dedupes the work item and converges `Delivered`. Memory-only continuation never becomes durable truth.

The session deactivates at every nonqualifying Immediate boundary: any non-`EnqueueSchedulerWork` or mixed/arbitrary/external outbox work; any non-empty context snapshot or context mutation, including a context-only commit; continuation delivery failure; or terminal/no-continuation state. Those paths retain the ordinary durable outbox processor and normal redrive behavior. Eligibility is expressed only in generic checkpoint/context/outbox terms; it does not inspect Execution Evidence, D1, D2, fusion mode, scheduler recovery authority, or provider policy.

**T024/T025 versus T028/T029 proof ownership:** the authority/accessor stage proves capture from an actual outer D1 durable `ScheduleActivity` dispatch plus stack-restoring ambient nesting semantics; it does not claim a real D2→D1 execution path. The T028 RED stage owns the qualifying/nonqualifying Immediate-boundary matrix, live-overlay delayed acknowledgement, crash-before/after-inline convergence through ordinary durable redrive, and the actual shipped D2 completion pump re-entering D1 while retaining the outer authority. T029 owns only the minimal generic continuation implementation, expected primarily in `CoalescingRuntimeCheckpointCommitStore` and `RuntimeCoalescingSession`, reusing the existing outbox processor/decorator, scheduler overlay, fusion driver, and queue unless RED evidence exposes a necessary adapter gap. It does not change the committer's override, the preparation replayer, or source-domain progression. The numbered tasks must reflect this split after this plan amendment is reviewed.

**Rationale:** Current coalescing correctly unions buffered state changes and `PostCommitOutbox` at fold, so the plan must not describe those outbox items as dropped. The missing contract is per-logical-checkpoint durable/replay-stable provenance/order; a canonical-input reservation write per logical checkpoint closes that gap without weakening FR-002/SC-003. That reservation is replay input, not a second progression authority. Preserving ADR 0047/spec 123's original-source redelivery keeps D1/D2 crash semantics intact, while explicit source-independent adoption closes the genuinely no-source case without silently accepting an abandoned fence. It may approach logical checkpoint payload size, so benchmark storage as well as throughput/allocation; correctness outweighs the coalescing benefit.

**Alternatives rejected:** globally folding every `Prepared` entry before scheduler dispatch; treating a missing declared source as permission for provider recovery; rotating the durable ledger token during fence refresh; silently accepting the abandoned fence as current; teaching the provider about `ScheduleActivity`, fusion, or Execution Evidence; disabling fusion; creating a synthetic recovery checkpoint/order; deactivating after every Immediate commit; directly invoking a scheduler handler; duplicating an intent/work item; acknowledging memory-only inline delivery before durable incorporation; and adding an Evidence-, D1-, D2-, fusion-, or provider-specific continuation branch.

## Decision: association uses fenced generic attach and evidence reservations

**Decision:** Runtime exposes optional generic start context and `AttachExecutionContextIfAbsentAsync`. Start context flows in the dispatch/scheduler envelope. Late attach is a scheduler command owned by `IRuntimeExecutionOwnershipService`: it waits behind the active owner drain and reaches `RuntimeCheckpointCommitter` with the current fence, rather than directly mutating a provider store.

Attach preparation's `Prepared` reservation does **not** attach generic context. The immediate attach commit CAS checks and writes all of: absent generic entry, expected context revision, expected checkpoint-order revision, expected execution fence, logical ledger/marker token, state, and outbox. Only that committed checkpoint makes the context visible and yields `effectiveFromWorkflowCheckpointOrder`. A competing attach, including one from another Evidence session, gets an explicit conflict because the generic write-once entry exists. Retry after an uncertain response reuses the caller’s operation key and reads the durable Runtime/Evidence receipt; it does not dispatch a second attach.

Evidence reserves association state before dispatch. For associate-and-start, authoritative Runtime admission creates a `Starting` association; the first committed checkpoint promotes it to `Active`. For late attach, a reservation precedes its fenced command. Completion atomically freezes both associations and pending reservations. A reservation created before freeze must resolve: if Runtime commits while Evidence finalization is racing a freeze, the committed winner is finalized into the frozen set; rejected/skipped/failed start or attach resolves to no association. A reservation attempted after freeze is rejected.

If a start is admitted but the first checkpoint fails, its authoritative Runtime failure removes the `Starting` association. If it is not yet authoritatively resolved, it remains truthful `Starting` and makes completion incomplete rather than becoming an invented active association or a permanent ghost. Reconciliation is keyed by admission/operation receipt and removes it on an authoritative failure.

**Rationale:** A session-only lock cannot serialize a Runtime drain, an ownership handoff, or two sessions. The scheduler fence plus provider CAS creates one generic linearization point while session reservations preserve correct freeze behavior around it.

## Decision: deterministic capture, exact atomicity, and delivery

**Decision:** `ExecutionEvidenceCheckpointEnricher : IRuntimeCheckpointCommitEnricher` runs after generic preparation. It reads only immutable checkpoint commit/provenance, canonical catalog, and fixed bounded configuration. When the opaque association entry is absent or the checkpoint has no baseline transition it leaves the commit unchanged. For an eligible checkpoint it:

1. projects only `workflow.started`, `workflow.completed`, `activity.started`, and `activity.completed` v1 facts;
2. sorts candidates by the canonical transition discriminator and affected stable identifiers;
3. assigns zero-based `CheckpointOrdinal`s; and
4. appends exactly one opaque `execution-evidence.batch.v1` post-commit intent.

`EvidenceBatchId`, intent ID, record ID, and canonical payload fingerprint are SHA-256 domain-separated values of stable `CommitId`, fixed discriminator/version, full provenance, catalog kind/schema, and ordinal. `RecordedAt` copies the checkpoint’s time only as diagnostics. Enrichment creates no clock, GUID, randomness, mutable read, or delivery-attempt identity. The intent payload is canonical JSON with ordinal-sorted maps and bounded arrays.

`RuntimeCheckpointCommitter` already enriches before persistence/fingerprinting, folds post-commit intents into `RuntimeCheckpointStateChangeSet`, and rejects `SkipHasPostCommitWork`. The durable reservation makes provenance/order recoverable; deterministic enrichment recreates the buffered bytes across coalescing/replay. An evidence handler runs only after the Runtime commit, may execute at least once, and upserts by `EvidenceRecordId`; failure changes generic outbox state but never rolls back workflow state. The base feature registers both its handler contribution and an `IRecurringTask` driver that invokes the generic processor for the evidence kind.

## Decision: generic outbox-status reader and terminal completion

**Decision:** Add `IRuntimePostCommitOutboxStatusReader` in Runtime Core. Its bounded request requires workflow execution and intent kind, supports normalized subset of the six statuses and optional inclusive checkpoint-order upper bound, accepts a bounded page size, and has an opaque cursor. Pages sort `(WorkflowCheckpointOrder, OutboxItemId)`; cursors bind protocol version, workflow, intent kind, normalized statuses, upper bound, page size, and last key. Each outbox item carries the committed logical checkpoint order.

On completion, the Evidence aggregate atomically freezes associations and reservations. It reads generic terminal-checkpoint observation for each frozen winning workflow. Only a committed terminal workflow checkpoint with observed-through order qualifies; suspension, idleness, and quiescence do not. It reads every status page through each cutoff: `Pending`, `Delivering`, `FailedRetryable` means incomplete; `FailedFinal`, `Cancelled` means terminal integrity failure; only all `Delivered` permits completed lifecycle, completed-range-without-match, and delete.

## Decision: bounded query/wait and wire protocol

**Decision:** Query and wait traverse a deterministic candidate stream sorted `(workflowExecutionId, WorkflowCheckpointOrder, CheckpointOrdinal, RecordId)`. A request `pageSize` is bounded and the response contains at most that many records. The scan advances at most one bounded candidate page per result/wakeup; its `nextCursor` encodes the last **examined** stream key, including a filtered nonmatch or the position reached before timeout. Thus reusing a cursor neither repeats nor skips a candidate.

The opaque cursor binds version, session, normalized filters, caller tenant/access scope, page size, and last examined key. A deleted/malformed/mismatched/reused-with-a-different-page-size cursor fails. Correlation key/value are both present or both absent; `workflowCheckpointOrderFrom > workflowCheckpointOrderTo` is rejected for both query and wait. Wait returns only match, inconclusive timeout, incomplete delivery, terminal integrity failure, or completed-range-without-match; none is a definitive negative.

The OpenAPI 3.1 contract uses actual OAuth2 `elsaPermissions` scopes `execution-evidence.read` and `execution-evidence.manage`. Its top-level `recordShape` discriminator makes `typed` baseline records and `registered-unknown` envelopes disjoint; the typed branch has a nested `kind` discriminator across the four baseline references.

## Decision: governance, scope, maps, and verification

**Decision:** Before ADR amendments, resolve the duplicate number by renaming the still-proposed Evidence durability ADR to 0063 and updating the Execution Evidence PRD, link paths, dossier references, and maps. The Evidence series is then 0052–0061 plus 0063; the JavaScript binding-grammar ADR remains 0062. Submit E2.1 and the planned ADR amendments for architecture review, but do not represent any boundary as ratified until accepted.

The implementation records the current 27 direct Runtime committer callers in 21 production files and uses them as a no-bypass regression inventory. T015's 28-caller/22-file result remains the historical `bd94b3c8d` audit; the former synthetic direct caller is now covered by the separate provider-atomic prepared-fold gate. It adds a domain README, `EXTENSION_POINTS.md`, root entry, host composition reference, and an authorized narrow map refresh. Groundwork/full lifecycle/value capture/UI/shared protocol fixtures/J-Test remain out of scope.
