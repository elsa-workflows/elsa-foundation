# Feature Specification: Continuous HTTP ETL runs

**Feature Branch**: `agent/claude/2908-continuous-http` (stacked on `agent/claude/2908-csv-delivery`)

**Created**: 2026-11-02

**Status**: Draft

**Input**: The continuous half of the first criterion of [#2908](https://github.com/elsa-workflows/elsa-foundation/issues/2908): "Finite and continuous runs process two or more independently leased partitions with resumable cursors and model-owned completion/cancellation. Coordinator execution does not process one workflow per record." It belongs to [Program #2892](https://github.com/elsa-workflows/elsa-foundation/issues/2892) ("finite batches and continuous event integration"). [Claim](https://github.com/elsa-workflows/elsa-foundation/issues/2908#issuecomment-6582649901).

## Context

Today every v2 connector run is finite:
- the partition plan is fixed when preparation commits;
- an HTTP shard completes when a page returns no continuation;
- the run settles itself once every partition is complete;
- the native `RunEtl` activity waits for that single terminal result.

The finite half of the criterion is met by file and HTTP runs that span two or more leased partitions. The continuous half has no implementation, and every ETL document still calls itself finite.

This slice adds one continuous mode for an HTTP cursor feed. The pipeline's HTTP shards are its partitions:
- Each shard reads pages as it does today.
- When a shard catches up, it does not complete. It waits for a poll interval, then requests the same cursor again.
- The run continues until its model is asked to stop.
  - **Completion** drains every shard to caught-up, then settles through the existing outcome rules.
  - **Cancellation** settles `Cancelled` as it does today.

The worker, lease, fence, effect and acceptance machinery stays unchanged. A continuous run still processes bounded batches over leased partitions, never one workflow per record.

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Keep ingesting from a cursor feed (Priority: P1)

An author publishes a pipeline that reads an HTTP cursor feed, with two host-configured shards, and runs it continuously. Records the feed gains after the run starts are delivered to the routes without starting a new run.

**Why this priority**: this is the continuous behaviour the criterion asks for.

**Independent Test**: start a continuous run against a local feed with two shards. Append records to each shard over time, and check that every appended record is delivered exactly once to the routes while the run stays `Running`.

**Acceptance Scenarios**:

1. **Given** a continuous run whose two shards have caught up, **When** the feed appends records to one shard, **Then** that shard's next poll delivers them and the other shard keeps idling. The run stays `Running`.
2. **Given** a shard that is caught up, **When** it polls repeatedly with nothing new, **Then** no page, request, batch or retained byte is added for those polls. Only the shard's wait time and its poll journal change.
3. **Given** the same pipeline without a run policy, **When** it runs, **Then** it behaves exactly as today: finite, with the same artifact bytes, identities and outcomes.

### User Story 2 — Complete or cancel a continuous run (Priority: P1)

An operator, or a workflow, ends a continuous run. Completion processes what the feed holds when the request is made and then settles. Cancellation stops it.

**Why this priority**: the criterion requires model-owned completion and cancellation. Without them a continuous run cannot end.

**Independent Test**: request completion of a running continuous run while one shard idles and the other has records waiting, then check:
- both shards drain;
- the run settles `Succeeded`;
- the native workflow resumes through `Done`.

Cancel a second run and check that it settles `Cancelled`.

**Acceptance Scenarios**:

1. **Given** a continuous run, **When** completion is requested, **Then**:
   - idling shards poll again without waiting for their interval;
   - each shard completes at its next caught-up response;
   - the run settles `Succeeded`, or `BusinessError` if any record was rejected.
2. **Given** a completion request, **When** it is repeated, **Then** it is idempotent.
3. **Given** a terminal run, **When** completion is requested, **Then** the request is refused with a terminal status and changes nothing.
4. **Given** a finite run, **When** completion is requested, **Then** the request is refused and the run is unchanged.
5. **Given** a continuous run with a completion request, **When** it is cancelled before it drains, **Then** it settles `Cancelled`, because the first terminal outcome wins.

### User Story 3 — Recover a continuous run after worker loss (Priority: P1)

A worker dies while a shard is fetching, reading a page, delivering it, or idling. A replacement continues from the shard's durable cursor.

**Why this priority**: the criterion requires resumable cursors; a continuous run is long-lived and must outlive workers.

**Independent Test**: in real worker processes over one runtime store, kill a worker:
- after a page is captured and before acceptance;
- while a shard idles.

In both cases, check that the replacement continues from the durable cursor without losing or duplicating accepted records.

**Acceptance Scenarios**:

1. **Given** a worker killed after a non-empty page is captured, **When** a replacement claims the shard, **Then** it replays that page from the immutable page cache and does not fetch it again.
2. **Given** a worker killed while its shard idles, **When** the shard becomes due, **Then** any worker polls the same cursor. Nothing from before the kill is repeated.
3. **Given** a caught-up response that was not yet recorded when the worker died, **When** the shard is claimed again, **Then** the poll happens again. That is safe because a caught-up response carries no records.

### User Story 4 — Author and operate a continuous run in Studio (Priority: P2)

An author turns on continuous mode and sets a poll interval. An operator inspecting the run sees that it is continuous, sees which shards are idling and until when, and can request completion.

**Why this priority**: the parent needs Studio authoring and inspection; completion needs a visible action next to Cancel.

**Independent Test**:
- A component test and a browser test cover the authoring controls.
- A real browser, against owned hosts, shows a continuous run's inspection and requests its completion.

**Acceptance Scenarios**:

1. **Given** an HTTP pipeline, **When** the author turns on continuous mode, **Then** they set a poll interval in seconds, and the saved artifact carries the run policy.
2. **Given** a file source, **When** the author views the run controls, **Then** continuous mode is not offered.
3. **Given** a running continuous run, **When** an operator inspects it, **Then**:
   - the run is labelled "Continuous";
   - each idling shard shows when it polls next;
   - a "Complete run" action appears next to "Cancel run".

### User Story 5 — Run it from a workflow on a real host (Priority: P2)

A native Flowchart starts a continuous run, and the run is later completed through the runtime API. The workflow resumes with the run's outcome.

**Independent Test**: an actual Workbench journey driver, against a local cursor-feed server with two shards:
1. Start the run.
2. Append records twice.
3. Check delivery and idle polling.
4. Request completion, then check that the Flowchart ends through `Done`.

A second run is cancelled and ends `Cancelled`.

### Edge Cases

- **A caught-up response** is an HTTP 200 page with zero records whose continuation is either absent or resolves to the request it answers.
  - It is not published to the page cache, and it does not advance the cursor.
  - It resets the shard's poll journal and schedules the next poll for one interval later.
  - A page with records always advances: a continuation that does not move still fails with `etl.http.pagination-cycle`.
- **A page with records and no continuation** ends that shard as it does today, because the feed has declared its end. The run continues while any other shard is live. A run whose every shard has ended settles as a finite run would.
- **A zero-record page whose continuation moves** advances immediately, as it does today. It is not a caught-up response.
- **Single-page pagination** has no cursor to resume from, so it is refused for continuous mode at validation with `etl.continuous-pagination-unsupported`.
- **File sources** (CSV and JSON Lines) are refused for continuous mode with `etl.continuous-source-unsupported`. Their inputs are pinned immutable snapshots.
- **Retries and rate limits** inside one poll keep their existing attempt and deadline rules. A caught-up response starts the next poll with a fresh journal, so idle time never spends retry budget.
- **A completion request** arriving while a shard holds an owed batch or an attached page does not interrupt it. The shard completes at its next caught-up response, after that work is accepted.
- **A shard that ended**, because a page had records and no continuation, stays complete. Completion does not reopen it.
- **The poll interval** is between 1 and 86,400 seconds. Values outside that range are refused at validation.
- **Lifetime budgets**: until compaction lands, the following still accumulate per accepted non-empty page:
  - pages and requests;
  - batches;
  - retained input, metadata and output bytes.

  A continuous run that reaches `MaxPagesPerPartition` or a retention budget settles with the existing failure codes. Caught-up polls consume none of these.
- **Mixed-version fleets**: a runtime without this slice refuses a continuous artifact, because unknown members are disallowed. All workers in a scope must run this slice before any continuous run is admitted. That is the same rule specs 1190 and 1191 state for their new shapes.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001** A v2 pipeline may carry an optional run policy. Absent means finite.
  - When present it states continuous mode and a poll interval in seconds.
  - A pipeline without the policy serializes byte-identically to today, so artifact hashes, invocation fingerprints, batch IDs and request and page identities are unchanged.
- **FR-002** The policy is validated wherever it is read: the design compiler, the executable reader and the runtime codec.
  - It is refused for file sources (`etl.continuous-source-unsupported`).
  - It is refused for single-page pagination (`etl.continuous-pagination-unsupported`).
  - An out-of-range interval is refused (`etl.continuous-poll-interval-invalid`).
- **FR-003** A continuous HTTP run plans its partitions exactly as a finite run does: one per host-pinned shard, two or more when the host configures them. Each partition is leased, fenced and heartbeat-renewed independently.
- **FR-004** In a continuous run, a caught-up response (defined under Edge Cases) is recorded by the store in one transaction under the current lease and fence. It:
  - closes the attempt with a caught-up outcome;
  - replaces the request's poll journal with a fresh one;
  - sets the partition's next-eligible time to the response time plus the poll interval;
  - releases the lease.

  It adds no page, request or batch row, and no retained or reserved bytes. Metadata accounting moves from the old journal to the new one, and every existing accounting invariant still holds.
- **FR-005** A due idle partition is claimable by any worker, which issues a new attempt for the same request identity. Request, page and batch identities for non-empty pages are derived as today, so replay and duplicate detection are unchanged.
- **FR-006** `POST /etl/runs/{runId}/complete` records a durable completion request on a running continuous run, under the same authorization as cancel.
  - It returns:
    - `accepted` the first time;
    - `alreadyRequested` on a repeat;
    - `terminal` for a settled run;
    - `notContinuous` for a finite run, which is refused.
  - The request clears every idle partition's next-eligible time, so each polls promptly.
- **FR-007** With completion requested, a caught-up response completes its partition instead of re-arming it. Once every partition is complete and nothing is outstanding, the existing completion rules settle the run: `Succeeded`, or `BusinessError` with `etl.rows-rejected`. The terminal result and outbox are written in that same transaction.
- **FR-008** Cancellation and fault settlement are unchanged for continuous runs, and the first terminal outcome wins over a pending completion request.
- **FR-009** The native `RunEtl` activity is unchanged:
  - it waits for the single terminal result;
  - a continuous run keeps it suspended until the run settles;
  - outcomes map as today.
- **FR-010** The runtime API and inspection report:
  - the run mode, `finite` or `continuous`;
  - whether completion has been requested;
  - each partition's next-poll time while it idles.
- **FR-011** In Studio:
  - The connector editor offers continuous mode, with a poll interval, for HTTP sources only.
  - Run inspection labels a continuous run, shows when each idling shard polls next, and offers "Complete run" next to "Cancel run" while the run is running and continuous.
- **FR-012** Persistence stays compatible:
  - New state lives in additive nullable columns, so prior rows read unchanged. One example is the completion-request time on the connector run.
  - Prior writers' rows are captured before the format changes.
  - The run status, terminal outcome and position enumerations gain no members.
- **FR-013** The ETL README, the Core README and the affected `EXTENSION_POINTS.md` files describe continuous mode, completion, the caught-up contract a feed must follow, and the lifetime-budget limit.

### Key Entities

- **Run policy**: optional pipeline member holding the mode and the poll interval.
- **Caught-up outcome**: a fetch outcome that re-arms, or with completion requested completes, a partition without a page.
- **Completion request**: a nullable time on the connector run.
- **Idle partition**: incomplete, with no owed batch and no attached page, and a future next-eligible time.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001** A continuous run with two shards delivers every record appended after it started, accepted exactly once across both shards. Over 20 or more caught-up polls, page, request, batch and retained-byte counts do not change.
- **SC-002** Completion drains and settles `Succeeded`, then the native Flowchart resumes through `Done`. Cancellation settles `Cancelled`. Completion of a finite or terminal run is refused.
- **SC-003** In real worker processes, a kill after capture replays from the page cache, and a kill while idling resumes from the same cursor, each with exact accounting.
- **SC-004** Every behavioural requirement has a test that fails when its guard is mutated, recorded as a bite-proof.
- **SC-005** Studio authoring and inspection, including "Complete run", work in a real browser against owned hosts, with no axe violations at 320 px and 1024 px.
- **SC-006** An actual Workbench journey shows continuous delivery, idle polling, completion and cancellation end to end.
- **SC-007** Finite pipelines keep their artifact bytes and identities. The whole ETL suite passes unchanged apart from intended additions.

## Assumptions and boundaries

- **Feed contract.** The feed is a cursor feed. Re-requesting a cursor returns the records after it. When nothing is new, the feed returns an empty page whose continuation is absent or the same cursor. Feeds that cannot meet this, such as a collection that re-lists everything, are not continuous sources.
- **Bounded state is a follow-up.** Compacting accepted pages, requests, batches and retained bytes, so that a continuous run's lifetime is unbounded, is a separate follow-up slice. Criterion 1 is checked only after it lands.
- **Native completion is out of scope.** A workflow cannot request completion through an activity in this slice. The runtime API and Studio can, and native cancellation already works. A native completion activity belongs with the BPMN and Studio criterion.
- **Other continuous sources come later.** RabbitMQ, SQL watermarks and file-drop folders get their own slices.
- **Deferred:** Orleans, the task-runner host, log CDC, distributed shuffle, windows and stream joins remain deferred.
