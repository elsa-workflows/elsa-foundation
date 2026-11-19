# Ownership and fencing

> **Approved design, with conditions.** Gate 2 granted design approval on 18 November 2026 at
> `addffea7c850063d1ebe710b07866ff1640a93cb` ([verdict](https://github.com/elsa-workflows/elsa-foundation/issues/4229#issuecomment-6990560103)). The implementation evidence is owed at K1 to K12,
> and the [gate 2 conditions](../program-goals/clean-slate-kernel.md#gate-2-conditions) stay open until it lands.
>
> **Amendment, 19 November 2026: the node lease, approved with conditions.** The owner replaced the per-partition
> lease renewal with a node-level heartbeat ([register](decisions.md#the-node-lease-amendment-19-november)). The
> independent architecture review approved it with conditions on its delta pass at `170ac1f7c`
> ([verdict](https://github.com/elsa-workflows/elsa-foundation/issues/4229#issuecomment-7006654424); [first pass](https://github.com/elsa-workflows/elsa-foundation/issues/4229#issuecomment-7005856502)); its condition is NL-C1 in the plan's
> [gate 2 conditions](../program-goals/clean-slate-kernel.md#gate-2-conditions). Milestone K1b builds it. Fences stay
> per partition: what moves to the node is the lease, which only decides who may take.

A runtime places each partition on a node. A lease, held by the node, helps it decide who should work there. The database fence decides whose changes can become durable. One partition holds many instances, so one fence can protect a batch of their turns.

The runtime may briefly activate the same partition twice. Correctness must survive that overlap, including an old owner that resumes after its replacement has started.

## Why a lease is not enough

An owner can pause after checking its lease and resume after another node takes the partition. A network split can leave it unable to hear that it has been replaced. A slow packet can reach the database after the sender loses ownership. Clock skew can make two nodes disagree about whether a deadline has passed.

A timer in the owner cannot recall those packets or stop an effect that already started. The local lease-deadline guard reduces new work after the owner's conservative deadline, but it is not the authority for a database commit. External effects remain at least once; they need idempotency or reconciliation where repetition matters.

Lease expiry makes a partition eligible for a take. It does not by itself make an already-sent commit invalid. The change of epoch at a successful take fences that commit out.

## The partition record

| Field | Purpose |
|---|---|
| `id` | The ownership scope: one partition. |
| `owner` | The process that took it, named uniquely per process start, so a restarted process is a new owner (finding F9); used for placement, release and recovery. |
| `epoch` | A generation incremented by every successful take, including a retake by the same process. |
| `position` | The last allocated journal position in this partition. |

The partition record carries no lease. Each process has one node record, and its lease covers every partition the
process owns:

| Field | Purpose |
|---|---|
| `id` | The process name, the value partitions store as `owner`. |
| `seen_at` | Database time of the last renewal; membership counts the nodes renewed inside its window. |
| `lease_until` | Database time until which no other process may take this process's partitions. |

A take conditionally updates the partition row. It accepts an unowned partition, a partition whose owner's node record
is missing or has `lease_until` before database time, read in the take's transaction, or a retake by the recorded
owner. It increments the epoch and returns the resulting position. The successful update and the reading of that
position must be one atomic operation or transaction. The same transaction renews the taker's own node record, so a
process never holds a partition it took under an expired lease of its own.

The take reads the owners' node records without locking them. A renewal that commits after the take read an expired
lease is too late for the partitions that take changed, and it keeps every partition the take left alone. Such a take
also counts a death on the partitions it moved, by its read; a race between a renewal and a take can therefore cost
one death count, and the fence, not the count, protects correctness. Safety does
not rest on that read: the epoch the take increments fences the previous owner out, as before.

**A take that changes zero rows returns nothing.** It has acquired no authority and must not return a separately read position as though it had succeeded. A take that succeeds learns the true position, including any commit that finished before it.

## The commit fence

An owner preparing a batch remembers epoch `e` and position `n`. If the batch writes `k` journal rows, its transaction conditionally performs the equivalent of:

```sql
UPDATE partition
SET position = n + k
WHERE id = p AND epoch = e AND position = n;
```

Exactly one row must match. The same transaction writes the instance snapshots, checks their expected revisions, consumes messages, inserts outgoing messages, changes subscriptions and writes journal rows at positions `n + 1` through `n + k`. If the fence does not match, the entire transaction rolls back.

This is a compare-and-swap on the partition record. The instance revision guards and consumed-row counts remain part of the contract; a partition fence does not replace them. See the [storage model](storage-model.md) for the row invariants and query paths.

A batch that writes no journal rows has `k = 0`. This includes a batch of seen marks only and can include turns at Minimal history. Its fence still checks and locks every touched row at the epoch and expected position, but does not advance the journal: the owned SQL sends the unchanged update, which matches one row on every engine, and the EF reference path marks `Position` modified even when it is unchanged, so the fence is always sent. The fence renews no lease: the node's heartbeat does. On MongoDB, where a transaction that only reads a document does not conflict with a later write to it, the fence still writes each touched partition document (it increments a fence count), so a concurrent take conflicts with it.

## Why epoch and position

The epoch identifies an ownership generation. An old owner cannot commit after a successful take has incremented it, even when the journal position has not moved.

The position's main job is to allocate gapless, ordered journal positions within each partition. Deleting an instance can later remove its earlier journal rows while preserving that high-water mark, so surviving rows can have gaps. It also checks the owner's belief about that journal's end. The two fields answer different questions: *which generation prepared this?* and *at which journal position?*

A lost answer illustrates the distinction:

1. An owner sends a batch at `(epoch 7, position 20)` that would write one journal row.
2. The connection breaks before the owner learns whether it committed. The database may be at position 20 or 21.
3. The owner discards its memory and resets. A successful new take increments the epoch and returns the actual position.
4. The old batch cannot commit under epoch 7 after that take. Recovery loads committed state under the new generation.

**The reset and its epoch-changing take prevent a second commit after an unknown outcome.** The owner must not guess the result and continue. If that reset were missing, the position predicate would reject a retry at position 20 when the first batch had advanced it to 21. That is a useful additional check, not a substitute for reset: a batch with no journal rows may leave the position unchanged.

## A take racing a stale commit

The partition record orders the two transactions. On relational engines, a successful conditional update holds a write lock until its transaction ends; the table below describes that locking case.

| Order at the partition row | Result |
|---|---|
| The old commit obtains the row first. | Its writes either commit together or roll back. A take waits. If it subsequently succeeds, it receives the position left by that transaction and increments the epoch. A still-valid lease of the owner's node record can instead make that take decline. |
| The take obtains the row first. | It increments the epoch and commits. The old commit waits, then finds its expected epoch no longer matches. Its entire transaction rolls back. |

MongoDB can instead reject a competing transaction immediately with a transient write conflict. Its caller retries; a successful take still changes the epoch before a stale commit can land. It need not wait for the winner as a relational lock contender does.

The fence need not be the first SQL statement for this safety argument: writes made earlier in the same transaction must roll back when the fence fails. Statement order does matter for contention and deadlocks.

In a traced PostgreSQL batch, EF inserted the instance, journal and message rows, updated the partition fence, then inserted a subscription. EF checks the affected-row count before sending COMMIT. This path took two wire round trips. The separate checked fence runs first and took three.

Two commit paths exist, and SQL Server's default differs between them ([COMMIT-SQL.md](../../prototypes/kernel-model/COMMIT-SQL.md#defaults); `SqlServerEngine.FenceInBatchByDefault` in `Engines.cs`, `EfStore.FenceInBatch`):

| Path | SQL Server default | Lock order |
|---|---|---|
| Owned commit SQL (the default writer on every engine) | Fence in the batch | The fence is the first statement of the one command, so the partition row is the commit's first lock, as it is the take's. A stale commit waits on the partition row before it holds any message row |
| EF reference path (`commit=ef`) | Separate checked fence | EF orders the batch's statements by table and writes message rows (seen and trial marks, sent messages) before the partition row. Checking the fence first, as its own round trip, restores the take's order |

E9 is the evidence for the distinction. A take locks the partition row, then message rows; EF's in-batch commit locked message rows, then the partition row, so the two could wait for each other. In the same 40-second SQL Server stress, EF with the fence in the batch had 74 deadlocks, every victim a take; the owned commit with the fence in the batch had none, and so did the checked fence ([COMMIT-SQL.md, E9](../../prototypes/kernel-model/COMMIT-SQL.md#e9-on-sql-server-the-owned-in-batch-fence)). Measured, on one machine. For the k-row fence of the Worker refactor, ascending lock order is enforced today on PostgreSQL only, whose statement locks the rows with `ORDER BY "Id" FOR UPDATE`. SQL Server's statement (`WITH (UPDLOCK, ROWLOCK)`) and MySQL's (`FOR UPDATE`) carry no ordering clause, so their lock order is left to the query plan and is unenforced; SQLite has one writer. The kernel fences one row today, so no run covers this. K1 and K4 must enforce ascending order on every engine and show two overlapping range commits completing with no deadlock.

## The node's heartbeat

A process renews its lease with one heartbeat, at least three times per lease and at most half the membership window
apart, since the same write carries membership's `seen_at` (today every second: a 10-second lease and a 3-second
window), whether its partitions are busy or idle. Every host of a Worker owns its node's heartbeat: the built-in runtime's node loop, the fixed host and the
simulation's node. It runs on its own cadence, never behind a balancing round's moves,
and hands the Worker its answer with the monotonic time just before it was sent. The heartbeat is one statement or
transaction: it sets its node record's `seen_at` to database time and `lease_until` to database
time plus the lease, and reads back the partitions whose `owner` is this process, with their epochs. That read writes
nothing. The Worker stands down every partition it held at the epoch it held when it sent the heartbeat that is
missing from the answer or at another epoch, as a refused fence makes it stand down; a partition it took after sending
that heartbeat is judged by the next one. The answer is also reconciled the other way: a partition it lists as this
process's, at an epoch the Worker does not hold, and with no take of it in flight between the heartbeat's send and its
answer, is released at that epoch (or taken back) before the next heartbeat, so a release or retake whose
answer was lost cannot leave a row owned by a live process that does not work it; a release that fails is retried at
the next heartbeat. The release side is skipped while a retake is in progress, since a retake runs one row at a time
and covers its whole remaining list. A host with several Workers (a fixed host with one per partition) divides the
answer: each Worker judges only the partitions it holds, and the release side applies only to listed partitions that
no Worker of the host holds or is taking. An answer is judged only within the Worker generation it was sent in: a Worker that reset
between a heartbeat's send and its answer discards that answer on both sides, so its own one-by-one retakes after a
reset are never released or stood down by an answer from before them. When the Worker stands a partition down, it drops that partition's memory and work, and a later take may claim it
again at a new epoch.

**Lock order.** A take first reads the node records of the owners it may take from, with a plain read that holds
nothing afterwards (never a locking read or a subquery inside the update, which MySQL makes locking); then it writes
its own node record; then it locks the partition rows in ascending id. A heartbeat writes its own node record, then
reads partition rows. A commit locks only partition rows, in ascending id. The take's transaction runs at read committed on every engine, whatever
the store's configured isolation, so the read's shared locks, where the engine takes them, end with the statement (MySQL
already sets this as the take's own level). With this order no cycle forms, including
under SQL Server without RCSI, where a plain read takes shared locks: a take reads while it holds no lock; a take or
heartbeat that holds its own node record then waits only for partition rows, and whoever holds those never waits for
another process's node record.

The heartbeat takes over both jobs of the former per-partition renewal, an empty fenced commit per idle partition: it
renews the lease, now once for the whole set, and it tells the owner that a partition was taken from it. A commit no
longer renews anything. An idle owner learns of a lost partition at its next heartbeat; a busy one also learns at its
next refused commit. A heartbeat late by more than the lease leaves the process past its local guard: it stands every
partition down, and its next successful heartbeat and takes start again. On MongoDB a single-document heartbeat needs
no transaction; a take's write to the taker's node document can conflict with that process's concurrent heartbeat,
and the transient conflict is retried.

**Liveness follows the Worker's passes.** A host renews its node record only while each of its Workers that holds a partition has
completed a pass within the last lease; a Worker that has never held a partition, or gave its partitions up by release, is live
and does not hold the heartbeat back, so a fresh node heartbeats and takes. A Worker that stood down because its guard
expired is not live: its host withholds the heartbeat and takes nothing until its node lease has expired (the last
answer's database time plus the lease), and releases none of the partitions it stood down, so another process's take
counts their deaths. A Worker whose pass does not complete, because a commit, poll or load hangs (a dead
connection), stops the renewal: the lease expires, another process takes the partitions, and the takeover counts the
death the trial protocol needs, as the per-partition beat did, since that beat ran inside the same pass. A host that
withholds its heartbeat also takes nothing: the gate closes every take, the Worker's gains, its retakes after a reset and
the runtime's activations, so takes cannot renew a wedged host's lease. Every host wakes each Worker for a pass at least once per poll interval (today
every second), and passes run on their own loop, apart from the balancing round, so a long round of moves never holds
passes back beyond the lease. A heartbeat statement carries a timeout of a third of the lease. PostgreSQL's statement timeout
and MongoDB's `maxTimeMS` enforce it on the server; on SQL Server (attention) and MySQL (`KILL QUERY`) the client's
timer sends the cancel, so there the bound assumes the client's timer runs, and K4 qualifies it. Under that assumption
a heartbeat sent just before the gate closes lands within that time or not at all: the lease expires within two and a third leases
of the last completed pass, and the takeover follows within one lock bound (below) and one balancing round after that.

A commit that stalls on a live connection holds its partition row locks until its transaction ends, and a take waits on
them whatever the lease says (as it did under the per-partition lease). Two bounds keep that wait finite. The store rolls back instead of
sending COMMIT when the monotonic time since its batch was sent has passed the lease, the guard's deadline checked at
the commit step, on every engine; this covers a client that pauses between the batch's answer and COMMIT. The server
bounds a holder whose client is gone or silent: PostgreSQL's `idle_in_transaction_session_timeout` and statement
timeout, MongoDB's `transactionLifetimeLimitSeconds`, and on SQL Server and MySQL the end of the dead client's session
by connection keepalive. A take waits at most its own lock timeout (`LOCK_TIMEOUT`, `innodb_lock_wait_timeout`,
PostgreSQL's `lock_timeout`), no longer than the lease; past it the take fails as a transient error, its transaction is
rolled back, and it is retried at the next balancing round. A client that is stuck and never resumes, while its
connection stays open, is ended by its host's watchdog: when an owner-path store call (commit, take, poll, load or
heartbeat) has outlasted twice the lease, the watchdog first closes that call's connection from another thread, which
closes the socket and makes the server roll the transaction back; it ends the process only when the connection cannot
be closed, and in the Embedded profile, where the host process is the application, that last step is the
application's policy. A batch's command timeout is at most the lease. This is the bound on SQL Server,
where `LOCK_TIMEOUT` cancels only the waiting take and keepalive detects only a dead peer. K4 qualifies these bounds on
each provider, the server-side heartbeat timeout and a stuck holder on a live connection included. With one Worker per node (D1) that Worker is the node's; a fixed host with a Worker per
partition stops renewing when any of its Workers is wedged, and so gives up all its partitions.

A turn whose body loops runs off the pass, so passes keep completing; that was so under the per-partition beat too, and
it is not a lease matter. The operator's forced take ([observability](observability.md)) or a model-level turn timeout
moves such work.

## What the fence transaction also does

The fence transaction also marks newly polled messages `seen` at the current epoch before they are delivered. Polling itself is a read. A poll result is not evidence that the owner still owns the partition, and reading a message does not authorize its execution. The fenced mark establishes that this generation saw it; trial markers then identify the work whose repeated owner losses count toward poison handling.

The message marks are writes protected by the fence, rather than fields in the partition row. After a takeover, the higher epoch lets the new owner discover messages seen by its predecessor.

## Why the check costs a round trip

The provider must learn that its conditional write succeeded before it authorizes a commit. Sending an unconditional `COMMIT` blindly after an update that matched zero rows would still commit the other writes.

Both commit paths offer two forms:

| Form | How success is checked | Cost shape |
|---|---|---|
| Fence in the batch | Owned SQL: the fence is the batch's first statement, and its affected-row count is read before `COMMIT`. EF: epoch and position are concurrency tokens, checked before `COMMIT`. | The fence shares the data-writing batch. A simple PostgreSQL checkpoint takes two round trips: batch and commit. The default for the owned commit on every engine, SQL Server included. |
| Separate checked fence | Send the conditional update, read its result, then send the data-writing batch and commit. | The check is its own round trip. The same PostgreSQL checkpoint takes three. The default for SQL Server's EF reference path; selectable everywhere. |

These are measured checkpoint shapes, not universal counts for every provider or operation. Transaction setup, polling, loading, sent messages, parameter limits and exceptional paths can add work. SQLite counts calls into its engine rather than network round trips. The provider must preserve the atomic fence contract even when its wire protocol groups commands differently.

An explicitly requested [commit window](storage-model.md#optional-commit-window) is an additional conditional
capability, separate from ownership and immutable commit-time facts. The opt-in PostgreSQL batch path checks its
interval with a deferred database constraint; other physical providers refuse it. It does not replace epoch/position
fencing or make external effects exactly once. Its schema and check cost must be qualified separately from the
ordinary checkpoint shapes above.

## Side effects

The fence decides which commit lands. It does not reach the systems an activity talks to. An HTTP request sent, an
email handed to a mail server or a message put on a broker before the turn's commit stays sent when that commit is
refused, and the replacement owner, the redelivery or the replayed burst sends it again. Side effects therefore run
**at least once**, in every persistence mode. Durable mode narrows the window by committing an External activity's
attempt before it runs; no mode closes it, and the kernel makes no exactly-once claim for anything outside its store.

What the kernel gives instead is a **stable effect key** per activity attempt, derived from committed facts only, in
the shape the owner decided on 16 November: `{instance origin}/{node}/{iteration path}/{attempt}` (`EffectKey.For` in
the prototype's `src/Kernel.Workflows`). The instance origin is the instance's id, fixed at creation, with its creation;
the key never reads partitions or positions. The node is the activity's stable id in the definition. The iteration path
is the iteration numbers of the repeating scopes that enclose the activation (a loop index, a loop pass, a flowchart
round, the merge and source a flowchart token passed, a flowchart join's firing ordinal in its round, a statechart
visit serial, a retry generation), root first, each taken from the enclosing activity's committed state. The attempt is
0, or 1 for an activity whose attempt Durable mode commits before it runs; it takes no other value. One logical
activation re-derives one key on whichever owner replays it; two activations of one activity never share one; a retry
is a new generation in the path, so a new key. What a replayed activation carries as content may differ from its first
sending; the key says they are one activation, and the receiver keeps the first.

The frame number of the activation is deliberately absent (the owner's reason): in Balanced and Ephemeral mode a
replayed burst mints its frame numbers from the committed state in agenda order, and a replay over a longer inbox (a
timer that became due meanwhile) orders the agenda differently, so a frame number can land on another activity, or on
another iteration of the same one. A key that named the frame would then name two different effects, and a receiver
that deduplicates by it would drop a genuine effect as a repeat. The iteration path does not move with the agenda, and
no extra commit is paid for it. The activity hands the key to the receiver where the receiver looks for a repeat: the
built-in HTTP activity sends it as the `Idempotency-Key` header, the email activity as the local part of the
`Message-ID`. **Deduplication is the receiving side's job**, and conditional: a receiver that keeps the keys it has seen
drops the repeat, one that does not sees the effect twice. The kernel records no key and drops no repeat. The
prototype's simulation (NATIVE.md, N7) checks that an outbound effect carries its key, that a resend after a lost turn
result or a takeover carries the same key, that one key belongs to one activation, and that a key never leaves in two
turns that both landed; a resend's content is not compared, since the receiver keeps the first. Its smoke replays one
committed state over a shorter and a longer inbox and asserts the same; six mutations (`UnstableEffectKey`,
`NodelessEffectKey`, `PathlessEffectKey`, `TurnLocalJoinOrdinal`, `FreshTurnSwapsLoopKeys` and, in the retry witness,
`FlatRetryGeneration`) show the checks bite.

## Alternatives considered

| Alternative | Why the kernel does not use it as the ownership fence |
|---|---|
| Lease only | It cannot stop a paused owner or a delayed write after a replacement takes over. |
| External lock service | A lock does not itself fence writes in the data store. A separate service also adds an operational dependency and a failure boundary between lock ownership and the commit. |
| Optimistic concurrency on every instance | Revision guards are useful and retained, but they do not give the partition one authority boundary for its inbox, journal and batched writes. Using them as the ownership mechanism spreads that coordination across the rows. |
| Database locks held on a connection | A lock held across workflow waits ties ownership to a long-lived connection. Connection loss releases it while old work or effects may continue. It also restricts hosting and store implementations. Short transaction locks are still used for the fence. |
| STONITH: kill or power off the old owner | Confirmed infrastructure fencing can be useful operationally, but the kernel must also work embedded and through pluggable runtimes without authority to power off their hosts. It cannot assume that an old process was killed. |
| A consensus log as the kernel's store | It could order ownership and work, but introduces another replicated log or a different storage architecture. The chosen store already provides the atomic transaction and conditional row update needed here. A log also would not make external effects exactly once. |

## Costs and limits

The partition row serializes commits within its partition. Batching amortizes that cost across instances; additional partitions provide parallel ownership scopes. A hot partition still has one writer, and long transactions or large flushes increase contention and takeover delay.

The checked fence costs an extra command/round trip compared with the batch form. Idle ownership costs one node-record update per heartbeat per process, whatever its number of partitions. A reset throws away caches and uncommitted work and requires recovery reads. Conservative leases and clocks can cost availability; a slow or failed owner can leave work waiting until another take succeeds.

The protocol depends on atomic rollback, reliable affected-row checks and read isolation supplied by each provider. The fence protects durable changes in that store. It does not cancel packets already sent to another system, make external effects exactly once, or establish ordering across different partitions.

## Acceptance cases (node lease)

Each runs first in the prototype, on the in-memory store and the simulation, then on the prototype's EF stores and
MongoDB (K1b), and on the providers (K4). Process P owns partitions {a, b, c}; Q is another process.

| Case | Scenario | Pass when | Milestone |
|---|---|---|---|
| NL-1 | **Idle cost.** Four idle processes at 1,024 partitions on PostgreSQL for 120 seconds, as K1's `x setidle` | One node-record update per heartbeat per process, and the WAL an hour reported against K1's 161 MB with the workload and units stated | K1b |
| NL-2 | **A paused owner.** P pauses past its lease; Q takes a, b and c; P resumes with a flush of a turn and seen marks over all three, then sends its heartbeat | The flush is refused whole; P's heartbeat answer lists none of the three, and P stands all three down; Q's journals have no hole and no stranded seen or trial mark; one death is counted per trial message of P's epoch. Mutation: the take ignores the owner's node lease and takes from a live process, so a live owner's partitions move (an invariant: no take from a live process except a release or a forced take) | K1b, K4 |
| NL-3 | **One partition moved from a live owner.** A test seam performs an operator's forced take of b to Q while P renews | P's next heartbeat answer omits b; P stands b down within one heartbeat and keeps a and c at their epochs. Mutation: the heartbeat ignores its ownership answer, so P keeps polling and executing b until a commit is refused (an invariant: a process stands down a partition taken from it within one heartbeat) | K1b, K4 |
| NL-4 | **A renewal racing a take.** P's lease expires; Q's set take reads P's node record as expired and is held before its commit while P's heartbeat commits; then Q's take commits, taking a and b and leaving c | a and b are Q's at new epochs and P stands them down; c stays P's at its epoch, and P's commits to c land | K1b, K4 |
| NL-5 | **A flush of seen marks only.** P's batch with no journal rows over a and b is on the wire when Q's take of b lands (after P's lease expired, or by a forced take); the batch then tries to land, on every store and on the owned and EF paths, MongoDB included | Refused whole; no seen or trial mark of P's epoch is written in a or b. Mutation: the fence of a batch with no journal rows is a plain read (on MongoDB, the partition document is not written), so P's stale marks land in b (M1) | K1b, K4 |
| NL-6 | **A restart under the same host.** P's process restarts as P′ (a new process name) while P's node lease is still live; P′ tries to take a | P′'s take of a is refused until P's node lease expires; then a, b and c move at new epochs, each counting a death for P's trial messages. Mutation: `StableNodeName` (a restarted process keeps its predecessor's name), so P′ retakes a at once as the recorded owner with no death and inherits b and c | K1b |
| NL-7 | **A take renews its taker.** Q, with a lease near expiry, takes a | Q's node record is renewed in the take's transaction. If the take's reply arrives before its own send time plus the lease, Q's guard deadline becomes the later of its current deadline and that send time plus the lease; it never moves earlier. A reply after its send time plus the lease restarts nothing: a newer heartbeat's later deadline stands, and with no live deadline Q stays latched expired and stands down as [LEASE.md](../../prototypes/kernel-model/LEASE.md) requires. Schedules: a take sent at 0 with its reply at 11 while a heartbeat set the deadline to 15 leaves 15; the same reply with no newer heartbeat leaves Q expired. A heartbeat answer read before the take does not stand a down | K1b, K4 |
| NL-8 | **A lost release or retake.** P releases b and the release's answer is lost (the store call fails after or before it lands), or P's retake of b after a reset fails; P stays live. A third run: P resets and retakes its set one partition at a time while a heartbeat sent before the reset, and one sent during the retakes, are answered. A fourth run: a fixed host with a Worker per partition holds a and b in two Workers, and one heartbeat answer lists both | Within one heartbeat b is free, or held and worked by P at its epoch; no row stays P's while P does not work it. In the third run no retaken partition is released or stood down by either answer, and every retaken row stays owned by P at its new epoch. In the fourth run both a and b stay owned and neither Worker releases the other's partition. Mutations `KeepUnheldRows` (the answer is reconciled one way only, and b stays P's while P lives) and `ReleaseDuringTake` (the release side ignores takes in flight and the generation, and frees P's retaken rows) | K1b, K4 |
| NL-9 | **Take, heartbeat and commit together.** One process's gain take, its heartbeat and its commit overlap with another process's take of overlapping partitions, on SQL Server with RCSI off and with RCSI, MySQL and PostgreSQL | No deadlock; every outcome is a landed or refused statement. A second run has two processes, both past their leases, whose set takes cover each other's partitions. Mutations: the take reads the owners' node records after it has locked partition rows (or as a subquery of its update), so under SQL Server without RCSI a take holding partition rows waits behind a heartbeat whose read waits on those rows; and the take's read runs at repeatable read, so two mutual set takes cycle on the node records | K1b (prototype engines), K4 |
| NL-10 | **A late heartbeat, and a wedged Worker.** P's heartbeat is delayed past its lease while P holds work. A second run: P's commit hangs (a store call that never answers) while its host keeps running and a third node R dies | First run: P's guard expires and P starts no new turn or job body; P stands every partition down; its next heartbeat and takes start again at new epochs. Second run: P's host sends no heartbeat later than one lease after the last completed pass and takes none of R's partitions; when P's guard expires and its Worker resets, P still sends no heartbeat and releases none of its partitions until its node lease has expired; a heartbeat sent just before that lands within its timeout (a third of the lease) or not at all, so the last renewal lands within one and a third leases of the pass; P's node lease expires within two and a third leases of P's last pass, and Q takes P's and R's partitions within one lock bound and one balancing round after that, P's stalled commit rolling back at its batch's command timeout, its commit-step deadline or the server bound, or, if P's client never resumes, when P's watchdog closes the stuck connection at twice the lease (the server bounds on the providers at K4), and counts a death for P's trial messages. Mutations: the host renews regardless of the Worker's passes, and P keeps its partitions while wedged; and a take renews a wedged host's lease, so P keeps them through its gains. Replaces the `--lease-check` witnesses whose renewal was a commit | K1b |

## Contract and evidence

The [design sketch](../reports/clean-slate-kernel/design-sketch-draft-5.md#the-commit) defines the commit and ownership protocol. The prototype's [DESIGN.md](../../prototypes/kernel-model/DESIGN.md), [lost-answer analysis](../../prototypes/kernel-model/FINDINGS.md#3-mutation-check), [engine notes](../../prototypes/kernel-model/ENGINES.md#e9-sql-server-fence-in-the-batch-merged-kernel-the-take-deadlocks-with-a-stale-commit-run-fixed-in-round-4-the-checked-fence-closed-by-round-5-b-the-owned-in-batch-fence-is-the-default-again) and [local lease guard](../../prototypes/kernel-model/LEASE.md) separate implementation evidence from this approved architecture. Until K1b builds the node lease, LEASE.md and
DESIGN.md describe the per-partition lease as built: the guard renews on a take or a landed commit, and the runtime's
renewal contract (`Seam.cs`, R1: a runtime that calls again when asked keeps an idle scope's lease) holds. K1b moves
the guard to the heartbeat and rewrites R1 for the node lease.
