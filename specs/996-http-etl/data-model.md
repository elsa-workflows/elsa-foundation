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
plus bounded headroom for recording in-flight HTTP outcomes and pending retry exhaustion. Fixed SQL row/index/file
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
route is due. Acknowledged routes are not resent. File publication evidence is still pending, so a
mixed file/HTTP batch remains eligible while its file route has no settled publication record.
Delivery acknowledgement neither advances input nor releases the owed record/byte reservation.
Atomic accepted progress and source-fetch attempt state remain required before runtime activation.
