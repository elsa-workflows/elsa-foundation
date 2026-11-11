---
status: proposed
date: 2026-11-11
---

# The runtime core keeps a journal per run and derives everything else

Status: proposed (2026-11-11). Written at the owner's direction of 11 November 2026 ("redo the core to achieve superiority in all axes: code cleanliness, robustness and performance"). Nothing is built. When the owner approves, one commit on the proposing pull request sets this record to accepted, with the date and a link to the approval, and the pull request is then merged.

Program: [Journal-first runtime core](../program-goals/journal-core.md), [#4175](https://github.com/elsa-workflows/elsa-foundation/issues/4175). Owner decisions already taken, recorded on [#2892](https://github.com/elsa-workflows/elsa-foundation/issues/2892#issuecomment-6807409653) and [#2564](https://github.com/elsa-workflows/elsa-foundation/issues/2564#issuecomment-6807592241): the new core is built beside the current one behind the same host-facing contracts; the break is clean (new tables, no migration of runs in flight, one storage contract in place of the per-entity stores); work on the current core for #2564 stopped after #4166.

Once accepted, this record supersedes the verdict of [ADR 0084](0084-runtime-commit-journal.md) ("not worth building now") and, for the runtime core, recommendation N2 of the [August execution-model comparison](../reports/execution-model-comparison-2026-08.md). It does not supersede N1 (no partitioning): see [Who drives a run](#who-drives-a-run). [ADR 0080 D2](0080-elsa-4-simplification-decisions.md#d2--persistence-modes-balanced-by-default) and [ADR 0032](0032-runtime-checkpoint-cadence-is-policy-driven-per-workflow.md) stay as they are, and so do the ports of the [9 November clarification](../program-goals/unified-execution.md).

## Decision

1. **A run's durable truth is its journal.** One table holds one row per commit of a run, keyed by run and sequence number. The row stores what the commit changed. A commit appends the next row.
2. **The journal's key is the fence.** Two writers of one run cannot both append sequence N+1; the database refuses the second. A node that lost a run fails at its append, and everything in that append's transaction fails with it.
3. **Run state is a working set.** The run's actor loads the latest snapshot and the rows after it once, keeps the state in memory, and appends what changed.
4. **The ports stay.** The new core is a second implementation behind the checkpoint commit port. The commit keeps its shape: what it carries is what the journal row stores. The store interfaces that work handlers, enrichers and activities use today are answered from the working set. Enrichers, intent handlers, BPMN's direct driver and the handlers' logic run unchanged.
5. **Work reaches a run through one inbox.** Adding a command is create-only by command identity. An append names the commands it consumed and deletes their rows in its transaction.
6. **Effects are rows written by the append.** Each effect a commit causes elsewhere is one row, inserted in the append's transaction and deleted when it has been delivered. It carries its own attempt count and due time.
7. **Tables that Studio and the API read are projections**, filled from the journal after the commit and never read by the engine to decide anything.
8. **One storage contract.** A provider implements a handful of operations. There is one implementation on EF Core for SQLite, PostgreSQL, SQL Server and MySQL, and one in memory.

## Shape

### What is stored

| Table | Row | Written by | Read by |
|---|---|---|---|
| Run journal | One per commit: run, sequence, commit id, the commit's changes; at intervals a snapshot, in chunks | The append | The run's actor on load; the projector |
| Run head | One per run: status, last sequence, snapshot position, next due time, the pinned executable, tenant, correlation. Kept as a tombstone when the run is deleted | The append, conditional on the last sequence | Instance lists and counts, the sweep, executable retention, whoever adds a command |
| Run inbox | One per command: run, command id, kind, payload, when it may run | Whoever adds work, conditional on a live head | The run's actor; deleted by the append that consumed it |
| Wait index | One per live wait: stimulus hash or due time, run, wait id, generation | The append | Stimulus routing, the timer pump, admission conditions of other runs |
| Effects | One per undelivered effect: kind, payload, attempt count, due time | The append; deleted on delivery | The dispatcher |
| Projections | Activity executions, inspection, hierarchy, incidents, run health | The projector, conditional on sequence | The HTTP API, Studio, the Dashboard |

These five tables replace thirteen that hold run state today (workflow execution, activity execution, inspection, hierarchy, bookmarks, durable values, scheduler state, scheduler work, liveness, incidents, run health, the commit marker and the outbox). The tables of other things a commit touches stay and are written in the append's transaction as they are in the commit's today: dispatch records, background activity work, alteration jobs, model executions and their inputs, holds, poison records, user tasks, test scopes. Definitions, executables, trigger bindings and activation slots are not run state and are untouched.

### A commit

One transaction: insert the journal row; update the head `where last sequence = expected`; insert and delete wait-index rows; delete the consumed inbox rows; insert effect rows. No row is read to decide how to write. The number of statements does not grow with the number of activities; the journal row does.

A commit id is unique in the journal, so a commit sent twice is refused the second time and answered from the stored row. Receipts that must outlive trimming (the inputs of a model execution, the commands an alteration job applied) are part of the run's state.

**Conditions on another run.** Some commits must hold only if another run still waits: a model execution is admitted only while its native parent's wait exists. Such a commit carries the condition, and the append touches that wait-index row conditionally in the same transaction. If the row is gone, the append fails. This is the one place an append writes outside its own run.

### Starting, resuming, finishing

A start inserts the head and the first inbox command in one transaction. A resume adds an inbox command that names its wait and generation. If the host dies, the sweep finds heads with unconsumed inbox rows. Two adders that commit in either order are both seen: the actor reads the rows that exist, and a row that commits later is read on the next pass.

An Ephemeral run that completes without a record deletes its head and inbox rows in one transaction. A commit that has an effect is always appended, whatever the mode.

### Who drives a run

As today: placement routes a run to a host, and a lease on the head decides who should work. Neither makes a write safe; decision 2 does. Ownership per partition of runs and commits shared across runs are a later slice of the program, after the switch, with their own decision. The journal needs no change for them.

### What a node that lost a run can still write

This is the question of #4043. On the new core:

- **Its append fails**, and with it the wait-index changes, inbox deletes and effect inserts of that append.
- **It can add a command to an inbox.** Any node may; it is create-only and conditional on a live head.
- **It can delete an effect row it delivered.** Delivery is at least once and keyed by run and sequence on the receiving side, so a second delivery by the new owner is absorbed.
- **It can write a projection row.** Projection writes are conditional on sequence, so an older write cannot replace a newer one.

There is no queue copy to reconcile, so the fault of #4043 has no counterpart. The conformance suite carries a test for each of the four lines.

### Persistence modes

Unchanged in meaning (ADR 0080 D2, ADR 0032). Durable appends after every activity. Balanced appends at a wait, a fault, a cancellation, an incident, an alteration, a child dispatch, completion, the end of a pass, and every N activities. Ephemeral appends at a wait or a fault and at the other boundaries the current core always saves, plus a completion record unless that is turned off. An activity that is not safe to repeat has its attempt appended before it runs.

### Size

A snapshot is written in chunks, so no single row has to hold a whole run. Terminal activity state that nothing can read again leaves the working set once it is in the journal; the rule that decides "nothing can read again" is the same one the Minimal history level needs. A commit whose changes exceed a row limit is split into chunks within one append.

### Projections and history

The owner's decisions of 11 November stand: the run viewer's rows are written after the commit by default, with a switch back, and the history level per workflow is Full, State only or Minimal with default Full. Incidents, activity state, bookmarks and dispatch records are working-set truth; the engine never reads a projection. Code outside the run's actor that needs another run's state reads its head, its wait-index rows, or loads it read-only.

### Executable retention

The head references the executable it pins, and the collector's delete is conditional on no head referencing it, in the delete statement itself. The dependency closure of an executable is stored as rows when the executable is saved. That replaces the root write lease taken at a run's first commit.

## Ports

The ports named on 9 November are unchanged in this decision: the protocol driver and source, the checkpoint commit store with its commit and fence, the bookmark resume dispatcher, the enrichers and the intent handlers. The new core implements the checkpoint commit store. The fence a commit carries identifies the load its working set came from, and the store turns it into the expected sequence.

Three seams bypass the ports today and are closed, read from the code: BPMN's six direct enqueues onto the scheduler queue become inbox commands; the activities that inject stores (`Parallel`, `GraphActivity`, `DispatchWorkflow`, `RunEtl`, `RunRules`, `EvaluateDecision`, `HumanReview`) are answered from the working set or a read-only load; the Dashboard reads projections through a query contract.

A cleaner commit shape and the removal of the per-entity store interfaces from the public surface are a later slice, after the old core is gone, with their own decision. Until then the per-entity interfaces remain as the working set's views and stop being provider contracts.

## What stays and what goes

Figures are from a survey of the branch of #4166 (`140c05c403`) on 11 November, by regular-expression counts, not compiler output.

| Stays | Goes when the old core is deleted |
|---|---|
| Activity authoring contracts and `ActivityExecutionContext` | The coalescing session and its decorators (5,116 lines) |
| The ports, the enrichers (13 registered) and the intent handlers | The EF checkpoint commit store, its staging helpers and the commit marker (6,617) |
| What the work handlers decide (14,269 lines) | The scheduler queue, its overlay, claims and anchors (2,419) |
| Alterations and incidents as behaviour (7,335 and 3,471) | The outbox's overlay copy and its reconciliation; the store and processor are replaced by the effects table and dispatcher (4,285 today) |
| Executables and their cache | Ownership rows, fencing tokens, fence cursors (2,823) |
| Persistence modes, ADR 0032 boundaries | The drainer's reconciliation (part of 4,419); the recovery scanner and resumption sweep in their present form (2,422) |
| The HTTP API and Studio behaviour | The EF implementations of the thirteen run-state stores, and those stores as provider contracts |
| | The current migrations (91,407 lines in 68 files, of which 4,385 belong to the distributed project) |

## How it is proven

Each figure has a before value and a target. Targets are derived from the design; nothing is measured, because nothing is built. They are part of what the owner approves.

**Robustness**

- The guarantees found in the documents and tests (139 in twelve areas, of which 19 are compatibility promises) become a conformance suite at the level of what a host can observe. It passes on the current core first, then on the new one. For 16 guarantees the survey found no test; each gets one.
- The append is tested with two writers and with a process killed at each step: before the transaction, inside it, after it and before the acknowledgement. The inbox and the effects table are each tested with two writers that commit in the opposite order to the one they started in.
- Every guard has a test and a removal check.

**Performance** (deterministic counts, no timing). "Today" was counted on 11 November with the command-cost project on the branch of #4166.

| Reference run | Today | Target | How the target follows |
|---|---:|---:|---|
| Ten WriteLines, Balanced: SQLite commands | 64 | 32 or fewer | Start: 2 inserts. One append: journal, head, inbox delete. Projector at Full history: 22 rows. Executable lookups: 2. About 29 |
| Ten WriteLines, Balanced: PostgreSQL round trips | 48 | 16 or fewer | Three transactions (start, append, projector) of one batch each at about 3 round trips, and 2 executable lookups. About 11 |
| The same run at 20 and 40 WriteLines: SQLite commands | grows by 2 per activity | equal to ten, projector writes excluded | The append's statements do not depend on length; the projector writes a row per activity at Full history |
| Ten WriteLines, Durable: PostgreSQL round trips | 230 | 80 or fewer | Twelve appends and up to twelve projector transactions at about 3 round trips each, the start, and 2 executable lookups. About 77 |
| Fork, wait, resume, join, Balanced: PostgreSQL round trips | 142 | 45 or fewer | Five appends and up to five projector transactions at about 3 round trips each, the start, the resume command, one load, and 2 executable lookups. About 40 |

PostgreSQL round trips are already the same at 10, 20 and 40 WriteLines today (48); the growth is in SQLite commands and in what the commit writes.

**Cleanliness**

| Measure | Today | Target |
|---|---:|---:|
| Operations a storage provider implements for run state | the store contracts of those thirteen tables, with their facets | 10 or fewer, in one contract |
| Capability flags a provider answers for run state | 4 on the scheduler queue, 4 on the commit store, 1 each on inspection and recovery | 0 on the storage contract; the commit store's 4 remain as constants until the later port slice |
| Tables that hold run state as truth | 13 | 5 |
| Lines of the mechanisms listed under "goes" | about 28,100, plus the EF run-state stores | 14,000 or fewer for what replaces them |
| Migration lines | 91,407 | one initial migration per engine |
| Code outside the core that writes run state without a commit | 6 queue enqueues in BPMN | 0 |

Architecture guard tests hold the storage contract's surface. The pull request that deletes the old core reports the after values.

## Answers to the earlier documents

**ADR 0084: "not worth building now".** ADR 0084 measured that a journal for the three per-activity tables would save 3 of 80 PostgreSQL round trips. This session's count on today's code agrees in kind: about 3 to 4 of 48. ADR 0084 kept every other table and added a journal beside them. This decision removes thirteen tables as truth, and with them the reads inside the commit, the ownership row, the commit marker and the reconciliation between queue, overlay and outbox. That is where the saving and the smaller code come from.

**Comparison N2** recommends against making an append-only log the source of truth, because every query then needs a projection pipeline and a second store. Here the projections stay in the same database, in tables shaped like today's, filled by one projector; there is no second store. What changes for a user is that run details can lag the run, which the owner accepted on 11 November.

**Comparison N1: no partitioning.** Not superseded. The first version keeps today's placement.

**Runtime Efficiency: "No journal rewrite".** That boundary was set for the continuation of 1 November. The owner's direction of 11 November replaces it; the program goal is amended in the same pull request.

**What the comparison found the current core does better.** Of its seven findings, three concern storage: the default commit boundaries, fencing kept apart from placement, and one store. All three are kept: the modes are unchanged, the fence is the journal key and independent of placement, and everything is in one database.

## Risks

- **Two cores at once.** Until the switch the code base is larger. The plan ends in a switch and a deletion slice, and the old core gets no new features meanwhile.
- **Tests tied to the old internals.** About 3,177 of 5,379 test methods construct internals of the current core and are deleted with it. Because the ports and the store interfaces stay, the tests that go through them can run on both cores.
- **A slow projector.** Studio shows less than is committed while the projector is behind. Its lag is observable, and a test hook waits for it.
- **The effects table is an outbox.** It keeps per-effect state because handlers defer and retry per effect. What disappears is the second copy and its reconciliation, not the table.
- **Conditions on another run** are the one cross-run write in an append and need their own two-writer tests.

## What the owner approves

1. The decision and the shape above.
2. The targets in "How it is proven".
3. That ownership per partition comes after the switch, as its own slice and decision.

The switch of the default and the deletion of the old core each come back for a go-ahead when their conditions are met.
