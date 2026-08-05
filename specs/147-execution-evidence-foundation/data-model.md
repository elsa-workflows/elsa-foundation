# Data Model: Execution Evidence Foundation

**Status:** Draft protocol/model design. The in-memory provider is not a database schema or a durability claim.

## Generic Runtime provenance and ledger (owned by Runtime)

| Model | Required state and invariant |
|---|---|
| `RuntimeExecutionContextSnapshot` | Immutable, bounded, versioned, canonically ordered opaque entries. Runtime validates only generic bounds and copies it unchanged. |
| `RuntimeCheckpointProvenance` | The snapshot plus positive `WorkflowCheckpointOrder`, assigned by generic prepare before every baseline checkpoint reaches enrichers and reused on replay. |
| `RuntimeCheckpointPreparationToken` | `CommitId`, logical-ledger token, provenance, expected order/context revisions, expected ownership fence, canonical input fingerprint/reference, and candidate disposition. It cannot be used after a fence/revision conflict without preflight. |
| `RuntimeLogicalCheckpointLedgerEntry` | Durable bounded canonical input reservation: `(logical CommitId, order, provenance/context fingerprint, stable source/operation identity, raw RuntimeCheckpoint, pre-enrichment RuntimeCheckpointStateChangeSet, requested context mutation, expected revisions/fence, input fingerprint, status)`. `Prepared` exists before enrichment; `Committed`, `Skipped`, or `Failed` is set by the later outcome. It is not a checkpoint, Evidence record, context attachment, state/outbox write, or durable enriched payload. After safe commit it may compact its input to an immutable receipt/marker. |
| `WorkflowExecutionState.CheckpointOrderHighWatermarks` | Durable committed and reserved order high-watermarks. A fresh logical checkpoint reserves one monotonic order; a fold commits entries in order; replay returns the stored order. |
| `RuntimePostCommitOutboxItem.WorkflowCheckpointOrder` | The logical committed checkpoint order copied from its source ledger/provenance, used by generic status reads and cutoff reconciliation. |

The generic context entry carrying an Evidence association is opaque to Runtime. It is bounded, versioned, write-once by generic `AttachIfAbsent`, and is not a Runtime constant, configuration field, or model branch. `RuntimeCheckpointCommitFingerprint` includes the full provenance, so altered order/context is a replay conflict.

For coalescing, a context-free logical checkpoint has a durable `Prepared` canonical-input reservation before enrichment even though full checkpoint state/outbox persistence is deferred. The store computes its order from the durable base plus buffered ordinal but does not attach/write context at reservation time. Recovery loads that input, verifies its fingerprint, reattaches the stored provenance/order, reruns deterministic enrichers, and continues decide/commit/fold without re-driving a scheduler source. At fold, one CAS writes `Committed` markers, high-watermark, state, context where applicable, and the unioned `PostCommitOutbox` without changing any logical order; safe committed entries may compact to receipts/markers. Post-fold duplicate `CommitId` reads the committed ledger/marker. Skip/failure consumes an internal order but exposes no committed checkpoint, outbox, association, or evidence; orders are monotonic rather than contiguous and #1134 owns gap semantics. The generic committer overrides deferral to immediate for an enriched commit with non-empty/mutating context or any post-commit work. Reservation input may approach logical checkpoint payload size and is benchmarked for storage, allocation, and throughput.

## Evidence session aggregate

```text
EvidenceSession
 ├── identity + owner access/tenant snapshot
 ├── metadata-only capture profile + bounded correlation
 ├── lifecycle: Open → Frozen → Completed | terminal integrity failure observation
 ├── reservation and association operation receipts
 │    └── workflow id, operation key, Runtime fence/receipt, context fingerprint, effective order
 ├── frozen association/reservation set + terminal cutoffs
 ├── deterministic batch/record id indexes
 ├── materialization and duplicate observations
 └── reconciled generic-outbox integrity
```

| Model | Fields / invariant |
|---|---|
| `EvidenceSessionId` | Opaque, nonblank, never reused after whole-session deletion. |
| `EvidenceSession` | Captures owner scope at creation. Every later operation must match that scope. `Frozen` blocks new reservations and retains pre-freeze unresolved reservations until authoritative resolution. |
| `EvidenceAssociationReservation` | Created before Runtime start/attach dispatch. Includes idempotency operation key, target/workflow identity when admitted, session context fingerprint, expected scheduler fence, and `Pending`, `Committed`, `Rejected`, or `Failed` outcome. |
| `EvidenceAssociation` | One session per workflow in this slice. `Starting` is an admitted but not first-checkpoint-committed start; `Active` has generic context fingerprint and effective order; `Frozen` is included in a frozen set. Mutable session state is never enricher input. |
| `RuntimeAssociationReceipt` | Generic authoritative admission/attach/checkpoint receipt used to reconcile uncertain retry and remove `Starting` on recorded failure. |
| `SessionWorkflowCutoff` | Present only for a frozen, committed association after Runtime reports a committed terminal workflow checkpoint. Includes observed-through order and terminal workflow status. Suspension/idleness is invalid. |
| `EvidenceIntegrityState` | Reconciled `IncompleteDelivery`, `TerminalIntegrityFailure`, or delivery-settled state. Also reports process-local limitation and duplicate suppression. It never claims gap-free completeness or a definitive negative. |

### Association and completion transitions

```text
Open --reserve/start admitted--> Starting --first checkpoint commits--> Active
Open --reserve/late attach commits--> Active(effective order)
Open --complete--> Frozen(including pending reservations)
Frozen --pending reservation resolves committed--> Frozen(active association included)
Starting or reservation --authoritative Runtime failure/skip/reject--> no association
Frozen --all terminal cutoffs + Delivered intents--> Completed
Frozen --pending/delivering/retryable--> Frozen + IncompleteDelivery
Frozen --failed final/cancelled--> Frozen + TerminalIntegrityFailure
Completed --delete as one unit--> removed
```

The session gate serializes reserve/freeze/finalize operations, but Runtime is linearized by its workflow-owner scheduler fence. Attach waits behind active drain and CAS-checks absent entry, fence, context revision, order revision, ledger/marker token, state, and outbox. Two session reservations targeting one workflow receive one generic attach winner; uncertain retry uses the same operation key and receipt. If freeze follows Runtime commit but precedes Evidence finalization, the earlier reservation makes the committed winner part of the frozen set. Failed/skipped/rejected runtime work resolves to no association. A still-unresolved admission remains `Starting`/incomplete until the authoritative Runtime result, then cannot remain a ghost.

## Catalog, batch, and evidence record

| Model | Required state / invariant |
|---|---|
| `EvidenceKindDescriptor` | Stable dotted kind, positive schema version, typed payload contract, metadata-only capture metadata. Canonical startup order is `(kind, schemaVersion)`; duplicate/conflicting descriptors fail. |
| Baseline catalog | `workflow.started@1`, `workflow.completed@1`, `activity.started@1`, `activity.completed@1` only. All payloads contain committed identifiers/transition metadata, no values. |
| `EvidenceBatch` | At most one nonempty bounded batch per eligible committed logical checkpoint. Stable `BatchId`, `IntentId`, full provenance, and canonically sorted records. |
| `EvidenceRecordEnvelope` | `RecordId`, session/kind/schema identity, workflow id, `WorkflowCheckpointOrder`, `CheckpointOrdinal`, checkpoint id, diagnostic occurred time, optional activity/causation/subject IDs, bounded correlation, and payload. Workflow-local semantic order is exactly `(WorkflowCheckpointOrder, CheckpointOrdinal)`. |
| Typed record / registered-unknown envelope | A required wire `recordShape` is respectively `typed` or `registered-unknown`. The typed shape is discriminated by the four known `kind` values; the unknown shape has opaque payload but a registered kind/schema. |

For one `CommitId`, IDs are domain-separated SHA-256 values over stable commit identity, fixed v1 discriminator, immutable provenance, canonical descriptor data, and ordinal. A batch’s `RecordedAt` is copied from the checkpoint. No source may use generated time, random data, a mutable session lookup, store-assigned ID, or delivery attempt. A duplicate record ID is a successful idempotent materialization, not a new fact.

## Runtime outbox reconciliation contract

`RuntimePostCommitOutboxStatusReadRequest` is generic Runtime Core input:

| Field | Rule |
|---|---|
| `WorkflowExecutionId` | Required nonblank execution scope. |
| `IntentKind` | Required nonblank generic Runtime kind. Evidence uses only its own intent kind. |
| `Statuses` | Canonical nonempty subset of `Pending`, `Delivering`, `Delivered`, `FailedRetryable`, `FailedFinal`, `Cancelled`; omitting means all six. |
| `ThroughWorkflowCheckpointOrder` | Optional positive inclusive upper bound. |
| `PageSize` | Positive and bounded; provider implementations must not substitute an unbounded scan. |
| `Cursor` | Opaque continuation bound to version, workflow, kind, normalized status set, upper bound, page size, and last `(order, outboxId)` key. |

The result exposes only safe generic status observations and is ordered by `(WorkflowCheckpointOrder, OutboxItemId)`. It returns no provider offset. Cursor malformedness, deleted/expired state, or a mismatched filter/page size is an explicit error, never a widened query.

## Query and wait

`EvidenceQuery` and `EvidenceWaitRequest` share normalized filters: session, kind, workflow, activity, subject, correlation pair, and optional checkpoint-order range. Correlation key/value must be supplied together; a `from > to` range is invalid. Both have a bounded `PageSize` and scan a deterministic candidate stream ordered `(workflowExecutionId, WorkflowCheckpointOrder, CheckpointOrdinal, RecordId)`.

`EvidenceCursor` binds protocol version, session, authorization-scoped tenant/access identity, normalized filters, page size, and last **examined** stream key. A response returns at most `PageSize` matches. Its `nextCursor` advances to the last candidate examined even if that candidate was a nonmatch or a deadline ended the wait, so continuation is exact and cannot repeat or skip data. Deleted/malformed/mismatched cursors are errors.

`EvidenceWaitResult` is exactly one of:

- `matched` — at least one matching materialized record after the supplied cursor;
- `timed-out-inconclusive` — deadline elapsed after a bounded scan;
- `incomplete-delivery` — frozen range still has Pending/Delivering/FailedRetryable intent;
- `terminal-integrity-failure` — FailedFinal or Cancelled intent is in range; or
- `completed-range-without-match` — every frozen workflow has a terminal cutoff and every relevant intent through it is Delivered.

The last outcome is observable but is not #1134’s settled/gap-free/definitive-negative claim.

## Out of scope extension room

`EvidenceCaptureProfile` can name `captured`, `redacted`, `omitted`, and `truncated`, but #1133 creates neither values nor disposition instances. No input, output, state, payload, exception, arbitrary checkpoint metadata, retention/TTL policy, recovery, shared conformance fixture, or J-Test type is modeled here.
