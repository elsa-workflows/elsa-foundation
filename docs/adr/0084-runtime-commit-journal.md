# Runtime Commits Append One Journal Row And The Per-Activity Tables Become Read Models

Status: proposed (2026-10-12)

Program: Runtime Efficiency, epic [#2564](https://github.com/elsa-workflows/elsa-foundation/issues/2564). Evidence:
[runtime command cost, October 2026](../reports/runtime-command-cost-2026-10.md). Constrained by
[ADR 0080 D2](0080-elsa-4-simplification-decisions.md#d2--persistence-modes-balanced-by-default) (persistence modes),
[ADR 0032](0032-runtime-checkpoint-cadence-is-policy-driven-per-workflow.md) (semantic boundaries),
[ADR 0020](0020-runtime-checkpoint-commit-post-commit-work.md) (commit and post-commit work) and
[ADR 0001](0001-checkpoint-gated-activity-execution-inspection.md) (checkpoint-gated inspection).

## Context

A checkpoint commit writes each changed activity execution as a row of `activity_execution_state`, each changed
inspection projection as a row of `activity_execution_inspection`, and a row of `activity_execution_hierarchy` per
inspection. Before it writes, it reads the existing rows of each participant type to decide between insert, update and
delete (`EfRuntimeCheckpointCommitStore.cs:151-156` on `64c91e945`, among the participants staged at `:118-196`). The
measured cost, from the report:

| 10x WriteLine | Balanced, SQLite | Balanced, PostgreSQL | Durable, SQLite | Durable, PostgreSQL |
|---|---:|---:|---:|---:|
| Activity and inspection row writes | 22 statements | inside 1 batched round trip | 118 statements | inside 66 batches |
| Activity, inspection and hierarchy staging reads | 3 statements | 3 round trips | 153 statements | 153 round trips |
| Whole run | 98 statements | 79 round trips | 1482 statements | 1646 round trips |

On SQLite, which runs one statement per command, the row writes are about a quarter of a Balanced run and grow by 2 per
activity. On PostgreSQL EF already batches them, so they cost no extra round trip; the staging reads do, one per
participant type per commit, and Durable makes 6 commits per activity.

This ADR proposes replacing the per-row writes with one appended row per commit and turning the three per-activity
tables into read models. It is a proposal: the report shows the gain is large for Durable and for SQLite, and small for
Balanced on PostgreSQL once session-authoritative reads have landed. The decisions the owner must make are listed at
the end.

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
| **Lazily**: a projector applies journal rows after commit, idempotently by sequence | Journal row only: the per-row writes and the three staging reads leave the commit | A read of a run's rows returns the read model plus the run's unapplied journal tail, merged in the store, so callers see current state | Recommended. |
| **On demand**: no per-activity read model; a reader folds the run's journal | Journal row only | Every read folds the journal, with periodic snapshots to bound the fold | Too slow for list and count queries across runs; the hierarchy pages and instance list need indexes. |

Under lazy fill the projector still writes the read-model rows, but off the run's path, in batches it chooses: on
SQLite a multi-row insert per table per pass, on PostgreSQL one batch. Under Durable it coalesces the six updates an
activity row receives into one write. Because the store merges the unapplied tail, the existing store contracts
(`IActivityExecutionStateStore`, `IActivityExecutionInspectionStore`, `IActivityExecutionHierarchyStore`) keep their
meaning and no caller changes.

### Expected commands and round trips per commit

Projected from the measured commit shapes, for a commit that changes `k` activity executions, excluding the
commit-marker lookup that precedes every commit today (the report recommends removing it independently). PostgreSQL
round trips assume EF's automatic savepoints are turned off in the commit store, also recommended independently; with
them on, add 4 to every PostgreSQL figure.

| Commit | SQLite statements today | SQLite with the journal | PostgreSQL round trips today | PostgreSQL with the journal |
|---|---:|---:|---:|---:|
| Balanced, 10 WriteLines (`k` = 11) | 32 | 8 | 9 | 6 |
| Durable, typical | about 9 | about 5 | about 7 | about 5 |

With the journal the commit's statements no longer depend on `k`: fence read and touch, the workflow and run-health
staging reads and writes where the commit carries them, the journal row and the marker (or one row if the journal row
is the marker). Over a whole 10x WriteLine run the report projects 57 to 33 SQLite commands for Balanced and about 947
to 742 for Durable, after #2582, #2584, #2586 and session-authoritative reads. On PostgreSQL the Balanced run gains 3
round trips; the Durable run gains about 153.

## Who reads the per-activity tables today, and how each would be served

File and line refer to `64c91e945`. "Session" means the session-authoritative reads of the report's prototype: inside a
drain that holds the ownership lease, the first read loads the run's rows (read model plus unapplied tail) and every
later read is answered from memory.

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
| Alterations | `WorkflowAlterationActorCommandExecutor.cs:79`, `WorkflowAlterationTargetCaptureTask.cs:142`, `RuntimeWorkflowMigrationQuiescenceProbe.cs:87`, `MigrateWorkflowAlterationHandler.cs:125` | Read model plus tail. If alterations take the ownership lease, which the report recommends for other reasons, they can use a session |
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

The journal changes what a commit writes, not when a commit happens, so ADR 0080 D2's table holds unchanged:

| Mode | Commits | After a crash, today | After a crash, with the journal |
|---|---|---|---|
| Durable | After every activity | Resumes at the last completed activity | The same: the last journal row is the last completed activity |
| Balanced | On suspend, fault, completion, end of burst, every N activities | Activities since the last commit run again | The same |
| Ephemeral | Only on suspend or fault, plus a completion record | A run that never suspended leaves only its completion record | The same: one journal row for the completion record, or none with `RecordCompletion` off |

What changes is the read model's freshness. Under lazy fill a crash between a commit and its projection leaves the read
model behind the journal; the projector catches up from the watermark, and every store read merges the tail meanwhile,
so no reader sees the gap. The journal is the source of truth; the read models can be rebuilt from it.

## Durable mode

Durable gains the most. It commits 6 times per WriteLine today, and each commit pays a staging read per participant
type and a write per changed row. With the journal a Durable commit is about 5 statements whatever it changed, and the
projector writes each activity row once instead of six times. Projected for 10x WriteLine after the other planned work:
about 947 to 742 SQLite commands and 1077 to 924 PostgreSQL round trips.

The report also measures that a Durable defined as one commit per activity, as D2 words it, costs about a fifth of
today's Immediate before any of this work. The journal composes with either definition.

## Providers

| Provider | Effect |
|---|---|
| SQLite | The largest gain: one statement per command, so the per-row writes and staging reads are each a command. The journal row is `TEXT`. The projector can use multi-row `INSERT ... VALUES`, which EF does not batch. One writer at a time, so the shorter commit also shortens the database lock. |
| PostgreSQL | EF already batches the writes into one round trip, so the gain is the staging reads (one round trip per participant type per commit) and fewer index updates on the hot path: three tables with several indexes each become one append. `text` is cheaper to write than `jsonb`; use `jsonb` only if the journal is queried. |
| SQL Server | Batches like PostgreSQL. `nvarchar(max)` payload. EF's automatic savepoints apply here too (`SAVE TRANSACTION`). Not measured. |
| MySQL | The Oracle provider's batching was not measured. A large commit's payload must stay under `max_allowed_packet`; the per-commit activity cap of Balanced bounds it. |

Payload size grows with the activities per commit: a Balanced commit of 40 WriteLines carries 82 row images. The
per-commit activity cap bounds it.

## Migration

Elsa 4 is unreleased, so there is no compatibility path. Add the journal table to `RuntimeDbContext` and its four
provider migrations, or to the single squashed migration per context that ADR 0080 D4 plans before the release
candidate. Switch the commit store to append, add the projector and the tail merge to the three stores, and recreate
preview databases. No dual-write period.

## Alternatives considered

- **SQLite multi-row inserts, no journal.** The commit store writes the two activity tables with one multi-row
  `INSERT` each instead of EF's per-row inserts. Balanced 10x WriteLine on SQLite goes from 57 to about 37 commands
  (after the other planned work) at a fraction of the journal's cost. It does nothing for PostgreSQL, which batches
  already, and leaves the staging reads. **The cheapest route to under 40 commands on SQLite.**
- **Slimmer inspection records.** Fewer bytes per row, the same number of statements and round trips: inspection rows
  are one per activity row either way. Helps WAL volume only.
- **Dropping inspection rows when no observer is attached.** Removes, on today's code, 11 inserts and 11 reads per
  Balanced 10x WriteLine run (22 SQLite commands, 11 PostgreSQL round trips); after session-authoritative reads only
  the inserts remain to save. But inspection is the run's history (ADR 0001): a run nobody watched could not be
  inspected later. Acceptable only as an explicit per-workflow setting, like `RecordCompletion`.
- **Session-authoritative reads alone.** Removes the reads, not the writes. Measured on the prototype; recommended
  regardless of this ADR.
- **Event-sourcing the whole run.** The journal as the only truth for every run table. Too large a change before GA.

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

1. **Build the journal, or stop at session-authoritative reads plus the small commit-store fixes plus SQLite multi-row
   inserts.** On PostgreSQL the journal adds 3 round trips of saving to a Balanced 10x WriteLine run after those; on
   Durable it adds about 153.
2. **Fill strategy.** Lazy with a tail merge (recommended), same transaction (no gain), or on demand.
3. **Whether the journal row is the commit marker**, which saves one statement per commit and makes the journal the
   replay key.
4. **Payload format.** Full row images per changed id (recommended: idempotent and simple to fold) or patches.
5. **Retention.** Keep applied journal rows as execution history, or compact them behind a snapshot.
6. **Inspection freshness contract.** Whether the inspection API may lag one projector pass, or must always merge the
   tail.
7. **Whether SQLite is a production target**, which decides whether its 22 per-row inserts justify the journal or the
   multi-row alternative suffices.
