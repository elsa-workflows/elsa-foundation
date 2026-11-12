---
status: proposed
date: 2026-11-11
---

# The runtime core keeps a journal per run and derives everything else

Status: proposed (2026-11-11, revised 2026-11-12 after two design review rounds). Written at the owner's direction of 11 November 2026 ("redo the core to achieve superiority in all axes: code cleanliness, robustness and performance"). Nothing is built. When the owner approves, one commit on the proposing pull request sets this record to accepted, with the date and a link to the approval, and the pull request is then merged.

Program: [Journal-first runtime core](../program-goals/journal-core.md), [#4175](https://github.com/elsa-workflows/elsa-foundation/issues/4175). Owner decisions already taken, recorded on [#2892](https://github.com/elsa-workflows/elsa-foundation/issues/2892#issuecomment-6807409653) and [#2564](https://github.com/elsa-workflows/elsa-foundation/issues/2564#issuecomment-6807592241): the new core is built beside the current one behind the same host-facing contracts; the break is clean (new tables, no migration of runs in flight, one storage contract in place of the per-entity stores); work on epic #2564 stopped after #4166.

Once accepted, this record supersedes the verdict of [ADR 0084](0084-runtime-commit-journal.md) ("not worth building now") and, for the runtime core, recommendation N2 of the [August execution-model comparison](../reports/execution-model-comparison-2026-08.md). It does not supersede N1 (no partitioning). [ADR 0080 D2](0080-elsa-4-simplification-decisions.md#d2--persistence-modes-balanced-by-default) and [ADR 0032](0032-runtime-checkpoint-cadence-is-policy-driven-per-workflow.md) stay as they are. The ports of the [9 November clarification](../program-goals/unified-execution.md) keep their contracts; keeping them is a proposal of this record, not an earlier decision.

## Decision

1. **A run's durable truth is its journal.** One table holds one row per commit of a run, keyed by run and sequence number. The row stores what the commit changed. A commit appends the next row.
2. **The head row is the fence.** Each run has one head row with an owner epoch and the last sequence. Taking or renewing the lease bumps the epoch in one statement. An append updates the head only where epoch and last sequence are the ones its working set was loaded under; otherwise the whole append fails. The journal's key refuses a second row at the same sequence as a second line of defence.
3. **Run state is a working set.** The run's actor loads the latest snapshot and the rows after it, keeps the state in memory, and appends what changed. Before each pass it confirms its epoch and sequence with the statement that renews its lease; if they moved, it loads again before it runs anything.
4. **The ports keep their contracts.** The new core is a second implementation behind the checkpoint commit port. The commit keeps its shape: what it carries is what the journal row stores. The store interfaces that work handlers, enrichers and activities use are answered from the working set inside a run's actor, and from a read-only load outside it.
5. **Work reaches a run through one inbox.** Adding a command is create-only by command identity. Rows are read in the order the database assigned them. An append names the commands it consumed and deletes their rows in its transaction.
6. **Effects are rows written by the append.** Each effect a commit causes elsewhere is one row with a status, a claim and an attempt count. A dispatcher claims and delivers it. A delivered row stays, marked delivered, until the run is trimmed, because code reads it as evidence.
7. **Tables that Studio and the API read are projections**, filled from the journal after the commit and never read by the engine to decide anything.
8. **One storage contract.** A provider implements one contract of about fifteen operations. There is one implementation on EF Core for SQLite, PostgreSQL, SQL Server and MySQL, and one in memory.

## Shape

### What is stored

| Table | Row | Written by | Read by |
|---|---|---|---|
| Run journal | One per commit: run, sequence, commit id, the commit's changes; at intervals a snapshot, in chunks | The append | The run's actor on load; read-only loads; the projector |
| Run head | One per run: status, owner, epoch, lease expiry, last sequence, snapshot position, next due time, the pinned executable, tenant, correlation. Kept as a tombstone when the run is deleted | The start; the lease statement; the append, conditional on epoch and last sequence | Instance lists and counts, the sweep, whoever adds a command |
| Run inbox | One per command: run, command id, kind, payload, when it may run, an order key assigned by the database | Whoever adds work, conditional on a live head | The run's actor; deleted by the append that consumed it |
| Wait index | One per live wait: stimulus hash or due time, run, wait id, generation | The append | Stimulus routing, the timer pump, admission conditions of other runs |
| Effects | One per effect: kind, payload, status, claim owner and token, attempt count, due time | The append; the dispatcher's claim and outcome | The dispatcher; lookups by effect id |
| Projections | One row per activity execution (its state and its inspection together), hierarchy, incidents, run health | The projector, conditional on sequence | The HTTP API, Studio, the Dashboard |

These five tables replace fourteen that hold run state today (workflow execution, activity execution, inspection, hierarchy, bookmarks, durable timers, durable values, scheduler state, scheduler work, liveness, incidents, run health, the commit marker and the outbox).

Other tables keep their own stores and their own checks:

- **Shared between runs, written inside an append's transaction as inside a commit's today:** dispatch records (a parent, a child and the dispatcher all write one, each under the record's own revision check), background activity work, alteration jobs, model executions and their inputs.
- **Written by their own stores outside a commit, as today:** holds, poison records, user tasks, test scopes.
- **Not run state, untouched:** definitions, executables, trigger bindings, activation slots.

### A commit

One transaction: insert the journal row; update the head `where epoch = expected and last sequence = expected`; insert and delete wait-index rows; delete the consumed inbox rows; insert effect rows. A run's own state is written without reading it first. The number of statements does not grow with the number of activities; the journal row does. Participants that own a shared table keep the reads and revision checks they have today.

A commit without a fence, which exists today for an alteration on a host that replaced the ownership service, is conditional on the last sequence only.

A commit id is unique in the journal, so a commit sent twice is refused the second time and answered from the stored row. Receipts that must outlive trimming (the inputs of a model execution, the commands an alteration job applied) are part of the run's state.

**Conditions on something outside the run.** Some commits hold only while something else is still true: a model execution is admitted only while its native parent's wait exists and its admitting decision is current, and a test run only while its scope is open. Such a commit carries the condition as it does today, and the append applies it in its transaction as a conditional write on that row. If the condition fails, the append fails.

### Starting, resuming, finishing

A start inserts the head, with the starter as owner, and the first inbox command in one transaction. A resume adds an inbox command that names its wait and generation.

A command that is handled without a commit is deleted by a small transaction that is conditional on the head's epoch. A command that must wait (a handler answered "retry after", or the work is temporarily unavailable) keeps its row and gets a later "may run" time under the same condition.

The sweep finds a run in two ways: its head has unconsumed inbox rows that may run, or its lease has lapsed while the head says a pass was under way. The second covers a host that died after consuming a command and before its next append.

An Ephemeral run that completes without a record deletes its head and inbox rows in one transaction. A commit that has an effect is always appended, whatever the mode.

### Who drives a run

Placement routes a run to a host as today. The host takes the lease with one statement on the head, which bumps the epoch. From then on an older owner's appends fail, also if that owner was only paused. Ownership per partition of runs and commits shared across runs are a later slice of the program, after the old core is deleted, with their own decision.

### What a node that lost a run can still write

This is the question of #4043. On the new core:

- **Its append fails**, and with it the wait-index changes, inbox deletes and effect inserts of that append.
- **Its deletes and delays of inbox commands fail**, because they are conditional on the epoch.
- **It can add a command to an inbox.** Any node may; it is create-only and conditional on a live head.
- **It can finish an effect it had claimed** only while its claim is current; a claim that lapsed and was retaken refuses it.
- **It can write a projection row.** Projection writes are conditional on sequence, so an older write cannot replace a newer one.

There is no queue copy to reconcile, so the fault of #4043 has no counterpart. The conformance suite carries a test for each line.

### Persistence modes

Unchanged in meaning; [ADR 0080 D2](0080-elsa-4-simplification-decisions.md#d2--persistence-modes-balanced-by-default) and ADR 0032 say which boundaries each mode saves, and each of those is an append. An activity that is not safe to repeat has its attempt appended before it runs, as today.

### Size

A snapshot is written in chunks, so no single row has to hold a whole run. A commit whose changes exceed a row limit is split into chunks within one append. Terminal activity state can leave the working set once it is in the journal, but a short record of it stays (identity, node, parent, status, a reference to its result), because code outside the run asks for it: the executor that resumes a parent checks the parent activity's status, and the HTTP response delivery lists a run's completed activities.

### Projections and history

The owner's decisions of 11 November stand: the run viewer's rows are written after the commit by default, with a switch back, and the history level per workflow is Full, State only or Minimal with default Full. Activity state, bookmarks and incidents are working-set truth; the engine never reads a projection. The engine's readers of inspection rows today (the accumulator that merges a projection, the migrate alteration and the poisoned-work incident observer) read the working set.

### Executable retention

The head references the executable it pins through an enforced reference, cleared when the head becomes a tombstone. The database then refuses to delete an executable a run pins, whatever the timing. That replaces the root write lease taken at a run's first commit.

## Ports

The contracts named on 9 November stay: the protocol driver and source, the checkpoint commit store with its commit and fence, the bookmark resume dispatcher, the enrichers and the intent handlers. The new core implements the checkpoint commit store, and the lookup of an effect by id that BPMN and DispatchWorkflow use.

Code that reaches past the ports today is changed, read from the code:

- BPMN's six direct enqueues onto the scheduler queue, five of them inside its intent handlers, become inbox commands. BPMN also checks for three concrete types of the current core (the coalescing execution-state store, the in-process actor provider and the committer); those checks are replaced.
- The activities that inject stores (`Parallel`, `GraphActivity`, `DispatchWorkflow`, `RunEtl`, `RunRules`, `EvaluateDecision`, `HumanReview`) are answered from the working set or a read-only load.
- The Dashboard, and the tools and probes that read the runtime database context directly, read projections through a query contract.

A cleaner commit shape and the removal of the per-entity store interfaces from the public surface are a later slice, after the old core is gone, with their own decision.

## What stays and what goes

Figures are from a survey of the branch of #4166 (`140c05c403`) on 11 November, by regular-expression counts, not compiler output.

| Stays | Goes when the old core is deleted |
|---|---|
| Activity authoring contracts and `ActivityExecutionContext` | The coalescing session and its decorators (5,116 lines) |
| The port contracts and commit models (1,214 lines), the committer (803), the enrichers (13 registered) and the intent handlers | The EF and in-memory checkpoint commit stores with their staging helpers and the commit marker (about 4,600) |
| What the work handlers decide (14,269 lines) | The scheduler queue, its overlay, claims and anchors (2,419) |
| Alterations and incidents as behaviour (7,335 and 3,471) | The outbox store, its overlay copy and processor (4,285), replaced by the effects table and dispatcher |
| Executables and their cache; the executable store | The liveness store, fencing-token bookkeeping and fence cursors (part of 2,823; the ownership service is reimplemented on the head) |
| Persistence modes, ADR 0032 boundaries | The recovery scanner and resumption sweep in their present form (2,422) |
| The drainer as the loop that runs a pass; its reconciliation goes (4,419 in all) | The EF implementations of the stores of the fourteen run-state tables, and those stores as provider contracts |
| The HTTP API and Studio behaviour | The current migrations (91,407 lines in 68 files, of which 4,385 belong to the distributed project) |

## How it is proven

Each figure has a before value and a target. Targets are derived from the design; nothing is measured, because nothing is built. They are part of what the owner approves.

**Robustness**

- The guarantees found in the documents and tests (139 in twelve areas, of which 19 are compatibility promises) become a conformance suite at the level of what a host can observe. It passes on the current core first, then on the new one. Where this design changes a guarantee on purpose (run details may lag), the suite states both forms. The survey found no test for 16 guarantees and did not read the tests of two of those; each of the 16 ends with a test in the suite.
- The append is tested with two writers and with a process killed at each step: before the transaction, inside it, after it and before the acknowledgement. The inbox, the effects table and the lease statement are each tested with two writers that commit in the opposite order to the one they started in, and with an old owner that wakes after a takeover.
- Every guard has a test and a removal check.

**Performance** (deterministic counts, no timing). "Today" was counted on 11 November with the command-cost project on the branch of #4166.

| Reference run | Today | Target | How the target follows |
|---|---:|---:|---|
| Ten WriteLines, Balanced: SQLite commands | 64 | 32 or fewer | Start 2. Inbox read 1. Append 3. Dispatch enricher's query 1. Executable lookups 2. Projector at Full history: 1 journal read, 11 activity rows, 1 run-health row, 1 position write. About 23 |
| Ten WriteLines, Balanced: PostgreSQL round trips | 48 | 16 or fewer | Start, append and projector as one transaction each at about 3 round trips; inbox read, dispatch query, projector read, 2 executable lookups. About 14 |
| The same run at 20 and 40 WriteLines: SQLite commands | grows by 2 per activity | grows by 1 per activity at Full history, by 0 at State only | The append's statements do not depend on length; the projector writes one row per activity at Full history |
| Ten WriteLines, Durable: PostgreSQL round trips | 230 | 100 or fewer | Twelve appends and up to twelve projector passes at about 3 and 4 round trips, the start, and the reads above. About 91 if the projector runs after every commit |
| Fork, wait, resume, join, Balanced: PostgreSQL round trips | 142 | 55 or fewer | Five appends at 3, up to five projector passes at 4, the start 3, the resume command 2, one load 2, one lease statement 1, stimulus lookup 2, two inbox reads, the dispatch query, 2 executable lookups. About 50 |

PostgreSQL round trips are already the same at 10, 20 and 40 WriteLines today (48); the growth is in SQLite commands and in what the commit writes.

**Cleanliness**

| Measure | Today | Target |
|---|---:|---:|
| What a storage provider implements for run state | the store contracts of the fourteen tables, with their facets | one contract of 15 operations or fewer |
| Capability flags a provider answers for run state | 12: 4 on the scheduler queue, 4 on the commit store, 1 each on inspection, timers, health and recovery | 0 on the storage contract; the commit store's 4 remain as constants until the later port slice |
| Tables that hold run state as truth | 14 | 5 |
| Lines of the mechanisms listed under "goes" | about 21,000, plus the EF run-state stores | 16,000 or fewer for what replaces them |
| Migration lines | 91,407 | one initial migration per engine |
| Code outside the core that writes run state without a commit | 6 queue enqueues in BPMN | 0 |

Architecture guard tests hold the storage contract's surface. The pull request that deletes the old core reports the after values.

## Answers to the earlier documents

**ADR 0084: "not worth building now".** ADR 0084 projected that a journal for the three per-activity tables would save 3 of 80 PostgreSQL round trips. It kept every other table and added a journal beside them. This decision removes fourteen tables as truth, and with them the reads inside the commit, the commit marker, and the reconciliation between queue, overlay and outbox. That is where the saving and the smaller surface come from.

**Comparison N2** recommends against making an append-only log the source of truth, because every query then needs a projection pipeline and a second store. Here the projections stay in the same database, filled by one projector; there is no second store. What changes for a user is that run details can lag the run, which the owner accepted on 11 November.

**Comparison N1: no partitioning.** Not superseded. The first version keeps today's placement.

**Runtime Efficiency: "No journal rewrite".** That boundary was set for the continuation of 1 November. The owner's direction of 11 November replaces it; the program goal is amended in the same pull request.

**What the comparison found the current core does better.** Of its seven findings, three concern storage: the default commit boundaries, fencing kept apart from placement, and one store. All three are kept: the modes are unchanged, the fence is the head row and independent of placement, and everything is in one database.

## What two review rounds showed

Two independent rounds each found this design unsound, and each fix brought back something the current core already has: effect rows with status and claims, a lease epoch that fences appends, dispatch records as a shared table, a record of terminal activities. Those parts of the current core are essential and are rebuilt here, not avoided. What this decision removes is narrower than first written: per-entity state rows as truth, the second copies of queue and outbox with their reconciliation, the reads inside a commit, and the per-run root write lease.

## Risks

- **Two cores at once.** Until the switch the code base is larger. The plan ends in a switch and a deletion slice.
- **Rebuilding what works.** The lease, the effects table and the dispatcher are new implementations of things the current core does correctly today. Each needs the tests the current ones have.
- **Tests tied to the old internals.** About 3,177 of 5,379 test methods construct internals of the current core and are deleted with it. Because the ports and the store interfaces stay, tests that go through them can run on both cores.
- **A slow projector.** Studio shows less than is committed while the projector is behind. Its lag is observable, and a test hook waits for it.
- **Conditions outside the run** are writes an append makes to rows it does not own and need their own two-writer tests.

## What the owner approves

1. The decision and the shape above, including that the ports keep their contracts.
2. The targets in "How it is proven".
3. That ownership per partition comes after the old core is deleted, as its own slice and decision, although the goal lists it in the target shape.

The switch of the default and the deletion of the old core each come back for a go-ahead when their conditions are met.
