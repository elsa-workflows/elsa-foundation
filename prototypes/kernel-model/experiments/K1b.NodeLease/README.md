# K1b NL-9: take, heartbeat and commit together (slice 2b, 19 November 2026)

This experiment runs acceptance case NL-9 of [ownership and fencing](../../../../docs/architecture/ownership-and-fencing.md#acceptance-cases-node-lease)
on the prototype's PostgreSQL, SQL Server and MySQL paths. Under the node lease's lock order no cycle forms:

- a take reads the owners' node records with a plain read first, then writes its own record, then locks partition rows
  in ascending id;
- a heartbeat writes its own record, then reads partition rows;
- a commit locks partition rows only.

Each of the two NL-9 mutations must deadlock where its engine's locks close the cycle. Provider qualification is K4.

The probe is `x nodelease` in `ef/Kernel.Ef.Run/NodeLeaseRun.cs`. The store is `EfStore` with the owned commit, fence
in the batch, and the switch `NodeLease` on.

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
- **`mutation=subquery`** (`EfStore.NodeReadInUpdate`): the take reads the node records as a subquery of its update,
  after it has locked partition rows.
- **`mutation=rr`** (`EfStore.RepeatableReadTake`): the take's transaction runs at REPEATABLE READ.

Recorded per run:
- whether the sessions were waiting before the release;
- each call's answer;
- the engine's deadlock counter (`pg_stat_database.deadlocks`, SQL Server's `Number of Deadlocks/sec`, InnoDB's
  `lock_deadlocks`);
- the owner and epoch of partitions 1 to 5.

```
dotnet build ef/Kernel.Ef.Run -c Release
dotnet ef/Kernel.Ef.Run/bin/Release/net10.0/Kernel.Ef.Run.dll x nodelease postgres|sqlserver|mysql "<connection with Database=...>" case=heartbeat|mutual [mutation=subquery|rr] [rcsi=0]
```

## Results (`nl9.txt`)

Local Docker on arm64: `postgres:17`; `mcr.microsoft.com/mssql/server:2022-latest` (amd64, emulated, run as root on
tmpfs); `mysql:8.4`. MySQL's store runs at REPEATABLE READ and its take at READ COMMITTED (NL-C1). The log is the run
of the commit that keeps it, with take victims classified as transient. Two earlier runs (before and after merging 2a's
review head) had the same outcomes in all 24 runs, apart from which take the engine chose as the victim in the mutual
negatives, and with the victims' errors unclassified.

| Case | Engine | Take | Forced | Deadlocks | Outcome | Time |
|---|---|---|---|---|---|---|
| heartbeat | SQL Server, locking | as built | yes | 0 | p's heartbeat lands first; q takes 1 and leaves the renewed p's 2 | 185 ms |
| heartbeat | SQL Server, locking | subquery | yes | 1 | p's heartbeat is the victim (1205); q takes 1 and 2 | 2,060 ms |
| heartbeat | SQL Server, locking | rr | yes | 0 | as built | 151 ms |
| heartbeat | PostgreSQL, SQL Server (RCSI), MySQL | any | no | 0 | reads take no locks, so the heartbeat never waits | about 20 s (two 10 s waits) |
| mutual | every engine | as built | yes | 0 | a takes 3 and 4, b takes 1 and 2 | 85 to 125 ms |
| mutual | SQL Server, locking | subquery | yes | 1 | b is the victim (Transient 1205); a takes 3 and 4 | 4,766 ms |
| mutual | SQL Server (RCSI) | rr | yes | 1 | b is the victim (Transient 1205); a takes 3 and 4 | 816 ms |
| mutual | SQL Server, locking | rr | yes | 1 | b is the victim (Transient 1205); a takes 3 and 4 | 2,708 ms |
| mutual | PostgreSQL, MySQL; SQL Server (RCSI) with subquery | negatives | yes | 0 | both takes land | 77 to 116 ms |

**The store as built: no deadlock on any engine, in either case.** In every run, every answer was landed, taken or
listed. Under SQL Server's locking READ COMMITTED the take's read waits behind p's heartbeat, which holds its own node
record. Its read holds nothing, so the heartbeat completes; q then sees p renewed and leaves p's partition 2.

**The negatives deadlock where the design says they do, and each engine broke the cycle:**
- the subquery read under SQL Server without RCSI, in both cases;
- the REPEATABLE READ take on SQL Server with and without RCSI (REPEATABLE READ holds its shared locks either way),
  in the mutual case.

**The verdict (coordinator, 19 November): SQL Server's deadlocks are the proof for both mutations.** Each mutation
targets an engine that takes a lock for the read. On the others the cycle cannot form, because the read takes no lock:

**Where the negatives do not deadlock, the engine takes no lock for the read.**
- PostgreSQL reads the node records from its snapshot, at any level.
- MySQL takes a consistent read for a plain SELECT. At READ COMMITTED, which NL-C1 sets for the take, it does the same
  for a subquery inside an UPDATE, so the subquery mutation reads without locking there. MySQL's subquery is a locking
  read only at REPEATABLE READ, and NL-C1 rules that level out for the take.
- SQL Server with RCSI reads the subquery from row versions.

**The heartbeat case is forced only under SQL Server's locking READ COMMITTED.** On PostgreSQL, SQL Server with RCSI
and MySQL, the heartbeat's read of the partition rows takes no lock, so it never waits at the held row 0. Those runs are
recorded as "not forced" and run unforced, for the same reason: the cycle the case looks for cannot form there.

Under the node lease, a take whose transaction is a deadlock victim or timed out on a lock fails as a transient
`StoreException`, as on the commit path (`EfStore.TransientTake`), and is taken again at the next round. The log shows
it as `failed: Transient mssql 1205`.

Safety held in every run:
- every partition ends with exactly one owner;
- each deadlock victim's transaction rolled back whole: the losing take's rows keep their old owner and epoch.
