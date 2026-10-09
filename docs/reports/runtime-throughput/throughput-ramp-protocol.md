# Throughput ramp protocol

**Track B, [#2534](https://github.com/elsa-workflows/elsa-foundation/issues/2534), Program [#2531](https://github.com/elsa-workflows/elsa-foundation/issues/2531).** This run is authorized only by the owner's scoped ADR 0073 D7 exception (9 October 2026; see the [program goal](../../program-goals/runtime-throughput.md)). It produces one-off evidence. It is not a CI gate, budget, SLA or reusable benchmark service. Do not schedule it, wire it into workflows, or extend the matrix below without a new owner decision.

## Question

Where does completed-execution throughput stop rising for the reference HTTP workflow, and which resource saturates first?

## Fixed inputs

| Input | Value |
|---|---|
| Build | **Release** Workbench at one pinned source SHA. Record the hash of the output closure. The earlier timing series used Debug and is not comparable in absolute terms. |
| Topology | Same as the [final timing](../runtime-db-access/final-timing.md): one Azure D8ds_v5 runner (8 vCPU), loopback client. Disposable PostgreSQL 16 for runtime state; owned SQLite for diagnostics. Same provider split as [spec 193 quickstart step 2](../../../specs/193-bounded-coalesced-pagination/quickstart.md). |
| Logging | `Microsoft.EntityFrameworkCore` at Warning; no command or transaction Debug. Diagnostic tracing runs separately and never inside a measured window. |
| Fixture | `Http4` = `RuntimeDbPaging2392Reference` (Sequence / HttpEndpoint / SetVariable / WriteHttpResponse). `Http16` = the T02 shape with 13 Set intrinsics. Cadence is authored on the definition and read back per instance. |
| Load | Closed loop, one request in flight per client. Levels 1, 2, 4, 8, 16, 32, 64. Each level: 15 s warm-up, then a 60 s window. |
| Stop rule | Stop a series when doubling clients adds less than 5% successful executions/s, or at the first failed request or unsettled instance. |
| Population | Every attempt is retained, with no retry or replacement. A failed step supplies no successful-throughput claim. |

## Series (at most six, each on a fresh host and database)

| # | Fixture | Cadence | Persisted diagnostics | Purpose |
|---|---|---|---|---|
| 1 | Http4 | Immediate | On | Default-durability baseline |
| 2 | Http4 | Coalesced | On | Current recommended cadence |
| 3 | Http4 | Immediate | Off | Telemetry-store contribution under Immediate |
| 4 | Http4 | Coalesced | Off | Telemetry-store contribution under Coalesced |
| 5 | Http16 | Coalesced | On | Workflow-length sensitivity |
| 6 | Http16 | Coalesced | Off | Same, without the telemetry store |

"Off" means removing `DiagnosticsOpenTelemetryEntityFrameworkCore` and `DiagnosticsStructuredLogsEntityFrameworkCore` from the temporary shell copy. OpenTelemetry then falls back to its in-memory store. Record the exact shell file hash.

## Per-series procedure

1. Start a fresh PostgreSQL 16 container and require a successful `SELECT 1`. Start the Release Workbench on a free loopback port from a temporary content root. Wait for one healthy readiness probe. Install `dotnet-counters` beforehand; do not install it inside a window.
2. Check `uptime`: the 1- and 5-minute load must both be below 8, twice, 30 s apart.
3. Run:

   ```powershell
   pwsh -NoProfile -File e2e-tests/http/Capture-RuntimeThroughputRamp.ps1 `
     -BaseUrl http://127.0.0.1:<port> -CandidateSha <40-hex> `
     -Fixture Http4 -Cadence Coalesced -PersistedDiagnostics On `
     -HostProcessId <workbench pid> -HostLogPath <host log> `
     -PostgresConnectionString '<libpq URI from the protected env file>' `
     -OutputPath <new absolute path>/series-2.json
   ```

   Pass the connection string through a protected environment variable expansion, never as literal text in shell history.
4. Keep the JSON, the per-level `*.counters.c<N>.json` files, the host log and the shell, configuration and output hashes. Stop only the processes and containers you started.

## Attribution rule

For each series, name the saturating resource only with direct evidence at the plateau level:

| Resource | Evidence |
|---|---|
| Host CPU | `hostCpuUtilizationOfAllCores` near 1, or System.Runtime `cpu-usage` near 100% |
| Thread pool | Rising `threadpool-queue-length` with CPU below saturation |
| Npgsql pool | Npgsql `connections` pinned at max with pending requests |
| PostgreSQL contention | Lock or IO wait events dominating `samples[].postgres.waits`, or `ungrantedLocks > 0` sustained |
| SQLite writer | Non-zero `sqliteBusyOrConcurrencyLogLinesAdded`, or a series 1 vs 3 (2 vs 4) throughput delta beyond the window-to-window spread |

If no counter reaches its limit, report the resource as **unattributed**. Do not infer it from latency alone.

## Report

Publish `docs/reports/runtime-throughput/baseline.md` and the evidence JSON under `docs/reports/runtime-throughput/evidence/`. Contents:

- for each series: the throughput curve, the plateau level, the stop reason and the attributed resource;
- all failed or invalid steps, retained;
- the source, build, fixture and configuration pins;
- explicit limits: single topology, Release on one VM size, exploratory p95.

Optimizations the evidence suggests become separate units on Program #2531. No CI gate, budget or SLA follows from this report.
