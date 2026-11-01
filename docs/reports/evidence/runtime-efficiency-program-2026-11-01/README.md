# Runtime Efficiency integrated evidence

This directory holds the completed diagnostics qualification cohort for program #2531 / #2613. It is an intermediate
receipt; final program integration and independent-host failure qualification remain open.

Production libraries: `63e4c1fc48be2b99505f0c26eb2aae46ff2c5d26`; native/Workbench Release Linux x64,
.NET 10.0.8 / SDK 10.0.300. The private eight-vCPU/32-GB Azure D8ds_v5 host ran PostgreSQL 16 Alpine and the
actual HTTP4 Workbench workflow. Runtime data used `/dev/sda1` (OS volume); persisted SQLite diagnostics used
that volume or `/dev/sdb1` (`/mnt`, temporary local storage). Temporary storage is qualification placement,
not durable production storage advice. Separate placement changes both contention and device speed.

`diagnostics-six-cases-r1.tar.xz` contains 235 raw files, including configurations, driver records, UTC windows,
CPU/counter samples, PostgreSQL WAL/wait samples, device placement, per-second iostat and retained failed attempts.
SHA-256: `c15bbaf95f8c1d7a0afe407f43a9559ac2f184d66ef2e61dad53eae0485d6cd9` (1,118,664 bytes).
`diagnostics-custody.json` records verified local custody and protected-value checks. Private connection/token files
are excluded. The archive includes the original failed series and the explicit attestation combining two original
passing cases with four passing recovery cases; no failed attempt was relabelled as passing.

The CSV and JSON summaries derive from those raw windows. Each configuration has one closed-loop concurrency ramp,
15-second warmups and 60-second target windows. All 32 measured windows pass request/settlement/counter accounting.
They are exploratory samples without replication/error bars, not SLOs or native no-op engine rates. WAL snapshots
span driver windows/asynchronous statistics and are not exact per-execution costs. iostat means align to actual UTC
measurement boundaries, exclude the first lifetime report and are not operation-weighted latency. Sampled PostgreSQL
wait counts are observations, not wait durations. `%util` alone does not establish device saturation.

Analysis scripts retain their original temporary evidence-root paths for provenance; set their `r`/`root` variables
to an extracted copy to reproduce elsewhere. Use Python 3.14 (supports .NET seven-digit timestamps); chart generation
used matplotlib 3.11.2. The scripts write derived files and never run workloads. The chart was visually inspected.

## Finite file ETL cohort

`etl-six-cases-r1.tar.xz` contains 667 selected raw files from six successful 100,000-row runs on worker
`elsa-eff-i67-w3` (D2s_v6, two vCPU / 8 GiB). SHA-256:
`e5726bb83a6e130740c522e94a64a6fb0d8e2e0c107d922e18893eafd085662d` (809,948 bytes).
It includes configurations, logs, results, synthetic input/snapshot CSV and every output JSONL chunk; SQLite database
binaries and process dumps are excluded. Its manifest verifies every file. `etl-summary.json` also records a separate
local re-read of all 600,000 output rows: exact names/categories, no duplicates/missing rows, all chunk digests and
expected chunk count. The qualification process itself compares each chunk's digest/length with the accepted durable
manifest before returning success. Filenames hash the effect identity, not the chunk contents; output is immutable
and effect-addressed, with separately verified content hashes.

The two samples at each configured attempt count are fresh processes / first finite invocations, with eight
partitions, 1,000-record batches, 128 padding characters per synthetic name and a one-row lookup. SQLite runs on that
worker's OS volume. Host migration, dataset generation and compilation are excluded; actual worker preparation/JIT,
wakeup, durable acceptance and 100-ms status polling are included. Each standalone run intentionally owes one native
callback because no native adapter is composed. No clustered ETL, complete parent-workflow drain or cross-model
comparison is claimed. Actual ranges remain visible rather than selecting the fastest sample.
