# Runtime command cost, October 2026

Epic [#2564](https://github.com/elsa-workflows/elsa-foundation/issues/2564), [ADR 0080 D2](../adr/0080-elsa-4-simplification-decisions.md#d2--persistence-modes-balanced-by-default).
Measured on `origin/main` at `64c91e945` plus the measurement tool. Every number in this report is a deterministic count of
EF commands, SQL statements or network round trips. Nothing is timed.

## Summary

- A Balanced run (today's Coalesced mode with the segment cap out of reach) of 10 WriteLines costs **98 EF commands on
  SQLite** and **79 round trips on PostgreSQL**. A Durable run (today's Immediate mode) costs **1482** and **1646**.
- Every cost source is exactly linear in the number of activities (residual 0 over 5, 10, 20 and 40 WriteLines), so
  each one is cleanly per activity, per commit or per run.
- In Balanced, the only per-activity costs are **3 reads that answer nothing** for a run that has never been persisted
  (two point reads that miss and one list of the whole run) and, on SQLite only, **2 row inserts** in the final commit. On PostgreSQL the inserts share one
  batched round trip, so Balanced costs 3 round trips per activity, all of them reads.
- A prototype in which a run answers reads of its own rows from its coalescing session removes those reads. Balanced
  10x WriteLine drops to **65 commands on SQLite** and **46 round trips on PostgreSQL**, and on PostgreSQL the cost no
  longer grows with the number of WriteLines at all (46 round trips for 5, 10, 20 and 40). Its correctness rests on the
  ownership fence, which has holes; they are named below.
- Durable is dominated by per-commit and per-hop plumbing: 6 commits per WriteLine, each costing about 10 statements on
  SQLite and 12 round trips on PostgreSQL, of which 4 are EF's automatic savepoints.
- Projected for 10x WriteLine under Balanced: about **88 / 69** (SQLite commands / PostgreSQL round trips) after #2582,
  #2584 and #2586, **57 / 38** with session-authoritative reads, **33 / 35** with the commit journal of
  [ADR 0084](../adr/0084-runtime-commit-journal.md), and **29 / 26** with five small unowned fixes.

## Method

The measurement tool is `Essentials/tests/Activities/Http/CommandCost/Tests` ([README](../../Essentials/tests/Activities/Http/CommandCost/Tests/README.md)).
It composes the same host and reference workflows as the EF command budgets and reproduces their counts exactly
(1482, 136 and 98 for `writeline-sequence`; 569 and 103 for `http-reference`; 507 and 180 for `fork-join-resume`). For
every command it records:

- the statements in it (verb and table), split the way the provider sends them;
- whether it ran in a transaction, and which one;
- the engine phase it ran in, read from the runtime's own tracing spans (`ActivitySourceWorkflowEngineTracer`) on a
  private activity source: the command kind of the work item being dispatched, and the checkpoint being committed
  with the persistence mode the policy chose;
- the first Elsa frame past the persistence infrastructure, with file and line.

On PostgreSQL (Testcontainers, `postgres:16-alpine`) every connection goes through a TCP relay that reads the wire
protocol. It counts a round trip each time the client sends and the server answers, plus Execute messages
(statements) and simple-protocol queries. The run fails unless the accounting closes: every statement on the wire is
one the EF context sent or one of EF's automatic savepoints, and every round trip is one command, one COMMIT or one
savepoint statement. This check held for all 15 PostgreSQL runs, and a deliberately broken recorder turned it red.

Modes are named as ADR 0080 D2 names them. **Durable** is today's `Immediate`. **Balanced** is today's `Coalesced` with
a segment cap of 100000 checkpoints, which no run reaches; that is what Balanced does with a per-commit activity cap
above the run length. **Coalesced-50** is today's `Coalesced` default, whose cap counts checkpoints, about 5 per
activity.

A command's phase is `commit` when it ran in a transaction that wrote a commit marker, `checkpoint` when it ran on the
committer's path outside that transaction, `handler:<kind>` while a work item of that kind was dispatched, `drain` in
the drain loop between dispatches, and `outside` before or after the drain.

Two adjustments, both checked not to change any count: timer-driven renewals and schema checks run on periods no run
reaches (as in the budgets), and the drain orchestrator's cycle bound is raised. With the default bound of 64, a
Durable run of 20 or more WriteLines stops with `DrainCycleLimitExceededException` (see Other findings).

## Totals

Measured, 10 WriteLines for `writeline-sequence`:

| Workflow | Mode | SQLite commands | PostgreSQL commands | PostgreSQL statements | PostgreSQL round trips | Durable commits |
|---|---|---:|---:|---:|---:|---:|
| writeline-sequence | Durable | 1482 | 1316 | 1482 | 1646 | 66 |
| writeline-sequence | Coalesced-50 | 136 | 106 | 136 | 116 | 2 |
| writeline-sequence | Balanced | 98 | 74 | 98 | 79 | 1 |
| http-reference | Durable | 569 | 505 | 569 | 615 | 22 |
| http-reference | Coalesced-50 | 103 | 84 | 103 | 94 | 2 |
| http-reference | Balanced | 103 | 84 | 103 | 94 | 2 |
| fork-join-resume | Durable | 507 | 452 | 507 | 557 | 21 |
| fork-join-resume | Coalesced-50 | 180 | 157 | 180 | 185 | 5 |
| fork-join-resume | Balanced | 180 | 157 | 180 | 185 | 5 |

PostgreSQL round trips are always commands + COMMITs + the savepoint statements EF adds: 4 per commit transaction, a
savepoint set and released around each of the commit store's two `SaveChangesAsync` calls inside its own transaction. Npgsql writes
`BEGIN` and the reset of a pooled connection (`DISCARD ALL`) together with the next command, so they cost server work
but no round trip: a Durable 10x WriteLine run sends 935 `DISCARD ALL`.

PostgreSQL batching: in a Balanced commit EF sends all row writes as one command (25 statements for 10 WriteLines);
Durable commits batch only 2 or 3 statements each. Statements per command for `writeline-sequence`: 1.32 (Balanced) and
1.13 (Durable).

## The WriteLine series

Measured over 5, 10, 20 and 40 WriteLines and fitted as `slope x WriteLines + intercept`. Every fit is exact.

| Provider | Mode | Measure | 5 | 10 | 20 | 40 | Per WriteLine | Intercept |
|---|---|---|---:|---:|---:|---:|---:|---:|
| SQLite | Balanced | commands | 73 | 98 | 148 | 248 | 5 | 48 |
| SQLite | Durable | commands | 832 | 1482 | 2782 | 5382 | 130 | 182 |
| PostgreSQL | Balanced | commands | 59 | 74 | 104 | 164 | 3 | 44 |
| PostgreSQL | Balanced | round trips | 64 | 79 | 109 | 169 | 3 | 49 |
| PostgreSQL | Durable | commands | 741 | 1316 | 2466 | 4766 | 115 | 166 |
| PostgreSQL | Durable | round trips | 921 | 1646 | 3096 | 5996 | 145 | 196 |
| both | Balanced | durable commits | 1 | 1 | 1 | 1 | 0 | 1 |
| both | Durable | durable commits | 36 | 66 | 126 | 246 | 6 | 6 |

Per commit, from the Durable fits: the commit transaction costs 55 statements per WriteLine on SQLite, so about 9.2
per commit, plus one marker lookup before it. On PostgreSQL a commit costs about 12.3 round trips: 3.3 staging reads,
the fence read, the batched writes, the marker insert, the marker lookup, COMMIT and 4 savepoint statements.

The Durable commits per WriteLine are, by checkpoint name: `ActivityScheduled`, `ActivityStarted`, two
`ActivityAttemptClaimed`, `ActivityInspectionCaptured` and `ActivityCompleted`.

## Where the commands go

Every statement is grouped into a cost source by table, phase and caller. Counts are measured at 10 WriteLines;
the class comes from the fits (per activity: grows with WriteLines in Balanced, where the commit count is fixed; per
commit: grows only in Durable; per run: grows in neither). Ranked by the Balanced count, then the Durable count. File
and line refer to `64c91e945`.

| # | Cost source | Balanced SQLite (per WL / per run) | Balanced PG RT | Durable SQLite (per WL) | Durable PG RT | Owning code | Removed by |
|---:|---|---:|---:|---:|---:|---|---|
| 1 | Handler reads of activity-state and inspection rows: misses and run-wide lists | 33 (3 / 3) | 33 | 139 (13) | 139 | `WorkflowScheduleActivitySchedulerWorkHandler.cs:97`, `RuntimeActivityExecutionInspectionAccumulator.cs:23`, `RuntimeActivityInputSnapshotMaterializer.cs:33`; in Durable also `WorkflowStartActivitySchedulerWorkHandler.cs:250`, `WorkflowInvokeActivitySchedulerWorkHandler.cs:65`, `WorkflowCompleteActivitySchedulerWorkHandler.cs:88`, `WorkflowParentActivityCompletionSchedulerWorkHandler.cs:93,100,391`, `RuntimeContainerScopeService.cs:52` | Unowned. Session-authoritative reads (prototype below) |
| 2 | Activity-state and inspection row writes in the commit (on PostgreSQL one batch per commit, which also carries rows 10 and 17 and the fence touch) | 22 (2 / 2) | 1 batch | 118 (11) | 66 batches | `EfRuntimeCheckpointCommitStore.cs:199` | Unowned. Commit journal (ADR 0084) or SQLite multi-row inserts |
| 3 | Root-write lease around a commit that carries the workflow row | 7 (0 / 7) | 7 | 14 (0) | 14 | `WorkflowExecutableRootWriteLeaseManager.cs:80,173`, `WorkflowExecutableDependencyGraph.cs:17` | #2586 |
| 4 | Workflow-state reads outside the commit | 7 (0 / 7) | 7 | 118 (10) | 118 | terminal check `WorkflowSchedulerDrainer.cs:217` (3 Balanced, 103 Durable); `RuntimeCheckpointCadenceResolver.cs:34`, `WorkflowStartSchedulerWorkHandler.cs:125`, `WorkflowCheckpointSchedulerWorkHandler.cs:145`, `BlockingIncidentWorkflowFaultObserver.cs:69`, `RuntimeContainerScopeService.cs:31` | #2582 for the terminal re-reads; the rest unowned |
| 5 | Ownership acquire and release | 6 (0 / 6) | 6 | 6 (0) | 6 | `RuntimeExecutionOwnershipService.cs:75,159,235` | Unowned. Conditional writes would make it 4 |
| 6 | Commit staging reads (one per participant type) | 5 (0 / 5) | 5 | 225 (20) | 225 | `EfRuntimeCheckpointActivityExecutionParticipantStaging.cs:35`, `EfRuntimeCheckpointInspectionParticipantStaging.cs:34,39`, `EfRuntimeCheckpointParticipantStaging.cs:31,374`, `EfRuntimeCheckpointRunHealthParticipantStaging.cs:68`, `EfRuntimeCheckpointOutboxParticipantStaging.cs:35` | Unowned. Journal removes the activity, inspection and hierarchy ones |
| 7 | Scheduler queue: enqueue, seed, claim, acknowledge | 5 (0 / 5) | 5 | 344 (30) | 344 | `WorkflowSchedulerCommandRouter.cs:103`, `RuntimeCoalescingSession.cs:655,883`; in Durable `WorkflowSchedulerDrainer.cs:508,533`, `RuntimeSchedulerPostCommitIntentDispatcher.cs:46`, `WorkflowCompleteActivitySchedulerWorkHandler.cs:144` | #2584 (Durable hops); the Balanced part is once per run |
| 8 | Commit fence: liveness read and touch | 2 (0 / 2) | 1 | 132 (12) | 66 | `EfRuntimeCheckpointParticipantStaging.cs:421,436` | Stays. One conditional update instead of two statements is unowned |
| 9 | Post-commit outbox: poll, delivery record | 2 (0 / 2) | 2 | 134 (12) | 134 | `RuntimePostCommitOutboxProcessor.cs:141,432`, `WorkflowDrainOrchestrator.cs:530` | #2584 |
| 10 | Workflow-state and run-health row writes | 2 (0 / 2) | in row 2 | 4 (0) | in row 2 | `EfRuntimeCheckpointCommitStore.cs:199` | Stays |
| 11 | Start dispatch: executable and source reference | 2 (0 / 2) | 2 | 2 (0) | 2 | `WorkflowExecutableCache.cs:168`, `WorkflowStartDispatcher.cs:303` | Stays |
| 12 | Commit marker row (second `SaveChangesAsync`) | 1 (0 / 1) | 1 | 66 (6) | 66 | `EfRuntimeCheckpointCommitStore.cs:200-201` | Stays; one `SaveChangesAsync` would save the PostgreSQL round trip |
| 13 | Commit-marker lookup before the commit | 1 (0 / 1) | 1 | 66 (6) | 66 | `EfRuntimeCheckpointCommitStore.cs:64,258` | Unowned. The unique key already resolves a replay (`:205-209`) |
| 14 | Post-drain incident check | 1 | 1 | 1 | 1 | `IncidentStrategyResolutionDrainObserver.cs:29` | Stays |
| 15 | Dispatch-record enrichment | 1 | 1 | 1 | 1 | `WorkflowDispatchCheckpointEnricher.cs:32` | Unowned |
| 16 | Durable-value reads | 1 (memoized) | 1 | 22 (2) | 22 | `RuntimeCoalescingDurableValuePageMemo.cs:124`; Durable `RuntimeActivityInputSnapshotMaterializer.cs:32`, `WorkflowInvokeActivitySchedulerWorkHandler.cs:136` | Durable: session reads |
| 17 | Outbox rows written in the commit | 0 | 0 | 44 (4) | in row 2 | `EfRuntimeCheckpointOutboxParticipantStaging` via `EfRuntimeCheckpointCommitStore.cs:199` | #2584 |
| 18 | Consumed work item deleted in the commit | 0 | 0 | 24 (2) | 24 | `EfRuntimeCheckpointParticipantStaging.cs:399` | #2584 extends it to more hops |
| 19 | Pause check per dispatch | 0 | 0 | 22 (2) | 22 | `RuntimePauseDecisionProvider.cs:24` | Stays: it reads control-plane state other writers change |
| 20 | Transaction control: COMMIT, SAVEPOINT, RELEASE (PostgreSQL only) | - | 5 | - | 330 | EF auto-savepoints around `EfRuntimeCheckpointCommitStore.cs:199,201` in the transaction begun at `:103` | Unowned. Turning auto-savepoints off in the commit store saves 4 round trips per commit |
| | **Total** | **98** | **79** | **1482** | **1646** | | |

PostgreSQL attributes batched writes to no Elsa frame, because EF executes them after an asynchronous resume inside
its own code; their phase is still exact, and SQLite attributes the same statements fully.

The plan for #2585 counted the Balanced 98 as 33 point reads, 22 inserts, 9 workflow-state reads, 5 lease statements and
about 29 others. The measurement corrects three of those: 11 of the 33 reads are run-wide list reads, not point reads;
the workflow-state reads are 8 (7 outside the commit, 1 inside it), not 9; and the root-write lease is 7 statements,
not 5, once its two executable reads are counted.

## Prototype: a run answers reads of rows it owns from its session

Local branch `spike/2564-session-authoritative-reads` (`6beac5cec`, `08b135bec`), never pushed.

While a coalescing session's drain holds the run's ownership lease, the session loads the run's durable
activity-execution rows, the ids of its durable inspection rows and its workflow-execution row at most once, and answers
point reads, list reads and counts from that baseline overlaid with its own writes. The overlay already holds every row
the session wrote, across flushes, so the baseline never needs re-reading; a flush adds the inspection rows it wrote to
the durable set. Losing ownership cancels the drain, and the cancellation callback drops every baseline. A nested drain
drops the outer session's baselines, as the existing code already does for its durable-value memo. Only the first load
after a resume touches the store.

### Measured effect

| Workflow | Mode | SQLite commands | PostgreSQL commands | PostgreSQL round trips |
|---|---|---|---|---|
| writeline-sequence (10) | Balanced | 98 to 65 | 74 to 41 | 79 to 46 |
| writeline-sequence (10) | Coalesced-50 | 136 to 103 | 106 to 73 | 116 to 83 |
| writeline-sequence (10) | Durable | 1482 unchanged | 1316 unchanged | 1646 unchanged |
| http-reference | Balanced and Coalesced-50 | 103 to 91 | 84 to 72 | 94 to 82 |
| http-reference | Durable | 569 unchanged | 505 unchanged | 615 unchanged |
| fork-join-resume | Balanced and Coalesced-50 | 180 to 161 | 157 to 138 | 185 to 166 |
| fork-join-resume | Durable | 507 unchanged | 452 unchanged | 557 unchanged |

Balanced WriteLine series with the prototype: SQLite 55, 65, 85, 125 (2 per WriteLine + 45, the two inserts);
PostgreSQL 41 commands and 46 round trips for every length. Durable does not change because Immediate registers no
coalescing session; the same idea for Durable needs a read session for Immediate drains (projected below).

The saving is two groups: handler reads of activity rows (33 removed, 3 first loads added: one activity list and the
two inspection summary queries, count and page), and workflow-state reads inside the session (3 here, 11 in
`fork-join-resume`).

### Suites run against the prototype

| Suite | On main | On the prototype |
|---|---|---|
| `Elsa.Workflows.Runtime.Tests` | 2253 / 2253 | 2248 / 2255 (two prototype tests added) |
| `Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests` | 937 / 937 | 937 / 937 |
| `Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.ProviderTests` (PostgreSQL, SQL Server, MySQL; native providers required) | not run | 191 / 191, 0 skipped |
| `Elsa.Activities.Runtime.Tests` | 400 / 400 | 400 / 400 |
| `Elsa.Activities.Http.IntegrationTests` (includes the budgets) | 51 / 51 | 51 / 51 |

The main column for the Http suite is this spike's own run on the measurement commit; the other main results are the
runs the #2585 planning session made on `64c91e945`.

Eight runtime tests failed on the first cut. With the assertions that count or size the inner store's reads set
aside, everything else in them passed except two:

- `Coalesced_page_merger_keeps_legacy_continuations_and_rejects_invalid_bindings` exposed a real defect: a continuation
  the provider minted was read as a baseline position and the page came back empty. The prototype now reads such a
  page from the provider; the test passes.
- `Coalesced_page_merger_memoizes_empty_terminal_source_and_observes_cancellation` cancels a provider read that is in
  flight. The prototype makes no provider read there, so the request completes before the cancellation. This test, and
  six others that pin how often or with what page size the inner store is read
  (`MemoizedInspectionReads_AreByteIdenticalToPerHopReads_AndCollapseDurableReadCount` and five
  `Coalesced_page_merger_*` / `Coalesced_activity_pages_*` tests), still fail. Their ordering, replacement, tombstone
  and byte-identity assertions pass. They relied on the read-through, not on what it returned.

Two prototype tests check the cache in both directions: one run's rows are answered from one baseline load, and a
session that lost ownership reads the store again. Each was bite-proved: with the drop ignored, the second returns the
stale row; with the baseline bypassed, both fail.

### Why it is correct, and where it is not

The property: while a session answers a read from its baseline, no row of the run has changed in the store except
through that session. The mechanism:

1. A drain acquires the run's ownership lease and makes it ambient for every commit
   (`WorkflowDrainOrchestrator.cs:196-197`). Acquisition issues a strictly greater fencing token with a revision
   check (`RuntimeExecutionOwnershipService.cs:62,75`).
2. Every checkpoint commit in the drain carries the lease as its expected fence (`RuntimeCheckpointCommitter.cs:86,226-235`).
   The EF commit validates it and touches the liveness row in the same transaction (`EfRuntimeCheckpointCommitStore.cs:116-125`,
   `EfRuntimeCheckpointParticipantStaging.cs:411-436`), so a superseded writer's commit fails.
3. The coalescing session lives inside the drain (`WorkflowDrainOrchestrator.cs:353-362`) and its overlay records every
   commit it makes, buffered or flushed (`RuntimeCoalescingSession.cs:118-147,276`; `CoalescingRuntimeCheckpointCommitStore.cs:80-173`).
4. Renewal failure cancels the drain (`WorkflowDrainOrchestrator.cs:300,307`); the prototype drops the baselines there.
5. On one node, commands for one execution are serialized by the actor's mailbox
   (`InProcessWorkflowExecutionActorProvider.cs:252,309`).

The holes:

- **Writers that are not fenced.** An alteration runs straight from the command router, outside any drain
  (`WorkflowSchedulerCommandRouter.cs:92-98`), so its commit carries no fence (`RuntimeCheckpointCommitter.cs:228-229`)
  and does not touch the liveness row. On one node the mailbox serializes it with drains. Across nodes it relies on
  placement alone: an alteration committed on another node while a session is live is invisible to that session, and
  the session's next flush is not rejected. Today the same holds for rows already in the overlay; the prototype extends
  it to every row of the run. Closing it needs alterations to take the ownership lease before committing.
- **Two-node handoff.** `AcquireAsync` takes over without checking whether a live lease exists
  (`RuntimeExecutionOwnershipService.cs:53-82`); the previous owner learns at its next fenced commit or heartbeat. The
  fence rejects its flush, so no stale row is written, but it can run activities on stale reads until then. Under
  Balanced that window already spans the whole segment, because the segment runs on the overlay; the prototype adds
  only the reads of rows it loaded but did not write.
- **Nested drains.** The EF commit store notes that one execution can commit from a nested drain between two commits
  of the outer one (`EfRuntimeCheckpointCommitStore.cs:106-110`). The prototype drops the outer baselines when any nested
  session begins; I did not find or construct a nested drain of the same execution, so this case is reasoned, not tested.
- **Incident resolution, the poison path and cancellation** are not holes: strategy resolution and poison projection run
  in drain observers after the session has ended and inside the lease (`WorkflowDrainOrchestrator.cs:361-362`), and a
  cancellation arrives as queued work that the owning drain processes.

## Projection for 10x WriteLine

Every step builds on the one above it. "Measured" means a count from a run; "projected" means arithmetic over measured
groups, with the assumption stated.

### Balanced

| Step | SQLite commands | PostgreSQL commands | PostgreSQL round trips | Status | Derivation |
|---|---:|---:|---:|---|---|
| Today | 98 | 74 | 79 | measured | |
| (i) after #2582, #2584 and #2586 | 88 | 64 | 69 | projected | #2586: the 7 root-write lease statements go. #2582: the 3 terminal re-reads go. #2584: nothing, because this run has no per-hop durable queue or outbox traffic (its 5 queue and 2 outbox statements are once per run). |
| (ii) plus session-authoritative reads | 57 | 33 | 38 | projected from measured | The prototype measured 33 fewer on each provider: 30 handler reads and 3 workflow-state reads. Only 1 of the 3 survives (i): one was a terminal re-read, and once the terminal reads are gone the baseline load falls on the next read instead. So it adds 31 on top of (i). |
| (iii) plus the commit journal, filled lazily | 33 | 30 | 35 | projected | SQLite: 22 row inserts become 1 journal insert (-21) and the activity, inspection and hierarchy staging reads go (-3). PostgreSQL: the inserts already shared one batched round trip, so only the 3 staging reads go. These are the run's own commands: the projector still writes the read-model rows later, outside the run (see below). |
| (iv) plus five unowned fixes | 29 | 25 | 26 | projected | Marker lookup (-1), fence as one conditional update (-1), ownership acquire and release as conditional writes (-2), and on PostgreSQL auto-savepoints off (-4 round trips) and one `SaveChangesAsync` (-1). |

### Durable

| Step | SQLite commands | PostgreSQL round trips | Status | Derivation |
|---|---:|---:|---|---|
| Today (Immediate) | 1482 | 1646 | measured | |
| (i) after #2582, #2584 and #2586 | about 1115 | about 1245 | projected | #2582: -103 terminal re-reads. #2586: -14. #2584, from spec 234's levers applied to the measured callers: the continuation written in the commit removes the outbox row (-44 / 0), the separate enqueue (-88 / -88) and the delivery record (-88 / -88) and adds one work-item insert per continuation (+44 / 0); folding the separate acknowledgements leaves one in-commit delete each (-34 / -68); routing the parent-completion hop in process removes its enqueue, claim and acknowledgement (about -40 / -40). |
| (ii) plus session-authoritative reads | about 947 | about 1077 | projected | Needs a read session for Immediate drains, which the prototype does not have. Handler reads of activity rows 139 to about 4 (-135), durable-value reads 22 to about 2 (-20), non-terminal workflow-state reads inside the drain 14 to 1 (-13). Pause checks stay. |
| (iii) plus the commit journal | about 742 | about 924 | projected | Activity, inspection and hierarchy staging reads go (-153 / -153); 118 row writes become 66 journal rows (-52 / 0). The run's own commands, as above. |
| (iv) plus the unowned fixes | about 610 | about 462 | projected | Marker lookup (-66 / -66), fence as one update (-66 / -66), auto-savepoints off (0 / -264), one `SaveChangesAsync` (0 / -66). |
| Alternative: one commit per activity | 291 | 282 | measured proxy | Today's Coalesced with a cap of 6 checkpoints, which folds about one activity per commit (10 commits for 11 activities), on today's code with none of the steps above. |

Step (iii) moves work rather than removing all of it. With the read models filled lazily, a projector writes the
read-model rows after the run: about 3 statements per pass with multi-row inserts on SQLite, one batch on PostgreSQL,
and the same 22 per-row inserts if it uses EF's per-row inserts on SQLite. Under Durable it coalesces the six updates
each activity row receives into one write. Filled on demand, the rows are never written. ADR 0084 compares the fill
strategies.

The last row matters more than any step above it. ADR 0080 D2 defines Durable as "commits after every activity";
today's Immediate commits about 6 times per activity. A Durable built on the Balanced machinery with an activity cap of
1 would match D2's wording at about a fifth of today's cost, before any of the other work. That is an owner decision,
listed below.

## Other findings

These are outside this epic's issues and are recorded here for triage, not fixed.

1. **A Durable run of 20 WriteLines fails on main.** Immediate needs 4 drain cycles per WriteLine plus 5; the default
   bound is 64 (`WorkflowDrainOrchestratorOptions.cs:5`, enforced at `WorkflowDrainOrchestrator.cs:397,438`), so a
   Sequence of 20 or more leaf activities started directly throws `DrainCycleLimitExceededException`. Immediate is
   still the default mode today.
2. **EF savepoints cost 4 PostgreSQL round trips per commit** (16% of a Durable run's round trips), with no benefit:
   the commit store rolls the whole transaction back on any failure.
3. **Run-wide list reads in handlers.** `RuntimeActivityInputSnapshotMaterializer.cs:33`,
   `StructuralParentEvaluationSupport.cs:166`, `WorkflowIntrinsicExecutor.cs:112,168,482` and both cancel handlers list
   every activity row of the run per call. On a resumed run with many rows the row volume grows with the square of the
   activity count. Session-authoritative reads serve these from memory.
4. **The commit-marker lookup before every commit** duplicates what the marker's unique key already enforces.
5. **`DISCARD ALL` on every pooled-connection reuse** on PostgreSQL: EF opens and closes the connection per command
   outside a transaction. It costs no round trip but is server work on every command.
6. **Alterations commit without a fence** (hole 1 above), relying on the actor mailbox and on placement.

## Recommendation

In order:

1. **Land #2582, #2584 and #2586 as planned.** They are the largest wins for Durable (about -370 commands for 10x
   WriteLine) and take Balanced from 98 to about 88.
2. **Build session-authoritative reads for Balanced next** (about -31: 88 to 57 on SQLite, 69 to 38 round trips on
   PostgreSQL). Close the fence hole first: alterations take the ownership lease before they commit. Rewrite the seven
   tests that pin the inner store's read count rather than its results. Measured on the prototype; low design risk.
3. **Take the four unowned fixes in the commit store** (auto-savepoints off, one `SaveChangesAsync`, no marker lookup,
   fence as one statement). Small, local, and worth about 7 round trips per commit on PostgreSQL. The fifth, conditional
   writes for ownership, saves 2 statements per drain.
4. **Decide what Durable means** before optimizing Immediate further. If D2's "commits after every activity" stands,
   Durable built on the Balanced machinery measures about 291 commands for 10x WriteLine today, against about 610 after
   every Immediate optimization in this report.
5. **Decide on the commit journal (ADR 0084) last.** On PostgreSQL it saves only 3 round trips per Balanced run once
   steps 1 to 3 have landed, because EF already batches the row writes. On SQLite it is the only way below 40 commands
   for 10x WriteLine (57 to 33). If SQLite is the target, multi-row inserts for the two activity tables reach about 37
   at far lower cost.

Expected numbers after steps 1 to 3: Balanced 10x WriteLine about 55 SQLite commands and 31 PostgreSQL round trips
(projected: (ii) less the marker lookup and the fence statement on both providers, and the savepoints and second
`SaveChangesAsync` on PostgreSQL).

## Open questions

- Does Durable mean one commit per activity (D2's wording) or one commit per checkpoint (today's Immediate, as the #2585
  plan maps it)?
- Does #2586 remove the root-write lease per commit entirely, or keep one acquire and release per run? The projection
  assumes entirely; keeping one pair per run would make (i) 4 statements higher.
- What exactly does #2582 remove? The projection assumes the terminal re-reads at `WorkflowSchedulerDrainer.cs:217` and
  nothing else.
- Should alterations take the ownership lease? The prototype's correctness across nodes depends on it.
- Is SQLite a production target that justifies the journal, or a development store where 57 commands is acceptable?
