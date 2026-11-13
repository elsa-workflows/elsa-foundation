# Ownership and fencing

> Proposed clean-slate kernel architecture. The design awaits owner approval; prototype evidence does not authorize product implementation.

A runtime places each partition on a node. A lease helps it decide who should work there. The database fence decides whose changes can become durable. One partition holds many instances, so one fence can protect a batch of their turns.

The runtime may briefly activate the same partition twice. Correctness must survive that overlap, including an old owner that resumes after its replacement has started.

## Why a lease is not enough

An owner can pause after checking its lease and resume after another node takes the partition. A network split can leave it unable to hear that it has been replaced. A slow packet can reach the database after the sender loses ownership. Clock skew can make two nodes disagree about whether a deadline has passed.

A timer in the owner cannot recall those packets or stop an effect that already started. The local lease-deadline guard reduces new work after the owner's conservative deadline, but it is not the authority for a database commit. External effects remain at least once; they need idempotency or reconciliation where repetition matters.

Lease expiry makes a partition eligible for a take. It does not by itself make an already-sent commit invalid. The change of epoch at a successful take fences that commit out.

## The partition record

| Field | Purpose |
|---|---|
| `id` | The ownership scope: one partition. |
| `owner` | The node that took it; used for placement, release and recovery. |
| `epoch` | A generation incremented by every successful take, including a retake by the same node. |
| `lease_until` | The deadline used to decide whether another node may take the partition. |
| `position` | The last allocated journal position in this partition. |

A take conditionally updates this row. It accepts an unowned partition, an expired lease according to database time, or a retake by the recorded owner. It increments the epoch and returns the resulting lease and position. The successful update and the reading of that position must be one atomic operation or transaction.

**A take that changes zero rows returns nothing.** It has acquired no authority and must not return a separately read position as though it had succeeded. A take that succeeds learns the true position, including any commit that finished before it.

## The commit fence

An owner preparing a batch remembers epoch `e` and position `n`. If the batch writes `k` journal rows, its transaction conditionally performs the equivalent of:

```sql
UPDATE partition
SET position = n + k, lease_until = renewed_deadline
WHERE id = p AND epoch = e AND position = n;
```

Exactly one row must match. The same transaction writes the instance snapshots, checks their expected revisions, consumes messages, inserts outgoing messages, changes subscriptions and writes journal rows at positions `n + 1` through `n + k`. If the fence does not match, the entire transaction rolls back.

This is a compare-and-swap on the partition record. The instance revision guards and consumed-row counts remain part of the contract; a partition fence does not replace them. See the [storage model](storage-model.md) for the row invariants and query paths.

A batch that writes no journal rows has `k = 0`. This includes an empty heartbeat and can include turns at Minimal history. Its fence still checks the epoch and expected position, but does not advance the journal.

## Why epoch and position

The epoch identifies an ownership generation. An old owner cannot commit after a successful take has incremented it, even when the journal position has not moved.

The position's main job is a gapless, ordered journal within each partition. It also checks the owner's belief about that journal's end. The two fields answer different questions: *which generation prepared this?* and *at which journal position?*

A lost answer illustrates the distinction:

1. An owner sends a batch at `(epoch 7, position 20)` that would write one journal row.
2. The connection breaks before the owner learns whether it committed. The database may be at position 20 or 21.
3. The owner discards its memory and resets. A successful new take increments the epoch and returns the actual position.
4. The old batch cannot commit under epoch 7 after that take. Recovery loads committed state under the new generation.

**The reset and its epoch-changing take prevent a second commit after an unknown outcome.** The owner must not guess the result and continue. If that reset were missing, the position predicate would reject a retry at position 20 when the first batch had advanced it to 21. That is a useful additional check, not a substitute for reset: a batch with no journal rows may leave the position unchanged.

## A take racing a stale commit

The partition row orders the two transactions. A successful conditional update holds a write lock until its transaction ends.

| Order at the partition row | Result |
|---|---|
| The old commit obtains the row first. | Its writes either commit together or roll back. A take waits. If it subsequently succeeds, it receives the position left by that transaction and increments the epoch. A renewed, still-valid lease can instead make that take decline. |
| The take obtains the row first. | It increments the epoch and commits. The old commit waits, then finds its expected epoch no longer matches. Its entire transaction rolls back. |

The fence need not be the first SQL statement for this safety argument: writes made earlier in the same transaction must roll back when the fence fails. Statement order does matter for contention and deadlocks.

EF sends the fence **last in its batch on every engine**. SQL Server uses the separate checked fence by default because a stale batch can otherwise hold a message lock while waiting for the partition row, while a take holds the partition row and waits for that message. This is the E9 reason for SQL Server's checked fence; it is an availability problem, not permission for a stale transaction to land. The checked fence acquires the partition lock before the remaining writes.

## What the fence transaction also does

A busy partition renews its lease through its commits. It does not need an additional heartbeat for the same purpose. An idle partition sends an empty fenced commit to renew the lease and discover if its epoch has been replaced.

The transaction also marks newly polled messages `seen` at the current epoch before they are delivered. Polling itself is a read. A poll result is not evidence that the owner still owns the partition, and reading a message does not authorize its execution. The fenced mark establishes that this generation saw it; trial markers then identify the work whose repeated owner losses count toward poison handling.

The message marks are writes protected by the fence, rather than fields in the partition row. After a takeover, the higher epoch lets the new owner discover messages seen by its predecessor.

## Why the check costs a round trip

The provider must learn that its conditional write succeeded before it authorizes a commit. Sending an unconditional `COMMIT` blindly after an update that matched zero rows would still commit the other writes.

The EF implementation has two forms:

| Form | How success is checked | Cost shape |
|---|---|---|
| Fence in the batch | Epoch and position are concurrency tokens. EF checks the affected-row result before sending `COMMIT`. | The fence shares the data-writing batch. A simple PostgreSQL checkpoint takes two round trips: batch and commit. |
| Separate checked fence | Send the conditional update, read its result, then send the data-writing batch and commit. | The check is its own round trip. The same PostgreSQL checkpoint takes three. SQL Server uses this form by default. |

These are measured checkpoint shapes, not universal counts for every provider or operation. Transaction setup, polling, loading, sent messages, parameter limits and exceptional paths can add work. SQLite counts calls into its engine rather than network round trips. The provider must preserve the atomic fence contract even when its wire protocol groups commands differently.

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

The checked fence costs an extra command/round trip compared with the batch form. Idle ownership costs renewal traffic. A reset throws away caches and uncommitted work and requires recovery reads. Conservative leases and clocks can cost availability; a slow or failed owner can leave work waiting until another take succeeds.

The protocol depends on atomic rollback, reliable affected-row checks and read isolation supplied by each provider. The fence protects durable changes in that store. It does not cancel packets already sent to another system, make external effects exactly once, or establish ordering across different partitions.

## Contract and evidence

The [design sketch](../reports/clean-slate-kernel/design-sketch-draft-5.md#the-commit) defines the commit and ownership protocol. The prototype's [DESIGN.md](../../prototypes/kernel-model/DESIGN.md), [lost-answer analysis](../../prototypes/kernel-model/FINDINGS.md#3-mutation-check), [engine notes](../../prototypes/kernel-model/ENGINES.md#e9-sql-server-fence-in-the-batch-merged-kernel-the-take-deadlocks-with-a-stale-commit-run-fixed-in-round-4-the-checked-fence-is-sql-servers-default) and [local lease guard](../../prototypes/kernel-model/LEASE.md) separate implementation evidence from this proposed architecture.
