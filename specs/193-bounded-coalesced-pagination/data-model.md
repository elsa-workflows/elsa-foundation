# Data Model: Bounded Coalesced Runtime-Store Page Merging

This describes the logical state needed to reason about the existing page merge and its bounded correction. It does not introduce public types or a new store contract.

## Runtime store page request

The existing request supplies an output limit and an opaque query-bound continuation. Its current default is 100, maximum is 500, and maximum continuation length is 16 KiB. Callers continue to treat the token as opaque.

## Overlay change

An overlay change is one session-scoped upsert or tombstone keyed by a stable string identity. The overlay contains the latest value for an upsert identity. Ordering is ordinal. For an identity present both in the durable source and as an overlay upsert, the overlay value is the visible row. A tombstone suppresses the durable row and contributes no output row.

## Per-call inner page buffer

The transient buffer contains a bounded ordered batch from the durable store, a position within that batch, the provider continuation from before the batch, and whether that batch is terminal. It lives only for one merge call. It lets selection and has-next look-ahead share already-read candidates. The row count never exceeds the smaller of the request limit and existing maximum page limit. Rows already emitted or suppressed can be released as the cursor advances.

## Merge cursor

The merge cursor contains the last emitted identity and the durable-store continuation/exhaustion state. It is bound to the same store query through the existing continuation binding. When a caller page ends with any un-emitted row from a fetched batch, the serialized position remains before that batch; the last-emitted identity filters out rows already returned when the page is resumed. A subsequent call can replay the bounded batch. No row payload crosses the request boundary.

The cursor may commit a provider position after a batch only once every row from that batch has been emitted or suppressed. A terminal provider page does not by itself mean the merge cursor is exhausted if a visible fetched row remains un-emitted. An empty terminal page proves exhaustion immediately. The existing `RuntimeStorePage<T>` constructor rejects an empty page with a provider continuation, so this merge contract neither accepts nor advances such a page.

## Visible merged result

For a stable durable dataset and overlay snapshot, the visible result is the ordinally sorted set of durable rows not replaced or tombstoned, plus all overlay upserts. Pagination partitions that sequence by output limit. Concatenating every page yields each visible identity exactly once. Cross-request snapshot isolation remains governed by the provider's existing continuation contract.

## State ownership

| State | Lifetime | May contain row payload? | Purpose |
|---|---|---:|---|
| Inner page buffer and read index | One merge call | Yes, bounded | Avoid repeat inner calls while selecting rows and checking has-next |
| Last emitted identity | Continuation lifetime | No | Exclude already emitted identities after a bounded page replay |
| Provider continuation before an unconsumed batch | Continuation lifetime | No | Recover rows fetched but not yet emitted |
| Exhaustion state | Per call and continuation | No | Prevent repeated terminal reads without advancing past an unconsumed row |
| Process-global cache | Not introduced | N/A | Would exceed this feature's scope and ownership model |
