# Evidence: Continuous HTTP ETL runs

Every receipt names the source revision it ran on. Each section states its limits.

## Actual Workbench journey (SC-001, SC-002, SC-006, US1, US2, US5)

The driver is [`continuous_workbench_journey.py`](continuous_workbench_journey.py), derived from spec 1191's driver (which stays unchanged as that spec's evidence).

**Setup.**
- Source `22df4effd`, clean tree. Debug Workbench built from that commit (.NET SDK 10.0.402), Python 3.14. Earlier runs at `e3dcb4c63` and `bb665fa53` passed the same checks.
- The Workbench ran on an ephemeral loopback port that the driver started and later stopped.
- The driver serves its own two-shard cursor feed from the same process: `?cursor=n` returns a shard's records after position `n`.
- It publishes a continuous HTTP → JSON Lines pipeline (poll interval 1 s) through the design API and runs it from a native Flowchart's `RunEtl` activity.

The derived summary is [`workbench-journey-summary.json`](workbench-journey-summary.json); the host URL and process id are removed from it.

| Check | Result |
|---|---|
| The live run reports `running` and connector mode `continuous` | passed |
| The driver waited for at least 20 caught-up polls after the first idle (26 caught-up responses over the whole case). Both shards' page sequences were still `[1, 0]` afterwards: no page was retained. Byte and row totals across polls are checked in-process, below | passed |
| `Ada` was in the feed before the run. `Bram` and `Cleo` were appended to different shards while it was live. Each reached the main route exactly once | passed |
| `POST /etl/runs/{id}/complete` returned `accepted`, then `alreadyRequested` | passed |
| The run settled `succeeded` (3 accepted). The Flowchart completed through `Done` and not `BusinessError` | passed |
| No outstanding records or bytes remained | passed |
| A second live run was cancelled: the run is `cancelled`, the workflow `Cancelled`, and completion afterwards returns `terminal` | passed |
| The run detail contains neither the host path nor the feed URL | passed |

**Limits.**
- SQLite stores only.
- The journey does not kill the host; kills are covered in real worker processes, below.

## Worker processes (SC-003, US3)

`EtlRuntimeJourneyTests.Continuous_worker_processes_resume_from_the_durable_cursor_after_a_kill` runs real child workers over one SQLite store.

- **Captured page.** The predecessor is killed at `BeforeAcceptance` after capturing shard `a`'s page. The replacement replays the page from the immutable page cache: the seed request is fetched exactly once. The run then idles, drains on completion and settles `Succeeded` with `Ada` and `Bram`, each once.
- **Idle poll.**
  1. The predecessor's first (seed) poll of shard `b` is held in flight, and the predecessor is killed.
  2. The replacement closes the uncertain attempt once it expires (request timeout 2 s) and polls the same cursor again.
  3. A record appended later is delivered once.
  4. Completion settles the run `Succeeded`.

## In-process journeys and API

Real worker and store, against a loopback cursor feed (`EtlRuntimeJourneyTests.Continuous`):

- **Delivery without growth.** Appended records are delivered. After 20 or more caught-up polls, page, request and batch counts, retained input, metadata and output bytes, and reservations are identical.
- **Drain race.** The poll interval is an hour. A poll that read the feed before the completion request answers caught up after the request. The shard is re-armed rather than ended, so a record appended in between is still delivered, and idle shards are woken.
- **Cancellation and refusals.** Cancellation settles `Cancelled`, also when a completion is pending but has not drained. At the store, completion reports `terminal`, `notContinuous` or `notFound` for a settled, finite or unknown run.
- **Business error.** A drained run with one rejected record settles `BusinessError` with `etl.rows-rejected`.
- **Moving cursor.** A cursor that moves without records waits one interval: with a 2 s interval, consecutive requests on each shard were at least 1.5 s apart (a hot loop would be milliseconds apart), both before and after a completion request, which wakes each waiting shard once.
- **Finite unchanged.** A finite run still fails `etl.http.pagination-cycle` on an empty page that repeats its cursor.

`EtlRuntimeApiHttpTests` (in `EtlContinuousRuntimeApiHttpTests.cs`) covers the completion endpoint:
- `etl/runs:complete` is required; `etl/runs:cancel` is refused with 403;
- a foreign scope gets 404;
- the results are `accepted`, `alreadyRequested`, `notContinuous` and `terminal`;
- only the idle shard is woken, and a 429 retry wait keeps its time;
- `nextPollAt` appears only on the idle shard, including while its worker still holds the lease.
- on a provider without a connector store, a v1 run answers `notContinuous` and a v2 run 503; a legacy `IEtlRuntimeOperations` replacement keeps the 503 default.

`Caught_up_records_need_the_live_lease_and_a_continuous_run_and_preparing_runs_accept_completion` refuses a caught-up record under a stale fence, from another attempt, outside its receipt window or for a finite run, and accepts completion during preparation. `A_finite_partition_may_not_wait_without_a_retry_journal` shows the audit still refuses an idle-style wait in a finite run.

## Studio (SC-005, US4)

The fixture-backed Playwright spec authors a continuous run (Run continuously, 30 s) at 320 px and 1024 px, with no axe violations on the authoring page (axe was not run on inspection or the Complete run action; those were checked in a real browser below): [320 px](studio/1192-studio-continuous-320.png), [1024 px](studio/1192-studio-continuous-1024.png).

Vitest covers:
- the editor's visibility rules;
- the inspection labels, with an idle shard's "Next poll at" kept apart from a retry wait's "Eligible after";
- the Complete run action, which appears only for a preparing or running continuous run that has not yet requested completion, and never for a host that does not report a mode.

## Real browser against the owned hosts (SC-005, US4)

The driver ran again with `--keep-host`. The owned Workbench was on 127.0.0.1:5393, still serving a live continuous run. An owned `Elsa.Studio.Web`, built from the same commit, was on 127.0.0.1:5394. Both were used through the built-in browser.

- [`studio/live-continuous-run.jpg`](studio/live-continuous-run.jpg): the run shows **Processing**, with **Complete run** next to **Cancel run**.
- [`studio/live-continuous-shards.jpg`](studio/live-continuous-shards.jpg): the run reads **Run mode Continuous**. Both shards are unclaimed and read "Caught up; polls again at …", with no retry scheduled.
- [`studio/live-continuous-completed.jpg`](studio/live-continuous-completed.jpg) and [`studio/live-continuous-completed.txt`](studio/live-continuous-completed.txt) (captured page text): after **Complete run** was confirmed, the page reads "Completion requested. The run settles once every shard has caught up." The run settled **Succeeded**, both shards read "source exhausted", the completion-request time is shown, and the actions are gone. The runtime API independently reported `succeeded` with `mode: continuous`.

Both owned processes were stopped. The owner's Workbench on 5095 was not touched.

This receipt ran at `e3dcb4c63`. Later review rounds changed the idle label from "Caught up; polls again at" (in these screenshots) to "Next poll at", and relaxed the inspection contract check so an idle shard that still shows a claim is accepted; neither changes what the receipt shows.

## Bite-proofs (SC-004)

[`bite-proofs.json`](bite-proofs.json) records every mutation at `e11275eb7`, with the failing test names, except the idle-invariant mutation, re-recorded at `4065f7856` after the round-3 revert changed its line, the moving-cursor and completion-after-terminal mutations, re-recorded at `3d227c08c` after round 3 edited their tests, and the connector-store mutation, recorded at `a41c556a4`. For each one the focused tests failed with the mutation in place and passed again after the file was restored from git.

| Mutation | Failing tests |
|---|---|
| Shared rule never refuses a file source | 2/13 |
| A null policy is serialized | 2/13 |
| An absent continuation on an empty page is not caught up | 1/1 |
| A caught-up record keeps the partition's input reservation | 1/1 |
| The audit refuses an idle partition's eligibility time | 1/1 |
| Completion ends a shard on a poll that started before the request | 1/1 |
| Completion also clears retry waits | 1/1 |
| A moving empty page is not delayed | 1/1 |
| The cache does not normalize a final page's cursor | 1/1 |
| A caught-up response may clear a non-in-flight journal | 1/7 |
| A repeated completion request is accepted again | 1/1 |
| Completion of a settled run is accepted | 1/1 |
| Completion requires only the cancel permission | 1/1 |
| Inspection rejects an idle shard that still shows a claim | 1/1 |
| A caught-up record ignores the lease fence | 1/1 |
| A caught-up receipt before the attempt started is accepted | 1/1 |
| Completion is accepted for a finite run | 1/1 |
| A retry wait reports a next poll | 1/1 |
| A v1 run on a provider without a connector store reports unavailable instead of `notContinuous` (round 5, at `a41c556a4`) | 1/1 |
| Studio offers Complete run for every running run | 1/14 |

**Tested without a bite-proof:** cancellation winning over a pending completion (it reuses the unchanged first-terminal settlement); the stale-attempt refusal of a caught-up record; the pagination and interval codes of the shared rule (the bite-proof covers the file-source code); completion accepted during preparation; the 404 for a foreign scope; `notContinuous` taking precedence for a settled finite run; the 503 for a v2 run without a connector store and the legacy operations default; the API's `mode` and `completionRequestedAt` projection; the completion migration's null column and CHECK; the editor's visibility rule; Studio's inspection labels (run mode, completion requested, "Next poll at" apart from "Eligible after") and the post-completion message.

**Not tested:** the acquirer's cache re-read before recording a caught-up response, and the publish-conflict recovery.

## Artifact reviews

**Spec review.** Independent, before implementation. It found three blockers, all adopted:
- a shard ends only through an exhausted position with a retained page;
- a partition's eligibility time must match its fetch journal;
- a "fresh" journal can only be a cleared one, with its reservations released.

It also found eight further issues, all adopted:
- classification order;
- the drain race;
- the moving-empty hot loop;
- the feed contract;
- idle-poll cost;
- store-clock semantics;
- completion API details;
- the byte-identity mechanism.

**Plan review.** No blockers; eight findings, all adopted:
- strict attempt liveness;
- the cache's continuous flag;
- publish-conflict recovery;
- inspection labels;
- 404/503 semantics;
- the API and Studio surface;
- a raw-SQL migration test;
- complete bite-proof coverage.

## Not covered here

- **Compaction** of accepted pages, requests, batches and retained bytes, which bounds a continuous run's lifetime. It is a follow-up slice, and criterion 1 stays unchecked until it lands. Addressed by spec [1196](../../1196-etl-continuous-compaction/spec.md).
- **Publish-conflict recovery race.** The acquirer captures the winning manifest when a predecessor publishes a page under the same request identity after the successor's re-read. This path is implemented but not exercised by a test.
- **Superseded late pages.** A superseded attempt that is still in flight when a successor records a caught-up response can later publish a page whose attempt is no longer in the cleared journal; the whole run then fails closed with `etl.http.page-provenance-invalid` (every shard stops and `RunEtl` resumes with that failure) rather than accepting the page. This needs a worker stall longer than its lease while the feed serves the stalled request late, with records appended in between. A poll-generation page identity removes it: spec [1196](../../1196-etl-continuous-compaction/spec.md).
- **Host clocks.** Whether a caught-up response ends a draining shard compares the completion host's clock with the worker's, so the hosts' clocks must agree, as leases already require.
- **Native completion activity.** A native workflow cannot request completion itself.
- **Other continuous sources:** RabbitMQ, SQL and file drops.
- **Other stores.** ETL stores are not qualified on databases other than SQLite.
