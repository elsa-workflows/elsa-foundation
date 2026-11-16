# PostgreSQL guidance for the kernel's tables

Round 5, item D (kernel program #4229). A report: it changes no kernel code. Terms: a **take** is a node acquiring a
kernel partition's lease; the **fence** is the check (epoch and position on the `Partitions` row) that stops a stale owner's
commit; a **pass** is one run of the owner loop (poll, turns, commit); a **VP** is a virtual partition, the fixed-count
kernel partitions of round 5 A (1,024 or 4,096, no rehash). "Kernel partition" below means a VP under round 5 A. It does three things:

1. tuning guidance for the four tables whose write pattern matters on PostgreSQL (`Messages`, `Instances`, `Journal`,
   `Partitions`), separating what was measured from what is only recommended;
2. the case for **decision 10, values by reference**, as a release 1 priority;
3. a fit report for **partitioning the inbox** (round 5 item A's virtual partitions) against the store's current keys.

Its evidence is [WRITES.md](WRITES.md), including its
[14 November scoped-inbox supplement](WRITES.md#14-november-scoped-inbox-foundation) and the dated folder
[measurements/write-cost-2026-11-14](measurements/write-cost-2026-11-14/README.md). The one new measurement is a
small partitioning probe, [measurements/partitioned-inbox-probe-2026-11-16](measurements/partitioned-inbox-probe-2026-11-16/probe.sql)
(PostgreSQL 17.11, a throwaway container, DDL and plans only; no workload).

## What the evidence is, and is not

Read every figure below with these limits.

- **Finite windows, not steady state.** The cost runs are single finite windows (about 2,200 instances, one pass, no
  repeat bounds). The stress runs are about 70 instance commits a second for one to three minutes on a shared
  laptop-class machine, with killed sessions. The 14 November run explicitly forces nothing and waits 70 seconds
  once. **None of these is steady-state throughput, sustained vacuum capacity, production sizing or a bloat
  equilibrium.** "Autovacuum kept up" means: in those windows, `pgstattuple` showed dead tuples fall back after each
  one-minute nap. It does not mean it will keep up at 1,000 commits a second for a week.
- **Two measurement eras.** The 13 November stress, checkpoint, bloat and mitigation runs use the earlier schema
  (key `Id` on `Messages`, checked fence). The 14 November run uses the current scoped-inbox keys and the batch fence,
  at defaults, with no tuning. WRITES.md says its later run does not requalify the earlier tuning conclusions. This
  document follows that rule: tuning numbers come from the 13 November runs and are labelled so.
- **WAL is not latency.** No run here measures speed, tail latency or checkpoint stalls.
- **Only PostgreSQL 17.11 on `postgres:17` was run.** Nothing here is evidence for SQL Server, MySQL, SQLite or MongoDB.

Status labels used below: **Measured** (a run in WRITES.md or the dated folder shows it), **Probed** (the 16 November
probe), **Recommended, unmeasured** (reasoned from PostgreSQL's documented behaviour and the table's write pattern;
the kernel was not run with it), **Open** (needs a decision or a run).

## What each table does to PostgreSQL

| Table | Write pattern | Why it matters | Measured per landed commit, Full history, small state (13 Nov stress) |
|---|---|---|---|
| `Messages` | insert, update (seen), update (done), delete or sweep; mostly short-lived | the largest WAL share at small states and the most dead tuples (peak 4,946 to 6,600) | 0.93 to 1.08 kB |
| `Instances` | one update of the whole row per commit; `State` over about 2 kB compressed goes through TOAST | HOT updates keep it cheap; large states make it the largest share | 0.64 to 0.71 kB (more than half is TOAST of six growing states) |
| `Journal` | insert-only at Full and State; rows leave only when an instance is deleted | its two indexes cost more than the row (60 % of its WAL) | about 0.29 kB |
| `Partitions` | one HOT update of one row per commit (the fence), and per take | very hot rows in a tiny table | 40 to 45 B |

Other tables (`Subscriptions`, `Events`, `Views`, `Nodes`, `Projections`) were not separately characterised and get
no guidance here.

## Tuning guidance

### Server-level

| Setting | Guidance | Status |
|---|---|---|
| `full_page_writes` | Keep on. The measured WAL figures are a floor: with a checkpoint every 30 s, full-page images added about a third (+34 %, 2,032 to 2,732 B) to a small-state commit and about a quarter (+27 %, 6,800 to 8,644 B) to a padded one. | Measured (the small-state comparison is one run at each interval; the padded 5-minute baseline is from the second set, which ran twice) |
| `checkpoint_timeout`, `max_wal_size` | Fewer checkpoints mean fewer full-page images. Start from a longer interval (for example 15 minutes) with `max_wal_size` sized so checkpoints are time-triggered, not WAL-triggered, and `checkpoint_completion_target` 0.9. The interval-to-image relation was measured only at 30 s and 5 min. A longer interval also lengthens crash recovery (more WAL to replay), which no run measured. The best values depend on how many distinct pages a deployment touches per interval, which no run measured. | Direction measured; values recommended, unmeasured |
| `wal_compression` | Consider `lz4` or `zstd` (PostgreSQL 15 and later) to shrink full-page images. | Recommended, unmeasured |
| `autovacuum_naptime` | The default 1 minute bounds how often any table is looked at, so per-table thresholds did nothing below it (see Messages). Try 15 to 30 seconds on a busy deployment. WRITES.md already names this as "the likely lever" without measuring it. | Recommended, unmeasured |
| `autovacuum_max_workers`, `autovacuum_vacuum_cost_limit` | Autovacuum's default cost throttling can leave it behind a fast producer. Raise the cost limit before adding workers; a few large tables share the cost budget. | Recommended, unmeasured |
| `synchronous_commit`, `fsync` | Do not relax. The kernel's commit is durable (the fenced atomic commit); the measurements all ran with both on. | Measured settings; relaxing is not recommended |
| Long transactions, idle-in-transaction sessions, lagging replication slots, `hot_standby_feedback` | Anything that pins the oldest transaction horizon stops vacuum from removing the dead tuples that the inbox and instance rows produce, whatever the vacuum settings are. Monitor and bound them (`idle_in_transaction_session_timeout`, slot lag alarms). | Recommended, unmeasured (not injected in any run) |

### `Messages`

- **Fill factor 70, set while the table is empty.** Measured on the 13 November schema. Under a trickle, HOT updates of
  `Messages` alone reached 99 % (91 to 95 % at fill factor 100; the 100 % and 91 to 97 % ranges in WRITES.md combine Instances and Messages). In a burst (1,000 outside starts arriving by poll)
  the commits' `Messages` WAL fell 44 to 47 % (1,062 kB to 564 to 594 kB), and the whole row, with its inserts counted,
  20 to 22 %. Across three repeats HOT updates over the cost run rose from 42 to 51 of 2,212 to 1,175 to 1,241. At
  fill factor 90 they were 434 to 447. The stress comparison's 4 % WAL saving is below the runs' resolution (about 5 %).
  The cost is a larger heap: 1.6 to 1.7 times (3.56 MB against 2.07 to 2.25 MB at the stress run's end). Fill factor 70
  therefore pays for bursts of outside work, not for steady small trickles. **Not requalified on the scoped-inbox
  schema.**
- **Per-table autovacuum scale factor and threshold.** The measured "fast vacuum" run (scale factor 0, threshold 500,
  insert threshold 2,000, no cost delay) changed nothing measurable: autovacuum ran 4 times against 3 to 4, and peak
  dead tuples stayed in the baseline's range (5,200 against 4,946 to 5,397). The reason is the 1-minute nap. So a
  per-table setting is only worth setting together with a shorter `autovacuum_naptime`. When it is, a low scale
  factor (for example 0.01 to 0.02) with a threshold around 1,000 matches how fast this table turns over.
  **Recommended, unmeasured.**
- **No extra indexes on updated columns.** `Seen` and `Done` change on every message; indexing either removes HOT
  updates. The poll uses `(Partition, Seq)`, which no update touches. Keep it that way. (General PostgreSQL behaviour.)
- **Expect the files not to shrink.** Reclaimed space is reused, not returned (the 14 November padded run's relation
  stayed 12,877,824 bytes after dead TOAST tuple bytes went from 141,939 to 0). Size for the high-water mark.
- **The sweep** deletes handled rows after `KeepUntil` in one statement (`DELETE ... WHERE "Done" <> false AND
  "KeepUntil" < now`). Run it often in small amounts, not rarely in one large delete, so autovacuum sees steady small
  batches. No index serves that predicate (none is declared), so its cost grows with the table. **Open:** whether the
  sweep needs an index; not measured.

### `Instances`

- **Fill factor.** Nothing measured: with the default fill factor, 93 to 97 % of instance updates were already HOT, and
  fill factor 70 gave no resolvable change. Leave it at 100, or 90 if a deployment's HOT percentage (see monitoring)
  falls below the mid-90s. Instances is 1.5 to 1.7 times larger at 70.
- **Do not index columns that change at each commit** (`Revision`, `Status`, `State`, and so on): it ends HOT updates.
  Only `Id` is a key today. Lookups by `Partition` (for rebalance or recovery) are the likeliest request for one. Add
  it only with the partitioned-store work below, since `Partition` itself never changes for an instance.
- **TOAST is where the cost is.** A state over about 2 kB after compression moves out of line, and a changed state is
  written out again whole (see the next section). Measured: an incompressible 4 kB state grew the TOAST table to 36 to
  39 MB for 17 MB live (2.1 to 2.3 times), with 4,759 to 5,919 dead chunks at the end and none after vacuum; and six
  small counter states that grew to 5 kB left 2.3 to 2.5 MB of TOAST for 30 kB live, 95 to 98 % free after the wait.
  Between vacuums the table holds the dead chunks of a whole nap; WRITES.md estimates about 240 MB of churn space at 1,000
  commits a second of 4 kB states and a 1-minute nap (**a formula, not a measurement**).
- **Recommended, unmeasured, for deployments with big states that decision 10 does not remove:** the TOAST relation has
  its own reloptions (`toast.autovacuum_vacuum_scale_factor`, `toast.autovacuum_vacuum_threshold`, set with
  `ALTER TABLE "Instances" SET (toast.autovacuum_vacuum_scale_factor = 0.02)`); `default_toast_compression = lz4`
  (PostgreSQL 14 and later) is cheaper than the default `pglz`. Real JSON compresses, so the incompressible
  measurement is an upper end, which was not measured.

### `Journal`

- Insert-only, no updates: fill factor 100, and nothing to tune for HOT.
- Dead tuples come from aborted inserts and deleted instances (peaks of 769 to 1,059). PostgreSQL 13 and later start an
  autovacuum on insert volume (`autovacuum_vacuum_insert_scale_factor`), which keeps the visibility map current for the
  projector's and viewer's range reads. The default is probably enough. **Recommended, unmeasured.**
- Its two indexes, `(Partition, Position)` and `(Partition, Instance)`, are most of its WAL. The second serves the delete of an
  instance's rows and the viewer's tail read (`ReadJournalOfAsync`). Do not add a third.
- Retention is not implemented (no journal TTL). Range partitioning by `Partition` does **not** give retention: it
  groups kernel partitions, not ages, so dropping a range would delete live instances' rows. Retention needs
  position- or time-bounded segments inside each partition, for example `RANGE (Partition, Position)`, which keeps the
  key and was accepted by PostgreSQL in the probe (stand-in table, no data at scale) but multiplies the table count.
  **Open** until retention is designed.

### `Partitions` (the fence)

- One row per partition, updated at every commit and take, always as a HOT update (the log's equality of HOT updates
  with landed commits plus takes was exact). 40 to 45 B per commit.
- **Recommended, unmeasured:** fill factor 50 to 70 so each page keeps room for the update chain and pruning can
  reuse it, and a per-table autovacuum scale factor of 0 with a small threshold (for example 50). The table is tiny, so
  the vacuum costs almost nothing. Under a pinned transaction horizon this is the first table to bloat visibly: each
  commit leaves a dead version that cannot be pruned.

### Monitoring to decide any of the above

Run these during a load test before changing a setting; they are the same sources the measurements use.

```sql
-- HOT share and dead tuples, per table (reset stats first; they count rolled-back rows too)
select relname, n_tup_ins, n_tup_upd, n_tup_hot_upd, n_tup_del, n_live_tup, n_dead_tup,
       last_autovacuum, autovacuum_count
from pg_stat_user_tables where relname in ('Messages','Instances','Journal','Partitions');
-- exact dead and free space (extension pgstattuple), including the TOAST relation
select * from pgstattuple('"Instances"');
select * from pgstattuple((select reltoastrelid from pg_class where relname = 'Instances'));
-- WAL and full-page images
select wal_records, wal_fpi, wal_bytes from pg_stat_wal;
-- what is holding back vacuum
select pid, state, xact_start, backend_xid, backend_xmin from pg_stat_activity where backend_xid is not null or backend_xmin is not null order by xact_start;
select slot_name, active, xmin, catalog_xmin from pg_replication_slots;
```

## What is measured and what is only recommended

| Measured (13 Nov schema unless stated) | Recommended, unmeasured |
|---|---|
| WAL per landed commit by table, Full/State/Minimal, with and without a 4 kB pad | `autovacuum_naptime` 15 to 30 s; cost limit; per-table scale factors on Messages, Partitions, TOAST |
| Fill factor 70 and 90 on Messages: HOT share, burst WAL, heap size | `checkpoint_timeout` 15 min with `max_wal_size` to match; `wal_compression` |
| Fill factor 70 on Instances: no resolvable effect | Fill factor 50 to 70 on `Partitions` |
| Per-table fast autovacuum with the default nap: no measurable effect | `lz4` TOAST compression; `toast.*` reloptions |
| Checkpoint every 30 s against 5 min: +33 % and +25 % WAL via full-page images (one run each) | Indexing the sweep predicate |
| TOAST growth to 2.1 to 2.3 times live data with an incompressible 4 kB state; files do not shrink | Behaviour with a pinned horizon, replicas, or a real 1,000 commits/s |
| 14 Nov (scoped keys, batch fence, defaults): 919 / 711 / 1,953 B per landed commit; padded 5,659 / 5,251 / 6,492 B | Anything on steady state, week-long vacuum, latency |

## Decision 10: values by reference, a release 1 priority

**Recommendation: build values by reference in release 1, before the first deployment profile that accepts
model-defined state sizes.** The sketch already lists it as a release 1 priority (design-sketch-draft-5, scope list)
and as decision 10 ("large values are stored by reference, and a size benchmark goes into the cost runs"). This
section gives the reason the measurements support.

**Why: the whole state is one value, and PostgreSQL rewrites a large one whole.** A commit writes the instance's
`State` as one JSON value. A value over about 2 kB after compression is stored out of line (TOAST) in 2 kB chunks;
an updated value is written out again as new chunks and the old chunks become dead rows. A change anywhere in the
state, however small, therefore costs the whole state's WAL and TOAST churn. The measurements:

| Case | Small state | Incompressible 4 kB state | Ratio |
|---|---:|---:|---:|
| Clean commit, ten durable no-ops (13 Nov cost run) | 912 B | 5,650 B | 6.2x |
| Clean commit, 1,000 starts on the owner (13 Nov) | 708 B | 5,247 B | 7.4x |
| Clean commit, 1,000 starts on the owner (14 Nov, current keys) | 711 B | 5,251 B | 7.4x |
| Clean commit, ten durable no-ops (14 Nov) | 919 B | 5,659 B | 6.2x |
| Instance row insert (13 Nov) | 286 B | 4,807 B | 16.8x |
| Whole-workload stress, per landed commit (13 Nov) | 2,125 to 2,144 B | 6,913 to 6,931 B | 3.2x |
| Instances share only, stress (13 Nov) | 663 to 681 B | 5,408 to 5,461 B | about 8x |

The additive cost is the stable number: **about 4.5 to 4.8 kB more WAL per commit in every case** (4,807 minus 286 for an
insert; 5,247 minus 708; 6,913 minus 2,125). The ratio depends on how much other work a commit carries. A lean commit
sees about 7 times (6.2 to 7.4); commits that also pay for polled inbox rows see less: 1,800 to 6,340 B (3.5x, 13 Nov),
1,953 to 6,492 B (3.3x, 14 Nov), the event start 2,256 to 6,808 B (3.0x), and the chaos stress 3.2x. The dilution comes
from the inbox and journal work in the commit, not from chaos. Then add:

- **TOAST bloat.** The TOAST table reaches 2.1 to 2.3 times its live data and is reused, not returned. At scale the
  churn space is roughly live state plus (commits per second times state size times the time between vacuums).
- **Replication and backup.** WAL volume is also replication and archive volume. At 1,000 commits a second the
  extrapolation is about 2.1 MB/s with small states and 6.9 MB/s with incompressible 4 kB states (extrapolated from
  per-commit figures, not run).
- **Full-page images.** The gap between large and small widens with frequent checkpoints (about 4.8 kB at 5 minutes,
  about 5.9 kB at 30 s).
- **Growing states are the worst case.** An agent thread appends at every turn, so its state is rewritten at every
  commit and grows. Six small growing counter states in the stress run cost 368 to 379 B per landed commit of TOAST
  WAL, a sixth of the whole run's WAL.

Caveats, so the case is not overstated:

- The padding is random base64 that changes at every commit, which is the worst case (nothing compresses, nothing is
  unchanged). Real JSON compresses, so the ratio should be lower. That was **not measured**.
- Whether the store avoids rewriting a `State` column that did not change was not measured. Even if it does, any real
  turn changes the state, and the column is a single value, so one changed field still rewrites all of it.
- The numbers above are the **ceiling** for what a reference saves: a reference written once removes the cost for every
  commit that does not change the value, but a value that changes every turn still pays once per change, and the
  reference adds a blob write and read (the witness uses a file store, not a table). Decision 10 is not a cure for a large value that changes every turn. That case is
  the lazy snapshot of WRITES.md, which is sketched, not built.
- The "external-values witness" mentioned in storage-model.md was built after the 13 November run. No write-cost
  measurement of it exists.

**The opt-in witness.** [VALUES.md](models/Values/VALUES.md) describes the values-by-reference experiment: the
checkpoint and Full journal store a length and SHA-256 digest, the value lives in a file store written through an
existing job ticket, and a later job reads and verifies it. Its measured costs (SQLite, ten value-bearing revisions,
sums of serialized UTF-8 bytes): a 4 KiB value is 59,351 state bytes inline against 1,351 by reference (journal 58,930
against 930); 100 KiB is 1,469,721 against 1,371. Engine calls rise: 5 inline against 13 by reference to the first
value-bearing commit, and 12 against 20 for a fresh owner to read it, plus one blob put and one blob read. **It
measured no WAL, TOAST, pages or PostgreSQL round trips, and file-store durability, pruning and erasure are
unqualified.** So the byte saving is shown, the WAL saving is inferred, and the cost is extra calls and a second store.

What decision 10 needs before it is accepted: the existing `pad=` case against that witness or a successor (same
states, the value changing each turn and not), measuring WAL per commit and TOAST growth on PostgreSQL, plus the load
path's round trips. The cost harness (`Writes.cs`, `pad=`) does not drive the witness today, so it needs wiring for the
reference side. **That run is the acceptance test, and it is unrun.**

## Partitioned inbox: fit report

**Question.** Round 5 item A fixes 1,024 or 4,096 virtual partitions (VPs), no rehash. Could the `Messages` table be
partitioned by VP on PostgreSQL, and what would that require of the store's keys?

### The store as it is

- `Messages` key: `(Id, InboxScope)` on PostgreSQL and SQLite (`(Id, InboxScope, IssuedWire)` on SQL Server and
  MySQL). `InboxScope` is zero for ordinary messages and a negative or private value for issued, job, child and event
  identities. Other indexes: `(Partition, Seq)`. `Seq` is an identity column.
- `Messages.Partition` is the **destination** instance's VP, not a function of `Id`. A caller chooses the `Id`.
- Ordinary add is `INSERT ... ON CONFLICT ("Id","InboxScope") DO NOTHING`. Its meaning is store-wide
  deduplication of a caller-supplied id: a second add of the same `Id` is a no-op wherever the target is.
- Statements that carry the partition: the poll (`WHERE "Partition" = p ...`) and the take's trial update (`Partition`
  and `Trial`).
- Statements that carry **no** partition: the event insert function (`WHERE "Id" = wire`, and
  `min("InboxScope") WHERE "Id" = wire`), the sweep (`"Done"` and `"KeepUntil"`), and the commit's own message writes:
  consumed (delete), done, held, trial and seen updates all go through `MailKey(InboxKey)` (EfStore.cs), which carries
  `Id` and scope but no `Partition`.
- `Journal`'s key is already `(Partition, Position)`. `Instances`' key is `(Id)`; its `Partition` is derived from the
  instance's address (an `InstanceRef` carries it). `Subscriptions` is keyed `(Key, Instance, Wait)` and carries a
  `Partition` column.

### Probed on PostgreSQL 17.11 ([probe](measurements/partitioned-inbox-probe-2026-11-16/probe.sql), [output](measurements/partitioned-inbox-probe-2026-11-16/output.txt))

| Layout | Result |
|---|---|
| Range or list by `Partition`, key kept as `(Id, InboxScope)` | **Refused:** "unique constraint on partitioned table must include all partitioning columns". |
| Range by `Partition`, key `(Partition, Id, InboxScope)` | Accepted. `ON CONFLICT (Id, InboxScope)` then fails ("no unique or exclusion constraint matching"). The same `Id` with two different partitions inserts twice: dedup scope narrows from the store to the partition. The poll prunes to one partition and uses that partition's `(Partition, Seq)` index. |
| Hash by `Id`, key kept as `(Id, InboxScope)` | Accepted. `ON CONFLICT (Id, InboxScope)` works and deduplicates store-wide. The poll (which filters on `Partition`) does a Merge Append over **every** partition's `(Partition, Seq)` index, so poll cost grows with the partition count. An id lookup prunes to one partition. |

The probe uses stand-in tables with the same key columns, not the EF DDL, and shows plans on a 2,000-row table. It is not a performance comparison and does not count WAL. It also covers a list-partitioned `Messages` stand-in (refused with the old key, like range) and two `Journal` stand-ins: key `(Partition, Position)` partitioned by range on `Partition`, and by range on `(Partition, Position)`; both accepted. SQL Server, MySQL and SQLite were not probed.

### What partitioning would and would not buy

- **No WAL saving.** The same inserts, updates and deletes happen (WRITES.md said this on the earlier schema, and
  nothing in the layout changes it).
- **Smaller vacuum units**: each partition is vacuumed on its own thresholds. This matters only if a single `Messages`
  table's vacuum becomes the bottleneck. In the measured windows it did not (1.0 to 2.2 MB heaps). **Not measured at a scale where it would
  bite.**
- **Poll locality and cheap partition moves.** A range-by-VP layout keeps a partition's inbox physically together and
  would let a range split or merge move one table, matching range-fence granularity. Speculative until round 5 item A
  gives numbers.

### What would have to change for range or list partitioning by VP

1. **The key gains `Partition`:** `(Partition, Id, InboxScope)`. This changes the dedup rule from store-wide to
   per-partition for ordinary ids. A caller who reuses one `Id` for two different targets would, today, have the second
   dropped; after the change both would be delivered. The kernel's own ids (issued, job, child, event, caller-key
   starts) are not all alike: issued ids are unique by construction, job tickets are destination-scoped, and
   `Custom(actor, id)` handles are origin-scoped (Turns.cs). The effect on custom handles and event starts is **not
   verified**. **This is an owner decision**
   because it narrows a documented guarantee ("scope zero preserves store-wide ordinary deduplication").
2. **The add's conflict target becomes `("Partition","Id","InboxScope")`.** The provider's `AddAsync` and `EfStore`
   statement change. `EventInsert` has no `ON CONFLICT` on any provider (it uses `pg_advisory_xact_lock` and lookups),
   so it needs only the partition in its lookups (it receives `part` already).
3. **Every statement that addresses a message by `Id` must also carry `Partition`,** or it scans all partitions:
   `kernel_insert_event`'s two `WHERE "Id" = wire` lookups, the SQL Server and MySQL equivalents, and every
   `MailKey`/`InboxKey` write (consume, done, held, trial, seen). `MailKey` and `InboxKey` must gain a `Partition`,
   supplied by the caller, for example `batch.Partition` in the commit (a message consumed by a commit belongs to the
   committing owner's partition). EF would then include it in the key predicate. Without a partition predicate those lookups need an `Id`-leading local index on every
   partition, and a probe becomes one index probe per partition.
4. **The sweep** deletes across all partitions with `Done`/`KeepUntil`. It still works, and runs per partition. No
   change is needed for correctness. Its missing index is a separate matter.
5. **One table per VP is not workable at 1,024 or 4,096 VPs.** A partitioned table with thousands of partitions
   makes planning and DDL slow, and every partition has its own indexes and vacuum state. Group VPs: range by
   `Partition` into tens to a few hundred tables (for example 64 groups of VP/64), which also lines up with range
   ownership. The exact count is **unmeasured** and should come out of round 5 A's rebalance and split/merge
   numbers.
6. **The other providers differ.** SQL Server partitioned tables need the partition column in a unique index as well, MySQL's
   partitioned tables cannot have foreign keys and need it in every unique key, and SQLite has none. Partitioning is
   therefore a PostgreSQL-provider option, not a change to the contract. SQL Server and MySQL were not probed.
   MongoDB would use its own scheme (sharding).

### Alternative: hash on `Id`, keys unchanged

Accepted by PostgreSQL with no key change, and it keeps store-wide dedup. It gives smaller vacuum units, not locality.
Its cost is that every poll touches every partition's index (see the plan above). With a fixed small partition count
(for example 8 to 16), that is tolerable; it grows linearly. It does not help moving a VP's inbox. **Not measured
beyond the plan.**

### `Journal`, `Instances`, `Subscriptions`

- **`Journal` fits without a key change.** `(Partition, Position)` already contains the partition column, so range
  partitioning by `Partition`, or by `(Partition, Position)`, is accepted by PostgreSQL as it stands (probed on a
  stand-in table). The `(Partition, Instance)` index is local to each table. A range on `Partition` alone gives smaller
  vacuum units and partition moves, not retention (it groups kernel partitions, not ages). Retention would need
  `RANGE (Partition, Position)` or time-bounded segments, which multiplies the table count; not probed at scale.
  Round 5 A's per-VP journals ("both per-VP and range-fence variants") are a logical split of positions; a physical split is a separate, later choice.
- **`Instances`** is keyed `(Id)`. It would need `(Partition, Id)`, and every load, update and delete would carry the
  partition, which the owner always knows. An instance id maps to one partition, so the dedup narrowing above does
  not arise; the cold loads that look an instance up by `Id` alone (for example a caller-key resolution from another
  partition) would have to be checked. Not probed.
- **`Subscriptions`** would be partitioned by `Partition`, but its lookups are by `Key` alone (a publisher finds
  waiters for an event key across all partitions); partitioning would hurt them. Do not partition it.

### Recommendation

1. **Do not partition `Messages` for release 1 by default.** The measured windows show no vacuum bottleneck, there is no
   WAL gain, and the cost is a store-contract change (dedup scope) plus statement changes in every provider that is
   not PostgreSQL. Keep the current keys.
2. **Keep the option open at no cost now.** Whatever round 5 A decides about VPs, new code should not rely on `Id`
   alone to locate a row; the store-wide guarantee is unchanged. It should address messages by
   `(Partition, Id, InboxScope)` where the partition is known (it nearly always is), so that switching the key later
   changes DDL, not call sites. This is an engineering
   suggestion for round 5 A and B; it is not a change made here.
3. **If a deployment proves vacuum or move cost on `Messages`,** use range-by-VP groups with the extended key, as an
   opt-in PostgreSQL DDL profile, after an owner decision on the dedup scope. Hash on `Id` is the fallback if the
   dedup scope must stay store-wide.
4. **If `Journal` is partitioned, it is the cheapest table to start with** (no key change). A range on `Partition` buys smaller vacuum units and moves; retention would need `RANGE (Partition, Position)` or time-bounded segments, designed with retention itself.
5. **Measure before any of it:** a stress run on a range-partitioned `Messages` (group counts 16, 64, 256) against the
   unpartitioned table: poll plans and latency under backlog, vacuum behaviour, and rebalance cost. None was run.

## Owner decisions this report raises

1. **Dedup scope for ordinary message ids**, if `Messages` is ever partitioned by VP: store-wide (today) or
   per-partition. Needed only if recommendation 3 above is taken. Not needed for release 1 if recommendation 1 stands.
2. **Decision 10 as a release 1 priority** is already adopted. The proposal is that its **acceptance** is the `pad=`
   run against a reference (see above), not just its implementation.
3. **Whether tuning guidance is an "operations note" or a shipped provider profile** (a DDL set applied by the PostgreSQL
   initializer, such as fill factor on `Messages`). Today `fillfactor=` exists only in the measurement tool; the store creates default tables.
   WRITES.md calls it a deployment note. This document keeps it so; making it a profile needs a decision and a run on
   the current schema.

## Reproducing the probe

```
docker run -d --rm -P -e POSTGRES_PASSWORD=probe --name km-r5d-probe-<unique> postgres:17
ready=0
for i in $(seq 1 30); do docker exec km-r5d-probe-<unique> pg_isready -h 127.0.0.1 -U postgres && { ready=1; break; }; sleep 1; done
if [ "$ready" != 1 ]; then echo "PostgreSQL did not become ready in 30 s; stopping" >&2; docker stop km-r5d-probe-<unique>; exit 1; fi
docker exec -i km-r5d-probe-<unique> psql -U postgres < prototypes/kernel-model/measurements/partitioned-inbox-probe-2026-11-16/probe.sql
docker stop km-r5d-probe-<unique>
```

Use a port Docker chooses (`-P`), never 5095, and stop the container afterwards.
