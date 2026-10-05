# Coalesced Page Merge Contract

This is a behavioral contract for the internal coalesced merge. Existing runtime-store interfaces and caller-visible page request/token shapes remain unchanged.

## Inputs

- A `RuntimeStorePageRequest` with a validated output limit and optional opaque continuation.
- A stable query binding used by the existing `crsp1` token validation.
- A provider page reader accepting a bounded row limit and provider-owned continuation.
- An ordered durable-row identity selector, an ordered overlay-upsert selector, and a predicate for overlay replacement/deletion.
- The request cancellation token.

## Output

- At most the request output limit of visible rows.
- A continuation only when another visible row exists. The continuation remains opaque and bound to the same store query.
- No successful partial page if cancellation interrupts a provider read.

## Merge rules

1. Compare durable and overlay identities with ordinal string comparison.
2. Select the lower identity first. On equality, select the overlay row and suppress its durable counterpart.
3. Omit durable rows suppressed by an overlay tombstone.
4. Advance the last-emitted identity only when a visible row is emitted; skipped/replayed identities at or below it are not emitted again.
5. Reuse fetched durable rows from a bounded in-call buffer until each is emitted, replaced, suppressed, or remains pending at the output boundary.
6. Do not read a source again in the current call after terminal exhaustion is established.

## Provider-page and continuation rules

- Every provider request is bounded by the lesser of the caller limit and `RuntimeStorePageRequest.MaximumLimit`.
- An empty page with a non-null continuation is rejected by the existing `RuntimeStorePage<T>` constructor; the merger does not accept or advance it. An empty page with no continuation is terminal.
- A non-empty page with a null next token is terminal only after all buffered rows are consumed or suppressed.
- Within one merge call, each provider cursor position is read at most once. The merger may replay a bounded page on a later call when the output boundary leaves rows un-emitted; it does not retain row payloads across calls.
- A request may read every fully consumed or filtered page needed to fill its output page or to establish whether another visible row exists or the source is exhausted. It may additionally retain at most one partially consumed batch or terminal probe. This permits scanning an arbitrarily long filtered tail to establish that no visible row remains. The same provider cursor position must not be re-read during that call, even when overlay rows are emitted between durable candidates.
- A returned continuation must not skip a fetched-but-unemitted row. If no payload-free provider position exists after the last emitted row, keep the token at the start of the buffered provider page and replay that bounded page on resume; filter out rows at or below the last-emitted identity.
- The continuation MUST NOT contain arbitrary row payloads or create cross-request global cache state.
- Valid pre-change `crsp1` tokens retain their meaning. Invalid and query-mismatched tokens retain current rejection behavior.

## Cancellation and failures

Pass cancellation to each provider call and propagate cancellation/failure without returning a success page. Do not convert cancellation or provider failure into exhaustion or a valid continuation.

## Correctness domain

For a stable durable result set and overlay snapshot, complete traversal returns exactly the sorted visible merged result once. Changes to underlying data between separate continuation requests retain the existing provider-defined semantics; this contract does not promise a new cross-request snapshot.

## Deterministic acceptance vectors

| Vector | Fixture | Required result | Required inner-call bound |
|---|---|---|---|
| Empty durable source | 128 overlay rows, output limit 7 | First page has 7 sorted overlays and a continuation | Exactly one read at the initial provider cursor position; it returns the terminal empty page. Emitting more overlays causes no additional read in that call. |
| Buffered candidate | Three earlier overlays `a,b,c`, one terminal durable row `d`, output and provider page limit 4 | One page returns `a,b,c,d` in order | Exactly one read at the initial provider cursor position, including look-ahead. |
| Interleaved identity mutations | A counting provider returns up to the requested page limit; durable `a,c,e` is terminal; overlays add `b,f`, replace `c`, delete `e`; output/provider limit 7 | One output page is `a,b,c(replacement),f` | Exactly one provider read, including terminal knowledge for look-ahead. |
| Interleaved multi-page correctness | Same fixture, output/provider limit 2, stable provider pages | Traversal returns `a,b,c(replacement),f` once each | First request: at most two distinct provider cursor positions. Second request: at most two positions (one replay of the initial cursor and the following cursor). No cursor position is read twice within one request. |
| Continuation boundary | Overlay rows `a,b` plus one terminal durable candidate `c`, output/provider limit 2 | First page returns `a,b`; the next page replays and returns `c` once, without token payload | Exactly one read at the initial cursor in each merge call; zero same-call rereads. |
| Terminal empty source | Empty durable source, no overlay rows | Empty result, no continuation | Exactly one read at the initial cursor in the merge call; terminal exhaustion is memoized. |
| Filtered tail exhaustion | Provider page size 2; durable rows `a` through `l`; tombstones for `c` through `l`; no overlay upserts; output limit 2 | Return `a,b` with no continuation after proving no later visible row remains | Exactly six distinct provider positions in one merge call: the page `a,b` plus five terminally exhausted or fully filtered following pages. |
| Legacy continuation | Existing valid `crsp1` plus malformed and wrong-binding controls | Valid resumes; controls fail as before | Not applicable |
| Cancellation | Provider blocks until request token is cancelled | Cancellation propagates, no partial page | No follow-up probes after cancellation |
| Invalid empty nonterminal page | Empty `RuntimeStorePage<T>` with a non-null continuation | Existing constructor rejects the page before merge processing | No merger read or token advancement; the public page contract is unchanged |
