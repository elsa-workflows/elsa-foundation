# Reproducing the native runtime assessment

The report evaluates native runtime commit `ab46bc22a52aa6e456bca6ac0e803c80904de081` on 31 October 2026. It is a local assessment, not a CI timing gate or a production capacity certificate.

## Evidence files

- `summary.json`, `tables.md`: accepted measurements, grouped by configuration; throughput and latency percentiles are medians of independent process results. The `processes` field means the number of repeated fresh processes, not concurrent nodes.
- `cluster-summary.json`: separate fixed-total-concurrency, shared-database process cohorts. Aggregate throughput uses total completed count divided by the common start-to-last-finish interval.
- `raw-results.tar.gz`: accepted result JSON, exact input configurations and environment/exclusion ledgers. Each result contains sorted per-workflow latency samples, allocation/GC/process-CPU measurements, command SQL/counts and completion checks. Do not average workloads with different modes, caps, variants or concurrency.
- `provenance.json`: original baseline source and assembly hashes, checkout and machine/runtime details. `experiment-hashes.json` identifies the separate experimental build. `cluster-hashes.json` identifies the cluster probe; all 33 Elsa assembly hashes match the baseline, while its untimed setup reuses the existing publication in the second process. The top-level `files` map identifies the frozen baseline. The original archived provenance also retains an older bootstrap snapshot under `environment.files`; that nested map is not the final baseline identity and is omitted from the standalone provenance file. Hashes describe the actual original build, not a guaranteed bit-for-bit portable rebuild.
- `allocation-profile.json`: sampled allocation types and first-Elsa-frame attribution from a separate warmed trace. These are not exclusive CPU percentages. The instrumented run is excluded from timing tables.
- `empty-input-experiment.patch`: diagnostic change, tested in its own binary directory and then reverted from product source. It has not passed a full correctness/recovery suite and is not a ready-to-merge patch.
- `harness/`: executable probe source, project template, matrix/cluster runners, aggregation and plot scripts. Text suffixes keep this private probe out of repository build discovery.

## Build and run

Use the measured commit in a clean checkout and .NET SDK 10.0.402 (the original runtime was 10.0.13). Build outside the repository so its central package configuration does not change the probe's explicit dependencies. On macOS use a canonical physical directory such as `/private/tmp/...`, rather than the `/tmp` symlink, when passing an MSBuild project path.

1. Copy `harness/Program.cs.txt` to `Program.cs` and `harness/Perf.csproj.txt` to `Perf.csproj` in a private directory. `dotnet build /absolute/private/Perf.csproj -c Release -p:ElsaRepo=/absolute/checkout` supplies the project references. The included `public-nuget.config` reproduces the public package source; add `--configfile /absolute/private/public-nuget.config` when restoring/building if needed. Respect the machine's build-slot wrapper and avoid concurrent builds during measurements.
2. Start a disposable PostgreSQL 16 instance bound only to `127.0.0.1:55431`, with user `postgres` and local trust authentication, as required by this probe. Use only an isolated benchmark instance. The original used `postgres:16-alpine`, image ID `sha256:f4610795587492cd4fb887c2dc3de301b813278bb6f616992061141b5f15ac36`. Preserve `fsync=on`, `synchronous_commit=on`, `full_page_writes=on`, `shared_buffers=128MB`, `max_connections=100` for comparison.
3. Create a configuration JSON as below. `Output` must be an absolute directory. Run `dotnet /absolute/private/bin/Release/net10.0/Perf.dll /absolute/private/config.json`. The probe creates a fresh database unless `Database` is supplied. SQLite databases are created under `Output`; Memory uses production in-memory stores.
4. Retain the JSON only if `Correct=true`, process exit is zero and the host-load window is acceptable. Inspect rejected runs separately. Do not run a profiler during throughput samples.
5. Remove only the disposable benchmark container/databases you created after collecting evidence.

```json
{
  "Label": "pg-balanced-ten-c1",
  "Provider": "PostgreSql",
  "Mode": "Balanced",
  "Activities": 10,
  "Concurrency": 1,
  "Seconds": 15,
  "Minimum": 100,
  "WarmupSeconds": 5,
  "WarmupMinimum": 10,
  "Cap": 50,
  "ContextPooling": false,
  "External": false,
  "Output": "/absolute/private/results"
}
```

`Activities=1` uses a single leaf. Larger values use Sequence plus that many leaves. `External=true` changes only the side-effect declaration of the no-op leaf; it performs no real external I/O. `EphemeralNoRecord` is the harness label for Ephemeral with `RecordCompletion=false`.

The driver scripts retain the exact original absolute workspace, container and output conventions. Adjust `ROOT`, `DLL`, the container name and the default output path when moving the experiment. Freeze the baseline output in `baseline-bin` before building the optional patch into `experiment-bin`; never time baseline and patched code from the same mutable output directory. Apply the experimental patch only in an isolated checkout and revert it after building. The matrix runners are resumable and skip existing successful result files; clear or relocate results before an independent rerun.

For the shared-database cohorts, build `harness/Program.cluster.cs.txt` as `Program.cs`, then freeze that output into `cluster-bin`. Every process uses `SkipInitialize=false`; the second process uses `ReusePublication=true`, retrieving the first process's published source reference and initializing its own activity registry. Use the same `Database`, `ArtifactKey` and `StartGate` for both processes. Initialization also admits the schema module and starts its finalization/backfill services. An earlier second-node setup that skipped initialization was superseded and excluded from the final comparison. Publishing the same create-only source reference twice is correctly rejected; an earlier setup attempt that did this was excluded and the original single-process cohort was superseded so both topologies use the same probe build.

For local-provider results, `harness/Program.local.cs.txt` adds post-timing details for incomplete in-memory workflow states. Build it into `local-bin`; `local-hashes.json` confirms unchanged Elsa assemblies and the timing loop is unchanged. `failed/` in the archive preserves the first in-memory validation failure; `diagnostic/` contains separate correctness-only runs, excluded from performance tables. The C16 diagnostic uses the local probe; the C1 diagnostic uses `Program.diagnostic.cs.txt`, which adds an immediate per-dispatch leaf-count check. `diagnostic-hashes.json` records its build identity. A later pass is not evidence that the original failure was fixed.

## Measurement details and limits

A published executable and live source reference are prepared once. Starts go through `IWorkflowStartDispatcher` with published-run provenance and the real native actor/scheduler. Production short workflow/activity ID generation replaces the test harness's deterministic generator. The harness provides lightweight service composition and a synthetic system authority; HTTP, authentication middleware, tenant activation, workflow compilation/publication and full application startup are excluded. Application logging/export pipelines are not configured. ReplaySafe hop fusion and completion-cascade fusion retain their shipped enabled defaults.

Cold execution is recorded separately. Baseline/concurrency/tuning/experiment runs warm for at least five seconds and ten starts, then measure at least fifteen seconds and 100 completions. The 100-leaf Durable cases use at least three warm starts and 30 measured completions because each execution is substantially longer. Local-provider exploration uses three seconds of warmup and at least ten seconds/30 measured completions. The later driver uses at least fifteen seconds/30 completions for exploratory cap probes; each archived configuration records the actual minimum used. Cluster cohorts warm each process independently, synchronize through a gate, and measure at least twenty seconds. Warmup starts are serial, including before the concurrency drives; initial concurrency ramp-up is included in measurement. The exact configuration remains authoritative.

EF finalization/backfill services started during schema initialization remain active during measurement. Their work contributes to process allocation/CPU and any overlapping SQL-accounting window. One Durable/100 accounting sample includes 163 schema-maintenance commands; see `schema-maintenance-accounting.json`.

One accounting execution after warmup has SQL/transaction counters enabled; counters are disabled during timing. Interceptor dispatch still exists, so this is not an instrumentation-free production binary. `Accounting.Count` is EF commands, not statements, wire round trips or opened connections. `Accounting.DurationMs` does not include all reader materialization and should not be presented as total database latency. Transaction counts are explicit EF transaction-commit callbacks and exclude implicit server-side transactions for individual commands. Checkpoint counts are inferred from marker INSERTs in recorded SQL.

`AllocatedBytesPerWorkflow` includes all managed allocations in the runtime process during the measured interval, including small probe bookkeeping costs. `CpuCoreEquivalent` excludes PostgreSQL/OrbStack and other host processes. Allocation is not live heap size. `PeakWorkingSet=0` is an unusable platform result and must not be interpreted as zero memory usage.

**Known output-field defect:** the probe does not stop its warmup stopwatch, so top-level `WarmupSeconds` includes later phases. Do not use it as the warmup duration. `Config.WarmupSeconds` and `Config.WarmupMinimum` correctly record the warmup policy; `Seconds`, measured timestamps and latency samples have their own correctly stopped measurement clock.

The driver excludes major external CPU bursts and waits between them. `environment-policy-change.json` and `environment-policy-timed-window.json` record the start-gate and timing-window refinements, with the earlier accepted cohorts listed explicitly. The final runner starts after five two-second samples at ≤400% external CPU and rejects two consecutive samples above 500% within the recorded measurement interval plus a two-second margin on each side. Earlier guards were stricter, applying to the entire process. No acceptance decision uses the throughput result itself. Original progress ledgers also contain exploratory/calibration attempts. The archive manifest and final result JSON, rather than every successful historical ledger entry, define the accepted cohort. Residual scheduler, thermal and VM interference remain possible. Repeated medians/ranges are descriptive, not statistical confidence intervals. Small-sample p99 (especially 30-run Durable/100) is effectively an extreme observation, not a stable tail-latency estimate. These closed-loop tests do not establish an arrival-rate SLO or queue stability under overload.

The database accumulates the warmup and measured history within each case but starts fresh for the next. Large retained history, suspension backlogs, sustained workloads, large payloads, expressions, cross-machine failover and real business side effects remain unqualified by this assessment.

SQLite uses the probe’s native library version 3.50.4. `sqlite-database-settings.json` records the persisted file headers after execution: read/write format bytes 2/2 indicate WAL, following the [SQLite file-format definition](https://sqlite.org/fileformat.html#file_format_version_numbers). Native compile defaults include synchronous=2, but the original connection-local synchronous setting was not queried; no cross-provider equivalence of power-loss durability is claimed.

The owned disposable PostgreSQL container and its anonymous volume were removed after the final evidence audit; `cleanup.json` records that scoped cleanup. Raw measurements, source templates and local diagnostic traces were retained.

## Allocation trace

The separate diagnostic capture used `dotnet-trace` 10.0.750501 with `dotnet-sampled-thread-time,gc-verbose`. `profile.py` waits for the probe's post-warmup gate, attaches the collector, then releases the timed workload. The trace includes that workload and a brief validation/shutdown tail; it is not used for throughput or an on-CPU percentage breakdown. `harness/trace-reader/` contains the allocation parser (TraceEvent 3.1.23). Copy the two text templates to their normal C# project names outside the repository, build, and run `dotnet TraceReader.dll /absolute/path/profile.nettrace` to reproduce the sampled-type/first-Elsa-frame JSON. Allocation-tick weights are sampling estimates, not exact object-size attribution.
