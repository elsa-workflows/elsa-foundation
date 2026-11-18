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
| PostgreSQL 17 | batch | ascending | landed | not landed | 0 | 113 ms |
| PostgreSQL 17 | batch | batch order | not landed (E1) | landed | 1 | 1,144 ms |
| PostgreSQL 17 | checked | ascending | landed | not landed | 0 | 108 ms |
| PostgreSQL 17 | checked | batch order | Transient, 40P01 | landed | 1 | 1,146 ms |
| SQL Server 2022 (RCSI) | batch | ascending | landed | not landed | 0 | 141 ms |
| SQL Server 2022 (RCSI) | batch | batch order | landed | not landed (E1) | 1 | 2,620 ms |
| SQL Server 2022 (RCSI) | checked | ascending | landed | not landed | 0 | 135 ms |
| SQL Server 2022 (RCSI) | checked | batch order | Transient, 1205 | landed | 1 | 2,789 ms |
| MySQL 8.4 (REPEATABLE READ) | batch | ascending | landed | not landed | 0 | 106 ms |
| MySQL 8.4 (REPEATABLE READ) | batch | batch order | landed | not landed (E1) | 1 | 103 ms |
| MySQL 8.4 (REPEATABLE READ) | checked | ascending | landed | not landed | 0 | 97 ms |
| MySQL 8.4 (REPEATABLE READ) | checked | batch order | landed | Transient, 1213 | 1 | 105 ms |

- **Ascending order: no deadlock on any engine in either fence mode.** Exactly one commit landed, the other was
  answered "not landed", every position moved to 1, and only the lander's rows were written.
- **Batch order deadlocks on every engine.** Each engine broke it: PostgreSQL after `deadlock_timeout` (about 1 s), SQL
  Server's deadlock monitor after about 2.5 s, and InnoDB at once. With the fence in the batch the victim's error is
  answered "not landed", because the other commit has landed and moved the fence (E1, as built); with the checked
  fence it surfaces as a transient failure. Safety held in all runs: one commit's rows, positions 1 1 1. The cost is a
  victim, a retry, and up to the engine's detection delay.
- A first MySQL pass counted waiters from `information_schema.INNODB_TRX` and never saw them, so its interleaving was
  not forced; those runs were discarded and rerun with `performance_schema.data_lock_waits`, as logged.
