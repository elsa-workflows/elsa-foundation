# Virtual partitions: the two fence granularities, measured (round 5 A)

The owner's experiment of 15 November (kernel sketch draft 5, decision 4): 1,024 and 4,096 fixed virtual partitions
(4,096 provisional), stable placement, no rehash, a journal per virtual partition, nodes owning ranges; and two places
the fence row can live:

- **(i) per virtual partition:** one fence row per virtual partition (the `Partitions` table of today, 1,024 or 4,096
  rows). A flush that touches *k* virtual partitions fences *k* rows. A node's range is a grouping of its rows: a take, a
  beat or a release is one statement over `Id BETWEEN lo AND hi`; nothing is ever split or merged.
- **(ii) per owned range:** one fence row per range (`Ranges`: lo, hi, owner, epoch, lease), a position row per virtual
  partition (`VpPositions`), and one epoch counter for all ranges, so `(lo, epoch)` never repeats after a split and a
  merge. A flush fences the one range row (its epoch and its bound `Hi`, so a split is noticed) and moves *k* position
  rows. A rebalance splits the owner's row (the kept part gets a new epoch and bound, the freed part a new row) and the
  taker merges adjacent rows it holds.

Both keep the kernel's rules: a fence that does not match answers "not landed" and the batch is rolled back whole; a
stale owner's commit after a takeover is refused; positions are gapless per virtual partition and move only with a
commit; placement is `VirtualPartitions.Of(name, count)` from `src/Kernel.Runtime/VirtualPartitions.cs`, the same hash
as today, fixed when the database is created.

This is experiment code: it changes no default. The kernel still hosts 64 partitions with one worker each; the two
variants exist here as EF stores (`Variants.cs`, the same entity classes, keys and concurrency tokens as `EfStore`,
the fence in the batch or checked as its own statement, takes as conditional statements with RETURNING, as
the take is today; carve, split and merge as conditional UPDATE, INSERT and DELETE statements checked by their
affected-row counts) with a harness (`Measure.cs`) and a seeded model of the ownership protocol (`OwnershipSim.cs`).
Nothing here is hand-tuned commit SQL: that is round 5 B. The results and the recommendation are in
[../../PARTITIONS.md](../../PARTITIONS.md).

## Run

```
dotnet build experiments/VirtualPartitions -c Release
VP="dotnet experiments/VirtualPartitions/bin/Release/net10.0/Kernel.VirtualPartitions.dll"
$VP measure sqlite                                       # both counts, both fence modes, both variants; files under .out/
$VP measure postgres "$PG" proxy=1                       # the same on PostgreSQL, round trips counted on the TCP stream
$VP measure postgres "$PG" count=4096 nodes=16 fence=batch variant=range proxy=1
$VP sim [seeds=2000] [actions=300] [nodes=4] [count=1024,4096]   # the ownership protocol under a seed, both variants
$VP mutations [seeds=300]                                # each rule of the protocol switched off: which seed catches it
```

`$PG` is a PostgreSQL connection without a database (README, Databases); the run creates `kernel_vp_*` databases.

## What a measurement run does

Four nodes share the count in fair ranges (256 or 1,024 virtual partitions each). Node B takes its range and flushes
once, uncounted, to warm up. Then, counted row by row (commands, BEGINs, COMMITs, the round trips as FINDINGS section 5
counts them, the TCP round trips and the Execute frames on PostgreSQL, the rows a statement returned, the time):

1. node A takes a free range ((ii): after carving it out of the fresh database's one free row);
2. A flushes 1, 10 and 100 new instances spread over its range by the placement hash, each with its start message
   consumed and a journal row; then the same instances again as updates;
3. A beats;
4. A's lease is let run out and C takes the range; A's commit must be refused; C's first flush lands at the inherited
   positions;
5. C gives the upper half of its range to B: release ((ii): split), B takes it, B merges it into its range ((ii): the
   rows; (i): nothing to write). A picture of C from before the move commits in the moved half (must be refused) and
   in the kept half: (i) lands, the kept rows did not change; (ii) is refused, the split changed the range row. C's own
   next flush lands without a reset in both; B's flush over the merged range lands;
6. five takeovers of a dead owner's range, for the time;
7. the journal is read back: every virtual partition's positions are 1..n with no gap and its position row says n.

## What the ownership simulation does

Nodes take ranges, commit over the virtual partitions they believe they hold (1, 3 or 10 at a time), beat, rebalance
toward the fair share of the live nodes (both strategies of `RangeMap`: contiguous moves and minimal moves), lose commit
answers (a doubt is a reset, as in the kernel), crash and restart, while leases run out and time passes. After every
action: no commit landed under a stale epoch or bound, no two rows at one position of a virtual partition, every
virtual partition's journal is gapless, and in (ii) the range rows cover without overlap. Then the system settles and
every node must be able to commit on everything it holds. `mutations` switches one rule off at a time.
