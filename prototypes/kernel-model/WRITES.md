# Write volume and bloat on PostgreSQL

Design-now question 7 of the sketch (`docs/reports/clean-slate-kernel/design-sketch-draft-5.md`, "Open questions"). A
commit rewrites the instance's whole row, appends a journal row (at Full history), and inserts, then updates, then
deletes inbox rows. The owner's direction of 12 November: keep snapshot plus journal as the default, measure bytes per
commit and PostgreSQL bloat, and try the mitigations. This file is that measurement. It changes no part of the kernel:
the tooling is in `ef/Kernel.Ef.Run` (`Writes.cs`, and options in `EngineRun.cs`).

**In short.** At Full history and small states, a clean commit (the cost run) writes 0.7 kB of WAL per instance
commit for a start that ends in its first turn, 0.9 kB for a turn woken by a message to self, and 2.3 kB for an
outside start, once its insert into the inbox is counted. Under the stress test's chaos, a landed instance commit
costs about 2.1 kB: 0.93 to 1.08 kB inbox, 0.64 to 0.71 kB instance row (more than half of it a few growing states),
0.29 kB journal, and about 0.1 kB of commit records. Rolled-back attempts add another 8 to 12 % on top, counted
apart. Dead tuples peak at about 1,100 on Instances and 6,600 on Messages. Autovacuum clears them at its default
one-minute nap, once they pass its threshold. The heaps keep 5 to 15 % free space on Instances and 13 to 30 % on
Messages. Write volume does not hurt at small states, so the lazy snapshot is not needed for them. Large states are
where it hurts. An incompressible 4 kB state costs about 6.9 kB of WAL per landed commit, because PostgreSQL rewrites
all of it through TOAST, and the TOAST table grows to 2.1 to 2.3 times its live data. A state that compresses costs
less, and below about 2 kB compressed it stays in the row. That cost is what decision 10 (values by reference) is for.
A fill factor of 70 on Messages helps only bursts. A partitioned inbox split by the kernel's partition does not fit
the current store without changing its keys.

## How it is measured

- **WAL bytes** are what the database writes durably for a run: `pg_current_wal_lsn()` before and after. The heap
  pages that checkpoints write later follow from the same changes. The WAL is split by relation and record type with
  `pg_walinspect` (`pg_get_wal_block_info`). A record counts once, on the relation of its first block. "Other" is WAL
  that names no relation of the run's database: commit records, and any other database's background work, such as
  autovacuum of an earlier run's database. WAL of a transaction that aborted (it has an abort record in the range: a
  fenced commit rolled back, a session killed mid-commit) is counted apart. The per-commit numbers below count all WAL but that of
  transactions that aborted; the per-table columns count the run's own relations. In the stress logs of the second
  set, the sequence behind Messages' `Seq` (about 4 B per instance commit) is labelled "(catalog) index"; the tool
  now counts it with Messages.
- **Tuple counts, dead tuples, vacuums and sizes** come from `pg_stat_user_tables`, `pg_relation_size`,
  `pg_indexes_size` and the TOAST table's size. They are sampled every second during the stress run, at the end, and
  again after a 70-second wait (autovacuum naps for one minute by default). The stress test kills about 2,100 to 3,000
  worker sessions. `n_tup_*` also count the rows of transactions that rolled back, so in the stress runs they
  overcount what landed: Instances' inserts exceed the rows that ever existed (live plus deleted) by 6 to 14 %. The WAL split by abort is
  the number to trust there. The cost runs kill and fence nothing. Before the end snapshot the run closes its pools,
  because a session reports its counts when it ends.
- **Exact dead and free space** comes from `pgstattuple`, at the end and after the wait.
- **History levels and a large state** come from a test actor type (`Leveled` in `Writes.cs`) placed over
  `WorkflowInstance`. It writes each row at the chosen level, with the journal row the history envelope gives at that
  level. With `pad=n` it adds n bytes of random base64 to the state as a property no model reads. The models therefore
  drop the property, and it is added again at each commit, so the state is rewritten whole each time, as a large
  variable would be. Random base64 does not compress, so TOAST compression cannot hide it (4,182 bytes of text are
  stored as 4,169).
- **Per instance commit** means per turn of a landed batch that wrote or deleted a row. A stress commit carries 2.25 to
  2.54 of them, depending on how the passes happened to batch. Aborted work is left out, as said above. What stays in
  is committed work that the chaos causes: takes, redelivery marks after takeovers, and polls that marked messages
  seen for an owner that then lost the partition. So the stress figures are a chaos run's cost per landed commit, and
  the cost run gives a clean commit's. One bias remains: a commit that landed but whose session was killed before the
  answer (685 to 775 per run, "unknown" in the summary) is counted in the WAL but not in the landed commits, so the
  per-commit figures run a few percent high.
- **Noise.** Batching, kills and checkpoint timing differ between stress runs, and six other database containers
  from other sessions shared the machine. With aborted work left out, two runs at the same settings agree within 2 %
  (Full 2,125 and 2,144 B; pad 6,913 and 6,931 B). Differences between variants of less than about 5 % are not
  resolved. The cost runs are deterministic: repeats gave 1,800,800 and 1,800,824 bytes. HOT counts vary a little between
  repeats of a cost run (see the fill factor row below).

Commands, against PostgreSQL 17.11 in Docker, run one at a time (the stress test kills worker sessions on the whole
server):

```
docker run -d --rm -P -e POSTGRES_PASSWORD=kernel --name km-writes-pg-4229 postgres:17 -c wal_keep_size=4GB
docker port km-writes-pg-4229 5432
PG="Host=localhost;Port=<port>;Username=postgres;Password=kernel"
EFR="dotnet ef/Kernel.Ef.Run/bin/Release/net10.0/Kernel.Ef.Run.dll"
$EFR x stress postgres "$PG;Database=kernel_wstress" seconds=40 messages=5000 writes=1 [history=state|minimal] [pad=4096] [fillfactor=70] [fastvacuum=1]
$EFR x cost postgres "$PG;Database=kernel_wcost" writes=1 [history=state|minimal] [pad=4096] [fillfactor=70|90]
# r2-cost-hot.log: after each cost run (fillfactor=100, 90, 70), a manual query of the whole run's counts
docker exec km-writes-pg-4229 psql -U postgres -d kernel_wcost -tAc "select relname, n_tup_ins, n_tup_upd, n_tup_hot_upd, n_tup_del from pg_stat_user_tables where relname in ('Messages','Instances')"
```

`wal_keep_size` keeps the run's WAL so that it can be read back. It does not change checkpoints. All other server
settings are the defaults: `autovacuum_naptime` 1 min, `max_wal_size` 1 GB, `checkpoint_timeout` 5 min,
`full_page_writes` on. `fillfactor=n` and `fastvacuum=1` (per-table `autovacuum_vacuum_scale_factor = 0`, threshold
500, insert threshold 2,000, no cost delay) are set on Instances and Messages while those tables are still empty.

## The cost run: bytes per commit, deterministic

Total WAL per instance commit. A row's setup (`before` in `EngineRun.cs`, such as the outside inserts that the
"by poll" row then polls) is measured separately and given as its own row:

| run | Full | State | Minimal | 4 kB pad, Full | 4 kB pad, Minimal | fill factor 70 |
|---|---:|---:|---:|---:|---:|---:|
| ten no-ops, durable, started on the owner (10 commits) | 912 | 904 | 648 | 5,650 | 5,388 | 904 |
| a start subscription, then an event that starts an instance (1 commit) | 2,256 | 2,256 | 2,000 | 6,808 | 6,552 | 2,256 |
| 1,000 balanced instances started at once | 708 | 579 | 292 | 5,247 | 4,832 | 708 |
| 1,000 balanced instances, their starts arriving by poll: the commits | 1,800 | 1,666 | 1,373 | 6,340 | 5,918 | 1,325 |
| the same row's setup, per start: the 1,000 outside inserts | 539 | 589 | 576 | 546 | 539 | 539 |

The setup's Messages share is 497 to 510 B per start. The rest is the commit records and, in some columns, up to
50 B of other background work on the database (statistics, pruning of earlier rows).

- **Instance row.** An insert of a small instance row writes 286 B. With the 4 kB pad it writes 4,807 B: the TOAST
  chunks, their index entries and the heap row. An update of a small row is a HOT update of about 150 B (durable no-ops:
  1,566 B for one insert and nine updates).
- **Journal row.** At Full a row writes 414 B: the heap row and two index entries (`PK (Partition, Position)` and
  `(Partition, Instance)`). At State an empty `[]` row still writes 281 B, mostly the same two index entries. Minimal
  writes nothing. For the toy models, State saves little over Full because their entries are a few words. A real
  model's entries would make Full larger.
- **Inbox.** A message to self, inserted and then deleted, writes about 365 B (durable no-ops: Messages 3,645 B for
  10). An outside start writes about 1.56 kB in all. The insert is 497 B. Then it is marked seen, then marked done,
  and it is kept until `keep_until`, which comes to 1,062 B in the commits. At fill factor 70 those updates write
  594 B; see below.
- **Round trips.** No variant adds any. State, the pad and fill factor give the same counts as Full in every row. At
  1,000 instances Minimal saves a command, because fewer statements fit in fewer batches. The projector round has
  nothing to read at Minimal (5 commands instead of 17).

## The stress run: a 40-second producer window, chaos until it ends, then settling

3 nodes and 2 partitions, fence checked. 5,000 outside messages, paced over a 40-second producer window. On this loaded
machine the producer ran 117 to 190 s, and the chaos lasted as long. Sessions were killed throughout, 2,084 to 3,001
per run. Each run had about 11,000 instance commits and 4,000 instances, so about 70 instance commits a second. Every
run ended with "all properties hold". At State and Minimal that check proves less: the journal checks I2 (one journal
row per revision) and I6 (rows against the position) have few or no rows to compare.

The table gives WAL per landed instance commit. "Total" is all WAL but that of transactions that aborted. The
per-table columns are the run's own relations, and "other" is commit records and the like. "Aborted" is the WAL of
transactions that rolled back, as a share on top of the run's relation WAL:

| run | total | Instances (of which TOAST) | Journal | Messages | Partitions | other | aborted | HOT % Inst. / Msg. | dead tuples, peak Inst. / Msg. | autovacuums Inst. / Msg. |
|---|---:|---:|---:|---:|---:|---:|---:|---|---|---|
| Full (two runs) | 2,125 to 2,144 | 663 to 681 (368 to 379) | 288 to 289 | 959 to 1,015 | 40 to 42 | 84 to 100 | 8.2 to 9.6 % | 94 / 92 to 93 | 577 to 723 / 4,946 to 5,397 | 2 / 3 |
| State | 2,112 | 710 (401) | 281 | 938 | 42 | 84 | 8.3 % | 93 / 93 | 1,099 / 6,552 | 2 / 3 |
| Minimal | 1,753 | 733 (417) | 16 | 843 | 40 | 72 | 5.5 % | 96 / 95 | 896 / 5,867 | 2 / 3 |
| 4 kB pad (two runs) | 6,913 to 6,931 | 5,408 to 5,461 (5,204 to 5,255) | 287 to 288 | 952 to 1,015 | 41 to 45 | 117 to 121 | 10.4 to 11.8 % | 96 to 97 / 92 to 94 | 459 to 504 / 5,303 to 5,374 | 2 / 3 to 4 |
| fill factor 70 | 2,049 | 642 (359) | 291 | 931 | 43 | 85 | 11.8 % | 100 / 99 | 836 / 5,248 | 3 / 4 |
| fast vacuum | 2,213 | 650 (363) | 290 | 1,079 | 42 | 89 | 8.5 % | 94 / 91 | 605 / 5,200 | 2 / 4 |

(Journal at Minimal: the journal rows of deletes and dropped starts, which every level keeps. State costs as much as
Full here because the toy models' entries are a word or two; the cost run shows the difference for 1,000 starts.)

Where the bytes go in the first Full run (record types from `pg_walinspect`):

- **Instances**, 681 B per landed instance commit: 267 heap, 35 index, 379 TOAST. The TOAST is six counter instances
  whose token list grows with every message to about 5 kB. At the end the TOAST table holds 18 live chunks (30 kB),
  but writing them took 379 B per instance commit, a sixth of the run's WAL. The largest state is 5,314 B stored, the
  average 65 B. That is the growing-state pattern of an agent's thread, at small scale.
- **Messages**, 959 B: inserts into the heap and into two indexes (`PK (Id)`, `(Partition, Seq)`), then updates
  (26,074 HOT, 2,021 not), deletes, and page pruning. The inbox is the largest share at small states. It is not the
  journal.
- **Journal**, 289 B: 115 heap, 174 index. The indexes cost more than the rows.
- **Partitions**, 40 to 45 B: the fence, a HOT update of one row per commit.
- **Aborted**, 8.2 % on top: the work of fenced commits and of sessions killed mid-commit, which the database writes
  and then throws away.

## Bloat

- **Heaps stay small and are vacuumed.** At the end of a Full run, `pgstattuple` gives these figures:
  - Instances: 184 to 190 dead tuples and 9.3 to 9.6 % free space in 737 kB.
  - Messages: 993 to 1,185 dead and 18 to 19 % free in 2.2 MB. It holds 6,529 kept outside messages.

  After the wait, Messages and Journal have 0 dead and Instances 117 to 131. Across the runs at the default fill
  factor, free space at the end and after the wait is 4.7 to 14.9 % on Instances and 12.7 to 29.9 % on Messages. Dead
  tuples peak at about 1,100 on Instances and Journal and 6,600 on Messages. They fall back each time autovacuum runs:
  the timeline in each log shows the saw tooth at the one-minute nap. Below autovacuum's threshold they stay until
  the next trigger. In the State run, Messages still had 539 dead tuples after the wait, with no further autovacuum.
- **HOT updates already happen under trickle load.** With the default fill factor of 100, 93 to 97 % of instance
  updates and 91 to 95 % of message updates are HOT. PostgreSQL's page pruning makes room on the page, so updates
  stay there.
- **Large states bloat TOAST.** With the 4 kB pad, the Instances TOAST table reaches 36 to 39 MB for 17 MB of live
  chunks (2.1 to 2.3 times). At the end, `pgstattuple` gives 4,759 to 5,919 dead chunks (7.5 to 9.2 MB) and 26 to
  36 % free. After the wait it gives 0 dead and 51 to 55 % free. The space is reused but not returned.
  - Each commit inserts the state's chunks again (three chunks for 4 kB) and deletes the old ones: about 22,600 chunk
    deletes in a run.
  - Between vacuums the TOAST table has to hold the dead chunks of a whole nap. Its steady size is roughly the live
    state plus (instance commits per second × state size × time between vacuums).
  - At 1,000 commits a second of 4 kB states and a one-minute nap, that is about 240 MB of churn space. This is an
    estimate from that formula, not a measurement.
- **The journal does not bloat.** It is insert-only, and rows go only when an instance is deleted.

At a sustained 1,000 instance commits per second, these numbers mean about 2.1 MB/s of WAL with small states and about
6.9 MB/s with incompressible 4 kB states, plus 8 to 12 % for rolled-back attempts under chaos. That should be within
reach of an ordinary PostgreSQL server, but it was not measured here. What grows with state size is the TOAST churn,
the WAL with it and the replication traffic behind it.

## The mitigations

| mitigation | measured effect | cost | round trips |
|---|---|---|---|
| **Fill factor 70 on Instances and Messages** | Under trickle load, HOT updates reach 99 to 100 % (they were 91 to 97 %). WAL per landed commit is 2,049 B against 2,125 to 2,144 B, which is within the spread of the runs. In a burst, when 1,000 outside starts arrive by poll, the commits' Messages WAL falls from 1,062 kB to 564 to 594 kB (44 to 47 % less). With the inserts counted, the row costs 20 % less: 2,339 B per start against 1,864 B. Over the whole cost run, HOT updates of Messages rise from 42 to 51 of 2,212 to 1,175 to 1,241 (`r2-cost-hot.log`). At fill factor 90 there are 434 to 447 HOT updates and the commits cost 1,630 B. | A larger heap. At the end of the stress run, Instances is 1.5 to 1.7 times the baseline's (1.14 MB against 0.69 to 0.75 MB). Messages is 1.6 to 1.7 times (3.56 MB against 2.07 to 2.25 MB). `pgstattuple` gives 35 % and 41 % free. | none (DDL at creation) |
| **Faster autovacuum per table** (scale factor 0, threshold 500, no cost delay) | Autovacuum runs 2 and 4 times instead of 2 and 3. Peak dead tuples stay in the baseline's range (605 / 5,200). `autovacuum_naptime`, a server setting, bounds how often a table is looked at, so per-table thresholds do little below it. | none | none |
| **History State or Minimal** | Saves the journal row. In the cost run that is about 130 B (State) or 415 B (Minimal) per instance commit. In the stress run, the journal's 270 B at Minimal. It is the owner's per-workflow choice, not a store setting. | No history for that workflow (Minimal), or no entries (State). | none; Minimal saves one command per 1,000-instance flush |
| **Partitioned inbox, split by the kernel's partition** | Not measured, because the current store cannot use it. On a Messages table partitioned by `Partition`, `AddAsync`'s `ON CONFLICT ("Id") DO NOTHING` fails with "there is no unique or exclusion constraint matching the ON CONFLICT specification". PostgreSQL also refuses a unique index on `Id` alone: "unique constraint on partitioned table must include all partitioning columns". Both were probed on PostgreSQL 17.11 (`r2-probe-partitioned.log`). Hash partitioning on `Id` would keep the current keys and give smaller vacuum units. Every poll, which filters on `Partition`, would then touch every partition. That was not tested. | A store change: the message key, the add's conflict target and the delete of consumed rows gain `Partition`. A message id would then be unique per partition, not across the store. That is a narrower guarantee, and an owner decision. It would not cut WAL (the same inserts, updates and deletes), only split vacuum work into smaller tables, and the inbox heap is already small and vacuumed. | none in principle (the same statements) |
| **Values by reference** (decision 10) | Not measured, because it is not built yet; no branch had it when this was written. The pad runs give its ceiling. An incompressible 4 kB value rewritten at every commit costs about 4.8 kB of WAL per landed commit (6,913 to 6,931 B against 2,125 to 2,144 B), plus the TOAST churn above. A reference written once removes that for every commit that does not change the value. | A table of values, their lifetime, and a read on load. | commit: none if the value rides in the same batch; load: none if read with the row, one more otherwise |

## Conclusions

1. **Keep snapshot plus journal.** At Full history and small states, a clean commit writes 0.7 to 2.3 kB of WAL per
   instance commit, depending on how its messages arrived (cost run). Under chaos, a landed instance commit costs about
   2.1 kB, plus 8 to 12 % for rolled-back attempts. Bloat stays small and is vacuumed. Nothing here calls for
   append-only or event-sourced storage.
2. **The inbox is the largest share at small states, not the journal.** Per landed instance commit at Full and State,
   the inbox writes 0.93 to 1.08 kB, the instance row 0.64 to 0.71 kB and the journal 0.28 to 0.29 kB. More than half
   of the instance row's share is TOAST of six growing states. Of the journal's cost, its two indexes are 60 %.
3. **Large states are the cost.** PostgreSQL rewrites a state of more than about 2 kB (after compression) through
   TOAST at every commit. For an incompressible 4 kB state, that is about 6.9 kB of WAL per commit, and the TOAST table
   bloats to 2.1 to 2.3 times its live data. Real JSON compresses, so this is the upper end. Values by reference
   (decision 10) is the fix for large values that do not change every turn. It should be measured with this tooling
   once it lands (`pad=` against the reference).
4. **Fill factor:** 90 or lower on Messages pays off when work arrives in bursts. It buys nothing measurable under
   trickle load and costs heap space. On Instances it buys nothing measurable. Neither is a kernel default; it is a
   deployment note for the EF store's PostgreSQL schema.
5. **Vacuum:** judged by `pgstattuple`, the default autovacuum kept up at these rates. The tuple counters behind
   autovacuum's triggers count rolled-back rows too, so under chaos they trigger it earlier, if anything. Per-table
   thresholds changed nothing measurable. Lowering `autovacuum_naptime` is the likely lever for a busy deployment, but
   it was not measured.

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
- What it would save: the row rewrite, which is 0.3 kB of heap for a small state and about 5.2 kB of TOAST for an incompressible 4 kB.
  The delta must be much smaller than the state for it to pay. For the toy models it is not.

## Raw logs

The stress numbers above come from the second set of runs: `r2-stress-full`, `full-b`, `state`, `minimal`, `pad4k`,
`pad4k-b`, `ff70` and `fastvac`. Their split leaves aborted transactions out, and their logs give the LSN range. A
first set (`stress-*.log`, eleven runs) counted aborted work in, with totals of 2,154 to 2,546 B per instance commit
at Full and 7,208 to 7,708 B with the pad. The other logs are:

- `r2-cost-*.log`: the cost runs, with the setup WAL;
- `r2-cost-hot.log`: fill factor 100, 90 and 70, with the HOT counts;
- `r2-probe-partitioned.log`: the partitioned-table probe;
- `cost-*.log`: an earlier set of cost runs, without the setup WAL.

Every log was written to `.out/writes/`, which is not committed (`.out/` is ignored). They are kept with round 4's
session notes on the machine that ran them (`writes-logs/`), and the commands above reproduce them. Each stress log
has:

- the per-table and per-record-type split;
- the tuple and size table before, at the end and after the wait;
- `pgstattuple`, including the Instances TOAST table;
- the dead-tuple timeline.
