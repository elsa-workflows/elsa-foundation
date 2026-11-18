# Virtual partitions: per-virtual-partition fence against per-range fence (round 5 A, 16 November 2026)

Status: **measured; decided by the owner on 16 November 2026** (see "The owner's decisions" at the end). The owner's experiment of 15 November (sketch draft 5, decision 4): 1,024 and 4,096
fixed virtual partitions (4,096 provisional), stable placement, no rehash, a journal per virtual partition, nodes owning
ranges, and the fence row either **(i) per virtual partition** or **(ii) per owned range**. This records what each
costs and what each means, on SQLite and PostgreSQL, with a seeded model of the ownership protocol at both counts.
**The fence granularity is the owner's decision**; the last section says what the numbers support and what is open.

What was run, and where the code is:

- `src/Kernel.Runtime/VirtualPartitions.cs`: addressing and placement (`VirtualPartitions.Of`, the same hash as
  today's `InstanceRef.Spread`; `VpRange`; `RangeMap` with split, merge, fair ranges and two rebalance strategies).
  `sim --placement-check` is its witness. Nothing in the kernel uses it yet: the built-in runtime still hosts 64
  partitions, one worker each, and every gate of the kernel runs unchanged.
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
| `ResetAll`: a refused flush resets by a take | seed 1 at both counts | W4, as a cost. It is safe and keeps service. It bumps the kept partitions' epochs and re-polls them. |
| `ResetPollsSeenBelow`: the poll after a keeping reset reads `Seen < epoch` | seed 2 at both counts | S1 (stranded) |
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
exactly what the Worker now does by design. Slice 7 aligns the model's reset and W4 with the Worker.

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

- **The kernel's `Worker` was not changed.** It owns one partition, one position and one fence; range ownership with
  per-virtual-partition positions is a refactor of `Worker`, both stores, the projector (per virtual partition), the
  balancer, the runtime conformance suite and the Orleans adapter, and it is the same refactor for either variant but
  for the fence rows. The kernel simulation at both sizes and its mutation pins (43 and 9) ran unchanged on 64
  partitions; they do not exercise range ownership. The ownership simulation above models the protocol; the worker itself is unmodelled.
- SQL Server and MySQL were not run (the checked-fence count above is PostgreSQL's); the SQL Server deadlock surface of
  *k* fence rows in one batch (E9) is an unmeasured risk for (i) in the batch there.
- Not modelled: a flush that spans two of a node's range fragments in (ii); MongoDB; in (i), the per-node Worker (D1)
  itself and its fairness (slice 5), poison (`MaxAttempts`) and trial groups, and deletes in the projector.
- `tcp` for one 100-instance flush read 3 instead of 2 once (README: the PostgreSQL stream varies by a read or two).
- **Unverified (raised in review):** on PostgreSQL every row update is a new tuple version, so (i)'s beat of *R* rows
  every few seconds per node is steady WAL and vacuum churn even when the cluster is idle (1,024 rows per beat at
  4,096 and four nodes); (ii) renews one row. A node-level lease row for the beat, as today's `Nodes.SeenAt`, with the
  per-virtual-partition rows left to the fence, is an option for (i). Not measured (WRITES.md's method would measure it).
- **Unverified (raised in review):** EF's SQL Server and MySQL providers put at most 42 statements in one command by
  default (ENGINES.md E7; the cost runs and the conformance suite set `MaxBatchSize(1000)`, the experiment's context does
  not), so a 100-instance flush there is about ten round trips in either variant at the default, and an in-batch fence
  rides only in the first command. Not measured.

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
- **Count: 1,024 virtual partitions.** The code keeps `VirtualPartitions.Provisional = 4096` as written on 15 November
  (no code changed in this round's documentation fix); the Worker refactor to range ownership will use 1,024.
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
