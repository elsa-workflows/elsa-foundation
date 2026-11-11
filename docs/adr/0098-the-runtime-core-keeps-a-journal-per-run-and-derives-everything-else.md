---
status: proposed
date: 2026-11-11
---

# The runtime core keeps a journal per run and derives everything else

Status: proposed (2026-11-11). Written at the owner's direction of 11 November 2026 ("redo the core to achieve superiority in all axes: code cleanliness, robustness and performance"). It becomes accepted when the owner approves this PR; nothing is built before that.

Program: [Journal-first runtime core](../program-goals/journal-core.md). Owner decisions already taken, recorded on [#2892](https://github.com/elsa-workflows/elsa-foundation/issues/2892#issuecomment-6807409653) and [#2564](https://github.com/elsa-workflows/elsa-foundation/issues/2564#issuecomment-6807592241): the new core is built beside the current one, the break is clean (new tables, no migration of runs in flight, one storage contract in place of the per-entity stores), and work on the current core stopped after #4166.

Supersedes, once accepted: the verdict of [ADR 0084](0084-runtime-commit-journal.md) ("not worth building now"), and for the runtime core the recommendations N1 and N2 of the [August execution-model comparison](../reports/execution-model-comparison-2026-08.md). It changes the ports of the [9 November clarification](../program-goals/unified-execution.md) as set out under [Ports](#ports). [ADR 0080 D2](0080-elsa-4-simplification-decisions.md#d2--persistence-modes-balanced-by-default) (persistence modes) and [ADR 0032](0032-runtime-checkpoint-cadence-is-policy-driven-per-workflow.md) (which boundaries are always saved) stay as they are.

## Decision

1. **A run's durable truth is its journal.** One table holds one row per commit of a run, keyed by run and sequence number. A commit appends the next row. Every other stored fact about a run is derived from the journal or is a small index written with the append.
2. **The journal's key is the fence.** Two writers of one run cannot both append sequence N+1; the database refuses the second. A node that lost a run fails at its append. No ownership row is read or written to make a commit safe.
3. **Run state is a working set.** The run's actor loads the latest snapshot and the rows after it once, keeps the state in memory, and appends what changed. Work handlers and activities read and write the working set.
4. **Work reaches a run through one inbox.** Anyone may add a command for a run (start, resume, cancel, alter, a timer that fell due, a result from another run). Adding is create-only by command identity. The run consumes commands in order and records in its append how far it has read.
5. **Effects leave a run through its journal.** What a commit must cause elsewhere (work for another run, a timer, an external handler) is part of the appended row. A dispatcher delivers those effects at least once, keyed by run and sequence.
6. **Tables that Studio and the API read are projections.** A projector fills them from the journal after the commit. What is projected follows the workflow's history level.
7. **One storage contract.** A provider implements a handful of operations (append, load, add command, read inbox, read journal, lease, trim). There is one implementation on EF Core for SQLite, PostgreSQL, SQL Server and MySQL, and one in memory.

## Shape

### What is stored

| Table | Row | Written by | Read by |
|---|---|---|---|
| Run journal | One per commit: run, sequence, commit id, what changed (state delta, waits added and removed, effects, inbox position), and at intervals a snapshot | The run's append | The run's actor on load; the dispatcher and the projector by cursor |
| Run head | One per run: status, last sequence, snapshot position, next due time, pinned executable, tenant, correlation, partition | The append, in the same transaction, conditional on the last sequence | Instance lists and counts, the sweep that finds runs with work, executable retention |
| Run inbox | One per command: run, command id, kind, payload, when it may run | Whoever adds work; create-only | The run's actor; trimmed behind the consumed position |
| Wait index | One per live wait: stimulus hash or due time, run, wait id | The append, in the same transaction | Stimulus routing and the timer pump |
| Cursors and leases | Dispatcher and projector positions; who drives which partition | Their owners | Their owners |
| Projections | Activity executions, inspection, hierarchy, incidents, run health | The projector | The HTTP API, Studio, the Dashboard |

Definitions, executables, trigger bindings, activation slots, test scopes and user tasks are not run state and keep their tables.

### A commit

One transaction: insert the journal row; update the head `where last sequence = expected`; insert and delete wait-index rows. Nothing is read. The number of statements does not depend on how many activities ran. On PostgreSQL, SQL Server and MySQL the statements go out as one batch.

A commit id is unique in the journal, so a commit that is sent twice is refused the second time and answered from the stored row. That replaces the commit marker table.

### Starting and resuming

A start inserts the head and the first inbox command in one transaction and hands the run to its actor. If the host dies, the sweep finds the head with unread inbox. A resume adds an inbox command; the actor, wherever it is placed, loads the run and reads its inbox.

### Who drives a run

Runs hash to partitions. A host leases partitions and drives their runs, their dispatcher cursor and their projector cursor. The lease decides who should work, never whether a write is safe: decision 2 does that. The first version keeps the placement the runtime has today. More partitions, and commits shared across runs, come later and need no change to the journal.

### Persistence modes

Unchanged in meaning (ADR 0080 D2). Durable appends after every activity. Balanced appends at a wait, a fault, completion, the end of a burst and every N activities. Ephemeral appends only at a wait or a fault, plus a completion record unless that is turned off. Suspend, bookmark and fault boundaries are appended in every mode (ADR 0032). An activity that is not safe to repeat has its attempt appended before it runs.

### History

The owner's decisions of 11 November stand: the journal row is the hand-off, the run viewer's rows are written after the commit by default with a switch back, and the history level per workflow is Full, State only or Minimal with default Full. With the journal as truth, "in the commit" means the projector runs inside the append's transaction for that workflow.

## Ports

The 9 November clarification named the shared ports between the host and execution models. They are not locked (one protocol driver exists), and three of them carry storage types today, read from the code:

- `IRuntimeCheckpointCommitStore` has capability probes that take store instances, and `RuntimeCheckpointCommit` carries a change set with one collection per stored entity.
- `IRuntimeCheckpointCommitEnricher` implementations rewrite those collections.
- `IRuntimeExecutionOwnershipService` exposes leases and fencing tokens to callers.

Proposed, for the owner's approval with this ADR:

| Port | Proposal |
|---|---|
| `IRuntimeExecutionProtocolDriver`, `IRuntimeExecutionProtocolSource` | Unchanged. |
| `IBookmarkResumeDispatcher` | Unchanged for callers. It adds an inbox command. |
| `IRuntimePostCommitIntentHandler` and the durable-source variant | Unchanged for implementers. Intents become effects in the journal row; the dispatcher calls the same handlers. |
| `IRuntimeCheckpointCommitEnricher` | Kept, with the commit it receives expressed as changes to the working set, not as per-table collections. Each of the 13 registered enrichers is ported. |
| `IRuntimeCheckpointCommitStore` with `RuntimeCheckpointCommit` | Replaced as the port by "commit this run's working set" on the run's context. The capability probes disappear: every core can do what the probes ask about. BPMN's direct driver commits model state through the same call. |
| `IRuntimeExecutionOwnershipService`, `RuntimeExecutionFence` | Removed from the port list. The fence is the journal key; leasing is internal to the core. |

The seams that bypass the ports today are closed in the same work: BPMN's five direct enqueues onto the scheduler queue become inbox commands; the seven activities that inject stores (`Parallel`, `GraphActivity`, `DispatchWorkflow`, `RunEtl`, `RunRules`, `EvaluateDecision`, `HumanReview`) read the working set or a projection; the Dashboard reads projections through a query contract.

## What stays and what goes

Figures are from a survey of `main` on 11 November (regular-expression counts, not compiler output).

| Stays | Goes when the old core is deleted |
|---|---|
| Activity authoring contracts and `ActivityExecutionContext` | The coalescing session and its decorators (5,116 lines) |
| What the work handlers decide (14,269 lines), reached through the working set | The checkpoint commit store, its staging helpers and the commit marker (6,617) |
| Alterations and incidents as behaviour (7,335 and 3,471) | The scheduler queue, its overlay, claims and anchors (2,419) |
| Executables, their cache and pinning | The post-commit outbox store and processor (4,285) |
| Persistence modes, ADR 0032 boundaries | Ownership rows, fencing tokens, fence cursors (2,823) |
| The HTTP API and Studio behaviour | The drainer and its reconciliation (4,419); the recovery scanner and resumption sweep in their present form (2,422) |
| | The per-entity stores (29 contracts, 31 facets, 14 capability flags) and their EF implementations |
| | The current migrations (91,407 lines in 68 files) |

## How it is proven

Each figure has a before value and a target. The targets are part of what the owner approves.

**Robustness**

- The 120 guarantees found in the documents and tests become a conformance suite at the level of what a host can observe. It passes on the current core first, then on the new one. The 16 guarantees that have no test today get one.
- The append is tested with two writers and with a process killed at each step: before the transaction, inside it, after it and before the acknowledgement.
- Every guard has a test and a removal check.

**Performance** (deterministic counts, no timing; "today" is the branch of #4166, counted on 11 November with the command-cost project)

| Reference run | Today | Target |
|---|---:|---:|
| Ten WriteLines, Balanced: SQLite commands | 64 | 32 or fewer |
| Ten WriteLines, Balanced: PostgreSQL round trips | 48 | 16 or fewer |
| The same for 20 and 40 WriteLines | grows by 2 commands per activity | equal to ten |
| Ten WriteLines, Durable: PostgreSQL round trips | 230 | 60 or fewer |
| Fork, wait, resume, join, Balanced: PostgreSQL round trips | 142 | 30 or fewer |

**Cleanliness**

| Measure | Today | Target |
|---|---:|---:|
| Operations a storage provider implements for run state | 29 store contracts and 31 facets | 10 or fewer, in one contract |
| Capability flags on those contracts | 14 | 0 |
| Tables that hold run state as truth | about 20 | 4 (journal, head, inbox, wait index) |
| Lines of the mechanisms listed under "goes" | about 28,100, plus the per-entity EF stores | 12,000 or fewer for what replaces them |
| Migration lines | 91,407 | one initial migration per engine |
| Places outside the core that write run state directly | queue enqueues, direct database reads by the Dashboard | 0 |

Architecture guard tests hold the contract surface. The PR that deletes the old core reports the after values.

## Answers to the earlier documents

**ADR 0084: "not worth building now".** Its figure still holds: writing the per-activity rows through a journal saves 3 to 4 of the 48 PostgreSQL round trips of a Balanced run. ADR 0084 kept every other table and added a journal beside them. This decision removes the other tables as truth, and with them the reads inside the commit, the ownership row, the commit marker, and the reconciliation between queue, overlay and outbox. The saving ADR 0084 could not reach comes from there.

**Comparison N2: "do not make an append-only log the source of truth, because every query then needs a projection pipeline and a second store".** The projection stays in the same database, in tables with the same shape as today's, filled by one projector. There is no second store. What changes for a user is that the run viewer can lag the run, which the owner accepted on 11 November.

**Comparison N1: no partitioning.** Safety does not depend on partitions here, and the first version keeps today's placement. Partitioning beyond that is a later decision.

**Runtime Efficiency: "No journal rewrite".** That boundary was set for the continuation of 1 November. The owner's direction of 11 November replaces it; the program goal is amended in this PR.

**What the current core does better and must not be lost.** The comparison found Elsa stronger on its default commit boundaries, on keeping fencing apart from placement, and on having one store. All three are kept: the modes are unchanged, the fence is the journal key and is independent of placement, and everything is in one database.

## Risks

- **Two cores at once.** Until the switch, the code base is larger. The plan has a dated switch decision and a deletion slice; the old core gets no new features meanwhile.
- **Tests tied to the old internals.** About 3,177 of 5,379 test methods construct internals of the current core. They are deleted with it. The conformance suite and the new core's own tests replace them; the count of guarantees covered must not fall.
- **A slow projector.** Studio shows less than is committed while the projector is behind. The projector's lag is observable, and a test hook waits for it.
- **A long journal.** A run with many commits is bounded by snapshots: load reads the latest snapshot and the rows after it. Rows behind a snapshot can be trimmed once projected and delivered.
- **Large rows.** A commit that changes much state writes a large row. The row holds a delta, not the whole state, and large values stay behind references as today.
- **Hot index.** The journal's key is written by every commit. It is an append in key order per run; contention is per run, where there is one writer.

## Open decisions for the owner

1. Approve the decision and the port changes above.
2. Approve the targets in "How it is proven".
3. The switch of the default and the deletion of the old core each come back for a go-ahead when their conditions are met.
