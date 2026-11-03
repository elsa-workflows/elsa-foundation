# Research: Bounded state for continuous ETL runs

Facts are from the source at `6226b78af`. Paths are relative to `Extensions/Etl/src/`.

## R1 — What a shard retains, and why nothing is ever deleted

- **What an accepted non-empty page adds:**
  - one page row (`EfEtlConnectorHttpPageStore.AttachHttpPageAsync`);
  - one batch row per batch (`EfEtlConnectorBatchStore`);
  - one request row for the next page (`EfEtlConnectorAcceptanceStore`, or the empty-page advance).
- **No connector row is ever removed.** No `Remove`, `RemoveRange` or `ExecuteDelete` targets a connector table.
- **The audits need the whole history:**
  - `EfEtlConnectorHistory.AuditHttpHistoryAsync` streams every request and page. It requires contiguity from 0 and rebuilds the continuation chain from page 0.
  - `EfEtlConnectorAcceptanceHistory.AuditConnectorBatchHistoryAsync` streams every batch from ordinal 0, and validates pages from 0.
  - Both run on every claim (`auditHistory: true`) and at settlement.
- **The schema forces a full chain:**
  - the request CHECK `CK_..._SequencePredecessor` requires a predecessor for every sequence above 0;
  - the predecessor foreign key (request *k* + 1 to page *k*) and the page-to-request foreign key are both `Restrict`.
- **Files are never removed either.** Cached page manifests and bodies live in the snapshot root, and so do the retained main and error route bodies of every batch (`EtlConnectorOutputProcessor.RetainRouteBodyAsync`).

**Decision:** keep a window of one page behind the current request, compact at each claim (R8), and audit from the window's base. The alternatives were:
- **Tombstones** (null the heavy columns, keep the key rows). Storage and audit cost would still grow with the page count.
- **Compacting finite runs too.** Finite runs are bounded by `MaxPagesPerPartition` already, and their full-history cycle refusal is a guarantee worth keeping.

## R2 — Why the request table has to be rebuilt

- After compaction, the base request's predecessor points at a deleted page. The only valid encoding is a null predecessor, which the CHECK forbids above sequence 0.
- SQLite cannot alter a CHECK. Every alternative that keeps the CHECK keeps the whole chain, because each kept predecessor page needs its own request, and that request needs its own predecessor.
- **Two ways to rebuild.**
  - `20261021120000_ConnectorBatchReservations` turns foreign keys off with `suppressTransaction`, which makes the rebuild non-atomic.
  - **Chosen:** keep the migration's transaction and use `PRAGMA defer_foreign_keys = ON`. The design review tested this on SQLite 3.54:
    1. copy the rows to a scratch table;
    2. drop the table (child rows become deferred violations);
    3. create it with the relaxed CHECK;
    4. copy the rows back (each re-inserted parent clears its children's violations).

    `foreign_key_check` came back empty, and `RESTRICT` children survived.
- **Traps:**
  - The alternate key must come back as a `UNIQUE` constraint, because the page and partition foreign keys reference it. Without it, every insert fails with "foreign key mismatch".
  - Every index must come back under its EF name.
  - The model snapshot must match exactly.
  - New partition columns use `ADD COLUMN … CHECK`, as spec 1192 did, so EF never schedules its own rebuild.

## R3 — Deletion order under restrict foreign keys

- `Restrict` fires per row, immediately, even under deferral. The chain is request *k* + 1 → page *k* → request *k*. A null predecessor is not enforced.
- **The order is therefore four set statements:**
  1. clear the predecessor pointers of every request at or below the new base;
  2. delete the batches below the base page;
  3. delete the pages below it;
  4. delete the requests below it.

  Nothing then references a page being deleted, and the pages referencing a deleted request are gone first.
- Batches reference only their partition. The owed batch, which the partition does reference, is never compacted.

## R4 — Poll generation

- **Where the race comes from.**
  - `ConnectorHttpPageId(admission, partition, sequence, request)` is the create-only manifest key.
  - A continuous shard re-requests the same (sequence, request) after each caught-up poll, so a stalled worker's late publication lands under the key the next poll reads.
  - `EtlHttpSourceAcquirer.CaptureAsync` then fails provenance, because the attempt was cleared from the journal.
- **The fix.** A counter incremented by each caught-up record separates the polls. Above 0 it is appended to the page identity, so generation 0 keeps today's bytes.
- **It is a partition column, not part of the position.** This is the design review's finding.
  - Putting it in `PositionJson` would change the position on every idle poll.
  - The execution releases with its pre-poll lease, and `UpdateConnectorLeaseAsync` compares `PositionJson`, so every release would be refused as `LeaseLost`.
  - The partition's metadata accounting, which counts the position's bytes, would also drift without a budget branch.
- **The claim copies it onto the lease**, so the acquirer has it before `Begin`.
- **The stalled worker** still holds its old fence, so the store refuses its capture as a lost lease, and `ConnectorFailureExpiryAsync` refuses its failure settlement.

## R5 — Per-page body identity

- Cache bodies are content-addressed (`BodyIdentity(hash)`), so two pages with identical bytes share a body file.
- The snapshot root is per host (`EtlConnectorExecution`), so bodies are shared even across runs. Deleting a shared body could break another page's replay with `etl.http.page-cache-corrupt`.
- **Decision:** a continuous run's body identity is its page identity plus its content hash.
  - The page identity part makes deletion safe.
  - The hash part keeps a successor's different response from conflicting with a body that a crashed predecessor published without its manifest.
- Manifest validation accepts the legacy form, so spec 1192 runs stay readable, but only per-page bodies are ever deleted.

## R6 — Who deletes files, and when

- The store cannot delete files: it runs in the database transaction, and files are not transactional.
- The worker deletes them after the transition that ends their use commits:
  - **Route bodies after acceptance.** A replayed acceptance reads rows only, and a stale worker is fenced off before it could need them. The route-body identity is per batch.
  - **The page's manifest and body after the advance.** The predecessor page is still retained as a row, but nothing reads its body: replay reads only an attached page, and the audits read rows.
- A crash between the commit and the deletion leaks those files once. No sweep is added: a sweep would have to tell one partition's files from another's under hashed names, which is impossible without a manifest change.

## R7 — Budgets and cycles in continuous runs

- **The page-count limit.** `MaxPagesPerPartition` is enforced as `pageSequence < max` at eight sites: `EfEtlConnectorSourceStore`, `EfEtlConnectorHttpPageStore`, `EfEtlConnectorHistory`, `EfEtlConnectorRuntimeStore`, `EtlConnectorRecordCodec`, `EtlHttpSourceFetchTransitions` (×2) and `EtlHttpPageCache`. In a continuous run the bound is skipped; the window is the bound.
- **Retention budgets** compare the retained counters, which compaction now decreases.
- **Cycle refusal** uses the unique `(partition, RequestHash)` index plus lookups. After compaction it covers the window only. A forward-moving cursor never repeats, and a feed that rewinds re-delivers records, which is at-least-once behaviour. The README states this.

## R8 — Compaction at the claim, not at the advance

The design review found that compacting at the advance runs after the transition's budget checks:
- `EfEtlConnectorAcceptanceStore` checks the metadata delta first.
- `ConnectorMetadataBudgetStatusAsync` treats every historical row as permanent.

As a result, a spec 1192 run already at its ceiling would fail `PermanentBudgetExceeded` before it could ever compact.

**Decision: compact in the claim transaction, after the claim audit.**
- Every connector step starts with a claim, because a worker takes one step per lease.
- Compaction therefore always precedes any work that is charged.
- A same-fence retry still finds its rows. Every compaction, whichever shard's claim runs it, keeps each shard's predecessor page and request with that page's batches, which is every row a same-fence replay reads, so a retry always finds its rows. Compacting a peer under a live lease is therefore safe; transactions are serialized, so the peer's next transition reads the compacted row.
- The claim is a single compaction site instead of two advance sites.
- **Every shard, not only the claimed one** (review round 1). Retention budgets are per run, so a shard without history could otherwise fail a resumed run whose history sits on a peer that has not been claimed yet.

**Rows the window must keep:**
- **The predecessor page's batches.** The batch audit reads them (`ReadAcceptedItemCountAsync`), and a same-fence replay reads them.
- **Every row reader stays at or above the predecessor page**, as the review checked:
  - position audit;
  - settled-batch replay;
  - empty-advance and attach replays;
  - settlement;
  - the inspector;
  - the budget helper.
