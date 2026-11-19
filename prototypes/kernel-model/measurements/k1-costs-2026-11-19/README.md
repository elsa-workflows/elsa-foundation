# K1 slice 8: set costs, raw outputs (19 November 2026)

What `PARTITIONS.md`'s "K1 costs" section reads. Release builds, from `prototypes/kernel-model`; Docker containers with
their data on tmpfs and ports picked by Docker; `EFR=dotnet ef/Kernel.Ef.Run/bin/Release/net10.0/Kernel.Ef.Run.dll`.
Each file starts with the load average and the revision it ran at. The 1,024 runs ran at `528eabf7e` with the harness
as committed after it, before its `count=` option, which changes nothing at 1,024.

| File | Command |
|---|---|
| `pg-setcost.txt` | `$EFR x setcost postgres "$PG;Database=k1s8_cost" proxy=1 writes=1 runs=5` (PostgreSQL 17.11) |
| `pg-setcost-4096.txt` | `$EFR x setcost postgres "$PG;Database=k1s8_cost4096" proxy=1 runs=3 count=4096 shape=scattered` |
| `sqlite-setcost.txt` | `$EFR x setcost sqlite "Data Source=<file>" runs=5` |
| `mysql-setcost.txt` | `$EFR x setcost mysql "server=localhost;port=<p>;database=k1s8_cost" proxy=1 runs=5` (MySQL 8.4) |
| `mssql-setcost.txt` | `$EFR x setcost sqlserver "$MS;Database=k1s8_cost" proxy=1 runs=5` (SQL Server 2022, amd64 emulation) |
| `pg-idle.txt` | `$EFR x setidle postgres "$PG;Database=k1s8_idle" seconds=120 writes=1` (D3) |
| `runtime-cost-1024.txt` | `Kernel.Runtime.Orleans.Conformance.dll cost 1024 10 1024` and `Kernel.Runtime.Conformance.Run.dll cost 1024 10 1024`, three interleaved runs each |
| `pg-owned-witness.txt`, `other-owned-witness.txt` | `Kernel.Ef.Conformance.dll postgres|sqlserver|mysql "<connection>" --fence-in-batch|--checked-fence semantic "owned set"` |
| `conformance-after-fix.txt`, `conformance-after-merge.txt` | the runtime suites after the holder-read fix, before and after merging 7b's `8e0cbb7e1` |
| `rtc-4096.txt` | `Kernel.Runtime.Conformance.Run.dll semantic --partitions 4096` after the merge, with the crash case's takeover line |
