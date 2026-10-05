# Bounded Coalesced Runtime-Store Page Merging

**Spec ID**: `193`

**Spec Directory**: `193-bounded-coalesced-pagination`

**Git Branch**: `claude/runtime-db-paging-2392` (the issue #2392 reference-capture lane)

**Created**: 2026-10-05

**Status**: Draft for lead review

**Input**: Program #2382, leaf T05 / issue #2392. Specify and reproduce the bounded correction for Coalesced runtime-store page merging; implementation is separate issue #2393.

## User Scenarios & Testing

### User Story 1 - Read a merged runtime-store page without repeated inner probes (Priority: P1)

An operator or runtime caller asks a Coalesced store for a page while the active coalescing session contains staged upserts and deletions. The store returns the same ordered logical view as applying those staged changes to the durable rows, while doing a bounded number of inner page reads.

**Why this priority**: The current merge loop requests one durable row on each iteration and can query an empty or exhausted durable source again for every overlay row. That turns an otherwise bounded page into work proportional to overlay length.

**Independent Test**: Use a deterministic counting store and fixed session overlay. Assert the full returned sequence and exact store-read count for an empty durable source with many overlays, and for an overlay preceding a durable candidate. Tests must fail against the current implementation and pass after #2393.

**Acceptance Scenarios**:

1. **Given** an empty durable store and more overlay upserts than the requested page limit, **When** consecutive pages are read, **Then** every overlay identity appears once in ordinal order and the empty durable source is probed at most once per request, regardless of the number of overlay rows emitted during that request.
2. **Given** one durable candidate ordered after several overlay upserts in the same requested output page, **When** the merged page is assembled, **Then** the candidate is fetched once and retained in that call while preceding overlays are emitted; look-ahead does not discard and refetch that candidate within the same call.
3. **Given** a durable candidate that has been fetched but not emitted when a response page ends, **When** a continuation is returned, **Then** it resumes from a position that can recover that candidate without storing the row payload in the token and without committing exhaustion past it.

### User Story 2 - Preserve merged row identity and ordering across pages (Priority: P1)

A caller traverses the durable-plus-overlay view with a page limit smaller than the combined result. Replacement and deletion overlays must be reflected in every page without duplicates, omissions, or order changes attributable to the merge.

**Why this priority**: Paging is a public store behavior. A performance correction is unacceptable if it changes which runtime state rows consumers observe.

**Independent Test**: Seed interleaved persisted rows, a replacement for one persisted identity, a tombstone for another, and overlay-only identities. Traverse with small page limits until no continuation remains; compare the concatenated pages to the expected ordinally sorted logical view.

**Acceptance Scenarios**:

1. **Given** interleaved overlay-only rows and durable rows, **When** pages are traversed under a fixed underlying dataset and session overlay, **Then** each visible identity is returned once, in ordinal identity order.
2. **Given** an overlay upsert with the same identity as a durable row, **When** the merged view reaches that identity, **Then** the overlay value is returned and the durable value is suppressed.
3. **Given** a tombstone for a durable identity, **When** the merged view is traversed, **Then** that identity is omitted and later visible identities remain available.
4. **Given** a continuation ending between rows or with a fetched-but-unemitted durable candidate, **When** traversal resumes, **Then** it neither duplicates nor skips any visible row.

### User Story 3 - Keep continuation, cancellation, and store contracts compatible (Priority: P1)

A caller can resume an existing opaque store continuation and cancel an in-progress read using the existing request contract. The paging correction is internal to coalesced merging and does not require callers or store implementations to adopt a new interface.

**Why this priority**: Runtime-store pagination is shared behavior. A change to continuation interpretation or cancellation propagation could break existing providers and callers even if merged ordering is correct.

**Independent Test**: Resume a valid continuation issued before the update with the same query, reject malformed or mismatched tokens as before, and cancel a blocked store read while asserting cancellation propagates and no partial page/token is returned.

**Acceptance Scenarios**:

1. **Given** a valid existing continuation token, **When** a Coalesced page request resumes with its matching query binding, **Then** it continues under the existing opaque-token contract.
2. **Given** a malformed or query-mismatched continuation, **When** a request is made, **Then** the existing validation failure is preserved.
3. **Given** cancellation while an inner page read is pending, **When** the request token is cancelled, **Then** cancellation propagates; no successful partial page or continuation is produced.
4. **Given** a runtime-store page is empty and carries a non-null continuation, **When** the page is constructed, **Then** the existing `RuntimeStorePage<T>` validation rejects it; an empty terminal page marks the inner source exhausted.

## Edge Cases

- A durable source can be empty on the first page. An empty page with a non-null continuation is rejected by the existing `RuntimeStorePage<T>` constructor and is not an input the merger needs to advance.
- The final provider page can contain rows and have no next token; exhaustion is safe to commit only after all fetched rows that remain visible have been emitted or intentionally suppressed by a matching overlay.
- A page can end while a durable candidate remains buffered in the current merge call. The response token must retain a position before that row; it must not claim the source is exhausted or advance past the candidate.
- An overlay can replace or delete the first, middle, or last durable identity in a fetched batch.
- Overlay and persisted identities can compare equal, requiring the overlay value to win exactly once.
- The output limit can be one; the internal fetch size can be larger than one only within the existing maximum page limit.
- A continuation can be absent, valid, malformed, or bound to another query.
- Cancellation can occur before the first read, during a read, or while moving between provider pages.
- The durable dataset may change between separate continuation requests. This feature promises stable ordering and no loss/duplication for a stable dataset and overlay; cross-request snapshot isolation remains the provider's existing contract.

## Requirements

### Functional Requirements

- **FR-001**: The Coalesced page merger MUST return the same logical rows as a stable ordinally sorted durable collection overlaid by the current session's upserts and tombstones.
- **FR-002**: The merger MUST keep fetched durable rows for a request within the caller's page limit and the existing public maximum page size; it MUST NOT materialize the full durable collection.
- **FR-003**: Within one merge call, each fetched durable candidate MUST remain available until emitted, replaced, suppressed, or proven beyond the returned page. Look-ahead MUST NOT discard a fetched candidate and refetch it during the same call.
- **FR-004**: A durable source proven exhausted during a merge call MUST NOT be queried again in that call, even while the merger emits further overlay rows.
- **FR-005**: Continuations MUST NOT contain arbitrary row payloads or require process-wide cached rows. A continuation MAY replay a bounded page at a later caller page boundary when an unconsumed candidate cannot be represented by a provider position; the replay MUST be bounded, filtered against the last emitted identity, and preserve correctness.
- **FR-006**: A continuation MUST represent the last emitted identity and the inner position needed to resume. It MUST NOT commit an inner position after a fetched-but-unemitted candidate, and MUST NOT mark the inner source exhausted while such a visible candidate can still be recovered.
- **FR-007**: Exhaustion MUST be established only by a terminal provider position with no unconsumed fetched row, or by an empty terminal page. The existing `RuntimeStorePage<T>` constructor rejection for an empty page with a non-null continuation MUST remain intact; this feature MUST NOT widen the provider or public page contract.
- **FR-008**: Merge ordering MUST use the same ordinal identity comparison as the existing coalescing overlay. Equal identities MUST resolve to the overlay value exactly once; tombstoned durable identities MUST be omitted.
- **FR-009**: Stable-dataset traversal across all returned pages MUST contain each visible identity exactly once, with no missing identity, in ordinal order.
- **FR-010**: Existing valid opaque continuations and query-binding behavior MUST remain compatible. Malformed or mismatched tokens MUST retain their current failure behavior unless a separately reviewed compatibility decision changes it.
- **FR-011**: Existing cancellation tokens MUST be passed to every inner page read, and cancellation MUST not be converted into a successful partial page.
- **FR-012**: This correction MUST preserve Immediate as the default mode, the current store interfaces, persistence durability/fencing/inspection guarantees, and non-coalesced pass-through behavior. It MUST NOT add blanket caching or change public pagination contracts.
- **FR-013**: Deterministic tests MUST assert both row correctness and store-read bounds. At least one test MUST fail against the unmodified current behavior due to repeated empty-source or candidate reads, and the same test MUST pass with the correction.
- **FR-014**: This correction MUST leave per-run checkpoint cadence, checkpoint boundaries, inspection readback, and durability behavior unchanged.

### Key Entities

- **Store page request**: Query-bound request containing the caller's output limit and opaque continuation token.
- **Overlay change**: A session-scoped upsert or tombstone keyed by the store row's ordinal identity.
- **Inner page buffer**: Bounded in-memory rows already fetched from the durable store for the current merge call. This is transient state, not durable token state.
- **Inner cursor**: Provider continuation plus exhaustion state used to resume the durable scan. It must remain before any candidate that was fetched but not emitted and is not recoverable from a provider position after that row.
- **Coalesced continuation**: Existing opaque token bound to a query, carrying merge progress rather than arbitrary row payloads. Compatibility with tokens created before the correction is required.
- **Visible merged row**: The durable row after overlay replacement and tombstone suppression, or an overlay-only upsert, ordered by identity.

## Success Criteria

### Measurable Outcomes

- **SC-001**: For a stable fixture traversed to completion with output limit 2, concatenated results exactly equal the expected sorted logical view, with zero duplicate, missing, incorrectly replaced, or tombstoned identities.
- **SC-002**: With an empty durable source and 128 overlay rows, a single page request with output limit 7 performs no more than one terminal empty-source probe; store reads do not grow with the number of overlay rows emitted in that request.
- **SC-003**: With one durable candidate after multiple earlier overlays, one page request fetches that candidate once; selection and has-next look-ahead do not fetch it repeatedly.
- **SC-004**: Every underlying page request is at most the caller limit and never exceeds the existing public maximum; live buffered durable rows never exceed that bound. Within one merge call, each provider cursor position is read at most once; pages needed again after an output boundary may be replayed on a later call.
- **SC-005**: Existing continuation tokens remain decodable and query-bound; cancellation and malformed-token behavior match the pre-change contract.

## Assumptions

- Store identities are stable strings and the existing ordinal comparison defines their order.
- The overlay snapshot is stable for one merge call; across requests, correctness is specified against a stable dataset/overlay and the provider's established continuation semantics.
- Output pages and inner provider pages remain distinct concepts. Each provider cursor position can be read at most once in one merge call; an output-page continuation may replay a bounded page in a later call. The continuation position and last-emitted identity must preserve correctness without a cross-call row cache.
- A later caller request may replay a bounded page to recover an unconsumed row because arbitrary row payloads are not stored in the opaque token. Avoiding that bounded replay would require a separate reviewed continuation/cache design.
- The exact historical custom transform is not required for the separate reference trace. That trace uses a deterministic built-in computation and must be labeled non-equivalent.

## Out of Scope

- Implementing the merger change tracked by #2393.
- Changing the public store interfaces, page request/token format, or pagination defaults.
- Introducing process-wide caches, durable candidate payloads, or generalized performance instrumentation.
- Changing checkpoint cadence defaults, durability/fencing, inspection behavior, outbox behavior, or provider contracts.
- Claiming a reproduction of historical command counts, workflow identity, or custom transform behavior.
