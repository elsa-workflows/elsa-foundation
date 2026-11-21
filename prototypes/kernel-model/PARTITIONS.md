# Virtual partitions: per-virtual-partition fence against per-range fence (round 5 A, 16 November 2026)

K1b keeps the per-virtual-partition fence and journal. Takes and node heartbeats renew one lease per process.
A commit still writes every touched fence row, including a batch with no journal entries. The K1 costs below are the
per-partition-lease baseline. K1b compares its idle PostgreSQL heartbeat with that baseline using `x setidle`.

Status: **measured; decided by the owner on 16 November 2026** (see "The owner's decisions" at the end). The owner's experiment of 15 November (sketch draft 5, decision 4): 1,024 and 4,096
fixed virtual partitions (4,096 provisional), stable placement, no rehash, a journal per virtual partition, nodes owning
ranges, and the fence row either **(i) per virtual partition** or **(ii) per owned range**. This records what each
costs and what each means, on SQLite and PostgreSQL, with a seeded model of the ownership protocol at both counts.
**The fence granularity is the owner's decision**; the last section says what the numbers support and what is open.

What was run, and where the code is:

- `src/Kernel.Runtime/VirtualPartitions.cs`: addressing and placement (`VirtualPartitions.Of`, the same hash as
  today's `InstanceRef.Spread`; `VpRange`; `RangeMap` with split, merge, fair ranges and two rebalance strategies).
  `sim --placement-check` is its witness. When this round ran, nothing in the kernel used it. Since K1 (the sections
  below) the kernel hosts 1,024 virtual partitions, and a node's one worker owns a set of them.
- `experiments/VirtualPartitions`: the two variants as EF stores over the kernel's entity classes, keys and
  concurrency tokens, the measurement harness, and the ownership simulation ([its README](experiments/VirtualPartitions/README.md)).
  Experiment code: no default changed. The commit SQL is EF's `SaveChanges` with the fence as concurrency tokens, or
  the fence checked as its own statement first, as `EfStore` does today; takes are conditional
  statements with RETURNING, as the take is today; carve, split and merge are conditional UPDATE, INSERT and DELETE
  statements checked by their affected-row counts. Hand-tuned commit SQL is round 5 B.

## The two variants, as built

| | (i) fence per virtual partition | (ii) fence per owned range |
|---|---|---|
| Fence rows | `Partitions`: one row per virtual partition (1,024 or 4,096): owner, epoch, lease, position | `Ranges`: one row per range (lo, hi, owner, epoch, lease), a handful; `VpPositions`: one row per virtual partition; `Epochs`: one counter |
| A flush over *k* virtual partitions fences | *k* rows, each on its own (epoch, position) | 1 row on (epoch, hi), and moves *k* position rows on (position) |
| Take of a range of *R* | 1 statement over `Id BETWEEN`, returns *R* (id, epoch, position); each virtual partition has its own epoch | 3 statements: the epoch counter, the range row, the *R* position rows |
| Beat | 1 statement, *R* rows | 1 statement, 1 row |
| Cooperative release of a tail | 1 statement frees the rows; the kept rows are untouched | split: the kept row gets a new epoch and bound, a new free row covers the tail (4 statements with the counter) |
| Merge of two adjacent ranges one node holds | nothing to write | 3 statements: counter, grow the first row at a new epoch, delete the second |
| Bootstrap | the rows exist | the fresh database has one free row; a node carves its range out of it (up to 9 statements) |
| Why the epoch counter | each virtual partition's epoch is its own and monotonic | `(lo, epoch)` must never repeat after a split and a merge (the child-id issuer and the trial marks key on it), so every take, split and merge draws from one counter |

Both: a fence that does not match answers "not landed" and the batch rolls back whole (ENGINES.md E1 handled for the
in-batch fence); a stale owner's commit after a takeover is refused; the journal is keyed `(virtual partition, position)`
and positions move only with a commit.

## Flush cost: round trips and statements

PostgreSQL 17 in Docker, 4 nodes (ranges of 256 at 1,024 and of 1,024 at 4,096), round trips counted on the TCP stream
through the cost runs' proxy (`tcp`), statements as PostgreSQL Execute frames (`exec`). *n* instances spread over the
owner's range by the placement hash touch *k* distinct virtual partitions; each new instance writes its row, consumes
its start message and writes a journal row (3 statements), an update writes its row and a journal row (2). The run was
the same at 1,024 and 4,096 but for *k*: 100 instances touched 81 virtual partitions at 1,024 and 93 at 4,096.

| Flush (PostgreSQL) | (i) in batch: tcp / exec | (ii) in batch: tcp / exec | (i) checked: tcp / exec | (ii) checked: tcp / exec |
|---|---|---|---|---|
| 1 new instance, k=1 | 2 / 4 | 2 / 5 | 3 / 4 | 3 / 5 |
| the same 1 again | 2 / 3 | 2 / 4 | 3 / 3 | 3 / 4 |
| 10 new, k=10 | 2 / 40 | 2 / 41 | 12 / 40 | 3 / 41 |
| the same 10 again | 2 / 30 | 2 / 31 | 12 / 30 | 3 / 31 |
| 100 new, k=81 (1,024) | 2 / 381 (one run showed 3) | 2 / 382 | 83 / 381 | 3 / 382 |
| the same 100 again, k=81 | 2 / 281 | 2 / 282 | 83 / 281 | 3 / 282 |
| 100 new, k=93 (4,096) | 2 / 393 | 2 / 394 | 95 / 393 | 3 / 394 |
| the same 100 again, k=93 | 2 / 293 | 2 / 294 | 95 / 293 | 3 / 294 |

So: **with the fence in the batch (the default on PostgreSQL, SQLite and MySQL) the two variants cost the same round
trips, 2, and (ii) sends one statement more.** The *k* rows are there in both: *k* fence rows in (i), *k* position rows
in (ii). **With the checked fence (SQL Server's default, E9) variant (i) as built costs *k* + 2 round trips** against 3,
because each virtual partition's fence is its own statement; one statement that checks *k* rows at once and returns
their count would bring it back to 3, and that is commit SQL (round 5 B). SQLite agrees on statements (its EF provider
sends one command per statement, so `commands` there equals `exec` here).

## Take, beat, takeover and rebalance

PostgreSQL, in-batch fence runs (the checked runs give the same statements and trips; times vary by a few ms).

| Operation | (i) 1,024 (R = 256) | (ii) 1,024 (R = 256) | (i) 4,096 (R = 1,024) | (ii) 4,096 (R = 1,024) |
|---|---|---|---|---|
| Carve the range out of the free row (bootstrap) | not needed | 2 tcp, 1 stmt here (the neighbour's carve, uncounted, had cut both bounds already; a first carve is up to 9 statements) | not needed | 2 tcp, 1 stmt |
| Take a free range | 2 tcp, 1 stmt, 256 rows back | 4 tcp, 3 stmt, 256 position rows back | 2 tcp, 1 stmt, 1,024 rows | 4 tcp, 3 stmt, 1,024 rows |
| Beat | 1 tcp, 1 stmt, 256 rows touched | 1 tcp, 1 stmt, 1 row | 1 tcp, 1 stmt, 1,024 rows | 1 tcp, 1 stmt, 1 row |
| Takeover of a dead owner's range | 2 tcp, 1 stmt, 256 rows | 4 tcp, 3 stmt | 2 tcp, 1 stmt, 1,024 rows | 4 tcp, 3 stmt |
| Takeover time, median of 5 (local Docker) | 3.8 ms (128 rows) | 4.1 ms (128 rows) | 3.9 ms (512 rows) | 8.6 ms (512 rows) |
| Release the upper half cooperatively | 2 tcp, 1 stmt | 5 tcp, 4 stmt (split) | 2 tcp, 1 stmt | 5 tcp, 4 stmt |
| The other node takes the freed half | 2 tcp, 1 stmt, 128 rows | 4 tcp, 3 stmt | 2 tcp, 1 stmt, 512 rows | 4 tcp, 3 stmt |
| It merges the half into its range | nothing | 4 tcp, 3 stmt | nothing | 4 tcp, 3 stmt |
| Stale owner after takeover commits | refused | refused | refused | refused |
| Picture from before the move commits in the moved half | refused | refused | refused | refused |
| Picture from before the move commits in the kept half | **lands** (kept rows unchanged) | **refused** (the range row changed) | lands | refused |
| The owner that split commits on, without a reset | lands | lands | lands | lands |
| Journal after all of it | gapless per virtual partition; 1,024 fence rows | gapless; 3 range rows + 1,024 position rows | gapless; 4,096 fence rows | gapless; 3 range rows + 4,096 position rows |

Takeover times are millisecond-level either way on a local container; the differences (3.8 against 4.1 ms, 3.9 against
8.6 ms) are within the run-to-run noise seen across the eight runs (min 1.8, max 17.5 ms over all of them) and are not
a ranking. What is structural: (i) returns *R* rows per take and touches *R* rows per beat, (ii) one row for each but
reads *R* position rows at a take; (i) writes nothing to move a range, (ii) splits and merges.

## The journal-position semantics

- Both keep **one gapless position sequence per virtual partition**, moved only by a commit, read back 1..n with the
  position row saying n, after flushes, a takeover, a split and a merge. A virtual partition's journal never changes
  identity: its number is in every instance id and in every journal key, whoever owns it.
- **(ii) cannot do without a position row per virtual partition.** The alternative, `max(position)` from the journal at a
  take, breaks on deletes: a delete removes its instance's journal rows (section 20 of FINDINGS, invariant 6: the
  projector tells a deleted row from a late one by the partition's position), so the position must be a row of its own.
  Hence (ii)'s *k* position rows per flush: (ii) keeps the per-virtual-partition writes, which move from fence rows to
  position rows. The position row's concurrency token is kept (it costs nothing and keeps "no two commits
  at one position" at the row as well as at the journal's primary key).
- **What makes a stale picture stale.** In (i) the kept virtual partitions of a range do not change when a tail is
  released, so a commit from an older picture into them lands (and should: no one else wrote). In (ii) the split
  changes the range row, so the whole range's picture is stale: a commit in flight at the moment of a split is refused
  and the owner resets, which the kernel's workers avoid by not standing down while a flush is in flight
  (`ReleaseAsync` waits for `_flushing`). The owner that splits its own row adopts the new epoch itself and commits on.
- **Epochs.** In (i) a node holds *R* (epoch, position) pairs after a take and each virtual partition's epoch moves
  independently; the trial marks and the child-id issuer, which key on (partition, epoch) today, key on (virtual
  partition, epoch) with no change of meaning. In (ii) a range's epoch comes from one counter, and the child-id issuer
  would key on (lo, epoch): unique because the counter never repeats, but a range's lo changes at a merge, so a turn
  must keep the issuer of the epoch it ran under (which it does today).
- **Taking only part of a range.** In (i) a take over a range that is partly held by a live node returns fewer rows than
  asked; the harness rolls that back, a kernel would keep what it got. In (ii) a take is all or nothing by construction.

## The ownership simulation

`experiments/VirtualPartitions` `sim`: nodes take, flush over the virtual partitions they believe they hold (1, 3 or 10
at a time, one flush in flight per range), beat, rebalance by both strategies of `RangeMap`, lose answers (a doubt is a
reset, as in the kernel), crash and restart, with leases running out; after every action: no commit landed under a
stale epoch or bound, no two rows at one position, every virtual partition's journal gapless, and in (ii) range rows
that cover without overlap; then liveness after settling.

Every run passed with 0 failures. Per seed, averaged. These figures are from `addffea7c` (the 16 November model). (ii)'s rows
still hold at K1 slice 0; (i)'s current figures are in the slice 0 section below.

| Run | Variant | Commits landed / refused | Resets | Takes (rows each) | Splits / merges | Virtual partitions moved | Fence rows per commit |
|---|---|---|---|---|---|---|---|
| 2,000 seeds x 300 actions, 4 nodes, 1,024 | (i) | 36.5 / 3.4 | 5.0 | 29.3 (146) | 0 / 0 | 531 | 2.69 |
| | (ii) | 26.7 / 4.2 | 5.7 | 22.4 (219) | 6.8 / 2.4 | 436 | 1.00 |
| 2,000 x 300, 4 nodes, 4,096 | (i) | 37.5 / 3.4 | 4.9 | 29.9 (571) | 0 / 0 | 2,125 | 2.74 |
| | (ii) | 26.7 / 4.2 | 5.7 | 22.4 (875) | 6.8 / 2.4 | 1,745 | 1.00 |
| 500 x 1,000, 4 nodes, 1,024 | (i) | 81.2 / 11.8 | 16.4 | 96.7 (124) | 0 / 0 | 1,836 | 3.32 |
| | (ii) | 70.8 / 15.3 | 19.7 | 83.6 (167) | 17.5 / 10.8 | 1,360 | 1.00 |
| 500 x 1,000, 4 nodes, 4,096 | (i) | 82.7 / 11.6 | 16.2 | 99.1 (480) | 0 / 0 | 7,340 | 3.48 |
| | (ii) | 70.8 / 15.3 | 19.7 | 83.6 (668) | 17.5 / 10.8 | 5,441 | 1.00 |
| 1,000 x 300, 16 nodes, 1,024 | (i) | 43.0 / 23.9 | 25.2 | 54.0 (74) | 0 / 0 | 189 | 2.06 |
| | (ii) | 61.0 / 25.2 | 26.8 | 63.5 (55) | 22.8 / 1.0 | 136 | 1.00 |
| 1,000 x 300, 16 nodes, 4,096 | (i) | 45.8 / 24.0 | 25.3 | 55.7 (286) | 0 / 0 | 750 | 2.11 |
| | (ii) | 61.2 / 25.3 | 26.9 | 63.7 (220) | 22.8 / 1.0 | 557 | 1.00 |

Reading it: the "fence rows per commit" column averages over every commit of a seed, the settle phase's one-virtual-
partition commits included, which pulls it down. Over the action phase alone (replays of seeds 1 to 40 at `addffea7c`, distinct
virtual partitions counted per logged commit) a commit of 1, 3 or 10 random virtual partitions touches on average
3.97 fence rows in (i) at 1,024 and 4 nodes (870 commits), 4.09 at 16 nodes, 4.18 at 4,096 and 4 nodes and 4.31 at
16 nodes; (ii) fences one row per commit and moves as many position rows as (i) fences, which the column leaves out. (ii) splits 7 to 23 times and merges 1 to 11 times per seed where
(i) writes nothing to move a range. The counts of commits and takes differ between the variants because (ii)'s takes
are all-or-nothing on a row while (i) takes runs of rows, so the seeds' schedules diverge; they are not a throughput
comparison. The 16-node runs refuse more because leases run out faster against more balancing.

`mutations` (300 seeds of 300 actions each, both counts): each rule switched off, with the expected verdict. Seeds
are at K1 slice 0; where `addffea7c` differed, its seeds are in brackets.

| Mutation | (i) | (ii) | Why, when not caught |
|---|---|---|---|
| The fence ignores the epoch | caught, seed 1 | caught, seed 1 | |
| The fence ignores the position | not caught (as expected) | not caught (as expected) | a lost answer is a reset, so the new epoch refuses a retry (the kernel gate documents the same for `FenceWithoutPosition`) |
| A lost answer is retried from the same picture | not caught (as expected) | not caught (as expected) | the position check refuses the retry |
| Both of the above | caught, seed 1 at both counts (14 and 19) | caught, seeds 1 and 13 | |
| (ii) The range fence ignores the bound; the split keeps the epoch; an owner splits with a flush in flight | | each alone and each pair not caught (as expected); all three caught, seed 7 | the bound and the split's new epoch are each other's backstop; an owner that splits between flushes knows its own split |
| (ii) A split frees the tail without writing its row | | caught, seed 1 | |
| (ii) A merge leaves the second row | | caught, seed 1 | |
| (i) A release while a flush is in flight | not caught (as expected) | | a release changes no kept row; a commit that lands on a freed row before its take moves the position the take reads |
| (i) A beat renews only the rows the last flush touched | not caught (as expected) | | an availability cost: idle rows expire and are taken; the owner resets; nothing wrong lands |

### K1 slice 0: marks per virtual-partition epoch, partial takeover, the resets, the projector (18 November)

Before any Worker change, variant (i) of the simulation now also models what K1 changes in the protocol
([the README](experiments/VirtualPartitions/README.md) has the detail):

- **D4.** Messages carry Seen and Trial marks recorded at their virtual partition's epoch at the take. The poll reads
  `Seen < epoch` against that partition's epoch. The suspect test asks whether this process took that (partition,
  epoch). A take from another owner counts an attempt on what was on trial at that partition's old epoch.
- **The witness.** A flush spanning several virtual partitions is outstanding. Some of them, never all, move to other
  nodes, by lease expiry or by a cooperative release during the flush. Then the flush lands, and it must be refused whole.
- **The keeping reset.** After a refused flush the owner re-reads its rows in one statement, renewing them like a beat
  that returns id, owner, epoch and position. It keeps each partition still its at the epoch it holds, with the store's
  position. It takes nothing and drops the rest. It then polls the kept partitions again, with `Seen <= epoch` this once.
- **The reset after a lost answer** (coordinator decision, binding on the Worker slices). The node re-takes, one
  partition at a time and each at a new epoch, the picture's partitions that are still its own or free, and drops the
  rest. Every take keeps what it got.
- **The projector.** It runs rounds per virtual partition, on any node. A round's cursor write is conditional, and a
  round may die between its read and its write.

The checks:
- **W1:** a flush lands whole or not at all.
- **W3 (liveness):** after the witness, the old owner serves every partition of the flush it still holds, at the
  store's epoch and position.
- **W4 (cost):** the reset took none of them again.
- **L1 (liveness):** after every reset, and in the final settle check, every partition a live process holds live in
  the store is served in one of its pictures. (`Map()` counts a live-lease row as owned, so the earlier check missed these.)
- **S1:** no message is marked above its partition's epoch, and none is stranded (marked at the epoch while the owner
  does not hold it in memory).
- **S2:** the suspect test agrees with who really marked the message.
- **T1 (the epoch check):** the take's attempt test on the recorded trial mark counts the same as the test on the epoch
  the trial really ran under.
- **C1:** a message is consumed once.
- **P1:** no row is projected twice or without a commit.
- **P3:** every row is projected after settling.

Every run passed, and (ii)'s figures are unchanged, bit for bit (its seeds draw the same actions). The figures are per
seed:

| Run (i) | Landed / refused | Resets: by a take / keeping | Takes (rows) | Partial takeovers in flight | Marks (trials, attempts counted) / consumed | Projector rounds: run / to catch up | Time |
|---|---|---|---|---|---|---|---|
| 2,000 x 300, 4 nodes, 1,024 | 31.3 / 4.4 | 1.3 / 4.3 | 25.5 (130) | 1.2 | 8.0 (0.28, 0.02) / 2.8 | 15.6 / 50.4 | 6.8 s |
| 2,000 x 300, 4 nodes, 4,096 | 31.9 / 4.4 | 1.3 / 4.3 | 26.0 (509) | 1.2 | 8.0 (0.28, 0.02) / 2.7 | 15.6 / 53.5 | 58.7 s |
| 10,000 x 300, 4 nodes, 1,024 | 31.0 / 4.3 | 1.3 / 4.3 | 25.3 (131) | 1.2 | 7.8 (0.25, 0.03) / 2.7 | 15.7 / 49.8 | 24.3 s |
| 10,000 x 300, 4 nodes, 4,096 | 31.6 / 4.4 | 1.3 / 4.3 | 25.8 (515) | 1.2 | 7.9 (0.25, 0.03) / 2.7 | 15.7 / 52.8 | 55.5 s |
| 500 x 1,000, 4 nodes, 1,024 | 70.2 / 15.1 | 4.0 / 14.8 | 88.5 (96) | 3.9 | 56.8 (9.26, 2.38) / 19.6 | 53.9 / 148.5 | 20.5 s |
| 500 x 1,000, 4 nodes, 4,096 | 71.8 / 15.4 | 4.0 / 15.1 | 89.7 (378) | 3.9 | 55.8 (9.19, 2.40) / 19.5 | 53.8 / 166.2 | 19.7 s |
| 1,000 x 300, 16 nodes, 1,024 | 41.8 / 22.2 | 1.2 / 22.1 | 46.7 (69) | 1.0 | 1.4 (0.01, 0.00) / 0.2 | 15.5 / 43.7 | 8.3 s |
| 1,000 x 300, 16 nodes, 4,096 | 43.7 / 22.2 | 1.2 / 22.2 | 47.7 (266) | 1.0 | 1.4 (0.01, 0.00) / 0.2 | 15.5 / 48.0 | 15.4 s |

Times were taken on a heavily loaded machine (load average about 150) and are not comparable with the table above.

Trials and attempts are rare at 300 actions, and near zero at 16 nodes. The 1,000-action runs exercise them. The new
mutations, at 300 seeds of 300 actions and both counts, are each caught:

| Mutation (i) | Caught | By |
|---|---|---|
| `FenceSubset`: the k-row fence covers only the rows still held, and the batch lands those | seed 1 at both counts | W1 |
| `SeenAtNodeEpoch`: a mark records the picture's highest epoch | seed 5 at both counts | S1; with S1 off, S2 at seed 34 |
| `ResetAll`: a refused flush resets by a take (slice 7: drops the whole picture) | seed 1 at both counts | W3 since slice 7 (below). At slice 0 it meant a reset by a take, which W4 caught as a cost; that reset is now the Worker's. |
| `ResetPollsSeenBelow`: the poll after a keeping reset reads `Seen < epoch` | seed 2 at both counts | S1 (stranded). Slice 7 changed its meaning (below). |
| `ProjectorBlindWrite`: the cursor write is not conditional | seed 1 at both counts | P1 |
| `ProjectorJumps`: the cursor write jumps to the partition's position | seed 1 at both counts | P3 |

- C1 has no mutation of its own: a second consumption needs a second landing, which the fence refuses and I1 catches first.
- L1 has no mutation either. A probe that put back the old reset, an all-or-nothing take of the picture's whole range,
  failed L1 at seed 1 at both counts.
- Every earlier mutation keeps its verdict.
- W3 and W4 skip a picture still in doubt under `RetryAfterLostAnswer`, which does not reset by design; the position
  check is its backstop.

What this settles for slices 2a, 2b and 7:
1. **The poll's `Seen < epoch` holds only after a reset by a take.** A reset that keeps partitions at their epoch must
   read `Seen <= epoch` once for them, or re-take them. Otherwise what it marked there is stranded until a takeover.
2. **A refused k-row fence does not say which row was stale.** The keeping reset re-reads its rows: one statement over
   the held set, returning id, owner, epoch and position.
3. **After a lost answer, re-take per partition at a new epoch, and keep what the take got.** In the model a lost answer
   always follows a commit that already ran, so `FenceWithoutPosition` stays uncaught with either reset. The reason is
   the real Worker's: a commit whose answer was presumed lost can still land later, and the new epoch fences it out.
4. **A mark takes its message's partition epoch,** so a batch carries an epoch per virtual partition and marks only
   messages of partitions it fences. Today `InMemoryStore` writes `batch.Epoch`, which is the `SeenAtNodeEpoch` defect
   once a batch spans partitions.
5. **Criterion 2 of the K1 plan, read correctly.** "No Seen or Trial mark left at the old owner's epoch in a or c" means
   no stranded mark: none at an epoch whose owner does not hold the message in memory. Under the keeping reset, marks at
   the kept epoch are expected. Slice 7 tests that reading.
6. **D1's fairness check** (one turn loop serving all of a node's partitions) is not modelled here. It is deferred to slice 5.

**What the integrated Worker chose (slice 2a, #4811).** The Worker resets by a retake per partition on both a refused
fence and a lost answer. After the reset it takes back, one at a time and at a new epoch, each partition its node still
holds at the epoch it held (`IKernelStore.RetakeAsync`). A row that was released, or taken since by another node or at
another epoch, is dropped. The model's keeping reset was the cheaper option. The review of #4811 found two failures in
its re-poll with `Seen <= epoch`:
- a handed-over timer beyond the poll horizon was skipped, and the re-poll then ended on a short poll, so the timer
  never fired;
- rows held in memory filled the poll limit, so new messages were never read.

A new epoch fixes both, with less mechanism. Its cost is one take and one re-poll per refused fence, which is what a
one-partition Worker pays today. The model still lists `ResetAll` as a caught mutation, so its W4 cost check flags
exactly what the Worker now does by design. Slice 7 aligned the model's reset and W4 with the Worker (below).

### K1 slice 5a: the per-node owned set (D1)

`DurableHost.ForNode` is the host of one node: one worker serves every scope the node activates, as one owned set.
Layer 1 still activates one scope per virtual partition.
- **Activation** adds the partition to the set: a take of that one fence row at a new epoch (`Worker.GainAsync`). The
  other partitions keep their epochs, positions and memory. The next pass polls the set with it, and nothing is handed
  over in memory until a short poll has read its inbox (F1). The set may be scattered.
- **Giving a scope up** removes the partition (`Worker.LoseAsync`). What it holds in memory only is written first, as at
  a stand-down (decision 4). If nothing of the partition is in flight (no read, commit or turn, running job or trial),
  its memory goes and its fence row is released at its epoch, and nothing else changes; rows sent to it that the poll
  would have read back no longer keep the worker behind (F1). Otherwise the worker writes what every partition holds
  in memory only, as a stand-down does, then resets, releases it and takes the rest back at new epochs, as after a
  refused fence. That is safe; its cost is the rest's memory, and the reset tells the callers it held "lost", as every
  reset does. Giving up the last partition is a stand-down.
- **One at a time.** The node's passes, activations and give-ups run one at a time, so the set changes only between
  passes, and so between flushes. One pass serves every scope: a scope that another scope's pass served since its own
  last call, with no wake or job result since, gets what is left of that pass's answer, so the node runs one pass per
  round however many scopes it holds. Once that answer's time has run out, the call runs the pass itself, so a timer is
  never postponed after the scope that ran the last pass is given up.
- **A store without owned sets** (`commit=ef`) gets a worker per scope from the same host, as before.
- **Start-up cost, for slice 8.** Gains are one-row takes, one at a time under the host's lock, so a node that starts
  with 1,024 free partitions makes up to 1,024 sequential take round trips. A batched take of the activations a round
  asks for together would be one; slice 8 measures it before anything changes. Measured in "K1 costs" below: 512
  round trips for 256 on PostgreSQL, 2,048 for 1,024.

The fixed host (a worker per scope, given up front) stays for the models and the simulation, whose worlds are slice 7's.
The built-in runtime's conformance suite runs on the per-node host; the Orleans adapter keeps a worker per scope until
slice 6.

The follow-ups that slice 2a recorded for this slice:
- **A retake that finds a take in flight drops a pending stand-down.** It cannot arise under the per-node host: a retake
  runs inside a pass or a give-up, and a take inside an activation, and the host runs those one at a time. It remains in
  the fixed host, where it costs liveness only (the leases run out).
- **A set take that got only part of its set serves what it got and never takes the rest.** The per-node host takes one
  partition per activation, so a partition the take did not get is declined and layer 1 asks again at its next round.
  The fixed host's set take is unchanged.
- **`commit=ef` stays one partition per worker:** `ForNode` falls back to a worker per scope on such a store.

### K1 slice 5b: the fewest-moves balancer, the projector per partition, and fairness

- **The balancer** (`Balancer.Plan`, which `PartitionRuntime.BalanceAsync` runs) is unchanged in what it decides, and
  now a pure function with a witness. A node's share is the partitions over the nodes heard from, rounded up. It gives up
  only what is above its share, the highest first, and asks for free partitions up to it, the lowest first. A join
  therefore moves only the surplus above the new share, a leave only the leaver's partitions, and the sets end scattered.
  `sim --placement-check` runs four joins, a leave and a join through the plan at 64 and at 1,024. At 1,024 the four
  joins move 0, 512, 340 and 256 (1,108), the leave 256 and the last join 256, each the fewest, and the four sets end in
  7 ranges. Under the new mutation `ContiguousOnlyBalancer` (each node keeps its fair contiguous range by rank) the same
  joins move 0, 512, 511 and 513, the check fails, and CI requires that it fails. `GreedyBalancer` fails it too. The
  mutation is a placement cost: correctness is unaffected, so the random sim cannot catch it, and it is not in the
  `--mutations` list (the pins stay 43 and 15). `Cluster.Rank` (the node's place in name order, from the in-memory
  membership) exists only for that mutation.
- **The projector per virtual partition** (`NodeProjection`). A landed commit nudges the partitions whose position it
  moved, a commit whose outcome is unknown the ones it may have moved, and a take or a gain the partition it took (its
  journal may not be projected yet). One drain at a time runs a round for each nudged partition, again at once while a
  round moves on and leaves more, and again after a delay that doubles up to a second while a round throws or loses its
  conditional write. Nothing sweeps every partition on a timer. The runtime conformance case writes in two of four
  partitions: the other two get exactly one round each, from their take, and the two written are projected to their
  journal. `--lease-check` has a round that throws once: it is tried again, and the tail is projected in pages of two.
- **Fairness under one worker per node** (the plan's D1 risk). The set's poll is first in, first out across the set: one
  query, in seq order, at most `PollLimit` rows. A message in a quiet partition therefore waits behind the rows of every
  partition of the node that arrived before it, and behind none that arrived after it. A full poll that read something
  new is now read on at the next pass, without waiting for a hint or the poll interval: before, a node drained a
  backlog at `PollLimit` rows per poll interval across its whole set, where a worker per partition drained each
  partition at that rate. Once read, it takes its turn in
  the next pass at the latest, because a pass gives every ready instance of the whole set one turn (the fairness page's
  per-instance rule, now spanning the set). The runtime conformance case puts 600 rows for 60 instances in a hot
  partition, then one row in another partition, then 600 more in the hot one: no later row is read before it, and no
  instance takes more than two turns between its read and its turn. **The trial pause:** the trial gate is the node's, so
  a trial in any partition of the set holds back the turns of all of them until the trial's commit (FINDINGS F11: a
  death kills every partition the process holds). Under a worker per partition the runtime suite gave each worker its
  own gate; under the per-node set the pause is structural, and it applies to the whole node. Trials happen only for
  suspects, so the pause is rare. It lasts two commits (the trial mark, which starts only when nothing else is in
  flight, and the turn on trial), or, for a job on trial, the run of that job (`Worker.StartJob`, FINDINGS F11).

### K1 slice 6: Orleans grains join the silo's owned set (D2)

- **One worker per silo.** The Orleans provider now serves a silo's partition grains through `DurableHost.ForNode`
  (`IRuntimeProvider.OneWorkerPerNode`). A grain's activation gains its partition (`GainAsync`, that one fence row), and
  its deactivation loses it (`LoseAsync`). Orleans runs a silo's grains in parallel; the host's lock runs their passes,
  gains and losses one at a time, and the one-pass rule answers a grain whose pass another grain just ran. The seam
  needed no change for this. The trial gate is the silo's, so F11 now holds under Orleans too (FINDINGS R6 described
  the worker per scope).
- **Placement.** Orleans places the grains, and `Balancer.Plan` is not used. A silo that joins gets nothing until a grain
  moves (FINDINGS R8): the reference case "join" still differs, as it does on main. The two cases that test the
  balancer (a join hands part of a set over; a join and a leave move the fewest) run only under the kernel's balancer
  (`Placement.Balancer`). Every other owned-set case now runs on Orleans too.
- **Batching across a silo's grains, and a move mid-flush** (new case, `Placement.Runtime`). One silo holds all four
  partitions, and a second silo has joined. Tokens in 0 and 2 reach the store together, so one flush spans both grains'
  partitions under the k-row fence, and that flush is held on its way to the store. While it is held, Orleans migrates
  the grain of 2 to the second silo (`MigrateOnIdle` with a placement hint, through `IRuntimeNode.MoveAsync`). The
  deactivation's loss waits for the flush: the flush lands, 2 is given up only after it returns, and 0, 1 and 3 keep
  their epochs and their memory (a quiet loss, no reset). The second silo takes 2, and both partitions go on in order. A host lock that
  admits every caller fails the case (2 is given up before the flush returns), and so does a loss that always resets.
- **The reset by a guarded retake.** The fence case now checks, on an owned set, that the refused commit reset the
  worker and that it took 1, 2 and 3 back by `RetakeAsync`, releasing none, while the thief kept 0. It runs on both
  runtimes. With the retake switched off it fails.
- **The cost of a pass** (`Kernel.Runtime.Conformance.Run ... cost` and `Kernel.Runtime.Orleans.Conformance ... cost`,
  in-memory store, load average 2.5 to 3). One node holds every partition, with a 6 s lease and a 1 s poll interval: 10
  idle seconds, then a burst of tokens over the partitions (400, 1,024 and 4,096), each with a hint. The columns are the
  tool's. "Set polls" is the node's partition polls over the partitions: the harness's store answers a set poll with
  one poll per partition, where a SQL store sends one query. "Store calls" counts each partition poll, so it overstates
  both runtimes' polls against a SQL store.

  | Runtime | Partitions | Window | Pass calls | Worker passes | Set polls | Commits with turns | Partitions per commit | Store calls |
  |---|---|---|---|---|---|---|---|---|
  | orleans | 16 | idle, a second | 24 | 2.5 | 2.0 | 0 | - | 32.5 |
  | built-in | 16 | idle, a second | 17.6 | 1.1 | 0.9 | 0 | - | 15.5 |
  | orleans | 16 | 400 tokens | 33 | 18 | 18 | 11 | 14.8 | 791 |
  | built-in | 16 | 400 tokens | 74 | 14 | 12 | 11 | 14.8 | 696 |
  | orleans | 64 | idle, a second | 96 | 3.4 | 2.9 | 0 | - | 186.1 |
  | built-in | 64 | idle, a second | 96 | 1.5 | 1.0 | 0 | - | 65.2 |
  | orleans | 64 | 1,024 tokens | 37 | 37 | 37 | 27 | 37.9 | 4,703 |
  | built-in | 64 | 1,024 tokens | 162 | 36 | 35 | 34 | 30.1 | 5,193 |
  | orleans | 256 | idle, a second | 384 | 6.3 | 5.8 | 0 | - | 1,485.3 |
  | built-in | 256 | idle, a second | 383.9 | 1.4 | 1.0 | 0 | - | 257.1 |
  | orleans | 256 | 4,096 tokens | 416 | 350 | 350 | 343 | 11.9 | 101,876 |
  | built-in | 256 | 4,096 tokens | 768 | 256 | 255 | 252 | 16.3 | 77,832 |

  - **Pass calls.** Each grain calls its pass about as often as the built-in loop visits a scope, and the one-pass rule
    answers most of those calls.
  - **Idle polls.** The poll is the difference. The built-in loop wakes every scope at once and polls the set about once
    a second. Each Orleans grain wakes the node's worker at its own poll tick, counted from its activation, and a wake
    that comes after a pass began costs another poll of the whole set. On a quiet machine Orleans polled the set 2.0 to
    5.8 times a second here, growing with the grains. A review under load (load average 8 to 85) measured 2.1 to 16 a
    second.
  - **The poll grid that was dropped.** One version of this slice put the grains' ticks on a shared grid. A first run
    showed it halving the polls, but the review's interleaved pairs did not reproduce that benefit, so it is not kept.
  - **Under load.** The two runtimes are close. One commit spans many partitions on both (the harness caps a batch at
    40 turns). Orleans makes fewer pass calls, because a hint reaches only its own grain.
  - **For slice 8.** It measures the Orleans set polls per poll interval on a quiet machine at 1,024 partitions.
    Measured in "K1 costs" below: 107 to 117 a second, against 1.0 for the built-in loop.
- **Open seam question, for slice 8.** A silo-level poll would bring Orleans to one set poll per interval: the silo
  wakes its owned set once, as the built-in loop does. The seam today promises a wake per held scope (`RuntimeTiming.PollInterval`),
  and a per-scope host (a store without owned sets) still needs that. So the change needs the seam to say that one wake
  covers a node's set, which is a seam decision to take before anyone builds it.

### K1 slice 7: the flip to 1,024, and the simulation's set worlds

- **The flip.** `VirtualPartitions.Provisional` is `Small`, 1,024. `Defaults.ServerPartitions` and
  `Defaults.SqlitePartitions` are that count (they were 64 and 1), so `EfStore.CreateAsync`, `MongoStore.CreateAsync` and
  the Orleans adapter create and place 1,024 virtual partitions by default, each with its fence row and journal. On
  SQLite one node owns them all as one set. Nothing is rehashed: an address embeds its virtual partition, and the count
  is fixed when the database is created. A deployment on `commit=ef`, which has no owned sets, gets a worker per
  partition, so it passes a smaller count.
- **The count is checked, never migrated** (CodeRabbit on #4819; no data exists before release, so a database is
  recreated):
  - `EfStore.CreateAsync` and `MongoStore.CreateAsync` refuse, definitely, to open a database at a count other than
    the one it was created with, naming both. Mongo writes its partition documents one at a time, so its first open
    records the count first, and an open cut short is finished by the next open at the same count. Until every
    partition document exists, the count a node's start reads refuses ("initialization incomplete"). EF writes its rows
    in one transaction.
  - `IKernelStore.PartitionCountAsync` gives that count. When the built-in runtime or the Orleans adapter starts a
    node's host (`DurableHost.ForNode`, `IScopeCount`), the runtime's scopes must equal `WorkerOptions.Partitions`,
    which spread placement hashes over, and both must equal the store's count: fewer would leave partitions, and
    perhaps their work, unserved. A mismatch refuses the start, naming the three counts. `ForNode` takes the count as
    an explicit argument, and refuses options that say another. Only the simulation's worlds place fewer than the
    store has, behind `ForNode`'s explicit `placesPart`.
  - The guard protects developer-local prototype databases, which are regenerated; anything further on it is K4's
    provider schema work.
- **Set worlds.** `sim --count 1024|4096` (1,024 by default) gives the world's store a fence row and a journal for each
  of the count's virtual partitions. The world places four of them on its two or three nodes. Each node runs one worker
  for its owned set, through `DurableHost.ForNode`, as the built-in runtime does. Its takes and give-ups name a partition
  and go through the node's `PartitionRuntime`, its flushes span the partitions they touch under the k-row fence, and a
  trial group may span the set. Every seventh seed runs a worker per partition, as on a store without owned sets, so
  both host shapes stay under the mutations. Two pieces of the world are the simulation's only:
  - `SerialHost` queues the host's activations, passes and give-ups in the order layer 1 asks. The host runs them one
    at a time under a lock, and a contended `SemaphoreSlim` resumes its waiter on the thread pool. The simulation is one
    thread, so the queue keeps every continuation on it.
  - `Placed` cuts the heartbeat's free partitions to the four the world places, so the fair share is of those. The
    other partitions stay free, and nothing asks for them.
- **The sim's `--count` only sizes the store's rows.** Every world places partitions 0 to 3. Spread placement
  (children, event starts, correlation instances) hashes over those four, and nothing in the kernel reads the count
  except the store's rows and the heartbeat's free list. So at 4,096 every seed runs exactly as at 1,024: the two
  `--coverage` runs of 10,000 seeds agree to the unit, and so do the native runs. Scattered ids would need new
  mechanism in spread placement and in the count guard, and K1 does not need them. **4,096 is qualified end to end by
  the integrated runtime suite** (`semantic --partitions 4096`, built-in runtime, below). The PR gate runs the sim at
  1,024 only; the nightly runs it at 4,096 too, at reduced seeds, in a job of its own with the runtime suite.
- **Multi-partition flushes.** In 10,000 seeds of 300 actions, at either count: 289,891 commits over several partitions
  landed fenced; 242 had part of their fences stale and 38 all of them, and each was refused whole.
- **Checks moved to per-partition epochs** (#4813's list):
  - I16 reads the epoch of the instance's own partition.
  - I14's "alone" half gathers the trial rows of every partition the process holds at its current epoch. The first
    set worlds failed it falsely: a trial group of a set may span two partitions, and the check looked for the instance
    among the trial rows of each partition alone.
  - I9 and I3 (`Memory`) check what a worker holds of each partition it holds at its current epoch, and skip the rest.
  - The fair-share check reads `Holds(p)` and `EpochOf(p)`.
  - The wire sends a set poll as one call (it went through the interface's default, one call per partition), and
    counts takes per partition for the outbound sendings it gives back after a failed commit.
- **Mutations.** Every one of the 43 keeps its verdict at `--seeds 20000`. `TrialPerPartition` (a worker's own gate) is
  caught at seed 7, a seed with a worker per partition: under one worker per node it changes nothing, because the gate
  is already the node's. `NoWriteFirst` is caught at seed 3208, earlier than 6946.
- **The sim's counters against main.** The set worlds change every seed's schedule, so the sweep's counters differ
  from main's (for example "owners lost", the native "finished instances matched"). They are not comparable seed for
  seed: a node is one actor now, four partitions spread the traffic thinner, and one worker serves a node's set. In the
  native mutations `FreshTurnSwapsLoopKeys` is first caught at seed 3589, against 269 on main, for the same reason
  (NATIVE.md); the PR gate runs the native mutations at 12,000 seeds to keep three times that.
- **Runtime conformance at 1,024.** `semantic --partitions 1024` runs the cases written for the default count of 4 at
  1,024. The owned-set cases name the partitions of their count and keep it. The built-in runtime passes 16 of 16 in
  about 41 s, and the Orleans adapter 14 required cases in about 40 s, with the reference case "join" differing as it
  does at 4: Orleans' placement does not move grains to a node that joins.
- **Runtime conformance at 4,096** (built-in runtime). The case "crash: a crashed node's scopes are taken over" did not
  end within 120 s in review (it takes 17 s at 1,024 and 33 s at 2,048). Above 1,024 a case's settling bounds and its
  time now grow with the count, so it records the takeover instead of failing, and the crash case logs how long the
  takeover took. That time was the harness's: the case read its holder list once per partition, and each read scanned
  every activation per scope, cubic in the count, slow enough that a live node's lease lapsed ("the killed node gave
  partition 0 up"). The watcher now groups the activations by scope in one pass, and the case reads the list once
  (slice 8's fix, cherry-picked). Run here three times: 17 of 17 passed in 48 to 49 s, and the crash case alone three
  more times in 14 s; its takeover (2,048 of 4,096 scopes) settles 7.4 to 7.6 s after the kill, the 6 s lease running
  out and a balancing round.
- **A runtime-suite case for the count check** ("count: a node whose workers place another count than its store and
  runtime is refused at its start") runs on the built-in runtime and the Orleans adapter. The runtime suite's watcher
  passes the check through to layer 2's host, and a store that does not say its count is refused too.
- **Recorded for slice 8** (measured in "K1 costs" below):
  - `RuntimeNode` reads every scope's due time each round: 1,024 cheap reads per round. It runs one worker pass per
    round (slice 5a's one-pass rule).
  - The Orleans keeper calls every grain every `KeepEvery`: 1,024 grain calls per silo per round, with no store call.
  - A node that starts with 1,024 free partitions takes them one at a time (slice 5a).
- **Settled: Mongo's millisecond tie** (#4815, review A's Nit 4). Mongo's sequence comes from each partition's clock,
  so a set poll orders rows of two partitions added in one millisecond either way. The contract orders across
  partitions only to the store's tick, and the scattered-take case now adds its rows a few milliseconds apart. Nothing
  in the Worker or the simulation's checks depends on the order across partitions: an instance lives in one partition.

### K1 slice 7b: the decisive witness, the set mutations, and the model's reset

**The decisive witness** (the plan's K1 row) runs in three places.
- **In the simulation, as an invariant.** W1 (`World.cs`, the wire's commit): a flush with one stale fence of its set
  does not land. It checks the landed flag and the fenced partitions' positions. The set worlds reach it on their own:
  242 flushes of 10,000 seeds had part of their fences stale. The checks that follow every action see more of it. I6
  finds a journal hole in any partition. M1 (new; the model's S1 without its stranded half) finds a Seen or Trial mark
  above its partition's epoch. I3 finds a mark stranded at the epoch a worker holds, on a message it
  does not hold in memory. The settle phase requires every placed partition to be served again.
- **As a deterministic store conformance case, the full witness,** in two variants: b of {a, b, c} moves by lease expiry, or by a
  cooperative release, while a flush with a turn, a Seen and a Trial mark in each of the three is held. The flush is
  refused whole, with no row, journal row, position or mark written. a then retakes a and c at new epochs, as the
  Worker resets, and b's retake by a is refused. The poll at the new epochs reads back everything a had marked there,
  and nothing is marked at a new epoch. a goes on with a and c, b with its own, and every journal starts at 1. It
  passes on the in-memory store, EF on SQLite in both fence modes, and MongoDB (single-node replica set, data on tmpfs)
  in both modes. `semantic-flaw FenceSubset "the K1 witness" "the held flush landed"` runs the in-memory store with
  that mutation and requires every matching case to fail with that text; both variants do, and CI requires it.
- **The ownership model** keeps its W1, W3 and W4 checks, with W4 aligned (below).

**The new kernel mutations** (`Flaw` bits 50 to 54), at 20,000 seeds; the pin moves from 43 to 48:

| Mutation | What it breaks | Caught by |
|---|---|---|
| `FenceSubset` | the k-row fence lands the part of a flush whose fences still match (`InMemoryStore`) | W1 |
| `SeenAtNodeEpoch` | a mark records the batch's highest epoch, not its partition's (`InMemoryStore.MarkEpoch`) | M1 |
| `SuspectAcrossPartitions` | the suspect test asks for the mark's epoch in any partition (`Worker.Mine`) | IDLE: a row an earlier owner marked is not tried, and the instances behind it never finish |
| `ChildIssuerPerWorker` | one child id issuer, at the worker's lowest partition (`Worker.ChildIdsFor`) | C2 (new): a child's address names the (partition, epoch) its parent's commit is fenced at |
| `TrialSparesOtherPartitions` | a trial holds back only its own partition of the set (`ReadyForTurns`, `RunTurn`) | I14 ("alone") |

- C2 states slice 2b's rule directly. A (partition, epoch) belongs to one take, so an issuer elsewhere mints no
  duplicate today. The rule is what keeps that so, and the deterministic checks of slice 2b test it too.
- **`UnorderedFenceLocks` has no sim form.** The simulation runs one action at a time on one thread, and the in-memory
  store commits under one lock, so nothing can deadlock there. Its witnesses stay the lock-order probe on PostgreSQL,
  SQL Server and MySQL (slice 3, [experiments/K1.LockOrder](experiments/K1.LockOrder/README.md)) and the in-memory
  store's lock-order case. Mongo has no form of it either (slice 4: a second writer fails at once).
- **`ResetAll` has no kernel sim form.** A worker that drops the partitions it keeps after a refused fence, instead of
  retaking them, stays safe: the balancer takes them again, so the random sim's checks, which judge safety and service
  once faults stop, cannot tell. It costs availability only. Slice 2a's `--lease-check` cases pin the retake directly
  (each kept partition retaken once, at a new epoch), and the model's W3 catches the drop.

**The model's reset, aligned with the Worker** (`experiments/VirtualPartitions`). A refused flush now resets as the
Worker does: each partition still the owner's at the epoch it holds is retaken, at a new epoch and the store's
position. The rest is dropped, nothing else is taken, and the poll at the new epochs reads back what was marked.
- W4 now checks that the reset retook each kept partition once, at the epoch after the one it held.
- `ResetAll` now drops the whole picture, what is still held included, and takes nothing back. W3 catches it at seed 1
  at both counts.
- `ResetPollsSeenBelow` now keeps the held epochs with no retake, and polls `Seen < epoch`: slice 0's keeping reset
  without its one `Seen <= epoch` poll. W4 catches it at seed 1 at both counts.
- `sim` (2,000 x 300 at both counts, and 500 x 1,000) passes. Every mutation run gives its expected verdict.

### K1 costs: ownership of a set in the integrated Worker (slice 8, 19 November)

The take, beat, takeover, release, rebalance and flush, measured on real stores in the integrated Worker and runtime,
for scattered and for contiguous sets, and compared with the experiment's tables above ("Flush cost" and "Take, beat,
takeover and rebalance"). Measurement only: no kernel behaviour changed (the two code fixes are in the suites' own
bookkeeping, below). The raw outputs are in
[measurements/k1-costs-2026-11-19](measurements/k1-costs-2026-11-19).

**The workload.** `EngineRun` `x setcost` (`ef/Kernel.Ef.Run/SetCost.cs`) drives the kernel's own types:
`DurableHost.ForNode` (one worker per node, D1), a fixed-host `Worker` over a set, and `PartitionRuntime` with its
`BalanceAsync` rounds. It uses 1,024 virtual partitions and four nodes, so 256 each.
- **Scattered sets** are the fewest-moves balancer's after four joins, as `sim --placement-check` runs them: 256 in 1,
  1, 2 and 3 ranges. Node A holds the set in 3 ranges (256-341, 768-853, 940-1023).
- **Contiguous sets** are the fair ranges; A holds 0-255.
- **Rebalance** runs the real rounds. Scattered uses the default balancer. Contiguous runs them with the
  `ContiguousOnlyBalancer` mutation (each node keeps its fair range by rank), the only contiguous balancer the code has;
  the harness supplies the rank, which the EF store's membership does not give.
- **Each row runs five times, most from a setup of their own, which is not counted** (the beat, heartbeat and idle-poll
  rows run on one node that holds A). The counts are the most common run's: a row's
  first run may open connections, and their handshakes count. The time is the median of the five.
- **The contiguous rounds' rank** comes from an extra read of the node rows at every heartbeat (`Ranked` in
  `SetCost.cs`), on the admin connection: it is not counted, but it is inside the rebalance rows' time.
- **Units, as above.** `tcp` is round trips on the TCP stream through the cost runs' proxy, and `exec` is PostgreSQL
  Execute frames (statements). `rows` is the fence rows the operation wrote, read back from `Partitions` before and
  after. The worker's lease is an hour, as in the cost runs, so no beat lands inside a row.
- **Setup.** PostgreSQL 17.11, SQL Server 2022 (amd64 emulation) and MySQL 8.4 in Docker, with data on tmpfs, and
  SQLite with WAL. Fence in the batch, owned commit SQL (the defaults). Load average 3 to 15 on 15 cores.

**PostgreSQL, 1,024, R = 256** (`x setcost postgres "$PG" proxy=1 writes=1 runs=5`):

| Operation | Experiment (i), 1,024 | Scattered: tcp / exec / rows / ms | Contiguous: tcp / exec / rows / ms | The difference |
|---|---|---|---|---|
| Take a free set, one set take (fixed host) | 2 tcp, 1 stmt, 256 rows | 2 / 1 / 256 / 3.4 | 2 / 1 / 256 / 1.2 | equal |
| The same take while a live node holds half the set | (not run) | 2 / 1 / 128 / 2.9 | 2 / 1 / 128 / 1.1 | the take keeps what it gets; it locks all 256 rows (below) |
| Take at start-up through the per-node host: 256 activations | (not run) | 512 / 256 / 256 / 195 | 512 / 256 / 256 / 83 | one one-row take per activation (D1, slice 5a): 2 tcp each |
| Beat of 256 | 1 tcp, 1 stmt, 256 rows | 2 / 2 / 256 / 3.6 | 2 / 2 / 256 / 1.4 | +1 tcp, +1 stmt: the beat is an empty fenced commit, a lock statement in ascending id (slice 3) and the UPDATE in one transaction, then the COMMIT |
| Heartbeat (node row, live count, free list) | (not run) | 3 / 3 / 0 / 2.7 | 3 / 3 / 0 / 2.6 | once per balancing round |
| Set poll over 256, nothing to read | (not run) | 1 / 1 / 0 / 1.4 | 1 / 1 / 0 / 1.6 | one query per pass |
| Takeover of a dead node's 256, one set take (fixed host) | 2 tcp, 1 stmt, 256 rows; 3.8 ms (median of 5 takes of 128 rows) | 3 / 2 / 256 / 2.6 | 3 / 2 / 256 / 1.8 | +1 tcp, +1 stmt: the death count (one attempt for what was on trial at each partition's old epoch), in the take's transaction; the experiment's take counted none |
| Takeover through the per-node host: 256 activations | (not run) | 768 / 512 / 256 / 161 | 768 / 512 / 256 / 131 | one take and one death count per partition: 3 tcp each |
| Takeover through the runtime: a node dies, the other three run rounds | (not run) | 786 / 530 / 256 / 138 | 1,041 / 700 / 341 / 212 | the survivors take the dead node's partitions one at a time (as the line above), plus 6 heartbeats; contiguous moves 341, with 85 one-row releases from `ContiguousOnlyBalancer` |
| Release of 128, one set release (the store's; a stand-down sends it) | 2 tcp, 1 stmt | 1 / 1 / 128 / 0.9 | 1 / 1 / 128 / 0.8 | one round trip fewer (no transaction) |
| Release of 128 through the per-node host, one scope each | (not run) | 128 / 128 / 128 / 34 | 128 / 128 / 128 / 27 | the runtime gives scopes up one at a time |
| Rebalance: a fourth node joins, rounds until none changes | a different workload: one move of half a range (128 of 256) between two nodes, a set release (2 tcp, 1 stmt) and a set take (2 tcp, 1 stmt), so 4 tcp and 2 stmt | 796 / 540 / 256 moved / 160 | 1,567 / 1,054 / 513 moved / 328 | K1's workload is three nodes holding all 1,024 and a fourth joining, with 9 heartbeats (18 statements): the joiner's first round and 2 rounds of the four nodes. Each move is a one-row release and a one-row take; contiguous moves twice as many (513 against 256, as the placement witness says) |
| Rebalance: a node leaves gracefully, its stop | (not run) | 256 / 256 / 256 / 52 | 256 / 256 / 256 / 55 | one release per scope |
| Then the other three run rounds | (not run) | 530 / 274 / 256 moved / 99 | 785 / 444 / 341 moved / 150 | one-row takes; contiguous also has 85 one-row releases, because `ContiguousOnlyBalancer` shifts a survivor's range: 341 moved, 256 of them the leaver's |
| Reset after a refused beat: retake the other 255 | (not modelled) | 258 / 258 / 255 / 62 | 258 / 258 / 255 / 58 | slice 2a's retake, one partition at a time |

**One 100-instance flush, PostgreSQL** (the same run): 100 instances spread over A by the placement hash, each with
its message added from outside. k, the distinct partitions, was 74 to 91 over the ten runs.

| Flush | Experiment (i) in batch: tcp / exec | Integrated, both shapes: tcp / exec / fence rows |
|---|---|---|
| 100 new instances | 2 / 381 (k = 81; one run showed 3) | the mark: 2 / 3 / k; the turns: 2 / 5 / k |
| the same 100 again | 2 / 281 | the mark: 2 / 3 / k; the turns: 2 / 104 / k |

The turns' commit read 3 round trips in 3 of the 20 runs of the two shapes, as the experiment's once did (FINDINGS
F-B6: the PostgreSQL stream varies by a read).

- **Statements are below the experiment's.** The owned commit (round 5 B) writes rows of one kind as one multi-row
  statement. Its updates of instance rows stay one statement each, hence 104 for 100 updates.
- **One commit more: the mark.** The integrated Worker marks what a poll read as Seen, at each partition's epoch, in a
  fenced commit of its own, before the turns that consume it run (the trial protocol, D4). The experiment committed the
  turns directly. Each of the two commits is 2 round trips, as the experiment's one. A start posted in memory
  (`Worker.Post`) has no row to mark.
- The scattered and contiguous runs give the same counts. Only k differs, with the names.

**The same on the other stores** (counts are per engine; the shapes agree, but for the moves):

| Operation | SQLite: counted (commands + BEGIN + COMMIT) | SQL Server 2022: tcp | MySQL 8.4: tcp |
|---|---|---|---|
| One set take of 256 | 3 | 3 | 6 |
| Start-up through the per-node host, 256 activations | 768 | 768 | 1,792 |
| Beat of 256 | 3 | 3 | 6 |
| Takeover, one set take with death count | 4 | 4 | 7 |
| Takeover through the per-node host, 256 activations | 1,024 | 1,024 | 2,048 |
| One set release of 128 | 1 | 1 | 3 |
| Join, scattered (256 moved) / contiguous (513 moved) | 1,052 / 2,080 | 1,052 / 2,080 | 2,644 / 5,214 |
| The mark and the turns of a 100-instance flush | 3 and 3 | 3 and 3 | 6 and 6 |

SqlClient sends BEGIN alone, and Connector/NET adds a Ping and an Init DB per pooled connection and two trips for a
BEGIN (ENGINES.md). Hence SQL Server's and MySQL's extra trips. SQL Server's commands match PostgreSQL's commands (EF
command counts; PostgreSQL's Execute frames count each statement of a command, so a beat is 1 command and 2 frames).
MySQL's set operations match too, but its one-row take and retake read the row back (no RETURNING), so its one-row
paths send more commands: start-up 512 against PostgreSQL's 256 commands, the takeover through the host 768 against
512, the reset 512 against 257, and the join 796 against 540 (scattered) and 1,567 against 1,054 (contiguous).

**4,096, R = 1,024, PostgreSQL scattered** (`... count=4096 shape=scattered runs=3`; one set take of 1,024 rows):

| Operation | tcp / exec / rows / ms |
|---|---|
| One set take | 2 / 1 / 1,024 / 11.5 |
| Start-up through the per-node host, 1,024 activations | 2,048 / 1,024 / 1,024 / 830 |
| Beat | 2 / 2 / 1,024 / 5.2 |
| Takeover, one set take | 3 / 2 / 1,024 / 8.9 |
| Takeover through the runtime (three survivors) | 3,090 / 2,066 / 1,024 moved / 599 |
| Join (1,024 moved) | 3,100 / 2,076 / 1,024 moved / 688 |

The experiment had 2 tcp and 1 statement for each take and 1 tcp for the beat at 4,096 too: the set operations scale
as there, and the one-row operations scale with the partitions moved.

**What is above the experiment, and why.**
1. **The beat and the set takeover are one round trip and one statement more.** The beat is a fenced commit, which
   locks its k rows in ascending id before its UPDATE (slice 3's lock order) and commits. The takeover counts deaths.
   Both are kernel rules the experiment did not have.
2. **Everything the runtime moves goes one partition at a time:** start-up, a move by the balancer, a takeover through
   the runtime and a give-up. Layer 1 activates one scope per virtual partition (D1), and `DurableHost` turns each
   activation into a one-row take (`Worker.GainAsync`) and each give-up into a one-row release (`Worker.LoseAsync`). A
   join at 1,024 is 796 round trips against the experiment's 2 for the take of the freed half. Locally that is 160 ms at
   1,024 and 690 ms at 4,096; on a network with 1 ms per round trip it would be about 0.8 s and 3 s. The store's set
   take and set release (one statement each) exist and are measured above; only the fixed host and a stand-down use
   them. A batched gain, one set take of what a round asks for, and a batched give-up would bring a round to a few
   round trips. That is a change of `DurableHost` and `PartitionRuntime`, and K1 does not build it (below).
3. **The reset after a refused fence retakes one partition at a time:** 258 round trips at 256, 1,026 at 1,024
   (slice 2a's choice).
4. **The mark before the turns** is one commit more for messages that arrive by poll (above).

None of these is a correctness defect. Each is the kernel's design at K1: the first is a kernel rule, the second and
third are the serial path that slices 2a and 5a recorded for this slice, and the fourth is the trial protocol.

**The crash case at 4,096 (raised in the 7a review; built-in runtime only).** Before the fix below, the built-in
runtime suite's case "crash: a crashed node's scopes are taken over" took 14.8 s at 1,024 here and did not end within
its 120 s at 4,096 ([crash-case-phases-before-1024.txt](measurements/k1-costs-2026-11-19/crash-case-phases-before-1024.txt),
[crash-case-phases-before.txt](measurements/k1-costs-2026-11-19/crash-case-phases-before.txt)).
- **Where the time went: before the kill.** The case built the holder list of every partition once per partition, and
  the list read every watched activation once per scope, so cubic in the count. Timed by phase at 4,096 before the fix
  ([crash-case-phases-before.txt](measurements/k1-costs-2026-11-19/crash-case-phases-before.txt); `crash-case-phases.txt`
  has the runs at 1,024, 2,048 and 4,096 after it): the case reached the steady state at 6.3 s and the kill at 198 s. The case's takeover line measures from the kill, so it
  showed 6 to 7 s while the case ran minutes; applying the case's 30 tokens after the takeover took about 1 ms.
- **The fix** (`528eabf7e`, the suite only; it landed on main with 7a, #4819): `Watcher.Holders` reads the live activations in one pass, and the case
  reads the list once. The case now takes 13.8 s at 1,024, 14.0 s at 2,048 and 14.3 s at 4,096, on the built-in
  runtime (the Orleans adapter was run at its default count and at 1,024). The takeover itself, from the kill until
  every scope is held again, takes 6.2 to 7.5 s at each count: the 6 s lease runs out and a balancing round takes the
  partitions. That phase does not grow from 1,024 to 4,096, so the serial gains (512 against 2,048 one-row takes on the
  in-memory store) add no measurable time.
- **Turn throughput across a large set** (asked after a re-check of 7a). The runtime `cost` burst of k tokens over k
  partitions took 1.55 s at 1,024 and 21.0 s at 4,096, growing with k squared (one run each at `2c1f7ecbf`, before the
  fix: [runtime-burst-before-setpoll.txt](measurements/k1-costs-2026-11-19/runtime-burst-before-setpoll.txt)). The suites' store wrappers (`FaultyStore`,
  `RecordingStore`, the runtime suite's `NodeStore`) did not forward the set poll, so it went through the interface's
  default, one poll per partition, and the in-memory store reads every message for each. A pass over k partitions was
  k scans of the inbox. With the set poll as one call (`961b599ea`, the suites only) the burst takes 0.13 s at 1,024
  and 0.92 s at 4,096 (one run each: [runtime-burst-after-setpoll.txt](measurements/k1-costs-2026-11-19/runtime-burst-after-setpoll.txt)).
  `FaultyStore` now gets one chance to inject a fault per set poll, where it had one per partition, so set polls in
  the stress runs see fewer injected faults; the stresses still hold.
  The other candidates are small: `RuntimeNode` makes 4,258 pass calls a second at 4,096 (4,700 before the fix) with
  1.4 to 1.5 worker passes, the one-pass rule answers the rest with no store call, and on PostgreSQL the set poll over 1,024 is one
  query of 1.6 ms. Since that fix the cost run's "Store calls" column counts a set poll once; "Set polls" is unchanged.
- The built-in suite at 4,096 took 215 s at `bc8021514` (7b's PR body, [#4820](https://github.com/elsa-workflows/elsa-foundation/pull/4820)), 50 s after the holder-read fix
  ([conformance-after-merge.txt](measurements/k1-costs-2026-11-19/conformance-after-merge.txt)) and 46 s after the
  set-poll forwarding as well. Every suite run at
  the default count and at 1,024 passes on both runtimes, with the stresses
  ([conformance-after-setpoll.txt](measurements/k1-costs-2026-11-19/conformance-after-setpoll.txt)).

**PostgreSQL: what the set statements lock** (`writes=1`, pg_walinspect, record counts on `Partitions`):
- The set take of 256 while a live node holds 128 of them writes 256 lock records and 128 updates. The locking read
  in ascending id (slice 3) locks the rows it does not change too, until the take's transaction ends, so a commit of
  the live node on those rows waits for it. The take is one statement and its COMMIT, about 3 ms. Recorded for K4.
- The set release of 128 writes 128 lock records and 128 updates, and the beat of 256 writes 262 lock records and 256
  updates. A row's lock record is about 54 bytes, and its update 72 to 107 bytes.

**SQL Server re-plans the set poll at every take** (#4812). 20 rounds of a give-up, a take and a set poll over 256
left 21 cached ad hoc plans of the set poll, against 1 before: the poll inlines each partition's epoch, so its text
changes with every take. Each plan is compiled for a 256-branch CASE; a round took 34 ms under emulation. Passing the
epochs as parameters is K4's provider work.

**The beat on PostgreSQL over an idle period (D3: observed).** `x setidle postgres "$PG" seconds=120 writes=1`: four
`RuntimeNode`s on the default loop and lease (10 s, a beat a third of it after the last renewal), joined one after
another and settled at 256 each, then 120 s with nothing to do:

| Measure, over 120 s idle | Value |
|---|---|
| Statements a second | 17.0: heartbeats 8.0 (2 statements each, a node a second), free lists 4.0, set polls 4.0 (one per node a second), beats 1.2 |
| Partitions rows updated | 36,864, so 307 a second (1,024 rows every 3.3 s); 95 % HOT |
| WAL | 5.36 MB, 44.7 kB a second, 161 MB an hour (153.5 MiB; the raw output at the time labels it MB) |
| Of which `Partitions` | 42.9 kB a second (96 %): updates 2.73 MB (HOT and non-HOT), lock records 2.09 MB, index 0.12 MB, pruning 0.2 MB |
| Of which `Nodes` (the four node rows, a heartbeat a second each) | 582 B a second |
| Dead tuples in `Partitions` | 2,584 before, 1,486 after; 2 autovacuums in the window |

- So the per-partition beat costs about 161 MB of WAL an hour on an idle four-node cluster at 1,024, and autovacuum
  keeps the 1,024-row table small (385 kB of heap).
- The cost grows with the partitions, which is the whole count however many nodes hold them. It does not grow with
  the nodes.
- A node-row beat writes about 145 bytes per heartbeat, so a node-level beat at the same cadence would be about 175
  bytes a second for the cluster, roughly 250 times less. It is not built in K1 (D3), and it changes how takeover is
  detected, so it is a decision with this measurement attached.
- The lock records are 41 % of the beat's WAL. Whether a beat can drop its locking read and keep slice 3's lock order
  against overlapping commits is a commit-SQL question for K4.

**Orleans idle polls at 1,024, on a quiet machine** (runtime `cost`, in-memory store, one silo or node holding every
partition, 10 idle seconds). After the set-poll fix above, two runs at load average 3.1 to 3.5 on 15 cores; before it,
three interleaved runs at load 2.6 to 2.8:

| Runtime | Pass calls a second | Worker passes a second | Set polls a second |
|---|---|---|---|
| Orleans, after the fix | 1,536, 1,536 | 107.6, 117.5 | 107.1, 117.0 |
| Orleans, before the fix (each set poll 1,024 scans) | 1,536, 1,536, 1,534 | 58.3, 55.3, 73.2 | 57.8, 54.9, 72.6 |
| built-in, before the fix (after it: 1,434 pass calls, 1.4 passes, 1.0 set polls) | 1,332, 1,420, 1,407 | 1.4, 1.4, 1.4 | 1.0, 1.0, 1.0 |

- **The spread** is 107 to 117 set polls a second for Orleans against 1.0 for the built-in loop. Slice 6 measured 2.0
  at 16 partitions, 2.9 at 64 and 5.8 at 256 on a quiet machine, so it grows faster than linearly with the grains.
- **Why.** Each grain's 1 s tick wakes the silo's worker, and a wake that comes after a pass began costs another poll
  of the whole set. With 1,024 ticks a second spread over the second, most of them land after the last pass. A slower
  pass gathers more ticks into one, which is why the old per-partition poll of the suite's wrappers showed about half.
- **On a SQL store** that is about 110 queries a second per idle silo, each over 1,024 partitions, against one.
- **The bursts were close:** after the fix, 1,024 tokens took 0.10 and 0.12 s on Orleans (two runs) and 0.13 s on
  the built-in runtime (one run), with 27 commits of 37.9 partitions each on both.

**The seam question for the silo-level poll.** A silo-level poll would bring Orleans to one set poll per interval. The
seam promises a wake per held scope (`RuntimeTiming.PollInterval`), so it needs the seam to say that one wake covers a
node's set (slice 6). Nothing was built. The measurement makes it worth deciding before the Orleans adapter goes to
production; see the last list below.

**`RuntimeNode` and the Orleans keeper.** The built-in loop calls a pass of every held scope each round: 1,332 to 1,420
pass calls a second at 1,024, of which 1.4 a second run a worker pass and the rest are answered by the one-pass rule,
with no store call. Orleans' grains make 1,536 pass calls a second. On the quiet machine the idle runtimes kept the
load average under 3. Neither is worth a change at 1,024. The keeper's grain calls (1,024 per silo per `KeepEvery`) were
not counted on their own.

**Not measured:** MongoDB. Its set poll plans as `SORT_MERGE` over one index scan per partition (#4815), so its cost
grows with k; it is left for K4's provider work. Nor the checked fence's variants of the rows above; the PostgreSQL
take and beat statements do not depend on the fence mode.

**For decision** (none blocks K1):
1. **Batched gain and give-up** (cost, built-in runtime): one set take and one set release per balancing round, in
   `DurableHost` and `PartitionRuntime`. It would cut a join at 1,024 from 796 round trips to a few. Recommended for K4
   or a K1 follow-up, measured against this table.
2. **The beat's WAL (D3):** 161 MB an hour idle at 1,024 on PostgreSQL. A node-level beat or a cheaper beat statement.
3. **The silo-level poll (Orleans):** 107 to 117 set polls a second per idle silo at 1,024. A seam decision.

## Placement and rebalance (`sim --placement-check`)

- `VirtualPartitions.Of(name, count)` is the hash of today, so an address embeds its virtual partition as it embeds
  its partition now; 100,000 names spread with min 68 / max 129 per virtual partition at 1,024 (mean 97.7) and
  min 11 / max 41 at 4,096 (mean 24.4).
- Contiguous fair ranges (one range per node, boundaries recomputed from the node count) move 2,560 virtual partitions
  over four joins at 1,024 (10,240 at 4,096): every boundary shifts at every join. Minimal moves (a joiner takes a
  slice from each node) move 2,133 (8,533), the least possible, and leave 7 ranges for 4 nodes. In (i) fragmentation is
  free (rows are per virtual partition; a node's statements can say `Owner = me`); in (ii) each fragment is a fence row
  to take, beat and keep, and a flush of one worker spans one range, so fragments are more workers per node.

## Findings

- **F-A1. Equal on the default fence, unequal on the checked one.** In the batch, both variants are 2 round trips per
  flush at 1, 10 and 100 instances; (ii) sends one statement more. Checked, (i) as built is *k* + 2 round trips against
  3, where *k* is the distinct virtual partitions in the flush (81 to 93 for 100 instances). SQL Server's default is the
  checked fence (E9), so this is the one measured cost that separates them, and it is a commit-SQL question: one
  statement that fences *k* rows and returns their count (round 5 B) removes it.
- **F-A2. (ii) still writes *k* rows per flush.** The position row per virtual partition is forced by deletes
  (invariant 6). "One fence row" saves one token check per flush; the per-virtual-partition writes stay.
- **F-A3. (ii) carries more machinery:** one epoch counter (a statement in every take, split and merge), carve at
  bootstrap and at every contiguous rebalance, split and merge rows, and a range identity (lo) that changes at a merge.
  (i) carries *R* rows per take and per beat (one statement each) and *R* independent epochs in the owner's memory.
- **F-A4. A split in (ii) stales the whole range.** The kept half is refused for any picture from before the split;
  the owner that splits knows and commits on; a flush in flight at the split is refused and resets. (i) has no such
  moment: releasing rows changes nothing for the rest, and a release racing a flush is harmless (the simulation's
  mutation shows it).
- **F-A5. The counts.** 1,024 against 4,096 changes nothing in round trips or statements per flush. It changes the
  rows per take and per beat in (i) (256 against 1,024 at four nodes), the position rows read per take in (ii), the
  distinct virtual partitions a 100-instance flush touches (81 against 93), and the per-virtual-partition fan-out
  that was not measured here: projection cursors and projector rounds are per virtual partition in both variants.
- **F-A6. Both rebalance strategies keep placement stable** (no name changed its virtual partition over 2,000 names
  and four joins at either count); contiguous ranges move about 1.2 times the virtual partitions of minimal moves and
  keep one range per node; minimal moves fragment.
- **F-A7. Takeover is millisecond-level in both** on a local container at *R* up to 1,024, and the 4,096-row-return of
  a (i) take did not stand out from the noise.

## Not done, and unverified

Updated at K1 slice 8. What the round 5 A list said and K1 has since done is stated as done, with where.

- **Done in K1: the Worker owns a set** of the 1,024 virtual partitions, with a fence row, an epoch, a position and a
  journal per partition (slices 1 to 7). The kernel simulation runs set worlds at 1,024 and 4,096 with the pin at 48
  mutations (slice 7b), and the costs are measured in the integrated Worker ("K1 costs" above).
- **Done in K1: SQL Server and MySQL.** Slice 3 ran the lock-order case on PostgreSQL, SQL Server and MySQL in both
  fence modes: locked in ascending partition id, two overlapping range commits complete with no deadlock; locked in
  batch order they deadlock ([experiments/K1.LockOrder](experiments/K1.LockOrder/README.md)). Slice 8 ran the owned-set
  cases with the K1 witness on all three in both modes (9 passed each) and the set costs on both. Provider
  qualification, the deadlock surface under load included, is K4.
- **Measured in K1: the beat's WAL on PostgreSQL** (raised in review): 161 MB an hour on an idle four-node cluster at
  1,024, 96 % of it the `Partitions` rows, 41 % of that lock records; autovacuum keeps up. A node-level beat (D3) is not
  built; it is a decision with that measurement attached.
- **Measured in K1: the batch size on SQL Server and MySQL** (raised in review). The owned commit sends its own commands,
  so a 100-instance flush is one command there (3 round trips on SQL Server, 6 on MySQL with Connector/NET's own trips).
  EF's `SaveChanges` path (`commit=ef`) has `MaxBatchSize(1000)` since slice 3 (D5) and was not measured here.
- **Not done: batched gains and give-ups.** The runtime moves partitions one row at a time (start-up, the balancer's
  moves, a takeover through the runtime, a stand-down through layer 1): about 800 round trips for a join at 1,024 and
  3,100 at 4,096 on PostgreSQL, where the store's set take and set release are 2 and 1.
- **Not done: the silo-level poll** for Orleans (107 to 117 set polls a second per idle silo at 1,024). A seam decision.
- **Not done, K4:** the set poll's epochs as parameters (SQL Server caches an ad hoc plan per take); the PostgreSQL set
  take's lock on rows it does not change; whether the beat can drop its locking read; MongoDB's set poll at 1,024
  (`SORT_MERGE` over k index scans, not measured).
- **Not modelled** in the ownership simulation: a flush that spans two of a node's range fragments in (ii); MongoDB; in
  (i), poison (`MaxAttempts`) and trial groups, and deletes in the projector. The per-node Worker (D1) and its fairness
  are tested in the runtime conformance suite (slice 5b) and the kernel simulation's set worlds (slice 7).
- `tcp` for one 100-instance flush read 3 instead of 2 once in round 5 A (README: the PostgreSQL stream varies by a read
  or two). In K1 slice 8 the turns' commit of a 100-instance flush read 3 in 3 of 20 runs, the same variance
  (FINDINGS F-B6); the marks' commit read 2 in all 20.

## Recommendation, and what the owner decided

**Recommendation: (i), the fence per virtual partition, on the condition that round 5 B gives the checked fence one
statement for *k* rows.** The measured hot path is equal on the default fence; (i) has no split, no merge, no epoch
counter, no range identity, no moment at which a whole range's picture goes stale, rebalances by freeing rows, and
tolerates fragmentation at no cost; its one measured penalty, *k* + 2 round trips with the checked fence, is a
commit-SQL shape that B owns. Its structural costs are *R* rows per take and per beat (one statement each, 1,024 rows
at 4,096 and four nodes) and *R* epochs in the owner's memory. (ii) buys one fence row per flush and one row per beat
at the price of F-A3 and F-A4 and does not escape *k* rows per flush (F-A2).

Trade-offs the owner weighs:

- **(i)** more rows per take and beat; the per-virtual-partition epoch everywhere a (partition, epoch) key is used today;
  the checked-fence cost until B lands; a possible deadlock surface on SQL Server with *k* fence rows in one batch (E9,
  unmeasured).
- **(ii)** fewer rows per beat and take; split, merge, carve and an epoch counter; the whole range resets at a split;
  fragmentation is workers and rows; the range identity changes at a merge.

The decisions that were the owner's, with what the numbers said:

1. **Fence granularity:** (i) or (ii). Evidence above; the recommendation was (i).
2. **The count:** 1,024 or 4,096. No per-flush difference; 4,096 is four times the rows per take and beat in (i) and
   four times the projection cursors in both; 1,024 still gives 100 nodes ten virtual partitions each. The numbers lean
   to 1,024 unless a cluster of several hundred nodes is a goal.
3. **Whether B's scope includes the multi-row checked fence** (if (i)), or whether SQL Server moves to the in-batch
   fence with an ordering fix for E9.
4. **Rebalance strategy:** contiguous ranges (more moves, one range per node) or minimal moves (fewest moves,
   fragments). In (i) this is a balancer policy with no store cost; in (ii) it decides how many fence rows a node holds.
5. **When the worker refactor to range ownership happens:** it is the same work for either variant and larger than
   a round-5 slice; it belongs after this decision and, per the program order, with the design proposal.

## The owner's decisions (Sipke, 16 November 2026)

- **Fence granularity: (i), one fence row per virtual partition,** conditional on round 5 B sending the checked fence
  as one multi-row statement. **That multi-row checked fence is now in B's scope.**
- **Count: 1,024 virtual partitions.** The code kept `VirtualPartitions.Provisional = 4096` as written on 15 November
  (no code changed in this round's documentation fix); K1 slice 7 set it to 1,024.
- Decisions 4 and 5 above (rebalance strategy; when the Worker refactor happens) stayed open on 16 November. The owner
  answered them on 17 November:
  - **Rebalance strategy: fewest moves.** A node's partitions may be scattered, which costs nothing with a fence per
    partition.
  - **The Worker refactor** to range ownership of the 1,024 virtual partitions, with a journal per virtual partition,
    is release 1's first milestone, right after design approval and before product wiring. It is kernel behaviour, so
    it needs two independent reviews.
- **Delivered by round 5 B ([COMMIT-SQL.md](COMMIT-SQL.md)):** the checked fence over k rows is one statement with an
  affected-row check on every engine (`EfStore.FenceAsync`, probe `x fence`), so F-A1's k + 2 round trips become 3; the
  kernel still fences one row per commit until the Worker refactor.
