# Research: Execution Evidence Foundation

**Status:** Draft design input; decisions below implement the approved spec and are not ratification records.

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

## Decision: coalescing retains a logical-checkpoint ledger

**Decision:** `CoalescingRuntimeCheckpointCommitStore` gains a minimal durable workflow-local logical-checkpoint ledger plus committed/reserved high-watermarks. Before enrichment, each proposal writes a `Prepared` reservation containing `(logical CommitId, order, provenance/context fingerprint, bounded canonical stable source/operation identity, raw `RuntimeCheckpoint`, pre-enrichment state-change set, requested context mutation, expected revisions/fence, input fingerprint, status)`. The order is computed from the durable base plus its buffered ordinal. This reservation is separate from the later full checkpoint state/outbox write and is therefore a stable order for the logical checkpoint, not a synthetic fold order or a context attachment.

The buffered segment may be rehydrated after a crash from durable reservation input: recovery verifies the input fingerprint, reattaches stored provenance/order, reruns deterministic enrichers, and continues policy/commit/fold without assuming any scheduler source can be re-driven. A fold CAS-persistes every reservation’s `Committed` marker, high-watermark, state, and outbox in allocated order; after a safe fold it may compact canonical input to an immutable receipt/marker. A duplicate after the fold finds the persisted marker/ledger and returns the same receipt. Skip/failure becomes non-committed `Skipped`/`Failed` (or is deterministically reconciled from `Prepared`): it exposes neither checkpoint, outbox, association, nor evidence, but consumes an internal order. Orders are monotonic and deliberately not contiguous; #1134 owns gap semantics.

The committer must inspect the **enriched** generic commit, including outbox work folded from `PostCommitIntents`, before honoring `Deferred`. A non-empty snapshot, a context mutation, or any generic post-commit work forces an immediate physical commit. This preserves atomicity for Evidence without teaching Runtime about Evidence. Context-free checkpoints may still coalesce, but their own ledger provenance remains durable and replay-stable before enrichment.

**Rationale:** Current coalescing correctly unions buffered state changes and `PostCommitOutbox` at fold, so the plan must not describe those outbox items as dropped. The missing contract is per-logical-checkpoint durable/replay-stable provenance/order; a canonical-input reservation write per logical checkpoint closes that gap without weakening FR-002/SC-003. It may approach logical checkpoint payload size, so benchmark storage as well as throughput/allocation; correctness outweighs the coalescing benefit.

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

The implementation records the 28 direct Runtime committer callers in 22 production files and uses it as a no-bypass regression inventory. It adds a domain README, `EXTENSION_POINTS.md`, root entry, host composition reference, and an authorized narrow map refresh. Groundwork/full lifecycle/value capture/UI/shared protocol fixtures/J-Test remain out of scope.
