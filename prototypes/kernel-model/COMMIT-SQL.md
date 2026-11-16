# The owned commit: hand-written commit SQL on four engines (round 5 B, 16 November 2026)

Status: **built and measured on PostgreSQL, SQL Server, SQLite and MySQL; the default commit writer on all four.** The
owner's decision of 16 November: replace EF's commit path with a commit the store writes itself, on all four engines,
while EF keeps the schema, the migrations and the cold paths (reads, admin, bootstrap). The code is
`ef/Kernel.EntityFramework/EfStore.Owned.cs` (the shape of the commit, the same on every engine) and `CommitDialect.cs`,
`SqliteCommitDialect.cs`, `MySqlCommitDialect.cs` (what differs per engine). `CommitSql.EntityFramework` keeps the old
path as the reference (`commit=ef` in `EngineRun`, `--ef-commit` in the conformance runner). Nothing above the store
changed: `IKernelStore`, the turn contract and layer 3 see the same `CommitBatch` and `Committed`.

## What the owned commit is

One transaction, one command per round trip, through EF's connection and transaction (`IRelationalCommand`), so the
store's interceptors (the cost counters) and the engine's isolation settings apply as before. The commit's semantics
are `EfStore.CommitOnceAsync`'s, restated:

1. **The fence first.** `UPDATE "Partitions" SET "Position" = new, "LeaseUntil" = now + lease WHERE ("Id", "Epoch",
   "Position")` is what the owner believes, with an affected-row check. *Checked*, it is a round trip of its own and
   nothing else is sent until it held. *In the batch*, it is the first statement of the one command, so the partition
   row is the first lock the commit takes, as in the take (E9). The time comes from the database in both modes (EF's
   in-batch fence took it from the client's clock).
2. **k fence rows as one statement.** The fence is shaped for the Worker refactor to range ownership (PARTITIONS.md,
   the owner's decision of 16 November: one fence row per virtual partition, 1,024 of them): `FenceRow(Id, Epoch,
   Position, NewPosition)` per row, one `UPDATE ... FROM (VALUES ...)` (SQL Server: `UPDATE p ... FROM "Partitions" p
   JOIN (VALUES ...)`; MySQL and SQLite: a derived table of `UNION ALL`), the rows locked in ascending Id order first
   (`SELECT ... ORDER BY "Id" FOR UPDATE`; SQL Server `WITH (UPDLOCK, ROWLOCK)`; SQLite has one writer), and one count
   that must equal k. One row that does not match refuses the whole statement. `EfStore.FenceAsync(rows, lease)` runs
   it alone; `x fence <engine>` is its probe: eight partitions fenced 1, 3 and 8 at a time, then 8 with one stale row,
   refused whole and no position moved, on all four engines. The kernel's `CommitBatch` still names one partition, so
   a commit fences one row today.
3. **Every row with its count checked.** Instance deletes and updates (`WHERE "Id" = ... AND "Revision" = expected`,
   and `AND "Id" = actor` when the turn names a provider-equal alias of the row, as `AliasUpdateGuard` did for EF),
   consumed messages (`DELETE ... WHERE key AND "EventMatch" IS NULL AND "Done" = false`), done, held, seen and trial
   marks, journal rows, sent messages. A count that does not match answers "not landed", as EF's
   `DbUpdateConcurrencyException` did; the transaction is rolled back by its disposal.
4. **Rows of one kind as one statement.** Inserts of instances, journal rows and messages, and deletes and marks of
   messages by key, are gathered across the batch's turns and written as one multi-row statement each (at most 1,000
   rows per statement, SQL Server's `VALUES` limit), in a fixed order: per table deletes, then updates, then inserts
   (as EF's SaveChanges ordered them); Instances, then Messages, Journal and Subscriptions. A flush of 1,000 new
   instances that consume their starts is three statements (fence, instances, messages) where EF sent 2,001.
5. **Subscriptions are idempotent statements** (`ON CONFLICT DO NOTHING`; `INSERT ... WHERE NOT EXISTS`; `ON DUPLICATE
   KEY UPDATE`; an unconditional `DELETE`), so FINDINGS 25.2's careful retry (a second round trip per wait when a
   subscription exists already) has no work in the owned commit.
6. **The set-based deletes** of a terminate (every wait of the instance) and a delete (its journal rows) come last, in
   the same command.
7. **Parameters.** Numbers and booleans are literals; strings and byte arrays are parameters typed as their column
   (EF's type mapping for the property), so an instance row is 9 parameters and a message 6. SQL Server allows 2,100
   per request; the store starts a new command at 2,000. The fence is in the first command, and a later command is sent
   only after the earlier one's counts were read.

How a count comes back, per engine:

| Engine | Count of a statement | Seq of a sent message | A count that does not match |
|---|---|---|---|
| PostgreSQL | every statement ends in `RETURNING 1`: one result set per statement, its rows are the rows affected | `RETURNING "Seq"` on the multi-row insert, sorted ascending (identity is assigned in VALUES order) | the client stops reading at the first mismatch and rolls back; what later statements said is of no interest |
| SQL Server | the server checks: `IF @@ROWCOUNT <> n THROW 51011`; `SET NOCOUNT ON; SET XACT_ABORT ON` open every command; the fence's own check is `THROW 51010`, an alias that names another row `THROW 51029`; the client reads only the fence's `SELECT @kernel_fenced, now` and `OUTPUT inserted."Seq"` | `OUTPUT inserted."Seq"`, sorted ascending | a `SqlException` numbered 51010, 51011 or 51029 is "not landed"; the batch stopped at the failing check and XACT_ABORT rolled the transaction back |
| SQLite | as PostgreSQL (RETURNING, SQLite 3.35) | the store assigns `max("Seq") + i` for row i inside the insert, `RETURNING "Seq"` sorted ascending | as PostgreSQL |
| MySQL | every statement is followed by `SELECT ROW_COUNT()` (matched rows: `UseAffectedRows=false`), one result set per statement; Connector/NET's reader steps over the statements that return none | one insert per message, then `SELECT LAST_INSERT_ID(), ROW_COUNT()` | as PostgreSQL |

A stale owner with the fence in the batch: on SQL Server the server stops the batch at the fence's `THROW`, before any
other row is touched. On PostgreSQL, SQLite and MySQL the server runs the whole batch, but the client reads the fence's
result set first and stops there; a key violation a later statement raised (E1: the stale owner's journal row at the
successor's position) is consumed with the reader and never classified. So **E1 does not arise in the owned commit**:
the `--m5` mutation (a definite error beats a fence that did not match) changes nothing, and `KeyViolationsAsFence`
stays 0. The read of the partition row after a failed commit (`HoldsAsync`) is kept for the failures that remain: a
definite or transient error after the fence held is answered as such only when the fence still holds.

## Defaults

| Engine | Fence mode by default | Commit writer by default | Why |
|---|---|---|---|
| PostgreSQL | in the batch | owned | 2 round trips per commit; the fence's result is read first, so E1 cannot arise |
| SQL Server | **checked** (unchanged, E9, decided 13 November) | owned | the owned in-batch fence is the first lock of the batch and the E9 take deadlocks are gone (below), so the owner can move the default to the batch for one round trip less per commit; this round changes no fence default |
| SQLite | in the batch | owned | one writer; one command instead of one per row |
| MySQL | in the batch | owned | as PostgreSQL; the Oracle provider's partial-batch acknowledgement (E11) is not on this path |

`EfStore.CommitSql` chooses (`null`: the engine's default); `EfStore.FenceInBatch` as before.

## Evidence

Run on the branch heads of B1 to B4 (FINDINGS section 29 names them; B4's second commit `8c5e944a5` changes the
fence-count mutation only, and the pins that name it ran on that head), PostgreSQL 17, SQL Server 2022 (amd64 emulation
on arm64), MySQL 8.4 and SQLite in process, from the commands of README's "Databases" section with `commit=` and
`--ef-commit`/`--owned-commit` added. "Before" is main `093adf6eb` (EF SaveChanges), "after" the owned commit.

### Store conformance: 87 or 88 cases per engine and fence mode, unchanged

| Engine | checked, owned | in batch, owned | checked, EF | in batch, EF |
|---|---|---|---|---|
| PostgreSQL | 88 pass | 88 pass | 88 pass | 88 pass |
| SQL Server (RCSI) | 87 pass, 1 skipped | 87 pass, 1 skipped | 87 pass, 1 skipped | 87 pass, 1 skipped |
| SQLite | 87 pass, 1 skipped | 87 pass, 1 skipped | 87 pass, 1 skipped | 87 pass, 1 skipped |
| MySQL (REPEATABLE READ) | 87 pass, 1 skipped | 87 pass, 1 skipped | 87 pass, 1 skipped | 87 pass, 1 skipped |

The skipped case is "a row the store refuses" (only PostgreSQL refuses a NUL), as before.

### Round trips: the cost runs, EF → owned

Round trips on the TCP stream (`proxy=1`, the `tcp` column); SQLite: calls into the engine (BEGIN, commands, COMMIT).
Every row holds or drops.

| Run | postgres checked | postgres batch | sqlserver checked | sqlserver batch | sqlite checked | sqlite batch | mysql checked | mysql batch |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| ten no-ops, ephemeral, started on the owner | 0 → 0 | 0 → 0 | 0 → 0 | 0 → 0 | 0 → 0 | 0 → 0 | 0 → 0 | 0 → 0 |
| ten no-ops, balanced, started on the owner | 3 → 3 | 2 → 2 | 4 → 4 | 3 → 3 | 5 → 4 | 5 → 3 | 7 → 7 | 6 → 6 |
| ten no-ops, durable, started on the owner | 30 → 30 | 20 → 20 | 40 → 40 | 30 → 30 | 77 → 40 | 77 → 30 | 70 → 70 | 60 → 60 |
| ten no-ops, ephemeral, started from another node | 9 → 9 | 7 → 7 | 11 → 11 | 9 → 9 | 11 → 11 | 11 → 9 | 23 → 23 | 21 → 21 |
| ten no-ops, balanced, started from another node | 9 → 9 | 7 → 7 | 11 → 11 | 9 → 9 | 13 → 11 | 13 → 9 | 23 → 23 | 21 → 21 |
| ten no-ops, durable, started from another node | 36 → 36 | 25 → 25 | 47 → 47 | 36 → 36 | 85 → 47 | 85 → 36 | 86 → 86 | 75 → 75 |
| parent with three children, same partition | 12 → 12 | 8 → 8 | 21 → 21 | 16 → 16 | 57 → 16 | 57 → 12 | 38 → 38 | 33 → 33 |
| terminate a parent with three waiting children, from another node | 17 → 13 | 13 → 9 | 21 → 17 | 17 → 13 | 41 → 17 | 41 → 13 | 35 → 31 | 31 → 27 |
| delete that parent, from another node | 9 → 7 | 7 → 5 | 11 → 9 | 9 → 7 | 13 → 9 | 13 → 7 | 19 → 17 | 17 → 15 |
| a start subscription, then an event that starts an instance | 13 → 13 | 11 → 11 | 15 → 15 | 13 → 13 | 17 → 15 | 17 → 13 | 35 → 35 | 33 → 33 |
| a projector round over the journal so far | 3 → 3 | 3 → 3 | 6 → 6 | 6 → 6 | 7 → 7 | 7 → 7 | 13 → 13 | 13 → 13 |
| a heartbeat | 3 → 3 | 3 → 3 | 3 → 3 | 3 → 3 | 3 → 3 | 3 → 3 | 9 → 9 | 9 → 9 |
| 100 balanced instances started at once | 3 → 3 | 3 → 3 | 9 → 9 | 7 → 7 | 203 → 4 | 203 → 3 | 17 → 17 | 15 → 15 |
| 1000 balanced instances started at once | 4 → 3 | 5 → 3 | 18 → 12 | 16 → 10 | 2003 → 4 | 2003 → 3 | 18 → 17 | 17 → 15 |
| 100 balanced instances, their starts arriving by poll | 8 → 8 | 7 → 6 | 11 → 10 | 9 → 8 | 408 → 10 | 408 → 8 | 21 → 20 | 19 → 18 |
| 1000 balanced instances, their starts arriving by poll | 10 → 9 | 13 → 8 | 24 → 15 | 22 → 13 | 4008 → 10 | 4008 → 8 | 26 → 20 | 24 → 18 |

PostgreSQL statements (Execute frames on the wire) and commands per row, EF → owned:

| Run | statements, checked | statements, in batch | commands, checked | commands, in batch |
|---|---:|---:|---:|---:|
| ten no-ops, ephemeral, started on the owner | 0 → 0 | 0 → 0 | 0 → 0 | 0 → 0 |
| ten no-ops, balanced, started on the owner | 3 → 3 | 3 → 3 | 2 → 2 | 1 → 1 |
| ten no-ops, durable, started on the owner | 48 → 48 | 48 → 48 | 20 → 20 | 10 → 10 |
| ten no-ops, ephemeral, started from another node | 7 → 7 | 7 → 7 | 7 → 7 | 5 → 5 |
| ten no-ops, balanced, started from another node | 9 → 9 | 9 → 9 | 7 → 7 | 5 → 5 |
| ten no-ops, durable, started from another node | 54 → 54 | 54 → 54 | 25 → 25 | 14 → 14 |
| parent with three children, same partition | 46 → 21 | 46 → 21 | 8 → 8 | 4 → 4 |
| terminate a parent with three waiting children, from another node | 31 → 21 | 31 → 21 | 13 → 9 | 9 → 5 |
| delete that parent, from another node | 9 → 9 | 9 → 9 | 7 → 5 | 5 → 3 |
| a start subscription, then an event that starts an instance | 13 → 13 | 13 → 13 | 11 → 11 | 9 → 9 |
| a projector round over the journal so far | 3 → 3 | 3 → 3 | 3 → 3 | 3 → 3 |
| a heartbeat | 3 → 3 | 3 → 3 | 3 → 3 | 3 → 3 |
| 100 balanced instances started at once | 201 → 3 | 201 → 3 | 2 → 2 | 1 → 1 |
| 1000 balanced instances started at once | 2001 → 3 | 2001 → 3 | 3 → 2 | 3 → 1 |
| 100 balanced instances, their starts arriving by poll | 404 → 8 | 404 → 8 | 6 → 6 | 4 → 4 |
| 1000 balanced instances, their starts arriving by poll | 4004 → 8 | 4004 → 8 | 8 → 6 | 8 → 4 |

### Bytes per commit (WRITES.md), PostgreSQL, checked fence, EF → owned

Unchanged within the noise WRITES.md describes: 1,000 balanced instances started at once 711 → 711 B per instance
commit; their starts arriving by poll 1,953 → 1,954; ten durable no-ops 969 → 969; a balanced start from another node
1,656 → 1,632 (less "other", the padding and commit records of fewer statements); the parent with three children
1,192 → 1,192. The owned fence in the batch: 1,080, 969, 1,194 and 1,951 for the same rows.

### The stress test, 40 s and 5,000 messages (ENGINES.md's)

Every engine, both fence modes, EF (the harness fix of F-B1 applied) and owned: holds, 0 deadlocks; the E9 row is the
one exception, EF's. Resets are fenced commits plus commits of unknown outcome (killed sessions); SQLite has no sessions
to kill, so its resets are all fenced. The mutation rows are the pins (below).

| Engine | Fence | Commit | Mutation | Deadlocks | Resets | Definite | Result |
|---|---|---|---|---|---|---|---|
| postgres | batch | EF SaveChanges | fence broken (unfenced) | 0 | 335 (fenced 0, unknown 335) | 0 | holds |
| postgres | batch | EF SaveChanges |  | 0 | 321 (fenced 3, unknown 318) | 0 | holds |
| postgres | checked | EF SaveChanges | fence broken (unfenced) | 0 | 533 (fenced 0, unknown 533) | 0 | holds |
| postgres | checked | EF SaveChanges |  | 0 | 272 (fenced 0, unknown 272) | 0 | holds |
| postgres | batch | owned SQL | fence count ignored | 0 | 343 (fenced 17, unknown 326) | 58206 | 1708 violations |
| postgres | batch | owned SQL | E1 off (m5) | 0 | 250 (fenced 0, unknown 250) | 0 | holds |
| postgres | batch | owned SQL | fence broken (unfenced) | 0 | 189 (fenced 0, unknown 189) | 0 | holds |
| postgres | batch | owned SQL |  | 0 | 374 (fenced 0, unknown 374) | 0 | holds |
| postgres | checked | owned SQL | fence count ignored | 0 | 297 (fenced 0, unknown 297) | 0 | holds |
| postgres | checked | owned SQL | fence broken (unfenced) | 0 | 298 (fenced 0, unknown 298) | 0 | holds |
| postgres | checked | owned SQL |  | 0 | 469 (fenced 0, unknown 469) | 0 | holds |
| sqlserver | batch | EF SaveChanges |  | 74 | 1224 (fenced 91, unknown 1133) | 0 | 727 violations |
| sqlserver | checked | EF SaveChanges |  | 0 | 721 (fenced 4, unknown 717) | 0 | holds |
| sqlserver | batch | owned SQL | fence count ignored | 0 | 1028 (fenced 15, unknown 1013) | 8177 | 2191 violations |
| sqlserver | batch | owned SQL | E1 off (m5) | 0 | 1048 (fenced 16, unknown 1032) | 0 | holds |
| sqlserver | batch | owned SQL | fence broken (unfenced) | 0 | 1356 (fenced 13, unknown 1343) | 12458 | 2017 violations |
| sqlserver | batch | owned SQL |  | 0 | 1021 (fenced 45, unknown 976) | 0 | holds |
| sqlserver | checked | owned SQL | fence count ignored | 0 | 903 (fenced 1, unknown 902) | 13174 | 2157 violations |
| sqlserver | checked | owned SQL | fence broken (unfenced) | 0 | 811 (fenced 0, unknown 811) | 0 | holds |
| sqlserver | checked | owned SQL |  | 0 | 1009 (fenced 1, unknown 1008) | 0 | holds |
| sqlite | batch | EF SaveChanges |  | 0 | 280 (fenced 280, unknown 0) | 0 | holds |
| sqlite | checked | EF SaveChanges |  | 0 | 128 (fenced 128, unknown 0) | 0 | holds |
| sqlite | batch | owned SQL | fence count ignored | 0 | 420 (fenced 420, unknown 0) | 63441 | 2215 violations |
| sqlite | batch | owned SQL | E1 off (m5) | 0 | 158 (fenced 158, unknown 0) | 0 | holds |
| sqlite | batch | owned SQL | fence broken (unfenced) | 0 | 405 (fenced 405, unknown 0) | 51111 | 2135 violations |
| sqlite | batch | owned SQL |  | 0 | 283 (fenced 283, unknown 0) | 0 | holds |
| sqlite | checked | owned SQL | fence count ignored | 0 | 3 (fenced 3, unknown 0) | 54760 | 2196 violations |
| sqlite | checked | owned SQL | fence broken (unfenced) | 0 | 5 (fenced 5, unknown 0) | 59496 | 2110 violations |
| sqlite | checked | owned SQL |  | 0 | 212 (fenced 212, unknown 0) | 0 | holds |
| mysql | batch | EF SaveChanges |  | 0 | 457 (fenced 0, unknown 457) | 0 | holds |
| mysql | checked | EF SaveChanges |  | 0 | 437 (fenced 2, unknown 435) | 0 | holds |
| mysql | batch | owned SQL | fence count ignored | 0 | 589 (fenced 1, unknown 588) | 12471 | 686 violations |
| mysql | batch | owned SQL | E1 off (m5) | 0 | 437 (fenced 0, unknown 437) | 0 | holds |
| mysql | batch | owned SQL | fence broken (unfenced) | 0 | 487 (fenced 0, unknown 487) | 0 | holds |
| mysql | batch | owned SQL |  | 0 | 477 (fenced 0, unknown 477) | 0 | holds |
| mysql | checked | owned SQL | fence count ignored | 0 | 487 (fenced 0, unknown 487) | 0 | holds |
| mysql | checked | owned SQL | fence broken (unfenced) | 0 | 531 (fenced 0, unknown 531) | 0 | holds |
| mysql | checked | owned SQL |  | 0 | 736 (fenced 3, unknown 733) | 0 | holds |

### E9 on SQL Server: the owned in-batch fence

EF with the fence in the batch reproduces E9 as FINDINGS 25.1 recorded it: **74 deadlocks, every victim a take**, 1,224
resets, and the run did not drain within the default patience (727 "left behind" violations, none a wrong application).
The owned commit with the fence in the batch, same machine, same 40 s and 5,000 messages: **0 deadlocks, holds**, 1,021
resets (45 fenced, 976 unknown, the killed sessions), settled within the patience. The owned checked fence: 0
deadlocks, holds, as before. Why: the take locks the partition row first and the message rows second (the attempts
update of E8); EF's batch locked the message rows first (seen and trial marks, sent messages) and the partition row
last, because EF orders statements by table; the owned batch opens with the fence, so a stale commit waits on the
partition row before it holds any message row, and the cycle E9 describes cannot form. The k-row fence keeps the
order: the rows are locked in ascending Id order (the clustered key, the order a take of a range locks them) before the
UPDATE, so two commits over overlapping rows cannot wait for each other either (reasoned; the kernel fences one row
today).

### Mutations and pins

- **The fence broken on purpose** (`unfenced=1`, `semantic-broken`): the three semantic fence cases fail with the
  checked fence on every engine (3 of 3); with the fence in the batch 1 of 3 ("a take beside a commit in flight reads the
  true position") fails on the owned commit and 0 of 3 on EF's, where the journal's primary key is the backstop for the
  other two. The stress with the fence broken holds on PostgreSQL on both paths (FINDINGS 29 F-B2); on SQL Server the stress with the fence in the batch catches it (2,017 violations, 12,458 definite errors as stale
  rows collide) and with the checked fence holds; on SQLite it catches it in both modes (2,110 and 2,135 violations; its
  stalls produce stale commits, 128 to 283 fenced per clean run); on MySQL it holds in both modes, as on PostgreSQL.
- **The owned fence's count ignored** (`FenceIgnoresCount`: `--fence-ignores-count`, `fencecount=0`): the same three
  cases fail (3 of 3 checked, 1 of 3 in the batch) on every engine; the stress with the fence in the batch on PostgreSQL
  fails (I6 on both partitions, I3/I4, 1,708 violations, 58,206 definite errors as stale journal rows collide); the
  stress with the checked fence holds, as F-B2 says it must. On SQL Server and SQLite the stress catches it in both modes (SQL Server 2,157 and 2,191 violations; SQLite 2,196 and 2,215), on MySQL with the fence in the batch (686 violations) and not checked. On SQL Server the mutation had to skip the server's own `IF @@ROWCOUNT <> k THROW` as well as the client's read-back, or the server's check alone masked it (3 of 3 fence cases passed): FINDINGS 29 F-B7.
- **E1 off** (`--m5`, `m5=1`): changes nothing on the owned commit (3 of 3 fence cases pass on every engine; the stress
  holds with `KeyViolationsAsFence` 0), where EF's path failed 2 of 3 on PostgreSQL. E1 does not arise (above).
- **The kernel's own gates** (sim, mutations with the 43 and 9 pins, in-memory conformance, graph corpus): unchanged,
  no file under `src/` changed in this round; run once on the final tree (FINDINGS 29).

## Not done, and unverified

- The kernel's `CommitBatch` names one partition, so the k-row fence runs only in the probe `x fence`; the Worker
  refactor to range ownership (PARTITIONS.md, decision 5) is its consumer. The lock-order argument for k rows (the fence
  rows in ascending Id order before anything else, as a take of a range would take them) is reasoned, not run against a
  concurrent take of a range.
- Commit windows (`PgCommitWindow`, a bounded PostgreSQL experiment) are written by the owned fence's `UPDATE` as
  before, but no harness enables them against the EF store: unverified on the owned path.
- Values by reference (decision 10) is untouched: the schema shape did not change.
- The SQL Server run under amd64 emulation: counts are exact, times are not.
- The bytes per commit were measured on PostgreSQL only (WRITES.md's method needs `pg_walinspect`).
