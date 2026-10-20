# Finite file ETL data model

**Status:** Approved design for implementation. Names are logical model names, not frozen public CLR or storage contracts. [ADR-0087](../../docs/adr/0087-etl-owns-its-execution-domain.md) records the owner-delegated domain adoption.

This model supports only the finite topology in [spec.md](spec.md). The ETL feature owns its runtime persistence and transaction boundaries. The workflow runtime stores only the small typed invocation state and its ordinary checkpoint/outbox records; it does not store ETL rows, batches, partitions, or cursors.

## Records and ownership

| Record | Owner and purpose | Identity and important fields |
|---|---|---|
| `EtlDefinitionDraft` | Design store; editable authoring source. | Tenant/owner, definition ID, draft revision, schema, operation order, finite limits, and binding aliases. It contains no raw host paths, credentials, or mutable source-file content. |
| `PublishedEtlArtifact` | Design store and immutable runtime artifact store; the complete v1 executable pipeline. | Artifact ID/version, canonical content hash, operation/schema/dialect versions, validated source and lookup schemas, exact-key lookup, field mapping, literal filters, routing, finite batch/run budgets and lookup/output expansion bounds, and source/lookup/output aliases. Includes the stable finite topology fingerprint. Studio layout and draft history are excluded. |
| `HostFileBindingRevision` | Host configuration/connector boundary; resolves a permitted alias to a stable input/output revision. | Tenant/security scope, alias, role (`Source`, `Lookup`, `MainOutput`, `ErrorOutput`), immutable host revision, capabilities, and expected SHA-256 for readable source/lookup revisions. Paths and credentials are not serialized in authored artifacts. Output revisions pin an opaque destination and support staged, hash-verified, create-only atomic chunk publication. |
| `RetainedInputSnapshot` | ETL runtime store/object store; an immutable copy used by one run. | Run ID, input role, source alias, resolved host revision, expected and verified content hash, retained snapshot ID, length, dialect/schema version, and logical-record index identity. Created before any partition dispatch. |
| `EtlRun` | ETL runtime store; one native workflow invocation and its durable outcome. | Run ID, tenant/partition scope, stable invocation key, workflow execution ID, pinned workflow artifact/hash, execution node/activity identity, frozen native result address from the actual committed bookmark, ETL artifact ID/version/hash/topology fingerprint, binding aliases, input/lookup snapshot IDs and hashes, pinned output destination revisions/opaque identities, limits, lifecycle state, counters, terminal result ID, and safe diagnostic summary. |
| `EtlPartition` | ETL runtime store; an independently leased contiguous source range. | Run ID + partition ordinal, inclusive start/exclusive end logical-record ordinals, next unaccepted ordinal, lease owner, monotonic fence, lease expiry, revision, and status. At least two non-empty ranges are created for the acceptance case. |
| `EtlBatchReservation` | ETL runtime store; durable bounded work owed before a worker receives it. | Stable batch ID, run/partition, pinned artifact and snapshot hashes, record range, exact record count, deterministic logical byte charge, batch ordinal, reservation state, and immutable input descriptor/hash. Unique by logical range; no lease or worker ID. |
| `EtlOutputChunk` | Host output binding; immutable JSONL effect produced for one batch and route. | Stable chunk ID from batch ID + output role, destination alias, content SHA-256, byte length, record count, and host object reference. Main and error chunks are separate; a chunk is never overwritten or appended concurrently. |
| `EtlManifestEntry` | ETL runtime store; authoritative acceptance record for one batch route. | Run ID, stable partition/batch order, batch ID, route, chunk ID, accepted hash, and acceptance time. Unique by run + batch + route; accepted entries are ordered by stable logical key, not by worker commit timing. |
| `EtlTerminalResult` | ETL runtime store; immutable first-wins run outcome. | Run ID, result ID, outcome (`Succeeded`, `BusinessError`, `Faulted`, `Cancelled`), record counts, v1 diagnostic schema version, safe diagnostic code/summary, settled time, and terminal revision. Once present, no later worker can replace it. |
| `EtlNativeResultAddress` | Run admission and terminal outbox; immutable delivery address captured from the actual committed bookmark/registration. | Tenant/partition, workflow execution/activity/node IDs and executable pin, bookmark/registration ID, scoped resume-target ID, stimulus type/hash, payload type alias/schema version, deduplication policy and stable ETL completion provider ID. The provider ID is a version-1 adapter constant, persisted with the address and unchanged across retries. Not authored input. |
| `EtlTerminalOutboxItem` | ETL runtime store; durable delivery of the result to the waiting native activity. | Stable delivery ID derived from run + terminal result, complete frozen native result address, terminal payload hash, and retry/delivery metadata. The same address/payload identity is used on every retry. |

The CSV dialect, supported types, lookup behavior, transformations, output encoding, and base finite-limit semantics are the explicit defaults in [spec assumptions](spec.md#assumptions). The artifact hash covers the canonical definition and all behavior-affecting versions, including the logical batch and output serialization rules.

## Workflow invocation state

The native ETL activity stores one small, versioned `EtlInvocationState` in typed private state:

| Field | Purpose |
|---|---|
| `InvocationId` / `RunId` | Stable idempotency key generated once for this activity invocation; reused across resume and outbox replay. |
| Tenant and persistence partition | Derived from authorized workflow execution context, never accepted from authored input. Every ETL read/write is scoped by both. |
| Pinned workflow executable identity/hash, executable node ID, workflow execution ID, activity execution ID | Proves which published native node owns this invocation and its bookmark/result delivery. |
| ETL artifact ID/version/hash/topology fingerprint | Pins the executable ETL definition chosen by the published workflow. |
| Approved input, lookup, and output aliases | Binds only host-authorized names. Input and lookup revisions/content hashes are resolved later by ETL run admission, not workflow publication. |
| State schema version | Allows frozen resume-state validation and safe future versioning. |

The invocation state is not an ETL checkpoint. It carries no records, lookup data, batch payloads, cursors, worker lease, or run progress. The first activity attempt returns `Suspend` with this state and a registered typed ETL-result trigger. The ETL start intent is attached only when the actual `BookmarkCreated` checkpoint is committed, alongside the matching suspended state and bookmark. The existing cancellation checkpoint carries the frozen invocation state so ETL can persist a cancellation tombstone even if run admission has not happened. A tombstone wins against a later replayed start intent.

On a terminal trigger, the activity maps success and business error to their distinct declared outcomes, maps infrastructure/conflicting-effect failure to fault, and maps cancellation to cancel. The stable result delivery may be sent more than once; native workflow state accepts the invocation's terminal continuation once. This slice makes no claim that an outbox transport itself sends exactly once.

## Run admission and immutable input identity

1. The committed start intent is keyed by `InvocationId`. Reprocessing it returns the same admission result and does not create a second run.
2. Admission checks the cancellation tombstone, tenant/security partition, workflow executable/node identity, published ETL artifact hash, topology fingerprint, and host alias permissions.
3. The host binding resolves a stable immutable source and lookup revision, each with a required expected SHA-256. The ETL runtime copies the exact revisions into retained snapshots and verifies length/hash before making partitions runnable. A live mutable path without a versioned immutable-revision contract is rejected; hashing a copy of a concurrently changing path is not sufficient.
4. The retained snapshot and identity are stored with the run. Later changes to the original binding do not alter an existing run. A missing retained snapshot or a content mismatch against the pinned run hash refuses resume; the run never silently substitutes a newer binding revision.
5. The source scanner streams logical CSV records and persists the index/range metadata needed for stable logical ordinals and bounded contiguous partitions. It does not materialize the entire CSV into a worker buffer. Validate framing/encoding across the finite input before dispatch; failure settles as a business error without JSONL effects. Duplicate lookup keys also invalidate preparation before dispatch; lookup and transformed output expansion have finite configured bounds.
6. The output binding resolves a stable destination identity once for the run, then creates run-scoped immutable chunks there. Retries keep the recorded destination even if host configuration later retargets the alias. It accepts a stable chunk key and content hash, not an authored path or a shared append offset.

An admission refusal is visible as a business error when it is caused by an unsafe/missing input identity. Infrastructure failures and conflicting output content are faults. The run's v1 diagnostics use frozen stable codes and fields (including source ordinal and field name where applicable), record binding/revision identity when relevant, and never copy source rows or secrets.

## Partition, reservation, and output acceptance

Each `EtlPartition` owns a fixed half-open logical-record range `[StartOrdinal, EndOrdinal)`. `NextUnacceptedOrdinal` is the next record that still requires accepted effects. A lease acquisition or takeover atomically increments a monotonic fence and records owner/expiry. Renew, dispatch authorization, batch acceptance, and cursor changes require the current unexpired fence, expected cursor/revision, and a nonterminal run. Lease expiry makes work eligible for takeover; it does not terminate the old task or reclaim its memory.

The runtime forms a deterministic bounded batch from the next contiguous range. It checks both record and byte ceilings and the run-level outstanding budget before dispatch. The byte charge is measured from the canonical serialized batch handed to the worker, with a configured finite bound for output and diagnostic expansion; actual output must stay within that bound. `EtlBatchReservation` creation and the durable quota increment occur once in one ETL-store transaction. The partition cursor remains at the range start while the batch is owed. A retry of that range reuses the reservation and does not charge the durable quota again. Each live worker attempt must independently acquire its local record and byte permits; those permits are released only when that actual task unwinds, not when its lease expires.

The batch identity and chunk identities are stable across attempts and include the run/pinned artifact/source/lookup identity, partition and logical range. They exclude lease, attempt, and worker IDs. Processing is deterministic for the pinned run. Main and error routes may produce separate chunks; filtered rows produce no chunk. The row and output expansion bounds prevent one lookup or mapping from expanding a bounded input into unbounded output.

Acceptance follows this boundary:

| Step | Durable/physical effect | Recovery rule |
|---|---|---|
| Reserve | Store the batch range, deterministic byte charge, and run-level record/byte quota under one unique logical batch key. | A retry finds and reuses the same owed reservation. A lease timeout cannot delete or refund owed work. |
| Write | Stream bounded main/error JSONL into attempt-private staging files; flush/close and verify the digest, then atomically publish under the final key without overwriting on the declared shared filesystem. | A crash mid-write leaves only staging, never a partial final chunk. Retry uses another staging file; remove abandoned staging only after its writer stopped. A complete unaccepted final chunk is replayed by verifying identical bytes/hash. Different final content for the same effect is refused/faulted. Unsupported publication semantics refuse the binding. |
| Accept | In one ETL transaction, verify the current run/partition fence and expected cursor, record accepted manifest hashes, advance the cursor, mark the batch accepted, and settle its durable reservation. | No cursor passes an unaccepted batch. If the response is lost after commit, rereading the manifest/cursor recognizes acceptance. |

The manifest order key is stable by partition ordinal then batch/range ordinal. It is not worker commit order. Cross-partition physical line order remains unspecified as stated in the spec. The manifest is authoritative: a physical chunk absent from it is not accepted output. A late stale task may finish a physical write, but it cannot add a manifest entry, advance the cursor, alter quotas, or win terminal settlement. The output and ETL database are not one transaction, so physical effects are at least once.

## Lifecycle and terminal races

`EtlRun` lifecycle is `Snapshotting → Running → one terminal state` (`Succeeded`, `BusinessError`, `Faulted`, or `Cancelled`). Snapshot/hash rejection may settle as a business error before partitions are dispatched. A runtime/storage or conflicting-effect failure settles as fault. Row validation/lookup rejection is recorded as deterministic error output while classifiable records continue; any rejected row makes the terminal result `BusinessError`. Success means all source records are accounted for, all output chunks are accepted, all partition cursors reach their range ends, and no row was rejected.

All competing terminal transitions use one ETL-store compare-and-set. The winning transaction stores the immutable terminal result and creates its terminal outbox item together. Repeated terminal requests return the winner. Cancellation prevents new reservations once it wins. Already-reserved batch rows remain visible for audit; a late physical output is not made authoritative after cancellation. A pre-admission cancel creates the same tombstone keyed by invocation/run identity, so start/cancel ordering cannot resurrect work.

Terminal delivery retries the same `DeliveryId`, result hash and full frozen native result address, including provider ID and payload type. Pass the recorded bookmark ID explicitly in `BookmarkResumeDispatchRequest`. Before dispatch, match the live bookmark/registration against that address; a mismatch or unexpectedly retired active registration refuses delivery and records a visible diagnostic. Legitimate prior consumption or demonstrated parent/activity termination is a separate acknowledged disposition. Resume validates the same address again. Delivery attempts can repeat; the native workflow must accept no more than one continuation, verified at the workflow transition boundary rather than inferred from transport state.

## Required invariants

- All artifact, run, partition, snapshot, batch, output, manifest, and terminal queries are tenant/persistence-partition scoped. Authorization is checked for the node and host aliases at admission and inspection; callers cannot supply a different tenant or node identity.
- Runtime execution reads the published ETL artifact plus retained snapshots and runtime features. It never reads a mutable draft, Studio layout, original source path, or changed host binding revision to continue an old run.
- Every logical record belongs to one stable partition range. Every batch is a contiguous subrange and has one unique durable reservation.
- A batch is dispatched only after record and byte admission. Durable quota counts owed work once; retries reuse its reservation. Lease expiry alone neither advances the cursor nor releases quota or process-local memory permits.
- The same logical effect must have the same deterministic bytes and hash. Matching replay converges; conflicting hash faults. A stale fence cannot accept the result even if its physical write finishes.
- Manifest acceptance, batch settlement, quota update, and cursor advance commit atomically in ETL persistence. Terminal result and terminal outbox creation commit atomically in the same store.
- Cancellation before or after run admission is durable and first-wins. It cannot be undone by a replayed start, stale batch, or late terminal result.
- Only the manifest makes chunks visible as accepted output. No exactly-once physical I/O or atomic file/database transaction is promised.

## Implementation boundaries

The proposed package split in [ADR-0087](../../docs/adr/0087-etl-owns-its-execution-domain.md) keeps shared artifact/command/store contracts small in Core, design validation/publication separate from runtime execution, and EF persistence, API, workflow adapters, and Studio behind their actual consumers. Keep provider algorithms and transformation policy internal unless a real cross-package consumer requires a versioned contract. Do not add actor-provider abstractions, per-row workflows, a general graph format, a universal connector layer, or SLA/benchmark records to this first data model.
