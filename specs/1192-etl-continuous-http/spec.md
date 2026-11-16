# Feature Specification: Continuous HTTP ETL runs

**Feature Branch**: `agent/claude/2908-continuous-http` (stacked on `agent/claude/2908-csv-delivery`)

**Created**: 2026-11-02

**Status**: Implemented — [PR #3670](https://github.com/elsa-workflows/elsa-foundation/pull/3670); [evidence](evidence/README.md)

**Input**: The continuous half of the first criterion of [#2908](https://github.com/elsa-workflows/elsa-foundation/issues/2908): "Finite and continuous runs process two or more independently leased partitions with resumable cursors and model-owned completion/cancellation. Coordinator execution does not process one workflow per record." It belongs to [Program #2892](https://github.com/elsa-workflows/elsa-foundation/issues/2892) ("finite batches and continuous event integration"). [Claim](https://github.com/elsa-workflows/elsa-foundation/issues/2908#issuecomment-6582649901).

## Context

Every v2 connector run is finite today:
- the partition plan is fixed when preparation commits;
- an HTTP shard ends when its current retained page has no continuation (an exhausted position);
- the run settles itself once every partition is exhausted and nothing is outstanding;
- the native `RunEtl` activity waits for that single terminal result.

The finite half of the criterion is met by file and HTTP runs over two or more leased partitions. The continuous half has no implementation.

This slice adds one continuous mode, for an HTTP cursor feed. The pipeline's HTTP shards are its partitions.
- **Reading.** Each shard reads pages as it does today.
- **Catching up.** When a shard catches up, it records nothing but a wait. Its poll journal is cleared, its reservations are released and the partition waits for the poll interval before requesting the same cursor again.
- **Ending.** The run continues until its model is asked to stop:
  - **Completion** drains each shard to caught-up. The final caught-up response is then retained as the shard's last page, so the shard ends through the existing exhausted path. The run settles through the existing outcome rules.
  - **Cancellation** settles `Cancelled`, as today.

The worker, lease, fence, effect, acceptance and settlement machinery is reused unchanged. A continuous run processes bounded batches over leased partitions, never one workflow per record.

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Keep ingesting from a cursor feed (Priority: P1)

An author publishes a pipeline that reads an HTTP cursor feed through two host-configured shards, and runs it continuously. Records that the feed gains after the run starts reach the routes without a new run.

**Why this priority**: this is the continuous behaviour the criterion asks for.

**Independent Test**: start a continuous run against a local feed with two shards, then append records to each shard over time. Every appended record must reach the routes, accepted exactly once, while the run stays `Running`.

**Acceptance Scenarios**:

1. **Given** a continuous run whose two shards have caught up, **When** the feed appends records to one shard, **Then** that shard's next poll delivers them, the other shard keeps idling, and the run stays `Running`.
2. **Given** a caught-up shard, **When** it polls again with nothing new, **Then** after the store records that poll:
   - the shard's page, request and batch counts are unchanged;
   - its retained input, output and metadata bytes are unchanged;
   - its input and source-metadata reservations are back at zero;
   - only the shard's next-poll time has moved.
3. **Given** the same pipeline without a run policy, **When** it runs, **Then** it behaves exactly as today: finite, with the same artifact bytes, identities and outcomes.

### User Story 2 — Complete or cancel a continuous run (Priority: P1)

An operator ends a continuous run.
- Completion processes what the feed holds when the request is made, then settles.
- Cancellation stops the run.

**Why this priority**: the criterion requires model-owned completion and cancellation, and a continuous run cannot end without them.

**Independent Test**:
1. Request completion while one shard idles and the other has records waiting.
2. Check that both shards drain, the run settles `Succeeded`, and the native workflow resumes through `Done`.
3. Cancel a second run and check that it settles `Cancelled`.

**Acceptance Scenarios**:

1. **Given** a continuous run, **When** completion is requested, **Then**:
   - idling shards become due at once;
   - each shard ends at its first caught-up response from an attempt started after the request;
   - the run settles `Succeeded`, or `BusinessError` with `etl.rows-rejected` if any record was rejected.
2. **Given** a caught-up response from an attempt started before the completion request, **When** the store records it, **Then** the shard is due again at once rather than ended, so that records appended in between are not lost.
3. **Given** a completion request, **When** it is repeated, **Then** it is idempotent.
4. **Given** a terminal continuous run, **When** completion is requested, **Then** the result is `terminal`, and nothing changes.
5. **Given** a finite run, **When** completion is requested, **Then** the result is `notContinuous`, and nothing changes.
6. **Given** a continuous run with a completion request, **When** it is cancelled before it drains, **Then** it settles `Cancelled`, because the first terminal outcome wins.

### User Story 3 — Recover a continuous run after worker loss (Priority: P1)

A worker dies mid-poll or mid-batch, and a replacement continues from the shard's durable cursor.

**Why this priority**: the criterion requires resumable cursors, and a long-lived run must outlive its workers.

**Independent Test**: in real worker processes over one runtime store:
- kill a worker after a non-empty page is captured and before acceptance;
- kill a worker while its idle poll's attempt is in flight.

Check that the replacement continues without losing or duplicating accepted records.

**Acceptance Scenarios**:

1. **Given** a worker killed after a non-empty page is captured, **When** a replacement claims the shard, **Then** it replays that page from the immutable page cache and does not fetch it again.
2. **Given** a worker killed during an idle poll's attempt, **When** the attempt expires and a replacement claims the shard, **Then** the poll is retried under the existing uncertain-attempt rules. This is safe, because a caught-up response carries no records.
3. **Given** a shard between polls, **Then** it has no owner, because its lease was released. Any worker may claim it once it is due.

### User Story 4 — Author and operate a continuous run in Studio (Priority: P2)

An author turns on continuous mode and sets a poll interval. An operator sees that a run is continuous, which shards are idling and until when, and can request completion.

**Why this priority**: the parent requires Studio authoring and inspection, and completion needs a visible action next to Cancel.

**Independent Test**: component and browser tests cover the authoring controls. A real browser, against owned hosts, shows a continuous run's inspection and requests its completion.

**Acceptance Scenarios**:

1. **Given** an HTTP source with token or next-link pagination, **When** the author turns on continuous mode, **Then** they can set the poll interval in seconds, and the saved artifact carries the run policy.
2. **Given** a file source or single-page pagination, **Then** continuous mode is not offered.
3. **Given** a running continuous run, **When** an operator inspects it, **Then**:
   - the run is labelled "Continuous";
   - an idling shard shows "Next poll at …", which is distinct from a retry wait's "Eligible after …";
   - "Complete run" is offered next to "Cancel run";
   - once completion is requested, the run reads "Completion requested".

### User Story 5 — Run it from a workflow on a real host (Priority: P2)

A native Flowchart starts a continuous run, which is later completed through the runtime API. The workflow resumes with the run's outcome.

**Independent Test**: an actual Workbench journey driver against a local cursor-feed server with two shards:
1. start the run;
2. append records twice;
3. check delivery and idle polling;
4. request completion and check that the Flowchart ends through `Done`.

A second run is cancelled and ends `Cancelled`.

### Edge Cases

- **What a caught-up response is.** In a continuous run, an HTTP 200 page is caught up when it has zero records and its continuation is either absent or resolves to the request it answers. Resolution uses the same URI equality as the pagination-cycle check.
  - Classification happens after decoding and before continuation resolution, so a caught-up page never reaches `etl.http.pagination-cycle`.
  - Before recording a non-final caught-up response, the acquirer re-reads the page cache. A page that a predecessor published wins.
- **A page with records always advances.**
  - If its continuation does not move, it still fails with `etl.http.pagination-cycle`.
  - If it has no continuation, the feed has declared that shard's end, and the shard ends as today. The feed contract therefore requires a continuation on every non-empty page for a shard to stay live. The README and the Studio help state this.
- **A zero-record page whose continuation moves** advances as today, but in a continuous run the next request waits for one poll interval. This prevents a hot loop. Such a page consumes one page of budget per poll; the README states this.
- **Unsupported configurations** are refused at validation:
  - single-page pagination, with `etl.continuous-pagination-unsupported`;
  - file sources (CSV and JSON Lines), with `etl.continuous-source-unsupported`;
  - a poll interval outside 1 to 86,400 seconds, with `etl.continuous-poll-interval-invalid`.
- **Retries and rate limits** within one poll keep the existing attempt and deadline rules. Because a caught-up response clears the journal, idle time never spends retry budget. A feed outage longer than the fetch deadline (`DeadlineSeconds`, 900 by default) still fails the run. The README states this.
- **A completion request during work.** A shard holding an owed batch or an attached page is not interrupted. It ends at its first qualifying caught-up response after that work is accepted.
- **A completion request during preparation** is recorded. The shards it creates poll at once and drain as usual.
- **An exhausted shard stays exhausted.** Completion does not reopen it, and a run whose every shard is exhausted settles as a finite run would.
- **Clock.** A shard's next-poll time is the store's own time when it records the caught-up response, plus the interval. Claims compare against the same store clock. Whether a caught-up response qualifies for completion compares the request time, stamped by the host that accepted the request, with the attempt's start, stamped by the worker. Like lease expiry, this assumes the hosts' clocks agree; skew moves the cutoff by the skew.
- **Cost of an idle poll.** Each poll claims the partition, and every claim audits the shard's retained history. A poll adds no state, but its cost grows with that history until compaction lands.
- **Fairness.** Claims are ordered by run creation time, so a long-lived continuous run's due shards are claimed before shards of newer runs. Due shards are claimed only once their interval has passed, so idle shards hold no worker slots. A continuous run whose shards are never idle, because its feed keeps them busy, can however keep every connector slot in its scope and delay newer runs.
- **Lifetime budgets.** Until compaction lands, these still accumulate for each accepted non-empty page:
  - pages and requests;
  - batches;
  - retained input, metadata and output bytes.

  A continuous run that reaches `MaxPagesPerPartition` or a retention budget fails with the existing codes. Caught-up polls consume none of these budgets.
- **Mixed-version fleets.** A runtime without this slice refuses a continuous artifact, because unknown members are disallowed. All workers in a scope must run this slice before a continuous run is admitted. This is the same rule specs 1190 and 1191 state.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001** A v2 pipeline may carry an optional run policy holding continuous mode and a poll interval in seconds. Without it the run is finite.
  - The policy is an init-only property, omitted from serialization when null, following the default-budget precedent in `EtlArtifactV2Json`. The positional record's public constructor is unchanged.
  - A pipeline without the policy therefore serializes byte-identically to today, so its artifact hash, invocation fingerprint, and batch, request and page identities are unchanged.
  - An explicit `null` on read is accepted as absent.
- **FR-002** The policy is validated wherever the pipeline is read, using one shared rule (`EtlContinuousRunPolicy.FindViolation`):
  - the executable reader refuses with the codes listed in Edge Cases;
  - the runtime codec refuses at admission;
  - the design compiler reports the same codes without the `etl.` prefix, at path `continuous`, matching its existing diagnostic convention.
- **FR-003** A continuous HTTP run plans its partitions exactly as a finite one does: one per host-pinned shard, two or more when the host configures them. Each is leased, fenced and heartbeat-renewed independently.
- **FR-004** The acquirer classifies a caught-up response as described in Edge Cases.
  - Without a qualifying completion request, it records the response through a new store transition.
  - The transition first validates the attempt as capture does: it must be the latest attempt under the current fence, received within `[StartedAt, min(ExpiresAt, Deadline))`, and not after the store's time.
  - Then, in one transaction, it:
    - clears the request's poll journal;
    - releases the request's, partition's and connector run's input reservation, fetch metadata and source-metadata reserve;
    - sets the partition's next-eligible time to the store's time plus the interval.

    It adds no page, request or batch row.
  - Known limit: a superseded attempt that is still in flight when a successor records a caught-up response can later publish a page whose attempt is no longer in the cleared journal; the whole run then fails closed with `etl.http.page-provenance-invalid` (every shard stops and `RunEtl` resumes with that failure) rather than accepting the page. This needs a worker stall longer than its lease while the feed serves the stalled request late, with records appended in between. A poll-generation page identity removes it: spec [1196](../1196-etl-continuous-compaction/spec.md).
  - The position is unchanged, so the execution's normal release frees the lease.
- **FR-005** The audit invariants accept the idle state.
  - In a continuous run, a partition with no owed batch, no attached page, a cleared journal and a next-eligible time is idle, and that time is valid.
  - In a finite run, the existing invariant is unchanged: the next-eligible time equals the waiting journal's time, or is null.
  - When an idle shard becomes due, a worker begins a fresh journal for the same request identity, exactly as for a first fetch.
- **FR-006** With a qualifying completion request, the acquirer retains the caught-up response as the shard's final page instead.
  - A qualifying request is one made at or before the attempt's start time.
  - The response is decoded with its continuation normalized to absent: a caught-up continuation is a no-op by definition. The cache applies the same normalization on every read, so replay is deterministic.
  - It is published, captured and attached, then advanced through the existing empty-page path, which exhausts the shard.
  - A caught-up response from an earlier attempt is recorded as in FR-004, but the partition becomes due at once.
- **FR-007** `POST /etl/runs/{id}/complete` records a durable completion request on a continuous run, under a new `etl/runs:complete` permission.
  - It is described by an `etl-run-complete` resource entry, alongside `etl-run-cancel`.
  - It is implemented by a new `IEtlRuntimeOperations` member that has a default body.
  - It returns `200` with an `EtlRunCompletionView` whose result is one of:
    - `accepted`: the run is preparing or running;
    - `alreadyRequested`;
    - `terminal`;
    - `notContinuous`.
  - An unknown run, or one in another scope, is `404` through the same scoped lookup as cancellation.
  - `notContinuous` takes precedence over `terminal`: a finite run answers `notContinuous` before or after it settles, so the answer does not race settlement.
  - A v1 file run answers `notContinuous` even on a provider without a connector store; a v2 run there answers `503 etl.provider-unavailable`, as run detail does.
  - On `accepted`, it clears the next-eligible time of every idle partition in the same transaction. Partitions waiting on a retry are left alone.
- **FR-008** Once every partition is exhausted and nothing is outstanding, the existing completion rules settle the run: `Succeeded`, or `BusinessError` with `etl.rows-rejected`. The terminal result and outbox are written in the same transaction, as today.
- **FR-009** Cancellation and fault settlement are unchanged, and the first terminal outcome wins over a pending completion request.
- **FR-010** The native `RunEtl` activity is unchanged.
  - It waits for the single terminal result, so a continuous run keeps it suspended until the run settles.
  - Outcomes map as today.
- **FR-011** The runtime API and inspection add:
  - the run mode, `finite` or `continuous`;
  - the completion-request time;
  - for an idle partition, its next-poll time, as a field distinct from the existing retry-wait `notBefore`.

  Existing fields are unchanged.
- **FR-012** Studio:
  - The connector editor offers continuous mode and a poll interval for HTTP sources with token or next-link pagination only.
  - Run inspection labels a continuous run and shows each idle shard's next poll. It offers "Complete run" next to "Cancel run" while a continuous run is preparing or running, and shows "Completion requested" afterwards.
- **FR-013** Persistence remains compatible.
  - The completion-request time is a new nullable column on `elsa_etl_connector_runs`, added by a new SQLite runtime migration and model snapshot. Prior rows read it as null.
  - Before the column is added, a prior writer's finite HTTP run is captured and read back unchanged.
  - The run status, terminal outcome, fetch phase and position enumerations gain no members. Position and fetch JSON shapes are unchanged.
  - The module `SchemaVersion` changes only if the repository's migration rules require it for an additive nullable column. Research R5 records the rule that applies: additive migrations keep `"1.0.0"`.
- **FR-014** The ETL README, the Core README and the affected `EXTENSION_POINTS.md` files describe:
  - continuous mode;
  - completion;
  - the feed contract;
  - the idle-poll cost;
  - the deadline and lifetime-budget limits.

### Key Entities

- **Run policy**: an optional pipeline member holding the mode and the poll interval.
- **Idle partition**: in a continuous run, a partition with no owed batch, no attached page and a cleared journal, whose next-eligible time is set.
- **Completion request**: a nullable time on the connector run.
- **Final caught-up page**: the retained, zero-record page whose continuation is normalized to absent, and which ends a draining shard.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001** A continuous run with two shards delivers every record appended after it started, each accepted exactly once across both shards. After 20 or more caught-up polls, every count and byte total named in US1 scenario 2 equals its value before them, and every store read in between re-runs the accounting audit.
- **SC-002**
  - Completion drains the run and settles it `Succeeded`, and the native Flowchart resumes through `Done`.
  - A caught-up response from an attempt started before the completion request does not end the shard.
  - Cancellation settles `Cancelled`.
  - Completion of a finite or a terminal run is refused.
- **SC-003** In real worker processes, a kill after capture replays the page from the cache, and a kill during an idle poll's attempt retries the poll. Accounting is exact in both cases.
- **SC-004** Every behavioural requirement has a test that fails when its guard is mutated, recorded as a bite-proof, except the guards the evidence README names as tested without a bite-proof or not tested.
- **SC-005** Studio authoring works in a fixture-backed real browser with no axe violations at 320px and 1024px; inspection and "Complete run" work in a real browser against owned hosts (without axe), as the evidence README states.
- **SC-006** An actual Workbench journey shows continuous delivery, idle polling, completion and cancellation.
- **SC-007** Finite pipelines keep their artifact bytes and identities. The whole ETL suite passes, changed only by intended additions.

## Assumptions and boundaries

- **Feed contract.** The feed is a cursor feed: re-requesting a cursor returns the records after it, and every non-empty page carries a continuation. When nothing is new, the feed returns an empty page whose continuation is absent or is the same cursor. A collection that re-lists everything is not a continuous source. A superseded attempt's late page can fail the run closed (FR-004, known limit). Spec [1196](../1196-etl-continuous-compaction/spec.md) addresses it with a poll-generation page identity.
- **Bounded state.** Compacting accepted pages, requests, batches and retained bytes, so that a continuous run's lifetime is unbounded and its idle-poll cost bounded, is a separate follow-up slice. Criterion 1 is checked only after it lands. Addressed by spec [1196](../1196-etl-continuous-compaction/spec.md).
- **Native completion.** No workflow activity requests completion in this slice; the runtime API and Studio do. Native cancellation already works. A native completion activity belongs with the BPMN and Studio criterion.
- **Other continuous sources.** RabbitMQ, SQL watermarks and file-drop folders get their own slices.
- **Deferred.** Orleans, the task-runner host, log CDC, distributed shuffle, windows and stream joins remain deferred.
