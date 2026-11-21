# K1b NL-9: take, heartbeat and commit together (slice 2b, 19 November 2026)

This experiment runs acceptance case NL-9 of [ownership and fencing](../../../../docs/architecture/ownership-and-fencing.md#acceptance-cases-node-lease)
on the prototype's PostgreSQL, SQL Server and MySQL paths. Under the node lease's lock order no cycle forms:

- a take reads the owners' node records with a plain read first, then writes its own record, then locks partition rows
  in ascending id;
- a heartbeat writes its own record, then reads partition rows, and reads membership after its transaction;
- a commit locks partition rows only.

Each mutation must deadlock where its engine's locks close the cycle. Provider qualification is K4.

The probe is `x nodelease` in `ef/Kernel.Ef.Run/NodeLeaseRun.cs`. The probe uses `EfStore` with the owned commit and the fence in the batch.

## The cases

An outside transaction makes a write that it holds until release, so that each interleaving is forced. The probe waits
until the engine reports the sessions waiting, then releases the hold.

- **`case=heartbeat`:** one process's heartbeat, commit and gain take against another process's take.
  - Setup: r owns partition 1, and p owns 2 and 3. Both node leases have expired.
  - The hold writes partition row 0.
  - p's heartbeat writes p's record, and its read of the partition rows waits at row 0 where reads take locks.
  - q's take of {1, 2} is sent.
  - On release, p's commit in 3 and p's gain take of 5 are sent as well.
- **`case=mutual`:** two processes past their leases whose set takes cover each other's partitions.
  - Setup: a owns {1, 2} and b owns {3, 4}.
  - The hold writes both node records.
  - a's take of {3, 4} and b's take of {1, 2} are sent, then the hold is released.
- **`case=heartbeats`:** two heartbeats and the membership reads.
  - Setup: as `heartbeat`. The hold writes partition row 0.
  - p's and r's heartbeats both write their records and queue at row 0, then the hold is released.
- **`mutation=subquery`** (`EfStore.NodeReadInUpdate`): the take reads the node records as a subquery of its update,
  after it has locked partition rows.
- **`mutation=rr`** (`EfStore.RepeatableReadTake`): the take's transaction runs at REPEATABLE READ.
- **`mutation=membership`** (`EfStore.MembershipInHeartbeat`): the heartbeat reads the node count and the free list
  inside its transaction, while it holds its own record.
- **`mutation=rr-subquery`** on MySQL combines a take at REPEATABLE READ with the node-record lookup as a subquery
  of the update.
- **`mutation=share`** on PostgreSQL locks the other owners' node records with `FOR SHARE`, including expired
  records. It checks expiry after acquiring the locks.

**The exit code is the verdict.**
- As built, `expect=none` is the default and exits 1 on a deadlock, failure or hang. A negative (any `mutation=`)
  defaults to `expect=deadlock` and exits 1 unless it deadlocked. Explicit `expect=none` with a mutation exits 2
  before opening a connection.
- A mutual run, and every run on SQL Server without RCSI (where reads take locks), also exits 1 when its sessions were
  not waiting before the release.
- On PostgreSQL the victim's error decides, because `pg_stat_database.deadlocks` lags by up to a second.
- An unknown `case=`, `mutation=` or `expect=` value exits 2 before anything is opened.
- `nl9-wrong-expectations.txt` shows two runs given the wrong expectation, each exiting 1.

Recorded per run:
- whether the sessions were waiting before the release;
- each call's answer;
- the engine's deadlock counter (`pg_stat_database.deadlocks`, SQL Server's `Number of Deadlocks/sec`, InnoDB's
  `lock_deadlocks`);
- the owner and epoch of partitions 1 to 5.

```
dotnet build ef/Kernel.Ef.Run -c Release
dotnet ef/Kernel.Ef.Run/bin/Release/net10.0/Kernel.Ef.Run.dll x nodelease postgres|sqlserver|mysql "<connection with Database=...>" case=heartbeat|mutual|heartbeats [mutation=subquery|rr|membership|rr-subquery|share] [expect=none|deadlock] [rcsi=0]
```

## Results (`nl9.txt`)

Local Docker on arm64: `postgres:17`; `mcr.microsoft.com/mssql/server:2022-latest` (amd64, emulated, run as root on
tmpfs); `mysql:8.4`. MySQL's store runs at REPEATABLE READ and its take at READ COMMITTED (NL-C1). The log is the run
of the commit that keeps it: 32 runs, each with the expectation in the table, every one exiting 0. Three earlier runs
of the first 24 had the same outcomes, apart from which take the engine chose as victim in the mutual negatives.

| Case | Engine | Take | Expect | Forced | Outcome | Time |
|---|---|---|---|---|---|---|
| heartbeat | SQL Server, locking | as built | none | yes | p's heartbeat lands first; q takes 1 and leaves the renewed p's 2 | 167 ms |
| heartbeat | SQL Server, locking | subquery | deadlock | yes | p's heartbeat is the victim (1205); q takes 1 and 2 | 3,635 ms |
| heartbeat | SQL Server, locking | rr | none | yes | as built | 158 ms |
| heartbeat | PostgreSQL, SQL Server (RCSI), MySQL | any | none | no | reads take no locks, so the heartbeat never waits | about 20 s (two 10 s waits) |
| mutual | every engine | as built | none | yes | a takes 3 and 4, b takes 1 and 2 | 74 to 112 ms |
| mutual | SQL Server, locking | subquery | deadlock | yes | a is the victim (Transient 1205); b takes 1 and 2 | 3,546 ms |
| mutual | SQL Server (RCSI) | rr | deadlock | yes | b is the victim (Transient 1205); a takes 3 and 4 | 644 ms |
| mutual | SQL Server, locking | rr | deadlock | yes | b is the victim (Transient 1205); a takes 3 and 4 | 2,553 ms |
| mutual | PostgreSQL, MySQL; SQL Server (RCSI) with subquery | negatives | none | yes | both takes land | 63 to 117 ms |
| heartbeats | SQL Server, locking | as built | none | yes | both heartbeats list their partitions | 129 ms |
| heartbeats | SQL Server, locking | membership | deadlock | yes | p's heartbeat is the victim (1205) | 2,104 ms |
| heartbeats | PostgreSQL, SQL Server (RCSI), MySQL | any | none | no | reads take no locks, so neither heartbeat waits | about 20 s |

**The store as built: no deadlock on any engine, in any case.** In every run, every answer was landed, taken or
listed. Under SQL Server's locking READ COMMITTED the take's read waits behind p's heartbeat, which holds its own node
record. Its read holds nothing, so the heartbeat completes; q then sees p renewed and leaves p's partition 2.

**The negatives deadlock where the design says they do, and each engine broke the cycle:**
- the subquery read under SQL Server without RCSI, in both cases;
- the REPEATABLE READ take on SQL Server with and without RCSI (REPEATABLE READ holds its shared locks either way),
  in the mutual case;
- membership read inside the heartbeat's transaction, under SQL Server without RCSI: each heartbeat holds its own record
  and waits for the other's. As built, the membership reads run after the transaction and hold nothing.

**The verdict: the amended NL-C1 (owner card, 20 November).** The coordinator's reading of 19 November took SQL
Server's deadlocks as the proof for each mutation. The amended
[NL-C1](../../../../docs/program-goals/clean-slate-kernel.md#gate-2-conditions) asks every engine for a locking-read
mutation that deadlocks while the ordered take does not: SQL Server with the take at REPEATABLE READ, with and without
RCSI; MySQL at REPEATABLE READ with the node read as a subquery of the update; PostgreSQL with the node read taken
`FOR SHARE`. The [K1b evidence](../../../../docs/reports/clean-slate-kernel/k1b-evidence-2026-11-21.md#nl-9-ordered-and-locking-takes)
records the native runner and the added cases. The runs above predate those cases and show why the original mutations
deadlocked only on SQL Server:

**Where the negatives do not deadlock, the engine takes no lock for the read.**
- PostgreSQL reads the node records from its snapshot, at any level.
- MySQL takes a consistent read for a plain SELECT. At READ COMMITTED, which NL-C1 sets for the take, it does the same
  for a subquery inside an UPDATE, so the subquery mutation reads without locking there. MySQL's subquery is a locking
  read only at REPEATABLE READ, and NL-C1 rules that level out for the take.
- SQL Server with RCSI reads the subquery from row versions.

**The heartbeat cases are forced only under SQL Server's locking READ COMMITTED.** On PostgreSQL, SQL Server with RCSI
and MySQL, the heartbeat's read of the partition rows takes no lock, so it never waits at the held row 0. Those runs are
recorded as "not forced" and run unforced, for the same reason: the cycle the case looks for cannot form there.

Under the node lease, a take whose transaction is a deadlock victim or timed out on a lock fails as a transient
`StoreException`, as on the commit path (`EfStore.TransientTake`), and is taken again at the next round. The log shows
it as `failed: Transient mssql 1205`.

Safety held in every run:
- every partition ends with exactly one owner;
- each deadlock victim's transaction rolled back whole: a losing take's rows keep their old owner and epoch.
