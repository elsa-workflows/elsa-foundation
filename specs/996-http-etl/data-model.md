# HTTP ETL state model

## Published model

V1 file model remains unchanged. V2 contains connector-kind/alias selections, source schema and JSON field selectors, pagination record/continuation selectors, transform declarations, optional CSV lookup, finite payload/admission/page/retry limits and route destinations. Canonical bytes are produced by the format-specific writer; definition and artifact hashes remain separate identities. Runtime never reconstructs v1 bytes with a v2 writer.

### V2 declarations

- Source is either CSV (binding and schema) or HTTP JSON (binding, record-array pointer, named scalar field pointers, and single-page/token/next-link pagination).
- Optional CSV lookup owns its own binding/schema and explicit source/lookup join keys. Absence of a lookup does not create a synthetic lookup.
- Main and error destinations independently select JSONL or HTTP, so mixed routes are valid.
- Definition limits distinguish requested file partition count from an upper partition bound; HTTP partitions will come from bounded host shard seeds. Host admission applies its own ceilings.
- HTTP policy contains finite page/continuation/retention limits and attempt/deadline/request-time/backoff settings. Token parameter names are bounded literal names; the transport must URI-encode them, including common names such as `page[token]`.
- V2's compiler capability is additive. The native envelope key remains `etl.pipeline.v1`; its embedded artifact format selects the pipeline codec, independently of the unchanged activity state/completion format.

## Runtime state

- **Binding pin:** tenant/partition, role/kind, alias/revision, opaque metadata digest. HTTP metadata binds endpoint/shard seeds, connection name/type and non-secret config digest. No credential value.
- **HTTP partition:** stable shard identity, current request descriptor, monotonic page sequence, retained page reference/item offset, exhausted flag, current ownership/fence/expiry and retry eligibility. A file partition keeps its original ordinal range.
- **Request ledger:** immutable sequence/request identity per HTTP partition, with unique visited-request digest, a predecessor-page link and a current-request pointer. Normal lease/page transitions read indexed current/predecessor rows; ownership acquisition and terminal success audit the complete finite history.
- **Retained page:** immutable raw/canonical data reference, content hash/size, bounded record count, parsed next-request descriptor and source response consistency metadata where available. Never accepted solely because a staging path exists.
- **Owed batch:** stable input/page/item-range identity, actual canonical record/byte charge, retained per-route output body references and effect keys. It survives owner loss; each physical attempt owns separate local allowances.
- **Route delivery:** pinned destination, request body reference/hash/key, durable dispatch attempt/deadline/not-before, acknowledged status and bounded safe acknowledgement identity. Conflicting body/key is refused. Remote physical receipt is distinct from authoritative progress.
- **Accepted effect:** route-specific file publication or HTTP acknowledgement, counted with the batch position/settled reservation transaction. The manifest API must disclose effect kind, not repurpose file key semantics.
- **Terminal result/outbox:** existing per-run single decision and owed native delivery. Success requires exhausted partitions and no unsettled owed work. Cancellation can win while remote I/O is still completing.

## Transition invariants

Admission pins once. Request preparation is fenced. Page publication is create-only; attachment checks the expected request/fence. Batch reservation checks the current page/item position and both quota dimensions. A dispatch attempt is allocated durably before sending. Retry persists next eligibility and releases the claim after I/O unwinds, retaining owed data. Acknowledgement binds exact request identity; acceptance requires current unexpired ownership and expected position. Fully filtered nonempty batches accept zero effects only with matching input/count accounting and settle their reservations. Empty pages use a guarded progress transition without fake output. Exhaustion is explicit and terminal settlement is shared with the existing run machinery.

Migration must distinguish old file rows without changing their cursor/format meaning. New typed HTTP store operations must not oblige existing v1-only providers to claim support. Unsupported provider composition is diagnosed before HTTP execution. Unknown/corrupt state never advances progress.

## Connector storage upgrade boundary

Connector bindings and prepared inputs live in a companion run row; typed connector partitions use
separate rows from the retained v1 ordinal tables. The shared run, counters and terminal outbox keep
their existing representation. Each connector JSON snapshot has a bounded, canonical, closed shape;
its partition completion flag must agree with that shape. Preparation atomically records the input
snapshot and initial partitions before the shared run becomes runnable.

The additive migration preserves existing v1 rows, but **mixed old and new worker binaries are not
supported once v2 admissions are enabled**. Old workers do not know the connector marker and could
claim or settle a connector run through file-only code. Drain or upgrade all ETL workers before enabling
connector admissions. A schema upgrade alone is not a rolling-worker compatibility guarantee.

Connector admission/preparation and ownership are the first part of T005. Page attachment, durable
request history, reservations, route attempts/acknowledgements and atomic batch acceptance must be
added before the runtime's explicit connector-unavailable refusal is removed. Completion must then
include those owed-work tables in the same transaction as the shared terminal decision.

The HTTP page companion table retains immutable metadata keyed by scope/run/partition/page sequence,
with a unique request digest per partition. Its payload is an object reference, not inline response
JSON. Total retained-input accounting includes every retained page body. A separate authored metadata limit
(default 64 MiB, bounded by the host default ceiling of 256 MiB) accounts for UTF-8 bytes of all
request/page JSON, retained batch-descriptor/output-plan/delivery-journal JSON and each current partition-position JSON across the run,
plus bounded headroom for recording in-flight HTTP outcomes, pending retry exhaustion and accepted-position growth. Fixed SQL row/index/file
overhead is excluded; admission/preparation/terminal envelopes retain their independent bounds.
Partition and run counters are updated in the same transaction as metadata and position changes;
replay does not recharge. Empty-page progression keeps
that history for replay and loop checks. Source request identities are separate from worker attempts
and lease fences. Nonempty progress must be proved by atomic batch acceptance, not the empty-page
operation. Connector batch reservations use typed file ranges or HTTP page/item ranges, with identity bound
to the immutable admission, prepared inputs and measured canonical input. One indexed owed-batch pointer per
partition retains responsibility and shared record/byte charges through claim expiry and release; replacement
workers recover that descriptor before dispatch. Cancellation retains the reservation as evidence without
authorizing further dispatch. Reservations alone do not acknowledge outputs or advance source positions.

### Draft schema evolution

The connector migrations `20261021070000_ConnectorRuntimePreparation` and
`20261021100000_ConnectorHttpPages` are still part of unmerged draft PR #3156. This iteration
refines them to include the request ledger, linked current/predecessor identities and counters;
it leaves the released v1 migration unchanged. The supported upgrade target is the v1 schema.
There is no in-place upgrade from private databases that already applied the earlier draft
connector migrations. Disposable checkpoint databases must be recreated for this revised branch;
this is not a production rolling-upgrade claim.

### Retained connector output plans

Each owed batch may retain one complete transformation result before delivery: accepted/rejected/filtered
counts and fixed main/error route slots. A positive route count requires a verified immutable body; a
fully filtered nonempty batch has no bodies. Every input record is accounted for exactly once by these
three counts. Route kind must match the admitted destination, and its stable effect identity binds the
invocation, batch, route, destination revision and exact body content/size/count. Storage location and
physical attempt are not effect identity inputs, but a retained plan cannot be replaced by a different
body reference. Retaining a plan is not an acknowledgement and does not advance or settle the batch.

The authored `MaxRetainedOutputBytes` budget defaults to 1 GiB, with a configurable runtime host ceiling
defaulting to 4 GiB. It counts every referenced route body across the run, conservatively charging two
routes even when their content hashes match. It is separate from live attempt memory, retained input,
and metadata budgets. It does not bound unreferenced staging objects or physical storage overhead;
worker publication/cleanup integration remains required. The default is omitted from canonical v2 JSON,
preserving existing default artifact bytes. Nondefault values are explicit and positive.

Output-plan JSON/digest, batch byte counts and partition/run aggregates commit together under the current
lease. Identical replay does not charge again. A replacement claims the original plan, validates its
identity and body accounting, and keeps the original owed input charge. Cancellation retains that
evidence and rejects further writes. The additive output migration preserves existing reservations;
provider upgrade checks here are structural, not proof of retained old-worker execution.


### Durable HTTP route attempts (partial T005)

A retained HTTP route may allocate a bounded request attempt under current partition ownership.
The caller supplies an attempt request ID; only a non-null returned permit authorizes transport.
An identical live replay can return that same permit and must not be dispatched concurrently.
The stable effect ID and immutable body remain unchanged across retries and replacement workers.
There is no claim of exactly-once physical HTTP delivery.

Per-route journals retain allocated attempts, timeout and original deadline, response status and
parsed absolute Retry-After, closure time, next eligibility and explicit terminal failure reason.
Only 200/201/204 acknowledge; 202 is not synchronous completion. Uncertain delivery and the selected
transient statuses use capped exponential backoff, extended by a valid later Retry-After. A retry
at or beyond the deadline fails rather than starting early. Replacement/expired attempts first
record uncertainty and wait; stale physical completions cannot acknowledge the successor's work.

Canonical journal bytes are charged with 512 bytes of transition headroom per in-flight route and
256 per waiting route; terminal routes need none. Each completion releases enough space for its
bounded receipt or deadline outcome even when other work has filled the metadata budget. A next
attempt pays its history growth and replenished headroom before a permit is returned. Both actual
JSON and headroom must fit the private document bound. These are logical accounting allowances,
not a promise about physical database or live-worker memory.

The additive 1400 migration preserves prior reservations/output and foreign keys. When all remaining
HTTP routes are waiting, an indexed partition not-before value defers acquisition until the earliest
route is due. Acknowledged routes are not resent. A mixed file/HTTP batch remains eligible while its
file route has no publication record; a recorded file publication permits the remaining HTTP retry to defer it.
Delivery acknowledgement neither advances input nor releases the owed record/byte reservation.
Atomic accepted progress is covered by the following checkpoint; source-fetch attempt state remains
required before runtime activation.

## Connector acceptance and file publication checkpoint

A retained JSONL route has a fixed publication flag separate from accepted input. Recording that flag
requires the deterministic file key derived from the route effect identity and the retained body's
exact hash/length. The caller must verify the physical file before recording it. An HTTP route instead
requires its validated synchronous acknowledgement journal. Absent routes need no effect; a nonempty
fully filtered batch can therefore be accepted with no route bodies.

Each partition retains an accepted-batch count and accepted/rejected/filtered totals. Batch ordinals
form a settled prefix followed by at most one owed batch. HTTP page rows retain the accepted item
count. Atomic acceptance clears owed responsibility, releases outstanding admission, advances the
file cursor or HTTP page/item position, updates both partition and shared run counts, and retains
all batch/output evidence. Consuming a page creates its next immutable request or marks it exhausted.
The final batch or empty page also commits terminal result and outbox rows in the same transaction.
Expiry during either progress or terminal persistence rolls back the entire transition.

New reservations charge `AcceptanceMetadataBytes`, the exact positive growth of the next position
and any next request document, before transformation or dispatch. Acceptance removes that reserve and
charges the actual growth in the same transaction. Other partitions and route journals cannot spend
the reserved capacity. The additive 1500 migration leaves earlier checkpoint rows at zero reserve;
replaying output retention tops up missing capacity and is required before dispatching recovered
output, including file publication. HTTP permits independently enforce the top-up. A received HTTP
outcome can still be recorded without acquiring new dispatch capacity. If legacy already-delivered
work cannot obtain capacity, it remains retained and must receive an explicit terminal failure; it
must never exceed the quota, erase the receipt, or silently advance input.

Recovery and successful terminal settlement stream the full request/page/batch history and validate
contiguous ranges, admission-bound identities, route evidence and all aggregate counts. Ordinary
transitions and settled-batch replay use indexed bounded reads; replay returns any newer owed batch
without recounting old input. Once the run is terminal, duplicate acceptance returns `Terminal` and
leaves its existing result/outbox unchanged. These storage contracts do not yet establish the full
HTTP transport, native continuation, Studio or two-process acceptance journey.

## Source-fetch journal and capture boundary

The source request binds its page sequence and request hash, and owns a fixed retry deadline and a bounded ordered history of physical permits.
A permit binds its caller-generated request ID, attempt number, original owner fence, immutable source
request hash and start/expiry. Non-success HTTP outcomes, incomplete transport and invalid complete
pages are distinct facts. A bare 200 is not a durable success outcome; only a verified capture receipt
can establish a retained source page.

The capture receipt references the raw page descriptor and the original attempt ID/fence plus the
complete-response receive time. Recovery must match that tuple to a retained historical permit and
require start <= receive time < expiry and before the fixed deadline. The current lease authorizes
attachment; the receipt's old fence identifies the original request, and is not substituted for the
current lease. A valid capture can be discovered after that attempt was recorded uncertain or another
attempt began. Keep those physical histories unchanged and record capture separately. Capture stops
new dispatch, but does not prove that a later physical attempt has terminated or release its capacity.
A definite incompatible response or invalid-page outcome cannot be replaced by a successful receipt.

The file cache publishes raw content-addressed JSON before the request-addressed manifest. That
manifest is the recoverable pre-SQL receipt. Re-read and validate its actual body before attaching;
raw body references continue to measure the real JSON bytes. Zero-byte responses, malformed/truncated transport and
unavailable/corrupt storage never become source exhaustion. A body without a manifest is uncommitted
cache material; its cleanup and physical storage capacity belong to the runtime integration.

The request row now persists the source journal with canonical bytes/digest, outcome headroom and a
separate page/progress metadata reservation. Before returning a physical GET permit, one serializable
transaction reserves logical input capacity and bounded capture, attachment and future progress metadata.
Capture reduces the input reservation to the verified raw-page length; attachment transfers that capacity
into retained input. Nonempty batch reservation transfers the required progress capacity into the existing
acceptance reserve without charging it twice. Empty-page progress releases its unused reserve. All source
writes recheck current ownership after saving, before commit; delayed claims use persisted `NotBefore`.
Recovery and terminal audits validate current and historical journals against their page/request chain.
Legacy null source journals remain readable for already-retained pages; new attachments require capture.

Connector documents are bounded to 1 MiB. The earlier 512 KiB envelope could not hold two allowed 64 KiB
opaque continuations for every valid value: lossless UTF-16 Base64 still expands further when JSON escapes
`+`. The run-wide metadata quota remains independent, and the source permit checks the conservative
encoded bound before dispatch. No canonical serialization or v1 document limit changed.

The [source store checkpoint](evidence/source-store-checkpoint.md) records this storage integration.
Actual transport, cache verification/publication orchestration, physical request capacity/cleanup and
full native/product/process recovery remain runtime acceptance; these stores do not activate HTTP execution.

### Shared connector worker composition

The shared worker now consumes the optional typed connector capability alongside the unchanged v1 file path.
It rotates configured file/HTTP scopes and preparation/partition/result delivery within one physical attempt
pool. One connector attempt executes one bounded batch or empty-page transition and yields. Exact v2 typed
batches are reconstructed from retained HTTP pages or CSV indexes; route bodies are retained before dispatch,
and all route evidence precedes atomic acceptance. The [worker checkpoint](evidence/http-worker-checkpoint.md)
records the executed boundary and remaining native/product/process acceptance.

`BudgetExceeded` leaves responsibility pending for releasable outstanding capacity. The additive
`PermanentBudgetExceeded` reports a proven per-run/document retention limit that cannot fit after releasable
capacity returns; the worker settles a safe fenced failure without deleting unaccepted descriptors. Metadata
classification preserves historical source journals and fixed/monotonic file cursor bytes, and treats peer
active reservations conservatively. This distinction does not change v1 quota statuses or clear failure evidence.
