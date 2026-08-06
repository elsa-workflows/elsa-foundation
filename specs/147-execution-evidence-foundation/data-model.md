# Data Model: Execution Evidence Foundation

**Status:** Plan-reviewed protocol/model design. The Path A recovery models and durable scheduler-continuation handoff are approved. The T029 construction-time logical-inspection-projection plan, tasks, and RED packet passed independent and control-room review. T029a produced 24 tests: 14 protective PASS / 10 intentional RED; materialized-source lifecycle is 8/8 PASS, and the unchanged guardrails are 1/7 PASS with six canonical `FirstCheckpointId` REDs. Independent RED review passed with no P0–P3 finding and the control room approved it. T029a/T029b are complete; T029c is authorized in exactly `CoalescingRuntimeCheckpointCommitStore`, `RuntimeCoalescingSession`, and `CoalescingRuntimeStateStores`, while implementation and T029d remain pending. This records no Draft-constitution or proposed-ADR ratification. The in-memory provider is not a database schema or a durability claim.

## Generic Runtime provenance and ledger (owned by Runtime)

| Model | Required state and invariant |
|---|---|
| `RuntimeExecutionContextSnapshot` | Immutable, bounded, versioned, canonically ordered opaque entries. Runtime validates only generic bounds and copies it unchanged. |
| `RuntimeCheckpointProvenance` | The snapshot plus positive `WorkflowCheckpointOrder`, assigned by generic prepare before every baseline checkpoint reaches enrichers and reused on replay. |
| `RuntimeCheckpointRecoveryAuthority` | Optional declaration captured only from a scheduler dispatch: protocol `1`, kind `runtime.scheduler-work`, workflow execution ID, durable `WorkItemId`, and `sha256:` canonical work-item fingerprint. Bounds/canonicalization are fixed in research. Presence requires accepted source redelivery; null at original preparation alone permits source-independent replay. A declared value is immutable and never becomes null because lookup later fails. |
| `RuntimeCheckpointPreparationIdentity` | Immutable `CommitId`, stable durable `LedgerToken`, workflow identity, provenance/order, original preparation fence, original expected order/context revisions, exact recovery authority, canonical input reference/digest/fingerprint, and candidate disposition. |
| `RuntimeCheckpointAuthorityBinding` | Mutable current authority fence plus positive provider CAS revision. It starts from the original fence. Exact-set adoption may advance it to a strictly newer current-owner fence; exact same-current replay is idempotent. It is not part of the preparation identity. |
| `RuntimeLogicalCheckpointLedgerEntry` | Durable bounded canonical input reservation containing preparation identity, current authority binding, raw checkpoint/state changes, requested context mutation, and status. `Prepared` exists before enrichment; terminal status requires an explicit successful outcome. It is not a checkpoint, Evidence record, context attachment, state/outbox write, progression authority, or durable enriched payload. Only terminal success permits compaction. |
| `RuntimeCheckpointPreparedAdoptionRequest` | One workflow/route, inclusive `ThroughWorkflowCheckpointOrder`, a strictly newer target fence, and a complete ordered member list. Each member repeats immutable identity, exact authority, original fence/revisions, and expected current fence/CAS revision. Source-bound scope is every same-authority `Prepared` member through the bound; source-free scope is every `Prepared` member from the first nonterminal order through it. Provider comparison is exact-set and one atomic CAS for either route. |
| `WorkflowExecutionState.CheckpointOrderHighWatermarks` | Durable committed and reserved order high-watermarks. A fresh logical checkpoint reserves one monotonic order; a fold commits entries in order; replay returns the stored order. |
| `RuntimePostCommitOutboxItem.WorkflowCheckpointOrder` | The logical committed checkpoint order copied from its source ledger/provenance, used by generic status reads and cutoff reconciliation. |
| Active-session durably persisted continuation set | Internal coalescing-session index of the exact `OutboxItemId` values and committed row values imported after a qualifying Immediate commit. Eligibility requires empty context snapshot, no context mutation, and a nonempty outbox containing only `EnqueueSchedulerWork`. The set does not create a second durable row, intent, or work item and is not progression authority. |
| Active-session logical inspection projection | Internal map from activity execution ID to the ordered `ActivityExecutionInspectionProjection` built with existing merge semantics. An accepted Deferred contribution enters it immediately after buffer acceptance; a new Immediate/fold trailing contribution enters it only after new successful `Committed` finalization. It takes precedence over a durable baseline only in the matching active session, creates no durable record or public contract, and is discarded on crash/deactivation/quiescence. |

The generic context entry carrying an Evidence association is opaque to Runtime. It is bounded, versioned, write-once by generic `AttachIfAbsent`, and is not a Runtime constant, configuration field, or model branch. `RuntimeCheckpointCommitFingerprint` includes the full provenance, so altered order/context is a replay conflict.

The authority fingerprint covers the immutable scheduler item in fixed order: work-item/workflow/command/envelope/idempotency identities, numeric command kind, UTC-tick times, nullable sequence, execution scope, attempt lineage, canonical payload, and ordinal-keyed metadata. Identity/lineage strings are at most 450 UTF-16 code units; each metadata map has at most 64 entries with 128-unit keys and 4096-unit values; payload is at most 256 KiB canonical UTF-8/depth 64; total canonical material is at most 512 KiB. JSON objects sort recursively by ordinal property name, arrays retain order, validated numbers use minimal JSON, strings are not Unicode-normalized, and the hash is domain-separated by `elsa.runtime.scheduler-work-authority:v1`.

`WorkflowSchedulerDrainer` opens the authority accessor from the actually acquired durable item around dispatch. Nested D2/D1 calls inherit that scope. Only `RuntimeCheckpointCommitter` reads it into preparation; ordinary checkpoint callers and providers cannot populate or infer it.

For coalescing, a context-free logical checkpoint has a durable `Prepared` canonical-input reservation before enrichment even though full persistence is deferred. If the active dispatch has an accepted durable redelivery source, Runtime records that generic authority on every inline preparation; a D1/D2 fused dispatch therefore binds its whole unflushed prefix to the original durable `ScheduleActivity`. Pre-dispatch routing returns exactly `Absent`, `Exact`, `Missing`, `FingerprintMismatch`, `UnsupportedVersionOrKind`, or `Ambiguous`. Exact-key lookup or bounded keyset paging observes claimed-but-still-durable items. Only original null authority returns `Absent`; missing or invalid declared authority never creates source-independent eligibility.

Both an `Exact` source-bound set and a contiguous source-free `Absent` prefix use the same exact-set adoption request before dispatch/replay. Every member must be present once, in order, with one route authority and one expected current fence. Missing, extra, duplicate, partial, mixed authority/current fence, stale, downgrade, or unauthorized requests fail atomically. Adoption changes only current fence/CAS revision: it never changes original fence/revisions, rotates `LedgerToken`, changes canonical input/authority/provenance/order, advances a high-watermark, writes state/context/outbox/marker/receipt, or compacts input. An injected provider failure rolls back all members and permits no dispatch/replay/fold. Source-bound success leaves entries `Prepared` for normal source redelivery; source-free success may rehydrate/fold. Terminal fold writes explicit outcomes without renumbering; no error is inferred as `Skipped`/`Failed`. The generic after-enrichment Immediate override remains unchanged.

After a qualifying Immediate CAS, its checkpoint/state and scheduler-continuation rows are already durable. The active session applies that durable boundary state to its overlay, imports only the exact committed Pending rows, marks their IDs durably persisted, advances/consumes the current scheduler source with cap-flush semantics, and remains active. Existing outbox delivery may enqueue/consume the continuation in the overlay, but the durable row stays Pending while the downstream effect is memory-only. A later successful checkpoint/fold first incorporates that effect durably; only then may reconciliation transition the original row to Delivered. Crash before inline dispatch and crash after inline dispatch but before that commit/fold both retain the same durable Pending authority and idempotent redrive identity.

The active-session logical inspection projection is a construction overlay, not durable state. `FindAsync` returns it before any durable baseline while the session owns the workflow, so equivalent Deferred and Immediate logical contributions select the same next `FromState`/merge input. The Deferred contribution publishes immediately after buffer acceptance. Immediate/fold trailing contributions publish only after a new successful `Committed` result; already buffered fold members are not re-applied. Successful durable finalization invalidates the baseline memo. Failure, conflict, ownership loss, exception, replay, skip, and nonqualifying deactivation do not publish a candidate; cap adds only a successfully persisted trailing contribution. Outside the active session, or after crash/deactivation/quiescence, reads are durable pass-through and replay starts from durable truth. `CoalesceInspectionReads` controls only baseline-read locality: ON may memoize without a projection; OFF reads per call and may retain a diagnostic durable read when a projection is returned.

Any context snapshot/mutation, non-scheduler or mixed outbox, delivery failure, or terminal/no-continuation boundary deactivates the session. Arbitrary/external handlers use ordinary durable outbox processing. No state transition invokes a handler directly, creates a duplicate intent/work item, acknowledges delivery early, or inspects Evidence, D1/D2, fusion mode, or recovery authority.

### Runtime recovery transitions

```text
Prepared(original identity + current binding)
  -- route Missing/FingerprintMismatch/Unsupported/Ambiguous --> unchanged, no dispatch
  -- route Exact + exact-set adopt newer fence --> Prepared(updated binding) -- normal source redelivery --> terminal
  -- route Absent + exact-set adopt newer fence --> Prepared(updated binding) -- shared replay/fold --> terminal
  -- exact same-current adoption replay --> Prepared(same binding/receipt)
  -- any adoption mismatch or injected failure --> every member unchanged, no dispatch/replay/fold
```

### Qualifying Immediate scheduler-continuation transitions

```text
Immediate CAS commits checkpoint/state + exact EnqueueSchedulerWork rows(Pending)
  -- eligible active session --> import exact rows + mark durably persisted
                             --> apply durable boundary + cap-flush source advance
                             --> overlay outbox/queue/D2 dispatch (durable row still Pending)
                             --> later checkpoint/fold incorporates inline effect
                             --> reconcile original durable row Delivered
  -- crash before/after inline dispatch --> same durable Pending row redrives idempotently
  -- context, mixed/external outbox, delivery failure, terminal/no continuation --> deactivate
```

The T024/T025 model proof stops at actual outer D1 authority capture plus ambient stack restoration. The T028/T029 model proof owns this transition system, including the actual shipped D2→D1 re-entry retaining that outer authority and all crash/reconciliation branches.

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

`EvidenceCaptureProfile` can name `captured`, `redacted`, `omitted`, and `truncated`, but #1133 creates neither values nor disposition instances. No evidence input, output, state, payload, exception, arbitrary checkpoint metadata, retention/TTL policy, Evidence-query-store recovery, shared conformance fixture, or J-Test type is modeled here. Generic Runtime prepared recovery is checkpoint correctness, not Evidence persistence.
