# Write volume and bloat on PostgreSQL

Design-now question 7 of the sketch (`docs/reports/clean-slate-kernel/design-sketch-draft-5.md`, "Open questions"). A
commit rewrites the instance's whole row, appends a journal row (at Full history), and inserts, then updates, then
deletes inbox rows. The owner's direction of 12 November: keep snapshot plus journal as the default, measure bytes per
commit and PostgreSQL bloat, and try the mitigations. This file is that measurement. It changes no part of the kernel:
the tooling is in `ef/Kernel.Ef.Run` (`Writes.cs`, and options in `EngineRun.cs`).

**In short.** At Full history a commit of an instance with a small state writes about 2.1 to 2.2 kB of WAL. About
0.8 kB is the instance row, 0.3 kB the journal row and 0.8 to 1.0 kB the inbox. Dead tuples stay in the low thousands,
and autovacuum clears them at its default one-minute nap. The heaps keep 3 to 8 % free space on Instances and 8 to 28 %
on Messages. Write volume does not hurt at small states, so the lazy snapshot is not needed for them. Large states are
where it hurts. A 4 kB state costs 7.1 to 7.5 kB of WAL per commit, because PostgreSQL rewrites all of it through TOAST,
and the TOAST table grows to 2.3 to 2.7 times its live data. That cost is what decision 10 (values by reference) is for.
A fill factor of 70 on Messages helps only bursts. A partitioned inbox does not fit the current store without changing
its keys.

## How it is measured

- **WAL bytes** are what the database writes durably for a run: `pg_current_wal_lsn()` before and after. The heap
  pages that checkpoints write later follow from the same changes. The WAL is split by relation and record type with
  `pg_walinspect` (`pg_get_wal_block_info`). A record counts once, on the relation of its first block. "Other" is WAL
  that names no relation of the run's database: commit records, and any other database's background work, such as
  autovacuum of an earlier run's database. The per-commit numbers below count only the run's own relations.
- **Tuple counts, dead tuples, vacuums and sizes** come from `pg_stat_user_tables`, `pg_relation_size`,
  `pg_indexes_size` and the TOAST table's size. They are sampled every second during the stress run, at the end, and
  again after a 70-second wait (autovacuum naps for one minute by default). The stress test kills about 2,000 to 4,000
  worker sessions, and a killed backend loses the counts it had not yet reported. So `n_tup_*` undercount in the stress
  runs, and the WAL is the number to trust. The cost runs kill nothing, so their counts are exact.
- **Exact dead and free space** comes from `pgstattuple`, at the end and after the wait.
- **History levels and a large state** come from a test actor type (`Leveled` in `Writes.cs`) placed over
  `WorkflowInstance`. It writes each row at the chosen level, with the journal row the history envelope gives at that
  level. With `pad=n` it adds n bytes of random base64 to the state as a property no model reads. The models therefore
  drop the property, and it is added again at each commit, so the state is rewritten whole each time, as a large
  variable would be. Random base64 does not compress, so TOAST compression cannot hide it (4,182 bytes of text are
  stored as 4,169).
- **Per instance commit** means per turn of a landed batch that wrote or deleted a row. A stress commit carries 1.4 to
  2.6 of them, depending on how the passes happened to batch.
- **Noise.** The stress runs vary by about ±10 % between runs of the same settings: batching and checkpoint timing
  differ, and six other database containers from other sessions shared the machine. Differences smaller than that are
  not resolved. The cost runs are deterministic: a repeat gave 1,800,800 and 1,800,824 bytes.

Commands, against PostgreSQL 17.11 in Docker, run one at a time (the stress test kills worker sessions on the whole
server):

```
docker run -d --rm -P -e POSTGRES_PASSWORD=kernel --name km-writes-pg-4229 postgres:17 -c wal_keep_size=4GB
docker port km-writes-pg-4229 5432
PG="Host=localhost;Port=<port>;Username=postgres;Password=kernel"
EFR="dotnet ef/Kernel.Ef.Run/bin/Release/net10.0/Kernel.Ef.Run.dll"
$EFR x stress postgres "$PG;Database=kernel_wstress" seconds=40 messages=5000 writes=1 [history=state|minimal] [pad=4096] [fillfactor=70] [fastvacuum=1]
$EFR x cost postgres "$PG;Database=kernel_wcost" writes=1 [history=state|minimal] [pad=4096] [fillfactor=70|90]
```

`wal_keep_size` keeps the run's WAL so that it can be read back. It does not change checkpoints. All other server
settings are the defaults: `autovacuum_naptime` 1 min, `max_wal_size` 1 GB, `checkpoint_timeout` 5 min,
`full_page_writes` on. `fillfactor=n` and `fastvacuum=1` (per-table `autovacuum_vacuum_scale_factor = 0`, threshold
500, insert threshold 2,000, no cost delay) are set on Instances and Messages while those tables are still empty.

## The cost run: bytes per commit, deterministic

WAL bytes per instance commit, and the split by table for the largest rows:

| run | Full | State | Minimal | 4 kB pad, Full | 4 kB pad, Minimal | fill factor 70 |
|---|---:|---:|---:|---:|---:|---:|
| ten no-ops, durable, started on the owner (10 commits) | 904 | 904 | 640 | 5,652 | 5,380 | 912 |
| 1,000 balanced instances started at once | 708 | 579 | 292 | 5,262 | 4,831 | 708 |
| 1,000 balanced instances, their starts arriving by poll | 1,800 | 1,677 | 1,378 | 6,340 | 5,917 | 1,325 |

- **Instance row.** An insert of a small instance row writes 286 B. With the 4 kB pad it writes 4,807 B: the TOAST
  chunks, their index entries and the heap row. An update of a small row is a HOT update of about 150 B (durable no-ops:
  1,566 B for one insert and nine updates).
- **Journal row.** At Full a row writes 414 B: the heap row and two index entries (`PK (Partition, Position)` and
  `(Partition, Instance)`). At State an empty `[]` row still writes 281 B, mostly the same two index entries. Minimal
  writes nothing. For the toy models, State saves little over Full because their entries are a few words. A real
  model's entries would make Full larger.
- **Inbox.** A message to self that is inserted and then deleted writes about 355 B (durable no-ops: Messages 3,546 B
  for 10). An outside start writes 1,062 B: insert, marked seen, marked done and kept until `keep_until`. The 1,062 B
  shrinks to 594 B at fill factor 70; see below.
- **Round trips.** No variant adds any. State, the pad and fill factor give the same counts as Full in every row. At
  1,000 instances Minimal saves a command, because fewer statements fit in fewer batches. The projector round has
  nothing to read at Minimal (5 commands instead of 17).

## The stress run: 40 seconds of chaos, then settling

3 nodes, 2 partitions, 5,000 outside messages, sessions killed throughout, fence checked; about 11,000 instance
commits and 4,000 instances per run. Every run ended with "all properties hold". WAL per instance commit, counting the
run's own relations only:

| run | total | Instances | Journal | Messages | Partitions | HOT % Inst. / Msg. | dead tuples, peak Inst. / Msg. | autovacuums Inst. / Msg. |
|---|---:|---:|---:|---:|---:|---|---|---|
| Full (three runs) | 2,064 to 2,217 | 755 to 827 | 297 to 326 | 835 to 1,032 | 47 to 66 | 94 to 96 / 91 to 94 | 494 to 910 / 3,398 to 6,444 | 2 / 3 to 5 |
| State | 1,997 | 774 | 286 | 822 | 63 | 96 / 94 | 777 / 8,068 | 2 / 3 |
| Minimal | 1,709 | 768 | 16 | 815 | 59 | 96 / 94 | 944 / 7,767 | 1 / 3 |
| 4 kB pad (three runs) | 7,087 to 7,474 | 5,820 to 6,061 | 299 to 320 | 852 to 991 | 47 to 59 | 97 to 98 / 93 to 94 | 351 to 496 / 5,600 to 6,561 | 2 / 3 to 4 |
| fill factor 70 | 2,214 | 674 | 343 | 1,077 | 56 | 100 / 100 | 678 / 4,382 | 3 / 4 |
| fast vacuum | 2,404 | 714 | 349 | 1,220 | 56 | 90 / 92 | 591 / 5,220 | 4 / 4 |
| 4 kB pad, fill factor 70 | 7,591 | 6,168 | 330 | 985 | 48 | 100 / 100 | 879 / 5,493 | 3 / 4 |

(Journal at Minimal: the journal rows of deletes and dropped starts, which every level keeps.)

Where the bytes go in a Full run (the third one; record types from `pg_walinspect`):

- **Instances**, 755 B per instance commit: 307 heap, 39 index, 409 TOAST. Nearly all the TOAST is six counter
  instances whose token list grows with every message to about 5 kB (the largest state is 5,295 B stored, the average
  65 B). Those six instances out of 4,000 write a fifth of the run's WAL: the growing-state pattern of an agent's
  thread, at small scale.
- **Messages**, 1,032 B per instance commit: inserts into the heap and into two indexes (`PK (Id)`,
  `(Partition, Seq)`), then HOT updates (seen, trial, done), deletes, and page pruning. The inbox is the largest share
  at small states. It is not the journal.
- **Journal**, 326 B per instance commit: 131 heap, 195 index. The indexes cost more than the rows.
- **Partitions**, 47 to 66 B: the fence, a HOT update of one row per commit.

## Bloat

- **Heaps stay small and are vacuumed.** At the end of a Full run, `pgstattuple` gives Instances 121 dead tuples and
  3.0 % free space in 672 kB, and Messages 749 dead and 21 % free in 2.2 MB (it holds 6,529 kept outside messages).
  After the wait, Messages has 0 dead and Instances 59. Across all runs, dead tuples peak at about 1,000 on Instances
  and Journal and about 8,000 on Messages. They fall back each time autovacuum runs: the timeline in each log shows the
  saw tooth at the one-minute nap.
- **HOT updates already happen under trickle load.** With the default fill factor of 100, 94 to 96 % of instance
  updates and 91 to 94 % of message updates are HOT. PostgreSQL's page pruning makes room on the page, so updates
  stay there.
- **Large states bloat TOAST.** With the 4 kB pad, the Instances TOAST table reaches 39 to 46 MB for about 17 MB of
  live chunks. In the third run, `pgstattuple` gives 3,007 dead chunks (4.7 MB) and 44 % free at the end, and 0 dead
  and 56 % free after the wait. In the second, after a manual `VACUUM`, it is 0 % dead and 62 % free. The space is
  reused but not returned. Each commit inserts the state's chunks again (three chunks for 4 kB) and
  deletes the old ones, about 25,700 chunk deletes in a run. Between vacuums the TOAST table has to hold the dead
  chunks of a whole nap. Its steady size is roughly the live state plus (instance commits per second × state size ×
  time between vacuums). At 1,000 commits a second of 4 kB states and a one-minute nap, that is about 240 MB of
  churn space.
- **The journal does not bloat.** It is insert-only, and rows go only when an instance is deleted.

At a sustained 1,000 instance commits per second these numbers mean about 2.2 MB/s of WAL with small states and
7.1 to 7.5 MB/s with 4 kB states. PostgreSQL handles that on ordinary hardware. What grows with state size is the TOAST churn,
the WAL with it and the replication traffic behind it.

## The mitigations

| mitigation | measured effect | cost | round trips |
|---|---|---|---|
| **Fill factor 70 on Instances and Messages** | Under trickle load: HOT 100 % (was 94 to 96 %), but WAL per commit is the same within noise (2,214 B against 2,064 to 2,217 B). In a burst: when 1,000 outside starts arrive by poll, HOT updates of Messages rise from 42 of 2,212 to 1,241 of 2,212, and Messages WAL falls from 1,062 kB to 594 kB, so the row costs 26 to 28 % less (1,800 B per instance commit against 1,295 and 1,325 B in two runs). Fill factor 90: 447 HOT, 1,630 B. | 30 to 40 % more heap: `pgstattuple` gives 34 % free on Instances and 40 % on Messages at the end of the run. | none (DDL at creation) |
| **Faster autovacuum per table** (scale factor 0, threshold 500, no cost delay) | Autovacuum runs 4 times per table instead of 2 to 5. Peak dead tuples stay in the baseline's range (591 / 5,220): `autovacuum_naptime`, a server setting, bounds how often a table is looked at, so per-table thresholds do little below it. | none | none |
| **History State or Minimal** | Saves the journal row: about 130 B (State) or 415 B (Minimal) per instance commit in the cost run, and the journal's 300 B at Minimal in the stress run. It is the owner's per-workflow choice, not a store setting. | No history for that workflow (Minimal) or no entries (State). | none; Minimal saves one command per 1,000-instance flush |
| **Partitioned inbox** | Not measured: the current store cannot use it. On a partitioned Messages table, `AddAsync`'s `ON CONFLICT ("Id") DO NOTHING` fails ("there is no unique or exclusion constraint matching the ON CONFLICT specification"), and PostgreSQL refuses a unique index on `Id` alone ("unique constraint on partitioned table must include all partitioning columns"); probed on PostgreSQL 17. | A store change: the message key, the add's conflict target and the delete of consumed rows gain `Partition`. A message id would then be unique per partition, not across the store: a narrower guarantee, and an owner decision. It would not cut WAL (the same inserts, updates and deletes), only split vacuum work into smaller tables, and the inbox heap is already small and vacuumed. | none in principle (the same statements) |
| **Values by reference** (decision 10) | Not measured: not built yet. No branch had it when this was written. The pad runs give its ceiling. A 4 kB value rewritten at every commit costs about 5 kB of WAL per commit (7,087 to 7,474 B against 2,064 to 2,217 B) and the TOAST churn above. A reference written once removes that for every commit that does not change the value. | A table of values, their lifetime, and a read on load. | commit: none if the value rides in the same batch; load: none if read with the row, one more otherwise |

## Conclusions

1. **Keep snapshot plus journal.** At small states a commit writes about 2.1 to 2.2 kB of WAL, and bloat stays small
   and is vacuumed. Nothing here calls for append-only or event-sourced storage.
2. **The inbox is the largest share at small states, not the journal.** Per instance commit the inbox writes 0.8 to
   1.0 kB, the instance row 0.75 to 0.83 kB and the journal 0.3 kB. Of the journal's cost, its two indexes are 60 %.
3. **Large states are the cost.** PostgreSQL rewrites a state of more than about 2 kB through TOAST at every commit,
   and the TOAST table bloats to 2.3 to 2.7 times its live data. Values by reference (decision 10) is the fix for large
   values that do not change every turn. It should be measured with this tooling once it lands (`pad=` against the
   reference).
4. **Fill factor:** 90 or lower on Messages pays off when work arrives in bursts. It buys nothing under trickle load
   and costs heap space. On Instances it buys nothing measurable. Neither is a kernel default; it is a deployment note
   for the EF store's PostgreSQL schema.
5. **Vacuum:** the default autovacuum keeps up at these rates. Busy deployments should lower `autovacuum_naptime`
   rather than per-table thresholds.

## The lazy snapshot: a sketch, not built

The numbers do not ask for it at small states. It would pay only for large states that change at every turn, where a
reference does not help. If that case comes, this would be the spike:

- A commit appends the state change to the journal, as a delta or the whole value of the parts that changed. The
  instance row is rewritten only every N commits, or when the delta chain passes a size. The row carries the
  revision of its snapshot, and the journal rows after it are the tail.
- A load reads the row and its tail in one statement (a join, or two statements in one command), so it adds no round
  trip. A turn replays the tail before it runs: CPU, not database calls.
- The fence, the position and the commit protocol are unchanged. The journal row the commit already writes carries
  the delta.
- What it costs: an instance with a lazy snapshot cannot use Minimal history, because the tail is its state. Deleting
  journal rows must stop at the last snapshot. The projector and the viewer must tell delta rows from entries. Every
  execution model needs a delta format, or a whole-value fallback, which saves nothing.
- What it would save: the row rewrite, which is 0.3 kB of heap for a small state and about 5.8 kB of TOAST for 4 kB.
  The delta must be much smaller than the state for it to pay. For the toy models it is not.

## Raw logs

The full output of every run quoted here (stress: `full`, `full2`, `full3`, `state`, `minimal`, `pad4k`, `pad4k2`,
`pad4k3`, `ff70`, `fastvac`, `pad4k-ff70`; cost: Full, State, Minimal, pad, pad with Minimal, fill factor 70 and 90)
was written to `.out/writes/` (not committed: `.out/` is ignored) and kept with round 4's session notes on the machine that ran them (`writes-logs/`). The commands above reproduce them. Each stress log has the per-table and
per-record-type split, the tuple and size table before, at the end and after the wait, `pgstattuple`, and the
dead-tuple timeline.
