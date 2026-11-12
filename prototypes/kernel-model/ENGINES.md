# Engines

The EF store of the kernel model on PostgreSQL, SQL Server, MySQL and SQLite: the stress test of
[FINDINGS.md](FINDINGS.md) section 4, the cost count of section 5, probes for what a review of the protocol predicted,
the store conformance suite, and the fence broken on purpose on every engine.

**After the merge (FINDINGS.md section 23).** Round 3's store operations (heartbeat, start subscriptions, the
projector's row and the view rows, delete, unsubscribe-all, held messages, reply addresses) are implemented in every
dialect; E1, E4, E7 and E8 hold for them, and the conformance suite has cases for each. The figures below are the
branch's; section 23 has the merged ones.

**Round 4 (FINDINGS.md section 24).** The names are round 4's: an instance's step is a *turn* (`ITurn`), off-step
work is a *background job* (`StartJob`, `IJob`, `job-finished`, the row's `jobs`), and the runtime's call into a
partition is a *pass*. `src/Kernel` is now three projects: `src/Kernel.Runtime`, `src/Kernel.Durable` and
`src/Kernel.Workflows`; a file named below without a path is in one of them. Seeds, counts and costs are unchanged.

Evidence is marked as in FINDINGS: **run** means a command below produced it; **reasoned** means nobody ran it.
Raw output of every run is under `.out/` (not committed).

## Setup

| Engine | Image | Provider | Driver |
|---|---|---|---|
| PostgreSQL 17 | `postgres:17` | Npgsql.EntityFrameworkCore.PostgreSQL 10.0.3 | Npgsql 10.0.3 |
| SQL Server 2022 | `mcr.microsoft.com/mssql/server:2022-latest` (amd64, emulated on arm64) | Microsoft.EntityFrameworkCore.SqlServer 10.0.15 | Microsoft.Data.SqlClient 6.1.7 |
| MySQL 8.4.13 (InnoDB) | `mysql:8.4` | **MySql.EntityFrameworkCore 10.0.12** (Oracle) | MySql.Data 26.10.0 |
| SQLite | in process | Microsoft.EntityFrameworkCore.Sqlite 10.0.15 | Microsoft.Data.Sqlite 10.0.15 |

MySQL provider: Oracle's is the only one for EF Core 10. Pomelo's latest release is 9.0.0 (EF Core 9), checked on
nuget.org on 12 November 2026.

```
dotnet build ef/Kernel.Ef.Run -c Release
dotnet ef/Kernel.Ef.Run/bin/Release/net10.0/Kernel.Ef.Run.dll x stress|cost|probe|block|definite|deadlock <engine> "<connection>" [key=value ...]
dotnet build ef/Kernel.Ef.Conformance -c Release
dotnet ef/Kernel.Ef.Conformance/bin/Release/net10.0/Kernel.Ef.Conformance.dll postgres|sqlserver|mysql "<server>" [--fence-in-batch] [--m5] semantic
```

`EngineRun.cs` lists the options. The stress test is the one of FINDINGS section 4: three nodes, two partitions (four
in the runs marked "no kills"), six workers on 150 ms leases, stalls of 150 to 500 ms, a busy worker session killed
every 20 to 60 ms (`pg_terminate_backend`, `KILL`, `KILL CONNECTION`; SQLite has no sessions), an outside producer
that retries. Its end-state properties are the ones FINDINGS section 4 lists. Do not run the conformance suite on an
engine while a stress test runs there: the stress test kills the suite's `kernel-worker` sessions too (two suite runs
were discarded for that).

## Results on the merged kernel (`a4f7c49b65` merged, fixes applied). Run

40 s and 5,000 outside messages unless the row says otherwise. Resets are fenced commits plus commits with an unknown
outcome. Deadlocks are as the engine counted them, with where the store saw the victim.

| Engine | Isolation | Fence | Result | Deadlocks | Retries | Resets | Takeovers | Changed for it |
|---|---|---|---|---|---|---|---|---|
| PostgreSQL | READ COMMITTED | checked | holds | 0 | 0 | 667 | 320, 347 | E4 |
| PostgreSQL | READ COMMITTED | batch | holds | 0 | 0 | 452 | 223, 229 | E1, E4 |
| PostgreSQL | READ COMMITTED | batch, E1 off (`m5=1`) | holds; 708 commits answered *definite* | 0 | 0 | 194 | 215, 209 | M5 reproduced |
| SQL Server | RCSI | checked | holds | 0 | 0 | 758 | 401, 362 | E5, E7, E8 |
| SQL Server | RCSI | batch | holds | **127, all takes** (E9) | 0 | 1,081 | 578, 518 | E1, E5, E7, E8 |
| SQL Server | locking READ COMMITTED | checked | holds | 0 | 0 | 731 | 356, 385 | E7, E8 |
| SQL Server | locking, poll `READPAST` | batch | holds | 15, all takes | 0 | 959 | 470, 501 | (E6) |
| MySQL | REPEATABLE READ | checked | holds | 0 | 0 | 664 | 348, 343 | E7, E8 |
| MySQL | READ COMMITTED | checked | holds | 0 | 0 | 735 | 387, 404 | E7, E8 |
| MySQL | REPEATABLE READ | batch | holds | 1, a take | 0 | 611 | 306, 320 | E1, E7, E8 |
| SQLite | WAL, one process | checked | holds | 0 | 0 | 984 | 485, 499 | E4 |
| SQLite | rollback journal, two processes, 1 s timeout | checked | holds; 26 takes and 7 commits answered BUSY, the commits retried | 0 | 7 | 1,464 + B's | 1,391, 1,349 | E4 |

The same tests on the kernel of `301c3727c9`, before the merge (all hold unless said):

| Engine | Isolation | Fence | Result | Deadlocks | Retries | Resets |
|---|---|---|---|---|---|---|
| PostgreSQL | READ COMMITTED | batch, 40 s, 5,000 | holds | 0 | 0 | 527 |
| PostgreSQL | READ COMMITTED | batch, 60 s, 8,000 | holds | 0 | 0 | 788 |
| SQL Server | locking | checked | holds | 0 | 0 | 658 |
| SQL Server | locking | batch | holds | 12, all polls (E5) | 0 | 737 |
| SQL Server | locking, no kills, 4 partitions, 8,000 | checked | holds; a commit waited **11.4 s** | 4 polls + 1 harness count (E5) | 0 | 439 |
| SQL Server | RCSI | checked | holds | 0 | 0 | 816 |
| SQL Server | RCSI | batch | holds | 0 | 0 | 912 |
| SQL Server | RCSI, no kills, 4 partitions, 8,000 | checked | holds; longest commit 232 ms | 0 | 0 | 603 |
| SQL Server | locking, poll `READPAST` | checked | holds | 0 | 0 | 808 |
| SQL Server | locking, poll `READPAST` | batch | **2 of 2 runs failed**: 370 and 1,708 violations (E6) | 0 | 0 | 963 |
| MySQL | REPEATABLE READ | checked | holds | 0 | 0 | 627 |
| MySQL | REPEATABLE READ, no kills, 4 partitions, 8,000 | checked | holds | 0 | 0 | 748 |
| MySQL | READ COMMITTED | checked | holds | 0 | 0 | 671 |
| MySQL | REPEATABLE READ | batch | holds | 0 | 0 | 687 |
| SQLite | WAL, one process | checked | holds | 0 | 0 | 449 |
| SQLite | rollback journal, one process | checked | holds; 95 takes answered BUSY | 0 | 0 | 849 |
| SQLite | WAL, two processes | checked | holds | 0 | 0 | 305 + 299 |
| SQLite | rollback journal, two processes, 1 s timeout | checked | holds; 80 takes and 1 commit answered BUSY, the commit retried | 0 | 1 | 643 |

On the merged kernel MySQL REPEATABLE READ with the checked fence, before E8 was fixed, held with **1 deadlock**: the
graph is E8's.

### The fence broken on purpose. Run

15 s, 1,500 messages, `unfenced=1` (the fence's conditions are dropped; for the fence in the batch the tokens are read
from the row, so the check always passes).

| Engine | Kernel | Fence | Result |
|---|---|---|---|
| PostgreSQL | before merge | checked | 179 violations: I6 (659 journal rows at position 637), I3/I4, I11, instances missing |
| PostgreSQL | before merge | batch | 251 violations: I6, I3/I4, instances missing |
| PostgreSQL | merged | batch | 554 violations |
| SQL Server | before merge | checked | 575 violations: I6 on both partitions, messages left |
| SQL Server | merged | checked | 158 violations |
| MySQL | before merge | checked | 1 violation, I11 |
| MySQL | merged | checked | 328 violations |
| SQLite | before merge | checked | 7 violations, all I11 |
| SQLite | merged | checked | 106 violations: I6, I3/I4, I11, messages and a subscription left |

The test can fail on every engine. On MySQL and SQLite before the merge it failed narrowly (1 and 7 violations).

### The store conformance suite. Run

`conformance/Kernel.Store.Conformance` (merged from `6097f0174b`), 46 semantic cases, against the merged store with
the fixes. The runner `ef/Kernel.Ef.Conformance` now takes SQL Server and MySQL.

| Store | Required | Reference |
|---|---|---|
| PostgreSQL, checked | 43 pass | 3 differ (as MONGO.md: subscribe twice, unsubscribe what is not there, seqs of non-fixed-width ids) |
| PostgreSQL, batch | 43 pass | the same 3 |
| PostgreSQL, batch, E1 off (`--m5`) | **1 fails**: concurrent fence, `PK_Journal` | the same 3 |
| SQL Server RCSI, checked | 42 pass, 1 skipped | the same 3 |
| SQL Server locking, checked | 42 pass, 1 skipped | the same 3 |
| SQL Server RCSI, batch | 42 pass, 1 skipped | the same 3 |
| SQL Server, batch, E1 off | **1 fails**: concurrent fence, `PK_Journal` (0, 1) | the same 3 |
| SQL Server, fence broken | 5 fail (old epoch, wrong position, stale beat, concurrent fence, take beside a commit) | |
| MySQL REPEATABLE READ, checked | 42 pass, 1 skipped | the same 3 |
| MySQL READ COMMITTED, checked | 42 pass, 1 skipped | the same 3 |
| MySQL, batch | 42 pass, 1 skipped | the same 3 |
| MySQL, batch, E1 off | **1 fails**: concurrent fence, `Duplicate entry '0-1' for key 'Journal.PRIMARY'` | the same 3 |
| MySQL, fence broken | 5 fail, the same 5 | |
| SQLite, checked and batch | 43 pass | 2 differ |
| SQLite, batch, E1 off | **1 fails**: concurrent fence | 2 differ |

Skipped on SQL Server and MySQL: "a row the store refuses". Both store any string an instance row holds, so the
runner has no refused row to offer (PostgreSQL refuses a NUL).

## What it costs. Run

Round trips counted on the TCP stream by a proxy in front of the database (`proxy=1`, class `Wire`): a round trip
starts each time the client sends after the server last spoke. The cost harness also counts the way FINDINGS
section 5 does; where the two disagree, the proxy is right (E4, E10). After the fixes:

| Run | PG checked | PG batch | SQL Server checked | SQL Server batch | MySQL checked | MySQL batch | SQLite |
|---|---:|---:|---:|---:|---:|---:|---:|
| Ten no-ops, ephemeral, on the owner | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| Ten no-ops, balanced, on the owner | 3 | 2 | 4 | 3 | 7 | 6 | 5 |
| Ten no-ops, durable, on the owner | 30 | 20 | 40 | 30 | 70 | 60 | 77 |
| Ten no-ops, ephemeral, from another node | 9 | 7 | 11 | 9 | 23 | 21 | 11 |
| Ten no-ops, balanced, from another node | 9 | 7 | 11 | 9 | 23 | 21 | 13 |
| Ten no-ops, durable, from another node | 36 | 25 | 47 | 36 | 86 | 75 | 85 |
| Parent with three children | 12 | 8 | 16 | 12 | 28 | 24 | 49 |
| 100 balanced started at once | 3 | 2 | 4 | 3 | 7 | 6 | 203 |
| 1,000 balanced started at once | 4 | 4 to 5 | 11 | 10 | 8 | 8 | 2,003 |
| 100 balanced, starts by poll | 8 | 6 | 10 | 8 | 20 | 18 | 408 |
| 1,000 balanced, starts by poll | 10 | 10 | 19 | 17 | 24 | 22 | 4,008 |

What a round trip is, per provider:

- **PostgreSQL (Npgsql):** one per command and one per COMMIT; BEGIN rides with the first command. Measured equal to
  FINDINGS section 5 once EF's savepoints are off (E4); with them on, every commit costs two more.
- **SQL Server (SqlClient):** BEGIN TRANSACTION is a request of its own, so a commit is BEGIN, the fence, the
  SaveChanges, COMMIT: four with the checked fence, three in the batch. A request larger than a TDS packet is several
  network reads on the server (`sys.dm_exec_connections.num_reads`: 24 for the 100-instance flush) but one round trip.
- **MySQL (Connector/NET):** every connection taken from the pool sends COM_PING and COM_INIT_DB, and every transaction
  sends SET TRANSACTION ISOLATION LEVEL and BEGIN apart: six round trips for a commit with the fence in the batch,
  seven with it checked (E10). `performance_schema` counts statements, not requests (a two-statement fence is two).
- **SQLite:** no wire. Calls into the engine: BEGIN, each command, COMMIT. EF sends one command per row.

Before the fixes, on the same runs: PostgreSQL balanced 5 and 4, durable 50 and 40 (E4); SQL Server 100 started at
once 8 and 7, 1,000 at once 51 and 50 (E7); MySQL 100 at once 11 and 10 (E7).

## Findings

### E1 (= MONGO.md M5). With the fence in the batch, a stale owner is told "duplicate key", not "fenced". Run, all four engines

- **Where:** every engine, the fence in the batch. Found by the agent before me (`301c3727c9`) and fixed in
  `EfStore`; the MongoDB agent found it independently on the EF store of `a4f7c49b65`, which predates that fix.
- **Trace:** EF sends a SaveChanges' statements in its own order; `Partitions` sorts after `Journal` and `Messages`. A
  stale owner's journal row at position n+1, or a message id its successor already wrote, fails with
  `23505 duplicate key value violates unique constraint "PK_Journal"` (PostgreSQL), `2627 ... 'PK_Journal' ... (0, 1)`
  (SQL Server), `1062 Duplicate entry '0-1' for key 'Journal.PRIMARY'` (MySQL) before the fence's row count is read.
- **Sentence:** "A definite error (a value too long, a key violation, a deadlock victim): no reset ... the owner
  commits the batch's instances one by one and parks the one that fails."
- **Reproduced:** the conformance case "of many concurrent commits at one position, exactly one lands" fails with E1
  off (`--m5`) on PostgreSQL, SQL Server, MySQL and SQLite, and passes with it. In the stress test with E1 off
  (`m5=1`, PostgreSQL, fence in the batch) 708 commits answered *definite*; the end state still held, because every
  one-by-one commit and every park was itself fenced off and the owner reset, as MONGO.md reasoned. With E1 on, 233
  key violations in one run (31 on SQL Server, 201 on MySQL) were reported as the fence.
- **Fix, in `EfStore.CommitAsync` (comment names E1 and M5):** after a failure that says the batch did not happen
  (definite or transient), read the partition row; if epoch and position are no longer this owner's, answer "not
  landed". An unknown outcome stays unknown: that batch may have landed and moved the position itself. Epoch and
  position never go back, so a fence that holds after the failure held during it. **SQL Server and MySQL have the same
  problem, and the same fix covers them.** The checked fence is not affected.

### E2. SqlClient does not open a connection in invariant-globalization mode. Reasoned, not re-run

`InvariantGlobalization` is false in `Kernel.Ef.Run.csproj` and now `Kernel.Ef.Conformance.csproj`.

### E3. Connector/NET sends `SET TRANSACTION ISOLATION LEVEL` before every BEGIN. Run

Seen in `performance_schema` (one `set_option` per transaction) and on the wire (a round trip of its own). The level
must be passed at BEGIN (`Engine.BeginAsync`); a session setting is overwritten.

### E4. EF's savepoints cost two round trips per commit on PostgreSQL. Run

- **Where:** PostgreSQL (and one per commit on SQL Server, two on MySQL; on SQLite two engine calls).
- **Evidence:** proxy count, balanced on the owner, fence in the batch: 4 with EF's default, 2 with savepoints off;
  durable 40 against 20. The earlier comment said Npgsql sends the savepoint with the next command; it does not.
- **Sentence:** the cost table of the sketch ("2 (3 with the fence as its own statement)") and FINDINGS section 5
  hold only with savepoints off. The interceptor of section 5 does not see savepoints.
- **Fix:** `AutoSavepointsEnabled = false` for every commit (`EfStore`, `Engine.BeginAsync`). A failed commit is
  rolled back whole, so the savepoint never served.

### E5. SQL Server, locking READ COMMITTED: the poll waits behind writers and deadlocks with commits. Run

- **Probe (`x block`):** with a transaction holding an uncommitted insert, an uncommitted mark of the oldest row or an
  uncommitted delete, the poll does not answer in 3 s; under RCSI it answers in 1 to 10 ms with the committed rows. A
  poll stopped by `TOP` before it reaches the locked rows (5,000 rows ahead) does not wait.
- **Deadlock graph (`.out/mssql-deadlock-graphs-all.xml`):** the poll (`SELECT TOP (@p3) ...`, victim) holds S on a
  `PK_Messages` key and waits for S on another; a commit's SaveChanges holds X on that one and wants X on the first.
  The poll keeps shared locks on rows with `nvarchar(max)` values until the statement ends. Three-way variants with
  two polls. The waiters waited up to 11,336 ms: the deadlock monitor runs in rounds of up to 5 s.
- **Sentence:** "Polling: ... A plain read." On SQL Server under locking it is not plain: it blocks and is blocked.
- **Fix:** `READ_COMMITTED_SNAPSHOT ON`, set by `SqlServerEngine.CreatedAsync`, on unless `rcsi=0`. Rerun without
  kills: 0 deadlocks, longest commit 232 ms (11,389 ms before), poll 99th percentile 7.6 ms (49.5 ms before).

### E6. SQL Server, `READPAST` on the poll. Run, not explained to the line

- The probe shows `READPAST` skips a row whose mark is uncommitted and returns the next rows of the same target.
- With the checked fence a stale owner's commit holds the partition row before it touches a message, and the take
  waits on that row, so a poll can only skip uncommitted inserts or the owner's own in-flight commit. Reasoned: safe;
  run: holds.
- With the fence in the batch a stale owner locks message rows before its fence: two of two runs on the old kernel
  ended with a partition that stopped (a due timer on trial at the current epoch never ran; 313 messages left). On the
  merged kernel one run held. Not diagnosed to a line; the trial logic changed in the merge.
- **Fix:** none needed; do not use `READPAST`. RCSI (E5) is the fix for the poll.

### E7. SQL Server and MySQL put at most 42 statements in one command. Run

- 100 instances started at once were five commands on SQL Server (seven round trips with the fence in the batch),
  1,000 were 48. **Sentence:** "100 instance-commits cost the same round trips as one."
- **Fix:** `MaxBatchSize(1000)` in the provider options (`EngineRun.cs`, the conformance runner). SQL Server's provider
  still splits by its 2,100-parameter limit: 1,000 instances are eight commands.
- **Parameter limits and lock escalation, run:** a flush of 3,000 instances, and 3,000 starts arriving by poll (a
  `LoadAsync` of 3,000 ids), work on SQL Server. The largest commit held 9,001 key locks and no object lock: escalation
  is decided per statement and table at about 5,000, and no statement came near. The sketch's cap on rows per flush is
  still needed for this reason, not proven by it.

### E8. The merged kernel's take scans the whole message table. Run

- **Where:** the take of `a4f7c49b65`: `UPDATE "Messages" SET "Attempts" = "Attempts" + 1 WHERE "Partition" = p AND
  "Trial" = e AND "Done" = 0` has no index to use.
- **MySQL, REPEATABLE READ, deadlock (`.out/m-mysql-rr-deadlock.txt`):** the take holds `lock_mode X` (next-key) on
  4,622 rows of `Messages.PRIMARY`, every partition's; a commit of partition 0 inserting `0/par4666#1.0000` waits for
  an insert-intention gap lock inside that range, while the take waits for the commit's new row. InnoDB rolls back the
  commit, which retries. The review's gap-lock prediction was for draft 1's claim; it comes back with this statement.
- **SQL Server:** the same statement takes an update lock on every row it passes and waits behind every partition's
  uncommitted inserts; take median 6.3 ms against 0.9 ms before the merge.
- **Sentence:** "One node owns a partition at a time and is its only writer" (partitions do not touch each other).
- **Fix (`Engine.Model`, `MySqlEngine.TakeIsolation`):** an index on `(Partition, Trial)` on SQL Server and MySQL, and
  the take at READ COMMITTED on MySQL, where even the indexed update takes a gap lock before the first row it changes.
  Rerun: MySQL REPEATABLE READ checked 0 deadlocks; SQL Server and MySQL all hold. PostgreSQL locks only the rows it
  changes and needs neither (reasoned); the index would still save the scan.

### E9. SQL Server, fence in the batch, merged kernel: the take deadlocks with a stale commit. Run; fix described, not applied

- **Graph (`.out/f-mssql-deadlock-graphs.xml`):** a stale owner's SaveChanges holds X on a `PK_Messages` key (a seen
  or trial mark) and waits for U on `PK_Partitions` (its fence comes last in EF's order); the take holds X on
  `PK_Partitions` and waits for U on that message key, in the attempts update of E8. 127 in one run, victims all
  takes; one on MySQL. Waits up to 3 s.
- **Harm:** a take that loses is "not taken" and is tried again; nothing wrong lands (end state holds). It costs
  availability during takeovers.
- **Sentence:** "The row lock on the partition row orders it against a takeover." With the fence in the batch it does
  not come first.
- **Smallest fix:** use the checked fence on SQL Server (0 take deadlocks), or make the fence the first statement of
  the batch (EF orders by table, so that needs the fence as its own command, which is the checked fence). Changing the
  take's order would change the protocol shared by all engines; not done.

### E10. MySQL's driver costs four round trips per commit more than PostgreSQL's. Run; not fixed

- COM_PING and COM_INIT_DB at every pool checkout, SET TRANSACTION and BEGIN apart (E3). `connectionreset=true` adds a
  COM_CHANGE_USER; `false` (the default) removes nothing.
- **Sentence:** the sketch's cost table (2 round trips per commit) is a PostgreSQL figure. On MySQL it is 6.
- **Smallest fix, reasoned:** keep one open connection per worker instead of one per store call, or a driver that
  pipelines (MySqlConnector, once a provider for EF Core 10 uses it).

### Not a finding of the engines: a batch that names one message twice. Run

Before the merge, 1 or 2 times per run on PostgreSQL, SQL Server and MySQL, a commit carried the same message id in
`Seen` and in `Sent` (`0/par469#1.0000 in seen+sent`). EF refuses to track the id twice, the store answers *unknown*,
and the owner resets: safe, wasted. After the merge it appeared once, in a broken-fence run. Kernel side, reasoned: a
turn whose revision was already committed ran again from a stale load.

## The review's predictions

| Prediction | Result |
|---|---|
| The poll blocks on SQL Server without `READPAST` or RCSI | **Confirmed**, run (E5); and it deadlocks with commits |
| `READPAST` is a fix | Run: holds with the checked fence; failed twice with the fence in the batch on the old kernel (E6). RCSI instead |
| Gap-lock deadlocks on MySQL under REPEATABLE READ | **Not with draft 2's protocol**: 0 in four runs, one without kills; the claim UPDATE they were predicted for is gone. **Back with the merged kernel's take** (E8), run, fixed |
| No `RETURNING` on MySQL | Handled: take as UPDATE then SELECT in one transaction (the row lock is held); the fence as UPDATE plus `SELECT ROW_COUNT()` in one command, counting matched rows (`UseAffectedRows=false`). An UPDATE that changes no value reports 1 row on every engine (probe) |
| `seq` order inside one commit (MySQL) | Run, probe: in send order for 12, 1,000 and 3,000 messages with fixed-width ids, on SQL Server and MySQL as on PostgreSQL; not with non-fixed-width ids (F8). SQL Server assigns them through `MERGE ... OUTPUT`, whose identity order is not documented; it held in every probe (reasoned risk) |
| `SQLITE_BUSY` | Run. Microsoft.Data.Sqlite retries BUSY itself until the command timeout (30 s by default), so BUSY reaches the store only with a short timeout or as an immediate BUSY (two writers both holding SHARED). Before the merge 95 and 80 takes answered BUSY (not taken, tried again) and 1 commit (retried as the same batch); after it, 26 takes and 7 commits. The end state held each time. WAL with one or two processes: no BUSY reached the store; waits show as commits of 150 to 600 ms |
| BUSY means retry, not reset | **Holds**: a BUSY commit did not happen (the transaction is rolled back before the retry), so retrying the batch is right. Reasoned: `SQLITE_BUSY_SNAPSHOT` in WAL needs a new transaction, which the retry is |
| Parameter limits (2,100 on SQL Server) | Not hit: EF splits commands by parameter count; `Contains` over 3,000 ids worked (E7) |
| Lock escalation | Not seen at 9,001 key locks per commit (E7) |
| A key violation inside a transaction without `XACT_ABORT` | Run (`x definite`): with and without `XACT_ABORT`, a batch with an existing instance or a 602-character id leaves nothing behind, and the one-by-one commits land the rest. The store never sends COMMIT after an error, so the setting does not matter |
| Deadlocks between a commit and other partitions' message inserts | Not seen on SQL Server; on MySQL only through E8 |

## What was not run

- Wire capture with a packet tool: the proxy counts request bursts, not packets; Npgsql's and SqlClient's
  behaviour inside a burst is inferred.
- SQL Server on native hardware: it ran under amd64 emulation. Timings are slower than real; counts are not affected.
- SNAPSHOT isolation on SQL Server, MySQL's SERIALIZABLE, PostgreSQL REPEATABLE READ.
- The fence broken on purpose with the fence in the batch on SQL Server, MySQL and SQLite (only on PostgreSQL).
- The kernel side of the double-named message and of E6 is not diagnosed.
