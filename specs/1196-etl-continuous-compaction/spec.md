# Feature Specification: Bounded state for continuous ETL runs

**Feature Branch**: `agent/claude/2908-etl-compaction` (from `main` after spec 1192 merged as #3670)

**Created**: 2026-11-03

**Status**: Implemented — [PR #3695](https://github.com/elsa-workflows/elsa-foundation/pull/3695); [evidence](evidence/README.md)

**Input**: The remaining part of the first criterion of [#2908](https://github.com/elsa-workflows/elsa-foundation/issues/2908): "Finite and continuous runs process two or more independently leased partitions with resumable cursors and model-owned completion/cancellation." Spec [1192](../1192-etl-continuous-http/spec.md) delivered continuous HTTP runs, but declared two gaps that keep the criterion open. This slice closes them. It belongs to [Program #2892](https://github.com/elsa-workflows/elsa-foundation/issues/2892).

## Context

Spec 1192 left a continuous run with two limits.

- **Unbounded state.** A continuous shard keeps every page, request and batch row it ever accepted, plus each page's cached files.
  - Its lifetime is therefore capped by `MaxPagesPerPartition` and the retention budgets, which fail the run once reached.
  - Every claim audits the shard's whole history, so even an idle poll gets slower as the history grows.
- **A superseded late page fails the run.** A worker can stall past its lease while the feed serves its request late, with records appended in between.
  - The stalled worker can then publish a page under the same create-only cache key that the shard's next poll uses.
  - The successor finds that page, sees provenance from an attempt that is no longer in its journal, and fails the whole run with `etl.http.page-provenance-invalid`.

The cause of both is the same: a continuous shard's identities and history are those of a finite run, which never repeats a request and always ends.

This slice gives continuous runs two things:
- **A constant-size retained window per shard.**
  - Every claim in a continuous run deletes the rows behind every shard's window, in the claim transaction and before any work is charged. Retention budgets are per run, so peers are compacted too.
  - The worker deletes the files whose use a committed transition ended.
  - The audits start at the window's base.
- **A poll generation.** Each caught-up poll increments a counter on the shard's partition, and the page-cache key includes it. A stalled worker's late page lands under a key no later poll reads.

Finite runs are unchanged. They keep their full history, their full continuation-cycle refusal, their page-count limit and their identities.

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Run a continuous pipeline indefinitely (Priority: P1)

An operator leaves a continuous run going. However many pages the feed delivers over its life, the run's retained rows, bytes and files stay within a constant bound per shard.

**Why this priority**: a continuous run that eventually fails on a retention budget is not continuous. The criterion stays open until this holds.

**Independent Test**: run a continuous pipeline whose `MaxPagesPerPartition` is 3 and whose retention budgets admit only a few pages, over a feed that delivers many more non-empty pages than that on each of two shards.
- Every record is accepted exactly once, and the run keeps running.
- Each shard never retains more than its peak window.
- The lifetime counts (pages, batches, accepted, rejected and filtered records, effects) keep counting.

**Acceptance Scenarios**:

1. **Given** a continuous shard whose current request is *r*, **When** any worker claims any shard of the run, **Then**, in the claim transaction:
   - the page, request and batch rows of pages before *r* − 1 are deleted;
   - the retained byte counters of the shard and the run drop by exactly those rows' bytes;
   - the shard's compacted summary grows by exactly those rows' counts.
2. **Given** a compacted shard, **When** any worker claims it, **Then** the claim audit verifies the retained window and the summary, and its cost does not depend on how many pages the shard has accepted.
3. **Given** a compacted shard, **When** an operator inspects the run, **Then** the page, batch, record and effect counts are lifetime totals, as before compaction.
4. **Given** a continuous run, **When** a shard reaches page `MaxPagesPerPartition`, **Then** it continues: in a continuous run that limit is replaced by the retained window.
5. **Given** a finite run, **Then** nothing is deleted, and every row, identity and limit is as today.

### User Story 2 — Survive a superseded late page (Priority: P1)

A worker stalls past its lease mid-poll. A successor polls the same cursor, catches up, and later polls again while the feed has gained records. The stalled worker's late response does not fail the run.

**Why this priority**: spec 1192 declared this a limit. It fails a whole long-lived run on a timing accident, without any data being wrong.

**Independent Test**: drive the store and page cache through the exact interleaving. A stalled attempt in generation *g* publishes after a successor has recorded a caught-up response, then the successor polls in generation *g* + 1 and receives a non-empty page.

**Acceptance Scenarios**:

1. **Given** the interleaving above, **When** the successor's next poll publishes its page, **Then** it publishes under the generation *g* + 1 key, captures it and attaches it, and the run does not fail.
2. **Given** the stalled worker, **When** it tries to capture its late page, **Then** the store refuses it as a lost lease, and the run does not fail.
3. **Given** a shard that has never caught up, **Then** its generation is 0, and its page-cache keys are byte-identical to today's.

### User Story 3 — Recover across compaction (Priority: P1)

A worker dies at any point around an advance. A replacement continues from the durable state without loss, duplication or an audit failure.

**Independent Test**:
- In-process: retry an acceptance after it has committed and compacted.
- In real worker processes: kill a worker repeatedly during a long continuous run.

In both cases, the accepted records equal the fed records exactly.

**Acceptance Scenarios**:

1. **Given** an acceptance that committed, **When** the same worker retries it with the same fence, **Then** the store replays the result. Every compaction, whichever shard's claim runs it, keeps each shard's predecessor page and request with that page's batches, which is every row a same-fence replay reads, so a retry always finds its rows.
2. **Given** a worker killed after the advance commits but before it deletes the page's cached files, **Then** the files are left behind (a bounded leak per crash), and the run continues.
3. **Given** a continuous run started before this slice, **When** any of its shards is first claimed on this slice, **Then** that claim compacts every shard's whole history before any budget is charged, and legacy cache files stay readable. A run already at its retention ceiling therefore resumes.

### Edge Cases

- **What the window is.** After any claim of its run, a shard whose current request is *r* retains:
  - request *r* and, if attached, page *r* with its batches;
  - the predecessor page *r* − 1 and request *r* − 1, which the transition checks read, with page *r* − 1's batches, which the batch audit reads.

  Everything with a page sequence below *r* − 1 is deleted. Request *r* − 1 becomes the window's base: its predecessor pointer is cleared, which only a base request may do.
- **When compaction runs.** At every claim of a continuous shard, after the claim audit and before the claim commits.
  - A worker takes one step per lease, so a shard advances at most one page between claims.
  - A claim with nothing to delete costs one query.
  - A caught-up poll adds no row, so it leaves nothing to compact.
- **Peak per shard.** Between claims, an advance can add a request. At peak a shard holds 2 pages, 3 requests and the batches of 2 pages.
- **Constant work.** In steady state each claim deletes at most one page's rows per shard; a shard with nothing to delete costs no query. A run that started before this slice compacts every shard's whole history at its first claim, once.
- **Deletion order.** The page and request rows form a chain of restrict foreign keys: request *k* + 1 references page *k*, and page *k* references request *k*. Compaction runs these steps in order:
  1. clear the predecessor pointers of the base and of every request below it;
  2. delete the batches;
  3. delete the pages;
  4. delete the requests.

  Each step is a single set statement.
- **What stays exact.** The audits check retained rows plus the compacted summary against every counter they check today:
  - pages are contiguous from the base, and requests are contiguous from the base to the current request;
  - batch ordinals are contiguous from the compacted batch count;
  - the accepted, rejected and filtered record counts and the effect counts equal the retained sums plus the summary;
  - the retained byte counters equal the retained rows' sums.
- **Continuation cycles.** In a continuous run, a continuation that revisits a request older than the window is no longer detected as a cycle. It is fetched again, and its records are delivered again. Continuous feeds must move their cursor forward; the README states this. Finite runs keep their full-history refusal.
- **Budgets.** In a continuous run, `MaxPagesPerPartition` no longer limits the page sequence. The retained input, metadata and output budgets now bound retained state, which compaction releases.
  - A single window that exceeds a budget still fails with the existing codes.
  - Budgets are per run, so authors size them for one peak window per shard.
- **Cached files.** A continuous run's page body is stored under an identity derived from its page key and its content hash, rather than its content hash alone.
  - Deleting it therefore cannot remove a body that another page or run shares; the snapshot root is per host.
  - A successor that fetches different bytes after a crash between the body and the manifest does not conflict with the orphaned body.
  - The worker that advances a shard deletes the cached manifest and body of the page it left, after the advance commits.
  - Bodies of a run started before this slice keep their legacy shared identity. They stay readable and are never deleted.
- **Retained route bodies.** Each batch retains its main and error output bodies in the snapshot root until its effects are done. In a continuous run, the worker that accepts a batch deletes those bodies once the acceptance commits; nothing reads them after that, because a replayed acceptance reads only rows.
- **Orphans.** A stalled worker's late page under an old generation, and files whose deleting worker crashed, are left behind. Their number is bounded by stalls and crashes, not by the run's lifetime. The README states this.
- **The poll generation.**
  - It is a partition column, not part of the persisted position.
    - A caught-up poll therefore leaves the position, its metadata accounting and the lease release comparison unchanged.
    - Finite positions are byte-identical.
  - A claim copies it onto the lease.
  - The caught-up transition increments it.
  - The page-cache key includes it when it is above 0.
  - It never resets.
  - It cannot change while a page is attached, because a caught-up poll only happens with no page.
  - The final caught-up response that ends a shard under completion does not increment it, because that response is retained.
- **Mixed-version fleets.** A runtime without this slice refuses a position or descriptor it cannot read. All workers in a scope must run this slice, as specs 1190 to 1192 already require.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001** The partition gains a `PollGeneration` column. The database checks that it is non-negative, and the audit that it is above 0 only in a continuous run. The lease gains an init-only `PollGeneration`, which a claim copies from the partition.
- **FR-002** `RecordConnectorHttpCaughtUpAsync` increments the generation in the same transaction that clears the journal. The final path does not.
- **FR-003** The page cache derives its manifest key from the page sequence, the request and, when it is above 0, the lease's generation.
  - A continuous run stores each page body under an identity derived from that key and the content hash.
  - Reading accepts the legacy shared body identity, so pages published before this slice stay readable.
- **FR-004** The partition record also gains a compacted summary:
  - the base page sequence;
  - the compacted batch count;
  - the compacted accepted, rejected and filtered record counts;
  - the compacted main and error effect counts.

  Every value is non-negative, defaults to 0 and has a database check. All are 0 in a finite run.
- **FR-005** On every claim in a continuous run, the store compacts every shard of the run in the claim transaction, after the claimed shard's audit. For a shard whose current request is *r*:
  - It clears the predecessor pointers of request *r* − 1 and every request below it, making request *r* − 1 the base.
  - It then deletes the batches, pages and requests below page *r* − 1, in that order.
  - It subtracts those rows' bytes from the shard's and the run's retained input, metadata and output counters.
  - It adds their counts to the summary.
- **FR-006** The request table's predecessor check admits a null predecessor above sequence 0. The audits allow it only on a base request. Changing the check needs a table rebuild:
  - The migration preserves every row, the alternate key (as a unique constraint) and every index under its EF name.
  - It leaves `PRAGMA foreign_key_check` empty.
- **FR-007** The transition, claim and settlement audits start at the base:
  - the HTTP history audit checks request and page contiguity from the base, and rebuilds the continuation chain from the base request;
  - the batch history audit checks ordinals from the compacted batch count, and validates accepted pages from the base;
  - each checks its counters against the retained sums plus the summary.

  A null predecessor on any request other than sequence 0 or the base is a corruption.
- **FR-008** The inspector's effect counts include the summary's, at both inspection sites.
- **FR-009** In a continuous run, the store and the page cache apply no authored page-sequence bound.
  - The store decodes a continuous run's pipeline with `MaxPagesPerPartition` raised to `int.MaxValue`, so every check site sees it, and the pinned artifact is unchanged.
  - A continuous shard may therefore accept up to 2³¹ − 1 pages over its life.
  - The cycle refusal checks the retained rows only.
- **FR-010** In a continuous run, the worker deletes files once the transition that ends their use commits:
  - after an acceptance, the batch's retained main and error route bodies;
  - after an advance, the cached manifest and per-page body of the page the shard left.

  Missing files are not errors. Legacy shared bodies are never deleted.
- **FR-011** Persistence stays compatible:
  - Finite artifacts, positions, page descriptors and cache keys are byte-identical.
  - A migration adds the summary columns and rebuilds the request table.
  - Rows written by the spec 1192 writer are read, audited and compacted correctly.
- **FR-012** The ETL README, the Core README and the affected `EXTENSION_POINTS.md` files describe the window, the generation, the continuous budget rules, the cycle rule and the orphan bound. Spec 1192's two declared limits point here.

### Key Entities

- **Retained window**: the rows and files a continuous shard keeps; see Edge Cases.
- **Compacted summary**: the counts that the deleted rows contributed, kept on the partition.
- **Base request**: the oldest retained request of a compacted shard, whose predecessor pointer is null.
- **Poll generation**: the count of caught-up polls a shard has recorded. It is part of the page-cache key.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001** A two-shard continuous run with `MaxPagesPerPartition` 3 accepts at least 40 non-empty pages per shard, each record exactly once, and keeps running.
  - Each shard never retains more than 2 pages and 3 requests.
  - Each shard's retained byte counters equal its retained rows' sums.
- **SC-002** The claim audit reads only a shard's retained rows, which SC-001 bounds independently of the pages accepted.
- **SC-003** The superseded-late-page interleaving completes without a run failure.
- **SC-004** A migration test with rows written by the spec 1192 schema preserves every row, leaves `PRAGMA foreign_key_check` empty, and then compacts and audits that run correctly.
- **SC-005** Every behavioural requirement has a test that fails when its guard is mutated, recorded as a bite-proof, except the guards the evidence README names as tested without a bite-proof or not tested.
- **SC-006** An actual Workbench journey shows a long continuous run whose retained rows and cached files stay bounded while delivery continues. In real worker processes killed repeatedly before acceptance, no record is lost or duplicated.
- **SC-007** Finite pipelines keep their bytes, identities, rows and limits. The whole ETL suite passes, changed only by intended additions.

## Assumptions and boundaries

- **In scope:** HTTP continuous runs, the only continuous source.
- **Out of scope:**
  - compaction of finite runs;
  - purging a terminal run's rows and files;
  - sweeping orphaned cache files;
  - Studio changes;
  - SQL, RabbitMQ and transforms, which are later #2908 slices.
- **Qualification:** the ETL stores are qualified on SQLite only, as before. Qualification on more engines is tracked separately.
- **Criterion 1** of #2908 is checked once this slice is merged and verified on `main`.
