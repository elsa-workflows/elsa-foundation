---
status: proposed
date: 2026-11-11
---

# The runtime core keeps a journal per run and derives everything else

Status: proposed (2026-11-11, revised 2026-11-12 after three design review rounds). Written under the owner's goal of 11 November 2026, which is recorded word for word on [#4175](https://github.com/elsa-workflows/elsa-foundation/issues/4175#issuecomment-6817195557). Nothing is built. When the owner approves, one commit on the proposing pull request sets this record to accepted, with the date and a link to the approval, and the pull request is then merged.

Program: [Journal-first runtime core](../program-goals/journal-core.md), [#4175](https://github.com/elsa-workflows/elsa-foundation/issues/4175). Owner decisions already taken, recorded on [#2892](https://github.com/elsa-workflows/elsa-foundation/issues/2892#issuecomment-6807409653) and [#2564](https://github.com/elsa-workflows/elsa-foundation/issues/2564#issuecomment-6807592241): the new core is built beside the current one behind the same host-facing contracts; the break is clean (new tables, no migration of runs in flight, one storage contract in place of the per-entity stores); work on epic #2564 stopped after #4166.

Once accepted, this record supersedes the verdict of [ADR 0084](0084-runtime-commit-journal.md) ("not worth building now") and, for the runtime core, recommendation N2 of the [August execution-model comparison](../reports/execution-model-comparison-2026-08.md). It does not supersede N1 (no partitioning). [ADR 0080 D2](0080-elsa-4-simplification-decisions.md#d2--persistence-modes-balanced-by-default) and [ADR 0032](0032-runtime-checkpoint-cadence-is-policy-driven-per-workflow.md) stay as they are. The ports of the [9 November clarification](../program-goals/unified-execution.md) keep their contracts; keeping them is a proposal of this record, not an earlier decision.

## Decision

1. **A run's durable truth is its journal.** One table holds one row per commit of a run, keyed by run and sequence number. The row stores what the commit changed. A commit appends the next row.
2. **The head row is the fence.** Each run has one head row with an owner, an owner epoch, a lease expiry and the last sequence. Taking the lease bumps the epoch. Renewing it extends the expiry where owner and epoch still match and changes nothing else. An append updates the head only where epoch and last sequence are the ones its working set holds; otherwise the whole append fails. The journal's key refuses a second row at the same sequence as a second line of defence.
3. **Run state is a working set.** The run's actor loads the latest snapshot and the rows after it, keeps the state in memory, and appends what changed. A pass begins by taking the lease. That statement returns the last sequence, and the actor loads again if it differs from what it holds. The pass's last write releases the lease.
4. **The ports keep their contracts.** The new core is a second implementation behind the checkpoint commit port. The commit keeps its shape: what it carries is what the journal row stores. The store interfaces that work handlers, enrichers and activities use are answered from the working set inside a run's actor. Outside it they are answered from the head, from an activity's record row, or from a read-only copy of the working set.
5. **Work reaches a run through one inbox.** Adding a command is create-only by command identity. Rows are read in the order the database assigned them. An append names the commands it consumed and deletes their rows in its transaction. Work a run schedules for itself is working-set state, not inbox rows.
6. **Effects are rows written by the append.** Each effect a commit causes elsewhere is one row with a status, a claim and an attempt count. The append inserts it claimed by the committing node, which delivers it right after the commit. A dispatcher takes over rows whose claim lapsed or whose retry is due. A delivered row stays, marked delivered, until the run is deleted, because code reads it as evidence.
7. **Tables that Studio and the API read are projections**, filled from the journal after the commit and never read by the engine to decide anything.
8. **One storage contract.** A provider implements one contract of twenty operations or fewer, listed below. There is one implementation on EF Core for SQLite, PostgreSQL, SQL Server and MySQL, and one in memory.

## Shape

### What is stored

| Table | Row | Written by | Read by |
|---|---|---|---|
| Run journal | One per commit: run, sequence, commit id, the commit's changes; at intervals a snapshot, in chunks | The append | Loads; the projector |
| Run head | One per run: the execution record (status, pinned executable, tenant, correlation, parent, partition, authority), owner, epoch, lease expiry, last sequence, snapshot position, next due time. Kept as a tombstone when the run is deleted | The start; the lease statements; the append and the settling of a command, each conditional on the epoch | Finds of the execution record, instance lists and counts, the sweep, whoever adds a command |
| Run inbox | One per command: run, command id, kind, payload, when it may run, an order key assigned by the database | Whoever adds work, conditional on a live head | The run's actor; deleted by the append that consumed it |
| Wait index | One per live wait: stimulus hash or due time, run, wait id, generation, revision | The run's append. Another run's append that depends on the wait bumps its revision | Stimulus routing, the timer pump, admission conditions of other runs |
| Effects | One per effect: kind, payload, status, claim owner and token, attempt count, due time | The append; the claim and outcome statements | The committing node, the dispatcher, lookups by effect id |
| Activity records | One per terminal activity that left the working set: its state as the store interface returns it. Never updated | The append that writes a snapshot | Finds and lists of activities that miss the working set |
| Executable pins | One per run and executable in the closure of the run's executable, each an enforced reference to the executable's row | The start; removed when the head becomes a tombstone | The database, when an executable is deleted |

These seven tables replace fourteen that hold run state today (workflow execution, activity execution, inspection, hierarchy, bookmarks, durable timers, durable values, scheduler state, scheduler work, liveness, incidents, run health, the commit marker and the outbox). The projections are one row per activity execution (its state and its inspection together), hierarchy, incidents and run health, each written by the projector conditional on sequence.

Other tables keep their own stores and their own checks:

- **Shared between runs, written inside an append's transaction as inside a commit's today:** dispatch records (a parent, a child and the dispatcher all write one, each under the record's own revision check), background activity work, alteration jobs, model executions and their inputs.
- **Written by their own stores outside a commit, as today:** holds, poison records, user tasks, test scopes.
- **Not run state, their stores unchanged:** definitions, executables with their source references and coordination rows, activity templates, trigger bindings, recurring trigger schedules, activation slots. The executable root write lease keeps its table and its manager for its other callers (activation, test runs, the protocol router and BPMN's model runtime); the outbox store's use of it for a stimulus's recipients moves with that code to the effects path. What changes is that the new core does not take it when a run starts.

### A commit

One transaction: insert the journal row; update the head `where epoch = expected and last sequence = expected`; insert and delete wait-index rows; delete the consumed inbox rows; insert effect rows. The rows of one table go in one statement, however many there are. A run's own state is written without reading it first. The number of statements does not grow with the number of activities; the journal row does. Participants that own a shared table keep the reads and revision checks they have today.

The head update also writes the next due time, and it releases the lease when the pass ends with this append.

A commit without a fence, which exists today for an alteration on a host that replaced the ownership service, is conditional on the last sequence only.

A commit id is unique in the journal, so a commit sent twice is refused the second time and answered from the stored row. Receipts that must outlive trimming (the inputs of a model execution, the commands an alteration job applied) are part of the run's state.

**Conditions on something outside the run.** Some commits hold only while something else is still true: a model execution is admitted only while its native parent's wait exists and its admitting decision is current, and a test run only while its scope is open. Such a commit carries the condition as it does today.

- The append applies a condition on another run's wait as one write on that wait-index row: it matches the generation and bumps the revision. No row matched means the append fails.
- The run that owns a wait changes or deletes its row only where the revision is the one it last read. So both orders hold. An admission that comes after the owner removed the wait fails. An owner that removes the wait after an admission fails its append, reads its wait rows again, runs the pass again and now finds what was admitted.
- A pass that resumes or cancels a wait that can admit model executions reads the wait's row first, so the ordinary case does not pay a failed append. That is one key read on that path only.
- A wait row stands for its waiting activity. An append that completes, cancels or faults that activity, or ends the run, removes the row in the same transaction. The row at its generation therefore establishes what today's check of the parent's workflow and activity rows establishes. The facts that check also compares and that never change (tenant, authority, test scope) are read from the parent's head.

### Starting, resuming, finishing

A start is routed by placement as today. The host that receives it inserts the head with itself as owner, the executable pins and the first inbox command in one transaction. A resume adds an inbox command that names its wait and generation.

Commands are handled in the order of their keys. A command that is handled without a commit is deleted by a small transaction that is conditional on the head's epoch. A command that must wait (a handler answered "retry after", or the work is temporarily unavailable) keeps its row and gets a later "may run" time under the same condition; it holds back the commands behind it, as today.

Work a run schedules for itself (invoking or completing an activity, notifying a parent activity, a checkpoint) is working-set state, each item with a due time. Every append writes the head's next due time: the earliest due time of that work, the commit time when work is ready now, and nothing when the run can only continue on a new command. A handler that asks for a delay gives the item a new due time, and the pass does not end without an append that records it.

### Who drives a run

Placement routes a run to a host as today. A pass begins with one statement that takes the lease: it sets the owner, bumps the epoch and sets the expiry where no lease is live or the caller holds it, and returns the last sequence. From then on an older owner's appends fail, also if that owner was only paused. During a long pass a heartbeat renews the lease without changing the epoch, so it cannot disturb the pass's own append. The last write of a pass, an append or the settling of a command, releases the lease. Whether a live lease may be taken over stays the ownership service's rule, as today.

The node that adds a command also signals the run's host through the command transport, as today. The signal is not durable; the sweep is the fallback. The sweep finds a run whose lease is not live and that has an inbox command that may run, or a next due time that has passed. The second covers a host that died between two appends and delayed work that has come due.

An Ephemeral run that completes without a record deletes its head, pins and inbox rows in one transaction. A commit that has an effect is always appended, whatever the mode.

Ownership per partition of runs and commits shared across runs are a later slice of the program, after the old core is deleted, with their own decision.

### What a node that lost a run can still write

This is the question of #4043. On the new core:

- **Its append fails**, and with it the wait-index changes, inbox deletes, effect inserts and activity records of that append, and any bump it carried on another run's wait.
- **Its heartbeat changes no row**, because the epoch moved, and it stands down.
- **Its deletes and delays of inbox commands fail**, because they are conditional on the epoch.
- **It can add a command to an inbox.** Any node may; it is create-only and conditional on a live head.
- **It can finish an effect it had claimed** only while its claim is current; a claim that lapsed and was retaken refuses it.
- **It can write a projection row.** Projection writes are conditional on sequence, so an older write cannot replace a newer one.

There is no queue copy to reconcile, so the fault of #4043 has no counterpart. The conformance suite carries a test for each line.

### Reads outside the run's actor

Work handlers, enrichers and intent handlers call the store interfaces from outside the run's actor: the executor that passes a child's notification to its parent makes six finds on the parent, and every resume finds the run's execution record. Today each is a read by key. On the new core:

- **The execution record** is read from the head: one read by key.
- **An activity** is read from a read-only copy of the run's working set. The node whose actor holds the run answers from the state of its last append, after one read of the head confirms the sequence. Another node keeps a copy per run in a bounded cache and brings it up to date by reading the journal rows after the copy's sequence.
- **A terminal activity that left the working set** is read from its record row.

A read-only copy is never written from and decides nothing durable: whatever a handler concludes from it reaches the run as a command, and the run's actor decides again on its own state.

### Persistence modes

Unchanged in meaning; [ADR 0080 D2](0080-elsa-4-simplification-decisions.md#d2--persistence-modes-balanced-by-default) and ADR 0032 say which boundaries each mode saves, and each of those is an append. An activity that is not safe to repeat has its attempt appended before it runs, as today.

### Size and trimming

- **Snapshots** are written in chunks, so no single row has to hold a run. A commit whose changes exceed a row limit is split into chunks within one append.
- **Terminal activities** stay in the working set until the next snapshot. The append that writes that snapshot inserts their record rows and the snapshot leaves them out. A snapshot therefore grows with what is live, not with how long the run has been going, and so do a load and a read-only copy.
- **Finds and lists** are answered from memory while a run has dropped nothing; the working set counts what it dropped. After that, a find that misses the working set reads the record row, and a list adds the record rows to what the working set holds. An activity is in the working set, in a record row, or for one snapshot interval in both with the same content, so there is nothing to reconcile.
- **Trimming** removes journal rows and older snapshots behind the smaller of the newest snapshot's position and the projector's position, and never the newest row. Record rows and effect rows stay until the run is deleted.
- **A load** reads the head, the snapshot the head names and the rows after it. It checks that every chunk is there and that the sequences run without a gap up to the head's last sequence, and reads again if not. That is what makes a load safe beside a trim.

### Projections and history

The owner's decisions of 11 November stand: the run viewer's rows are written after the commit by default, with a switch back, and the history level per workflow is Full, State only or Minimal with default Full. Activity state, bookmarks and incidents are working-set truth; the engine never reads a projection. The engine's readers of inspection rows today (the accumulator that merges a projection, the migrate alteration and the poisoned-work incident observer) read the working set.

The projector reads a run's journal rows after its position, writes the projection rows and the position in one transaction, and can run behind. A sweep finds runs whose position is behind their head.

### Executable retention

Today a run's first commit takes the root write lease on its executable and that executable's dependency closure, so that the executable garbage collector cannot delete any of them while the run's row is being written. On the new core the start inserts one pin row per executable in the closure, each an enforced reference, in the transaction that inserts the head. The database then refuses to delete an executable a run pins, and refuses a pin on an executable that is gone, whatever the timing. The closure of an executable never changes, so it is computed once per node and cached with the executable.

One thing changes outside the core, in slice S10: the EF executable store treats the database's refusal as "still referenced". The garbage collector keeps asking the execution-state store interface which executables runs pin, and the new core answers that from the heads. The in-memory implementation enforces the same reference in process.

### The storage contract

The operations a provider implements. This is the design's count, not a list of signatures.

1. Start a run: head, pins, first command.
2. Add a command.
3. Begin a pass: take the lease and read the commands that may run.
4. Renew the lease.
5. Append.
6. Settle a command without a commit: delete or delay it; release the lease.
7. Load a run: head, snapshot, rows after a sequence. Read-only copies and the projector use the same operation.
8. Read heads: one by id, or a filtered list or count.
9. Read activity records: one by id, or a run's.
10. Query the wait index: by stimulus, by due time, by run.
11. Claim effects that are due.
12. Record an effect's outcome.
13. Find an effect by id.
14. Find runs to drive.
15. Write a run's projection rows and position.
16. Find runs whose projection is behind.
17. Trim a run.
18. Delete a run: tombstone, pins, rows.

The stores of the shared tables take part in an append's transaction as they take part in a commit's today. Reading the projections for the HTTP API, Studio and the Dashboard is a separate, read-only query contract.

The EF implementation writes its conditional and many-row statements as SQL, with a dialect per engine where the engines differ. The command and round-trip targets below depend on that: EF's own change tracking sends one command per row on SQLite, as the captures of 11 November show.

## Ports

The contracts named on 9 November stay: the protocol driver and source, the checkpoint commit store with its commit and fence, the bookmark resume dispatcher, the enrichers and the intent handlers. The new core implements the checkpoint commit store, and the lookup of an effect by id that BPMN and DispatchWorkflow use.

Code that reaches past the ports today is changed, read from the code:

- BPMN's six direct enqueues onto the scheduler queue, five of them inside its intent handlers, become inbox commands.
- BPMN tests for concrete types of the current core: the in-process actor provider at four sites, and the three coalescing stores (workflow execution, activity execution and outbox) by exact type in one place. Those tests are replaced.
- The activities that inject stores (`Parallel`, `GraphActivity`, `DispatchWorkflow`, `RunEtl`, `RunRules`, `EvaluateDecision`, `HumanReview`) are answered as "Reads outside the run's actor" says, or from the working set when they run inside it.
- The Dashboard, and the tools and probes that read the runtime database context directly, read projections through the query contract.

The per-entity store interfaces stay public and are answered by the new core; no provider implements them any more. Taking them off the public surface, and a cleaner commit shape, are a later slice (S16), after the old core is gone. That narrows the owner's decision of 11 November ("one storage contract in place of the per-entity stores") and is part of what he approves here.

## What stays and what goes

Figures are from a survey of the branch of #4166 (`140c05c403`) on 11 November, by regular-expression counts, not compiler output.

| Stays | Goes when the old core is deleted |
|---|---|
| Activity authoring contracts and `ActivityExecutionContext` | The coalescing session and its decorators (5,116 lines, which include the outbox's overlay copy of 222) |
| The port contracts and commit models (1,214 lines), the committer (803), the enrichers (13 registered) and the intent handlers | The EF and in-memory checkpoint commit stores with their staging helpers and the commit marker (about 4,600) |
| What the work handlers decide (14,269 lines) | The scheduler queue, its overlay, claims and anchors (2,419) |
| Alterations and incidents as behaviour (7,335 and 3,471) | The outbox store and processor (4,285), replaced by the effects table and dispatcher |
| Executables, their cache and their stores; the root write lease for its other callers (about 670 lines) | The liveness store, fencing-token bookkeeping and fence cursors (part of 2,823; the ownership service is reimplemented on the head) |
| Persistence modes, ADR 0032 boundaries | The recovery scanner and resumption sweep in their present form (2,422) |
| The drainer as the loop that runs a pass; its reconciliation goes (4,419 in all) | The EF implementations of the stores of the fourteen run-state tables, and those stores as provider contracts |
| The HTTP API and Studio behaviour | The current migrations (91,407 lines in 68 files, of which 4,385 belong to the distributed project) |

## How it is proven

Each figure has a before value and a target. Targets are derived from the design; nothing is measured, because nothing is built. They are part of what the owner approves.

**Robustness**

- The guarantees found in the documents and tests (139 in twelve areas, of which 19 are compatibility promises) become a conformance suite at the level of what a host can observe. It passes on the current core first, then on the new one. Where this design changes a guarantee on purpose (run details may lag), the suite states both forms. The survey lists 16 guarantees without a test it could name; for at least one of them test files exist that it did not read. Each of the 16 ends with a test in the suite.
- The append is tested with two writers and with a process killed at each step: before the transaction, inside it, after it and before the acknowledgement. The inbox, the effects table, the lease statements and a wait row's revision are each tested with two writers that commit in the opposite order to the one they started in, and with an old owner that wakes after a takeover. A load is tested beside a trim, and a read-only copy against a run that moved on.
- Every guard has a test and a removal check.

**Performance** (deterministic counts, no timing). "Today" was counted on 11 November with the command-cost project on the branch of #4166.

| Reference run | Today | Target | How the target follows |
|---|---:|---:|---|
| Ten WriteLines, Balanced: SQLite commands | 64 | 32 or fewer | Start 3 (head, pin, command). Begin pass 2. Append 3 (journal row, head, inbox delete). Dispatch enricher's query 1. Executable lookups 2. Projector at Full history 5 (journal read, activity rows, hierarchy rows, run health, position). About 16 |
| Ten WriteLines, Balanced: PostgreSQL round trips | 48 | 16 or fewer | Start 3. Begin pass 1. Append 3. Dispatch query 1. Executable lookups 2. Projector 4 (one read, one transaction of 3). About 14 |
| The same run at 20 and 40 WriteLines: SQLite commands | grows by 2 per activity | the same count at 10, 20 and 40 | Every statement is per table, not per activity; the rows grow |
| Ten WriteLines, Durable: PostgreSQL round trips | 230 | 100 or fewer | Start 3. Begin pass 1. Twelve appends at 3. Up to twelve projector passes at 4. Dispatch query 1. Executable lookups 2. About 91 |
| Fork, wait, resume, join, Balanced: PostgreSQL round trips | 142 | 60 or fewer | Start 3. Two passes begun at 1. Five appends at 3. Up to five projector passes at 4. Stimulus lookup 2. Resume command 2. One load 2. Two hold-state reads. Dispatch query 1. Executable lookups 2. One effect outcome 1. About 52 |
| A run that suspends and resumes 1,000 times, and a parent that dispatches a child 1,000 times: PostgreSQL round trips per resume and per child | not counted; counted on the current core in slice S1 | the same at the 10th and the 1,000th | A snapshot holds what is live; the execution record is on the head; a read-only copy reads only the rows after its sequence |

Two conditions sit under these figures: the five reads the current core makes after a drain are answered from memory, and a transaction costs about three round trips on PostgreSQL. The fork target was 55 in the previous version; a recount with the hold-state reads and the effect's outcome left it no margin, so it is 60.

PostgreSQL round trips are already the same at 10, 20 and 40 WriteLines today (48); the growth is in SQLite commands and in what the commit writes. A SQLite command that carries many rows does more work than one that carries one; the count is the measure the owner chose, and the row counts are reported beside it.

**Cleanliness**

| Measure | Today | Target |
|---|---:|---:|
| What a storage provider implements for run state | the store contracts of the fourteen tables, with their facets | one contract of 20 operations or fewer |
| Capability flags a provider answers for run state | 12: 4 on the scheduler queue, 4 on the commit store, 1 each on inspection, timers, health and recovery | 0 on the storage contract; the commit store's 4 remain as constants until the later port slice |
| Tables that hold run state as truth | 14 | 7 |
| Lines of the mechanisms listed under "goes" | about 21,000, plus the EF run-state stores | 16,000 or fewer for what replaces them |
| Migration lines | 91,407 | one initial migration per engine |
| Code outside the core that writes run state without a commit | 6 queue enqueues in BPMN | 0 |

Architecture guard tests hold the storage contract's surface. The pull request that deletes the old core reports the after values.

## Answers to the earlier documents

**ADR 0084: "not worth building now".** ADR 0084 projected that a journal for the three per-activity tables would save 3 of 80 PostgreSQL round trips. It kept every other table and added a journal beside them. This decision removes fourteen tables as truth, and with them the reads inside the commit, the commit marker, and the reconciliation between queue, overlay and outbox. That is where the saving and the smaller surface come from.

**Comparison N2** recommends against making an append-only command log the source of truth, because every query then needs a projection pipeline and a second store. This journal stores what each commit changed, not commands, and state is rebuilt by applying those changes, not by running workflow code again. Part of N2's cost still applies: the tables Studio and the API read become projections. They stay in the same database, filled by one projector; there is no second store. What changes for a user is that run details can lag the run, which the owner accepted on 11 November.

**Comparison N1: no partitioning.** Not superseded. The first version keeps today's placement.

**Runtime Efficiency: "No journal rewrite".** That boundary was set for the continuation of 1 November. The owner's direction of 11 November replaces it; the program goal is amended in the same pull request.

**What the comparison found the current core does better.** Of its seven findings, three concern storage: the default commit boundaries, fencing kept apart from placement, and one store. The modes are unchanged, and the fence is the head row and independent of placement. Everything stays in one database, but run details now lag the run, which the comparison counts as a cost of the designs it compared against.

## What three review rounds showed

Three independent rounds each found this design unsound, and each fix brought back something the current core already has:

- effect rows with status and claims, and delivery by the node that committed;
- a lease epoch that fences appends, with a renewal that does not move it;
- dispatch records as a shared table;
- rows for terminal activities, written once where today's are updated at every step;
- a revision on a wait that another run's admission bumps;
- a due time for delayed work that a sweep reads.

Those parts of the current core are essential and are rebuilt here, not avoided. What this decision removes is narrower than first written: per-entity state rows that are read and rewritten at every step, the second copies of queue and outbox with their reconciliation, the reads inside a commit, and the root write lease at a run's first commit.

## Risks

- **Two cores at once.** Until the switch the code base is larger. The plan ends in a switch and a deletion slice.
- **Rebuilding what works.** The lease, the effects table and the dispatcher are new implementations of things the current core does correctly today. Each needs the tests the current ones have.
- **Tests tied to the old internals.** About 3,177 of 5,379 test methods construct internals of the current core and are deleted with it. Because the ports and the store interfaces stay, tests that go through them can run on both cores.
- **A slow projector.** Studio shows less than is committed while the projector is behind, and a run's journal is not trimmed past it. Its lag is observable, and a test hook waits for it.
- **Conditions outside the run** are writes an append makes to rows it does not own and need their own two-writer tests.
- **Read-only copies** are a new mechanism. A handler that reads one can act on state the run has since left, as a handler that reads a row can today; the difference is a cache that has to be bounded and tested.
- **SQL per engine.** The many-row and conditional statements are written for four engines and tested on each.

## What the owner approves

1. The decision and the shape above, including that the ports keep their contracts.
2. The targets in "How it is proven".
3. That ownership per partition comes after the old core is deleted, as its own slice and decision, although the goal lists it in the target shape, and that #4175 closes before that slice.
4. That the per-entity store interfaces stay public, answered by the new core, until slice S16, which narrows the decision of 11 November as "Ports" says.

The switch of the default and the deletion of the old core each come back for a go-ahead when their conditions are met.
