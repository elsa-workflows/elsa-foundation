# K1 lock order: two overlapping range commits (slice 3, 18 November 2026)

The plan's K1 lock-order case on the prototype's PostgreSQL, SQL Server and MySQL paths: the k-row fence locks its rows
in ascending virtual-partition id, so two overlapping range commits complete with no deadlock. Provider qualification
is K4. The probe is `x lockorder` in `ef/Kernel.Ef.Run/EngineRun.cs`; the store is `EfStore` with the owned commit.

## The case

- One owner takes partitions 1, 2 and 3 with one set take.
- Commit X lists its fences 1, 2, 3 and commit Y lists them 3, 2, 1. Both are owned batches at the same picture, each
  writing one new instance per partition.
- An outside transaction holds row 2. X is sent, and the probe waits until the engine reports one session waiting for
  a row lock. Then Y is sent, and the probe waits for two. Then the hold is released.
- In ascending order (the store as built), Y queues behind X on row 1. X lands and Y is refused by its stale fence.
- With `unordered=1` (the `UnorderedFenceLocks` mutation, the negative), Y locks its rows in its listed order. Y holds
  row 3 while X holds rows 1 and 2, so the engine has to break a deadlock.
- Recorded per run: each commit's answer, the engine's own deadlock counter (`pg_stat_database.deadlocks`, SQL Server's
  `Number of Deadlocks/sec` counter, `INNODB_METRICS lock_deadlocks`), the E1 count (a deadlock or key error answered as
  a stale fence), the positions of 1, 2 and 3, and the rows written.

```
dotnet build ef/Kernel.Ef.Run -c Release
dotnet ef/Kernel.Ef.Run/bin/Release/net10.0/Kernel.Ef.Run.dll x lockorder postgres|sqlserver|mysql "<connection with Database=...>" fence=batch|checked [unordered=1]
```

## Results (`lockorder.log`)

Local Docker on arm64 (OrbStack): `postgres:17`; `mcr.microsoft.com/mssql/server:2022-CU14-ubuntu-22.04` (amd64,
emulated; the `2022-latest` image failed its setup step under emulation, OS error 31); `mysql:8.4`. Both sessions were
waiting before the release in every run, so each interleaving was forced.

| Engine | Fence | Lock order | X (1, 2, 3) | Y (3, 2, 1) | Deadlocks counted | Time |
|---|---|---|---|---|---|---|
| PostgreSQL 17 | batch | ascending | landed | not landed | 0 | 123 ms |
| PostgreSQL 17 | batch | batch order | not landed (E1) | landed | 1 | 1,140 ms |
| PostgreSQL 17 | checked | ascending | landed | not landed | 0 | 128 ms |
| PostgreSQL 17 | checked | batch order | Transient, 40P01 | landed | 1 | 1,173 ms |
| SQL Server 2022 (RCSI) | batch | ascending | landed | not landed | 0 | 179 ms |
| SQL Server 2022 (RCSI) | batch | batch order | Transient, 1205 | landed | 1 | 3,298 ms |
| SQL Server 2022 (RCSI) | checked | ascending | landed | not landed | 0 | 154 ms |
| SQL Server 2022 (RCSI) | checked | batch order | Transient, 1205 | landed | 1 | 2,406 ms |
| MySQL 8.4 (REPEATABLE READ) | batch | ascending | landed | not landed | 0 | 121 ms |
| MySQL 8.4 (REPEATABLE READ) | batch | batch order | landed | not landed (E1) | 1 | 150 ms |
| MySQL 8.4 (REPEATABLE READ) | checked | ascending | landed | not landed | 0 | 102 ms |
| MySQL 8.4 (REPEATABLE READ) | checked | batch order | landed | Transient, 1213 | 1 | 106 ms |

The log is the run at the slice's merged head (after slice 2a's `a1f83cc63`). An earlier run at `aefdd2211`, before
that merge, gave the same outcomes in all twelve rows but one: there, SQL Server's in-batch negative answered its
victim "not landed" (E1), with 2,620 ms.

- **Ascending order: no deadlock on any engine in either fence mode.** Exactly one commit landed, the other was
  answered "not landed", every position moved to 1, and only the lander's rows were written.
- **Batch order deadlocks on every engine.** Each engine broke it: PostgreSQL after `deadlock_timeout` (about 1 s), SQL
  Server's deadlock monitor after about 2.5 s, and InnoDB at once. With the checked fence the victim surfaces as a
  transient failure. With the fence in the batch the store rereads the victim's fences (E1, as built): if the other
  commit has landed by then, the victim is answered "not landed", and otherwise it is transient. Both were seen on SQL
  Server. Safety held in all runs: one commit's rows, positions 1 1 1. The cost is a
  victim, a retry, and up to the engine's detection delay.
- A first MySQL pass counted waiters from `information_schema.INNODB_TRX` and never saw them, so its interleaving was
  not forced; those runs were discarded and rerun with `performance_schema.data_lock_waits`, as logged.
