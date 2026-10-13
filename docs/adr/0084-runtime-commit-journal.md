# Runtime Commits Append One Journal Row And The Per-Activity Tables Become Read Models

Status: proposed (2026-10-12, numbers revised 2026-10-13 after #2585). Low priority by owner decision on #2564.

Program: Runtime Efficiency, epic [#2564](https://github.com/elsa-workflows/elsa-foundation/issues/2564). Evidence:
the measurement report posted on #2564, produced by `Essentials/tests/Activities/Http/CommandCost/Tests`. Constrained by
[ADR 0080 D2](0080-elsa-4-simplification-decisions.md#d2--persistence-modes-balanced-by-default) (persistence modes),
[ADR 0032](0032-runtime-checkpoint-cadence-is-policy-driven-per-workflow.md) (semantic boundaries),
[ADR 0020](0020-runtime-checkpoint-commit-post-commit-work.md) (commit and post-commit work) and
[ADR 0001](0001-checkpoint-gated-activity-execution-inspection.md) (checkpoint-gated inspection).

## Verdict at today's numbers

**Not worth building now.** On PostgreSQL, which is what counts because SQLite is a development and test target only,
the journal would save 3 of the 80 measured round trips of a Balanced run of 10 WriteLines (4%) and 36 of 321 for a
Durable one (11%). Cheaper, local changes save more: turning off EF's automatic savepoints in the commit store removes
the 4 and 48 savepoint round trips measured in those runs, and answering a run's reads of its own rows from its session
would save about 30 in both modes. All three savings are projections over measured cost sources. The journal adds a second
representation of the per-activity tables, a projector, a tail merge and a retention question. If PostgreSQL server
work rather than round trips becomes the concern, holding one connection per drain, which removes the pooled-connection
reset that precedes almost every command today, is the cheaper change to try first (see Alternatives).

Revisit it when one of these holds:

- after the cheaper changes have landed, the commit's per-activity staging reads and row writes are the largest
  remaining PostgreSQL cost of a Durable run;
- PostgreSQL write volume or index maintenance on the three per-activity tables shows up as a bottleneck. Neither was
  measured: every number here counts commands, statements and round trips, not bytes, index updates or time.

## Context

A checkpoint commit writes each changed activity execution as a row of `activity_execution_state`, each changed
inspection projection as a row of `activity_execution_inspection`, and a row of `activity_execution_hierarchy` per
inspection. Before it writes, it reads the existing rows of each participant type to decide between insert, update and
delete (`EfRuntimeCheckpointActivityExecutionParticipantStaging.cs:35`, `EfRuntimeCheckpointInspectionParticipantStaging.cs:34,39`
on `2132dea4a`); the writes go out in the commit store's `SaveChangesAsync` (`EfRuntimeCheckpointCommitStore.cs:199`).

Since #2585, Durable commits once per activity on the same folding session as Balanced, instead of once per checkpoint.
A Durable run of 10 WriteLines now makes 12 commits (one per activity plus one), not 66, and costs 321 SQLite commands
instead of 1482.

Measured on `2132dea4a`, 10 WriteLines:

| 10x WriteLine | Balanced, SQLite | Balanced, PostgreSQL | Durable, SQLite | Durable, PostgreSQL |
|---|---:|---:|---:|---:|
| Activity and inspection row writes | 22 statements (inserts) | inside 1 batched round trip | 46 statements (22 inserts, 24 updates) | inside 12 batched round trips, one per commit |
| Activity, inspection and hierarchy staging reads | 3 statements | 3 round trips | 36 statements | 36 round trips |
| Whole run | 99 commands | 80 round trips | 321 commands | 321 round trips |

Every count is exactly linear over 5, 10, 20 and 40 WriteLines. Per WriteLine, Balanced costs 5 SQLite commands and 3
PostgreSQL round trips, and Durable costs 24 of each. Ephemeral with `RecordCompletion` on costs what Balanced costs for
all three reference workflows; with it off, a WriteLine run that never commits costs 62 commands and 62 round trips and
writes no activity row at all.

The SQLite counts equal the committed EF command budgets (`ef-command-budgets.json`): 321, 99 and 99 for
`writeline-sequence`, 196, 102 and 102 for `http-reference`, 242, 175 and 175 for `fork-join-resume`.

On PostgreSQL EF opens the connection for each command outside a transaction and closes it after, so almost every
such command takes a pooled connection, and Npgsql resets a pooled connection with `DISCARD ALL` when it is next
used. Measured: 68 resets in a Balanced run of 10 WriteLines (75
commands) and 187 in a Durable run (261 commands), 3 and 13 per WriteLine. Npgsql writes the reset together with the
next command, so it costs no round trip, but the server runs it, and it discards the session's state, prepared
statements included. Not measured: what the resets cost the server, and whether prepared statements would help.

On SQLite, which runs one statement per command, the row writes are about a fifth of a Balanced run and grow by 2 per
activity. On PostgreSQL EF already batches them into the commit's single write round trip, so they cost nothing extra
in round trips; the staging reads do, one per participant type per commit.

This ADR proposes replacing the per-row writes with one appended row per commit and turning the three per-activity
tables into read models. The decisions the owner must make are listed at the end.

## Proposal

### The journal

Each durable checkpoint commit appends one row to a new table, `runtime_execution_journal`:

| Column | Content |
|---|---|
| `workflow_execution_id`, `sequence` | Primary key, so a second append with the same number fails. `sequence` is the run's next number, assigned by the single writer. |
| `commit_id` | The checkpoint commit id, unique. If the journal row is also the commit marker (decision 3), this is the replay key the marker holds today. |
| `fencing_token` | The ownership token the commit carried. |
| `schema_version`, `occurred_at` | |
| `payload` | One JSON document: the commit's activity-execution changes and inspection changes, each a full row image (upsert) or a tombstone (delete), already folded last-writer-per-id as `RuntimeCheckpointFold` folds them today. |

The workflow-execution row, run health, bookmarks, durable values, incidents, outbox and scheduler work stay where they
are and keep being written by the commit; they are per run, or read by other parties with their own indexes. Only the
three per-activity tables move behind the journal.

The append happens inside the commit's transaction, after the fence check, exactly where the per-row writes happen
today. The fence and the `(workflow_execution_id, sequence)` primary key together guarantee one totally ordered
journal per run.

### Filling the read models

The three tables stay, as read models of the journal, with a per-run watermark: the highest sequence applied. Three ways
to fill them were evaluated.

| Strategy | Commit path | Read path | Verdict |
|---|---|---|---|
| **Same transaction**: the commit appends the journal row and applies its changes to the read models | One more statement than today | Unchanged | No gain. Useful only as a migration step. |
| **Lazily**: a projector applies journal rows after commit, idempotently by sequence | Journal row only: the per-row writes and the three staging reads leave the commit | A read of a run's rows returns the read model plus the run's unapplied journal tail, merged in the store, so callers see current state | Recommended, if the journal is built. |
| **On demand**: no per-activity read model; a reader folds the run's journal | Journal row only | Every read folds the journal, with periodic snapshots to bound the fold | Too slow for list and count queries across runs; the hierarchy pages and instance list need indexes. |

Under lazy fill the projector still writes the read-model rows, but off the run's path, in batches it chooses. Under
Durable it coalesces the insert and the later update each activity row receives into one write. Because the store
merges the unapplied tail, the existing store contracts (`IActivityExecutionStateStore`,
`IActivityExecutionInspectionStore`, `IActivityExecutionHierarchyStore`) keep their meaning and no caller changes.

### Expected commands and round trips per commit

For a commit that changes `k` activity executions, excluding the commit-marker lookup that precedes every commit today
(removable independently). PostgreSQL round trips assume EF's automatic savepoints are turned off in the commit store,
also an independent change; with them on, add 4 to every PostgreSQL figure. "Today" is measured; "with the journal" is
projected by removing the three staging reads and replacing the activity and inspection row writes with one row.

| Commit | SQLite statements today | SQLite with the journal | PostgreSQL round trips today | PostgreSQL with the journal |
|---|---:|---:|---:|---:|
| Balanced, 10 WriteLines (`k` = 11) | 32 | 8 | 9 | 6 |
| Durable, typical (one activity) | 12 | 6 | 8 | 5 |

With the journal the commit's statements no longer depend on `k`: fence read and touch, the workflow, run-health and
outbox staging reads and writes where the commit carries them, the journal row and the marker (or one row if the
journal row is the marker).

### Whole runs, 10 WriteLines

Every row builds on the one above it. Measured rows come from a run; projected rows are arithmetic over measured
cost sources, with the assumption stated. Session-authoritative reads are projected from a prototype measured at
`64c91e945`, before #2585; since #2585 Durable runs on the same session, so the projection applies to both modes.

| Step | Balanced, PostgreSQL round trips | Durable, PostgreSQL round trips | Balanced, SQLite commands | Durable, SQLite commands | Status |
|---|---:|---:|---:|---:|---|
| Today | 80 | 321 | 99 | 321 | measured |
| Automatic savepoints off in the commit store | 76 | 273 | 99 | 321 | projected: 4 savepoint statements per commit |
| No commit-marker lookup before the commit | 75 | 261 | 98 | 309 | projected: 1 per commit; the marker's unique key already resolves a replay |
| Session-authoritative reads | about 45 | about 231 | about 68 | about 279 | projected from the prototype: the 3 handler reads per activity go, a few first loads remain |
| **Commit journal, filled lazily** | **about 42** | **about 195** | **about 44** | **about 209** | projected: Balanced -3 staging reads, and on SQLite 22 inserts become 1 row; Durable -36 staging reads, and on SQLite 46 writes become 12 rows |

The journal's saving is the same in absolute terms whatever lands before it: 3 round trips per Balanced run and 3 per
Durable commit on PostgreSQL. It is the only step whose cost is a new representation of the data.

The SQLite steps move work rather than remove all of it. A lazy projector still writes the read-model rows after the
run, on SQLite as multi-row inserts or as EF's per-row inserts.

## Who reads the per-activity tables today, and how each would be served

File and line refer to `64c91e945`, before #2585, and were not re-derived after it. "Session" means the
session-authoritative reads of the prototype: inside a drain that holds the ownership lease, the first read loads the
run's rows (read model plus unapplied tail) and every later read is answered from memory.

| Reader | Where | Served by |
|---|---|---|
| Inspection API: one activity's projection | `ActivityExecutionInspectionService.cs:23`, `ActivityExecutionValuePayloadReader.cs:28` | Read model plus tail, in the store |
| Inspection API: a run's activity summaries | `WorkflowInstanceDetailsService.cs:30` | Read model plus tail |
| Inspection API: descendants | `ActivityExecutionHierarchyReader.cs:36` | Hierarchy read model plus tail |
| Instance list: activity count per run | `WorkflowInstanceListService.cs:89` | Read model; a lag of one projector pass is acceptable for a count, or a counter on the workflow row |
| Dashboard and run health | `EfWorkflowRunHealthDataSource` over `workflow_run_health_state` | Unchanged: run health is a per-run row the commit keeps writing |
| Scheduling, start, invoke and completion handlers | `WorkflowScheduleActivitySchedulerWorkHandler.cs:97`, `WorkflowStartActivitySchedulerWorkHandler.cs:250`, `WorkflowInvokeActivitySchedulerWorkHandler.cs:65`, `WorkflowCompleteActivitySchedulerWorkHandler.cs:88`, `WorkflowParentActivityCompletionSchedulerWorkHandler.cs:93,100,391`, `WorkflowNotifyParentActivitySchedulerWorkHandler.cs:65,250,391`, `ReplaySafeFusionDriver.cs:372`, `RuntimeActivityExecutionInspectionAccumulator.cs:23` | Session |
| Resume | `WorkflowResumeBookmarkSchedulerWorkHandler.cs:110`, `WorkflowCreateBookmarkSchedulerWorkHandler.cs:114`, `WorkflowRetryActivityBoundarySchedulerWorkHandler.cs:47,70` | Session; its first load after a resume is the one store read |
| Run-wide lists inside a drain | `RuntimeActivityInputSnapshotMaterializer.cs:33`, `RuntimeContainerScopeService.cs:52`, `StructuralParentEvaluationSupport.cs:126,166`, `WorkflowIntrinsicExecutor.cs:112,168,482`, `Parallel.cs:205`, `WorkflowCancelSchedulerWorkHandler.cs:80`, `WorkflowCancelActivityScopeSchedulerWorkHandler.cs:49` | Session |
| Child-workflow parents | `DispatchWorkflow.cs:65`, `ParentResumeExecutor.cs:105`, `ChildFaultParentEvaluation.cs:40`, `ParentNotificationEvaluation.cs:46` | Session when inside the parent's drain, read model plus tail otherwise |
| Incident resolution | `IncidentResolutionBatchExecutor.cs:188`, `BlockingIncidentWorkflowFaultObserver.cs:164,170`, `PoisonedSchedulerWorkIncidentObserver.cs:226,421,467` | These run in drain observers inside the lease: read model plus tail (the session has ended by then) |
| Alterations | `WorkflowAlterationActorCommandExecutor.cs:79`, `WorkflowAlterationTargetCaptureTask.cs:142`, `RuntimeWorkflowMigrationQuiescenceProbe.cs:87`, `MigrateWorkflowAlterationHandler.cs:125` | Read model plus tail. If alterations take the ownership lease, which session-authoritative reads need anyway, they can use a session |
| Command routing | `WorkflowSchedulerCommandRouter.cs:249` | Read model plus tail |
| Synchronous HTTP response | `HttpResponseInstructionDelivery.cs:39` | Read model plus tail |
| Activity draft test runs | `ActivityDraftTestRunService.cs:584` | Read model plus tail |
| Execution evidence | `ExecutionEvidenceCheckpointEnricher.cs:76` reads the commit's inspection changes, not the table | Unchanged |

Every reader keeps its store contract. The merge of the tail is the store's concern, so the change stays below the
runtime: no handler, API or activity changes.

## Parallel branches and merge rules

The runtime has one writer per execution: the drain that holds the ownership lease. Branches of a Parallel run inside
that drain and touch different activity rows; the join updates the parent's row after them. So the journal needs no
merge between writers:

- Journal rows apply in `sequence` order. Within a row, changes apply in their stored order, and per activity id the
  last change wins, as `RuntimeCheckpointFold` already folds within a commit.
- A tombstone removes the row from the read model; a later upsert of the same id recreates it.
- A child workflow has its own journal. A parent learns of a child through its own queue and writes the parent's rows
  in its own drain.
- A superseded writer cannot append: the fence check runs in the append's transaction, and a reused sequence violates
  the primary key. A crashed writer's uncommitted rows never existed.

If the runtime ever allows more than one writer per execution, per-branch journals and a merge become necessary. That
is out of scope; the fence makes the current assumption checked rather than implied.

## Crash semantics

The journal changes what a commit writes, not when a commit happens, so ADR 0080 D2 and its 12 October amendment hold
unchanged:

| Mode | Commits | After a crash, today | After a crash, with the journal |
|---|---|---|---|
| Durable | Once per activity | Resumes at the last completed activity | The same: the last journal row is the last completed activity |
| Balanced | On suspension, fault, completion, the end of a pass, before an activity that is not safe to repeat, and every `MaxActivitiesPerCommit` activities | Activities since the last commit run again | The same |
| Ephemeral | On suspension, fault, cancellation, an incident or alteration, a child dispatch, before an activity that is not safe to repeat, at the end of a pass that does not complete the run, and at completion | The run starts again from its last commit, which may be the beginning | The same: one journal row per commit, and none for a run that `RecordCompletion` off leaves without a trace |

What changes is the read model's freshness. Under lazy fill a crash between a commit and its projection leaves the read
model behind the journal; the projector catches up from the watermark, and every store read merges the tail meanwhile,
so no reader sees the gap. The journal is the source of truth; the read models can be rebuilt from it.

## Durable mode

Durable gains the most, but much less than this ADR first projected. Before #2585 Durable committed about 6 times per
activity and the journal looked like the way to make that affordable. Since #2585 it commits once per activity, and a
typical commit inserts one row and updates another in each of the two activity tables. Measured per Durable commit today on PostgreSQL: the
fence read, three per-activity staging reads, the outbox staging read, one batched write, the marker insert and the
COMMIT, plus 4 savepoint statements. The journal removes the three staging reads. Over 10 WriteLines that is 36 of 321
round trips, against 48 for turning savepoints off.

## Providers

| Provider | Effect |
|---|---|
| PostgreSQL | The provider that decides. EF already batches the row writes into the commit's single write round trip, so the gain is the staging reads (one round trip per participant type per commit) and fewer index updates on the hot path: three tables with several indexes each become one append. That second effect was not measured. `text` is cheaper to write than `jsonb`; use `jsonb` only if the journal is queried. |
| SQLite | A development and test target only (owner decision on #2564). It runs one statement per command, so it shows the largest command reduction, but that reduction does not justify the design. Multi-row inserts for the two activity tables would reach most of it (Balanced 99 to about 79) at far lower cost if SQLite command counts ever matter. |
| SQL Server | Batches like PostgreSQL. `nvarchar(max)` payload. EF's automatic savepoints apply here too (`SAVE TRANSACTION`). Not measured. |
| MySQL | Batching not measured. A large commit's payload must stay under `max_allowed_packet`; the per-commit activity cap of Balanced bounds it. |

Payload size grows with the activities per commit: a Balanced commit of 40 WriteLines carries 82 row images. The
per-commit activity cap (50 by default) bounds it.

## Migration

Elsa 4 is unreleased, so there is no compatibility path. Add the journal table to `RuntimeDbContext` and its four
provider migrations, or to the single squashed migration per context that ADR 0080 D4 plans before the release
candidate. Switch the commit store to append, add the projector and the tail merge to the three stores, and recreate
preview databases. No dual-write period.

## Alternatives considered

Each figure is marked measured, a count from the runs on #2564, or projected, arithmetic over measured counts.

- **Automatic savepoints off, one `SaveChangesAsync`, no marker lookup.** Local changes to the commit store. Measured:
  per commit, EF's savepoints cost 4 round trips, the second `SaveChangesAsync` (the marker insert) 1, and the marker
  lookup before the commit 1. Projected: removing them saves 6 round trips per Balanced run and 72 per Durable run of
  10 WriteLines, twice what the journal would save for Durable. **Recommended before anything in this ADR.**
- **Session-authoritative reads.** A run answers reads of its own rows from its session. Measured: the handler reads
  are 3 per activity in both modes, 33 per run of 10 WriteLines. Projected: about 30 round trips saved per run, after
  the few first loads a session still makes, taken from a prototype measured before #2585. It needs alterations to take
  the ownership lease first. Recommended regardless of this ADR.
- **One connection per drain.** Outside a transaction EF opens the connection for each command and closes it after,
  so each command takes a pooled connection and pays a reset. Measured: 68 `DISCARD ALL` per Balanced run of 10
  WriteLines and 187 per Durable run. Projected: a drain that opens one connection and keeps it open for its stores'
  contexts sends none of those resets; round trips stay the same, because each reset already travels with the next
  command. Not measured: the server work saved, and whether Npgsql's automatic preparation (off by default) would then
  pay off on the reused connection. It changes how the EF stores obtain their connection, not what they write, so it
  is likely cheaper than the journal; it is the first thing to try if PostgreSQL server work matters.
- **SQLite multi-row inserts, no journal.** The commit store writes the two activity tables with one multi-row
  `INSERT` each instead of EF's per-row inserts. Measured: 22 per-row inserts per Balanced run of 10 WriteLines on
  SQLite. Projected: the run goes from 99 to about 79 commands. Does nothing for PostgreSQL. Only worth doing if SQLite
  command counts start to matter, which the owner's decision rules out for production.
- **Slimmer inspection records.** Fewer bytes per row, the same number of statements and round trips: inspection rows
  are one per activity row either way (measured: 11 inspection inserts per Balanced run of 10 WriteLines, as many as
  activity-row inserts). Helps write volume only, which was not measured.
- **Dropping inspection rows when no observer is attached.** Measured: 11 inspection inserts and 11 inspection reads
  per Balanced run of 10 WriteLines. Projected: dropping them removes 22 SQLite commands and 11 PostgreSQL round
  trips; after session-authoritative reads only the inserts remain, and on PostgreSQL they cost no round trip because
  they are batched. Inspection is the run's history (ADR 0001), so a run nobody watched could not be inspected later.
  Acceptable only as an explicit per-workflow setting, like `RecordCompletion`.
- **Event-sourcing the whole run.** The journal as the only truth for every run table. Too large a change before GA.
  No figures.

## Risks

- **Two representations that can diverge.** A projector bug leaves read models wrong while the journal is right.
  Mitigation: idempotent projection by sequence, a rebuild from the journal, and a contract test that a run's read
  model equals the fold of its journal on every provider.
- **Read freshness.** Without the tail merge, the inspection API would lag. The merge is mandatory under lazy fill.
- **Cross-run queries.** Anything that filters activity rows across runs needs the read models' indexes, so the read
  models cannot be dropped later without replacing those queries.
- **Retention.** The journal grows with every commit. Whether applied rows are kept (as execution history) or
  compacted is decision 5; execution evidence and retention policies must agree on it.
- **A second hot index.** `(workflow_execution_id, sequence)` takes every commit's append.

## Decisions for the owner

Settled on #2564: SQLite is a development and test target only, and this ADR stays proposed at low priority.

Open, and only worth taking if a revisit trigger above fires:

1. **Build the journal, or stop at the commit-store fixes and session-authoritative reads.** At today's numbers the
   journal adds 3 PostgreSQL round trips of saving to a Balanced 10x WriteLine run and about 36 to a Durable one.
2. **Fill strategy.** Lazy with a tail merge (recommended), same transaction (no gain), or on demand.
3. **Whether the journal row is the commit marker**, which saves one statement per commit and makes the journal the
   replay key.
4. **Payload format.** Full row images per changed id (recommended: idempotent and simple to fold) or patches.
5. **Retention.** Keep applied journal rows as execution history, or compact them behind a snapshot.
6. **Inspection freshness contract.** Whether the inspection API may lag one projector pass, or must always merge the
   tail.
