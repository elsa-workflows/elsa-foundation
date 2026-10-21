# HTTP ETL state model

## Published model

V1 file model remains unchanged. V2 contains connector-kind/alias selections, source schema and JSON field selectors, pagination record/continuation selectors, transform declarations, optional CSV lookup, finite payload/admission/page/retry limits and route destinations. Canonical bytes are produced by the format-specific writer; definition and artifact hashes remain separate identities. Runtime never reconstructs v1 bytes with a v2 writer.

## Runtime state

- **Binding pin:** tenant/partition, role/kind, alias/revision, opaque metadata digest. HTTP metadata binds endpoint/shard seeds, connection name/type and non-secret config digest. No credential value.
- **HTTP partition:** stable shard identity, current request descriptor, monotonic page sequence, retained page reference/item offset, exhausted flag, current ownership/fence/expiry and retry eligibility. A file partition keeps its original ordinal range.
- **Retained page:** immutable raw/canonical data reference, content hash/size, bounded record count, parsed next-request descriptor and source response consistency metadata where available. Never accepted solely because a staging path exists.
- **Owed batch:** stable input/page/item-range identity, actual canonical record/byte charge, retained per-route output body references and effect keys. It survives owner loss; each physical attempt owns separate local allowances.
- **Route delivery:** pinned destination, request body reference/hash/key, durable dispatch attempt/deadline/not-before, acknowledged status and bounded safe acknowledgement identity. Conflicting body/key is refused. Remote physical receipt is distinct from authoritative progress.
- **Accepted effect:** route-specific file publication or HTTP acknowledgement, counted with the batch position/settled reservation transaction. The manifest API must disclose effect kind, not repurpose file key semantics.
- **Terminal result/outbox:** existing per-run single decision and owed native delivery. Success requires exhausted partitions and no unsettled owed work. Cancellation can win while remote I/O is still completing.

## Transition invariants

Admission pins once. Request preparation is fenced. Page publication is create-only; attachment checks the expected request/fence. Batch reservation checks the current page/item position and both quota dimensions. A dispatch attempt is allocated durably before sending. Retry persists next eligibility and releases the claim after I/O unwinds, retaining owed data. Acknowledgement binds exact request identity; acceptance requires current unexpired ownership and expected position. Fully filtered nonempty batches accept zero effects only with matching input/count accounting and settle their reservations. Empty pages use a guarded progress transition without fake output. Exhaustion is explicit and terminal settlement is shared with the existing run machinery.

Migration must distinguish old file rows without changing their cursor/format meaning. New typed HTTP store operations must not oblige existing v1-only providers to claim support. Unsupported provider composition is diagnosed before HTTP execution. Unknown/corrupt state never advances progress.
