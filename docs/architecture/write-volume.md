# Write volume and bloat

> **Proposed.** The kernel design awaits owner approval; this page answers design-now question 7 and does not
> authorize product implementation. Its figures come from the prototype's PostgreSQL measurements, which are finite
> windows on one engine; they are not production sizing.

[Design-now question 7](../reports/clean-slate-kernel/design-sketch-draft-5.md#to-answer-in-the-design-proposal-before-the-fable-review-owner-decision-12-november)
asks what a commit costs in bytes, how much bloat it leaves on PostgreSQL, and whether the mitigations help: fill
factor, a partitioned inbox and values by reference. A commit rewrites the instance's whole state, appends a journal row
at Full history, and inserts, marks and deletes inbox rows. This page summarises the evidence and concludes. The
evidence lives in [WRITES.md](../../prototypes/kernel-model/WRITES.md) (measurements),
[POSTGRES.md](../../prototypes/kernel-model/POSTGRES.md) (guidance and the partitioned-inbox probe) and
[COMMIT-SQL.md](../../prototypes/kernel-model/COMMIT-SQL.md) (the owned commit); their tables are not repeated here.

**The answer in short.** Snapshot plus journal stays the default. At small states a commit costs about 0.7 to 2.3 kB of
WAL; in the measured windows bloat stayed small and autovacuum cleared it. Large states are the cost: an incompressible
4 kB state adds about 4.5 to 4.8 kB of WAL to every commit and doubles its TOAST table. Values by reference
(decision 10) is the fix and a release 1 priority; where its values live and how it is accepted are owner decisions,
and its acceptance run is still to do. Fill factor is a deployment note, the partitioned inbox stays out
of release 1, and the lazy snapshot remains a sketch.

## What was measured

All on PostgreSQL 17.11 in Docker, at default server settings unless a row says otherwise. Two eras: the 13 November
runs (earlier schema, checked fence, with a 40-second chaos stress) and the 14 November run (current scoped-inbox keys,
fence in the batch, no tuning, no chaos). The later run does not requalify the earlier tuning and bloat conclusions
([WRITES.md](../../prototypes/kernel-model/WRITES.md#14-november-scoped-inbox-foundation)).

### Bytes per commit

| Case, Full history | Small state | Incompressible 4 kB state | Source |
|---|---:|---:|---|
| 1,000 starts on the owner, per instance commit | 708 B (13 Nov); 711 B (14 Nov) | 5,247 B; 5,251 B | [cost run](../../prototypes/kernel-model/WRITES.md#the-cost-run-bytes-per-commit-without-chaos), [14 Nov](../../prototypes/kernel-model/WRITES.md#14-november-scoped-inbox-foundation) |
| 1,000 starts arriving by poll, setup included, per start | About 2.3 kB (13 Nov); 2,566 B (14 Nov) | 7,104 B (14 Nov) | as above; [storage model](storage-model.md#growth-retention-and-measurement-limits) |
| Chaos stress, per landed instance commit | 2,125 to 2,144 B | 6,913 to 6,931 B | [stress run](../../prototypes/kernel-model/WRITES.md#the-stress-run-a-40-second-producer-window-chaos-until-it-ends-then-settling) |

Where the bytes go at small states, under the stress: inbox 0.93 to 1.08 kB, instance row 0.64 to 0.71 kB (more than
half of it TOAST of six growing states), journal about 0.29 kB (60 % of it its two indexes), fence row 40 to 45 B.
Rolled-back attempts add 5.5 to 12 % on top. These figures are floors: with a checkpoint every 30 seconds, full-page
images added about a third to a small-state commit and a quarter to a padded one
([checkpoints](../../prototypes/kernel-model/WRITES.md#checkpoints)).

**The owned commit changed round trips and left bytes as they were, where both were measured.** Round 5 B replaced EF's
commit with hand-written SQL on all four engines. A flush of 1,000 new instances fell from 2,001 statements to 3. With
the **checked** fence, bytes per commit stayed within the noise: 711 to 711 B, 1,953 to 1,954 B, a remote balanced start
1,656 to 1,632 B (fewer commit records and padding)
([COMMIT-SQL.md](../../prototypes/kernel-model/COMMIT-SQL.md#bytes-per-commit-writesmd-postgresql-checked-fence-ef--owned)).
COMMIT-SQL also gives owned figures with the fence in the batch, PostgreSQL's default (1,080, 969, 1,194 and 1,951 B),
with no EF pair and no row labels, and WRITES.md says the in-batch bytes were not measured against EF. The figures above
carry over to the owned commit with the checked fence; for the in-batch default they are expected to, unverified.

### Dead tuples and bloat

These hold for the measured windows (one to three minutes of stress, a single 70-second wait); none establishes
steady-state bloat or sustained vacuum capacity
([POSTGRES.md](../../prototypes/kernel-model/POSTGRES.md#what-the-evidence-is-and-is-not)).

- At Full under the stress, dead tuples peaked at 577 to 723 on Instances and 4,946 to 5,397 on Messages, and up to
  about 1,100 and 6,600 in the other runs. Autovacuum cleared them at its one-minute nap; heaps kept 5 to 15 % free
  space on Instances and 13 to 30 % on Messages ([bloat](../../prototypes/kernel-model/WRITES.md#bloat)).
- 91 to 97 % of updates were HOT at the default fill factor, because page pruning makes room.
- A 4 kB state grows the Instances TOAST table to 2.1 to 2.3 times its live data. Autovacuum makes the space reusable
  and gives none of it back to the file system: in the 14 November padded run, dead TOAST bytes fell from 141,939 to
  zero while the relation stayed 12,877,824 bytes. A table rewrite (`VACUUM FULL`, `pg_repack`) returns it, at the cost
  of a lock or a copy.

### Retained identity rows (15 November)

WRITES.md's two 15 November sections measure what later identity work keeps
([physical identity](../../prototypes/kernel-model/WRITES.md#15-november-card4-physical-identity-experiment-on-landed-park),
[raw Delete repair](../../prototypes/kernel-model/WRITES.md#15-november-required-metadata-protocol-and-raw-delete-repair)):
retirement markers, bindings and receipts kept without expiry; a raw delete and a retirement at 512 and 656 B of WAL
with a 101-byte marker tuple; wide keys at 11,096 and 22,120 B of datum with 36,624 B of WAL in one window. These are
bounded samples. They grow with retired runs and accepted operations, outside the per-commit figures above, and
question 2 (data lifecycle) owns their retention.

## The mitigations, before and after

| Mitigation | Before | After | Verdict |
|---|---|---|---|
| Fill factor 70 on Instances and Messages (13 Nov) | Stress 2,125 to 2,144 B; burst commits' Messages WAL 1,062 kB; HOT 91 to 97 %; dead peaks 577 to 723 / 4,946 to 5,397 | Stress 2,049 B (4 %, below resolution); burst Messages WAL 564 to 594 kB (44 to 47 % less); HOT 99 to 100 %; dead peaks 836 / 5,248; heaps 1.5 to 1.7 times larger | Pays for bursts of outside work on Messages; nothing measurable on Instances. A deployment note |
| Faster per-table autovacuum (13 Nov) | as above | Dead peaks 605 / 5,200; no measurable effect | The server's nap bounds it; a shorter `autovacuum_naptime` is the likely lever, unmeasured |
| History State or Minimal | Full | About 130 B (State) or 415 B (Minimal) less per commit in the cost run | The author's per-workflow choice, already in the design |
| Partitioned inbox | Probed only: range or list by partition needs `Partition` in the message key, which narrows ordinary-id deduplication from store-wide to per partition; hash on `Id` keeps the key, and every poll then touches every partition | Not measured under load | No WAL saving in principle; out of release 1 ([fit report](../../prototypes/kernel-model/POSTGRES.md#partitioned-inbox-fit-report)) |
| Values by reference (decision 10) | A 4 kB value rewritten each commit: about 4.5 to 4.8 kB of extra WAL per commit, plus TOAST churn | Not measured on PostgreSQL. The witness ([VALUES.md](../../prototypes/kernel-model/models/Values/VALUES.md), SQLite) shrinks ten revisions of a 4 KiB value from 59,351 to 1,351 state bytes, and raises engine calls from 5 to 13 to the first value-bearing commit and from 12 to 20 for a fresh owner | The fix for large values that do not change every turn; the WAL saving is inferred |

## Conclusions

1. **Snapshot plus journal stays the default.** Nothing measured asks for append-only storage, event sourcing or a
   log-only store. Those stay off the default; the Log profile stays a later plan.
2. **Decision 10 is release 1's write-volume priority.** The whole state is one value, and PostgreSQL rewrites a large
   value whole through TOAST at every commit. Values by reference is the one mitigation that removes that cost for
   values that do not change every turn. Where its values live is decision 1, and how it is accepted is decision 2. If a
   payload codec encrypts states (#4707, later), it must compress before it encrypts, or every encrypted state above
   about 2 kB pays the incompressible cost measured here.
3. **Fill factor and vacuum settings are deployment notes**, written in POSTGRES.md's
   [tuning guidance](../../prototypes/kernel-model/POSTGRES.md#tuning-guidance) with each item marked measured or
   recommended. The store creates default tables.
4. **The partitioned inbox stays out of release 1.** New code addresses a message by partition, id and scope wherever
   the partition is known, so that a later change of key changes DDL and leaves call sites alone.
5. **The lazy snapshot is the fallback, sketched and not built.**

## The lazy-snapshot fallback

From [WRITES.md](../../prototypes/kernel-model/WRITES.md#the-lazy-snapshot-a-sketch-not-built):

- A commit appends the state change to the journal, as a delta or the changed parts; the instance row is rewritten
  every N commits, or when the delta chain passes a size. The row carries its snapshot's revision; the journal rows
  after it are the tail.
- A load reads the row and its tail in one command, so it adds no round trip; a turn replays the tail first, which costs
  CPU only. The fence, the position and the commit protocol are unchanged.
- It costs: no Minimal history for such an instance, since the tail is its state; journal deletion must stop at the
  last snapshot; the projector and viewer must tell delta rows from entries; every model needs a delta format.
- It saves the row rewrite: about 0.3 kB of heap for a small state, about 5.2 kB of TOAST for an incompressible 4 kB
  one. It pays only when the delta is much smaller than the state.

**Proposed trigger for the spike:** after values by reference lands, a representative workload (an agent thread, a
growing collection) whose instance-row WAL is still the largest share of its commit. The spike goes into the prototype
first, as the sketch requires.

## What release 1 does

- Snapshot plus journal, Full history by default, State and Minimal per workflow, as decided.
- Values by reference for large values, stored as decision 1 says and accepted as decision 2 says; a size benchmark
  stays in the cost runs.
- The owned commit on PostgreSQL, SQL Server, SQLite and MySQL, as built.
- Default table settings, with POSTGRES.md's guidance published as a PostgreSQL operations note.
- No partitioned inbox, no lazy snapshot, no new tuning default.
- A write-volume run on the release's final schema before release, since every later field, index and guard changes
  bytes and the old figures do not carry over to it.

## Not measured

- SQL Server, MySQL, SQLite and MongoDB bytes: WRITES.md's method needs `pg_walinspect`.
- Steady state: the runs are minutes long at about 70 instance commits a second; a sustained 1,000 commits a second
  (about 2.1 MB/s of WAL at small states, 6.9 MB/s at 4 kB) is an extrapolation.
- Real JSON, which compresses: the incompressible pad is the upper end.
- The WAL saving of values by reference, and the reference's own write, read and lifetime costs.
- Latency, checkpoint stalls, replication lag and a pinned transaction horizon.

## Decisions for the owner

### 1. Where values by reference live

This is one decision for this page and for the data-lifecycle page
([#4707](https://github.com/elsa-workflows/elsa-foundation/pull/4707)); its retention, purge and erasure rules follow
whichever option is chosen. #4707's current draft is written for option b.

**What is built.** [ValueStore.cs](../../prototypes/kernel-model/models/Values/ValueStore.cs) is a witness: `ValueRef`
is a SHA-256 digest (upper-case hex) with a length; `DirectoryValues` keeps one file per digest in one directory,
written to a temporary file, flushed and renamed, and verified on every read. It is content-addressed across the whole
directory, so two instances that store the same bytes share one object, and nothing in it is scoped by instance or
tenant. The model learns the reference from a job's result and commits it afterwards
([VALUES.md](../../prototypes/kernel-model/models/Values/VALUES.md)). Durability, pruning, reference tracking, tenant
scoping, encryption and erasure are unqualified there.

| Option | Write path | Lifecycle | For | Against |
|---|---|---|---|---|
| a. A values record in the same provider | An immutable row per value, keyed by instance and digest, written in the same fenced batch as the reference; read with the instance on load | No orphans: the row commits with its reference. A changed value writes a new row; superseded rows that Full-history journal rows still reference stay until the instance is deleted, and go with it | One store, one commit, one backup; MongoDB can do the same with a document | Large values still pass through the database once, with their own TOAST and WAL per value; superseded values accumulate for the instance's lifetime |
| b. An external content-addressed store | The value is written before the commit, as the witness does through a job ticket; the commit records the reference | Orphans after a lost commit need a sweep. For #4707's purge by instance and erasure, objects must be named per instance (or the digest keyed per instance or tenant), which differs from the witness's store-wide content addressing; shared objects would otherwise need reference counting | Keeps large bytes out of the database | A second store to run, secure and back up; erasure spans two systems; extra calls (the witness: 5 against 13 engine calls to the first value-bearing commit, 12 against 20 for a fresh owner) |

**Recommendation: a for release 1**, with b as a later value-store adapter for deployments that need large bytes kept
out of the database.

### 2. How decision 10 is accepted

POSTGRES.md proposes ([its owner decisions, item 2](../../prototypes/kernel-model/POSTGRES.md#owner-decisions-this-report-raises))
that values by reference count as delivered only after a run: the existing `pad=` case against a reference, on
PostgreSQL, measuring WAL per commit, TOAST growth and the load path's round trips, with the value changing every turn
and not. **Recommendation: adopt it**, with the run on the option chosen in decision 1.

### 3. Tuning as a note or a shipped profile

POSTGRES.md raises it: whether its guidance stays an operations note or becomes a DDL profile the PostgreSQL
initializer applies (fill factor on `Messages`, say). **Recommendation: an operations note for release 1**; a profile
needs a run on the current schema first.
