# Final bounded timing comparison

**Draft, 9 October 2026.** Root and independent review reconciled all 14 planned windows. The logging-evidence clarification from review is recorded below. Thirteen windows passed, and the failed before-source concurrency window is retained. These are finite observations, not a timing CI gate or an isolated estimate of any individual fix.

## Observed result

In the selected four-activity sequential HTTP workflow, Coalesced mean response time moved from 303.0 ms to 200.5 ms (33.8% lower), with median 239.4 ms to 149.5 ms. The 16-activity Coalesced control moved from 1,126.2 ms to 467.5 ms (58.5% lower mean). Immediate sequential results were broadly similar across sources. The small differences in Immediate and REST windows do not establish a meaningful speedup.

These timing observations cannot supply query-count reductions. [Integrated correctness](integrated-correctness.md) and the final request/settlement accounting have separate evidence requirements.

| Scenario | Cadence | Before: mean / median / p95, ms | After: mean / median / p95, ms | Mean change |
|---|---|---:|---:|---:|
| 4-activity HTTP, sequential | Immediate | 851.4 / 868.9 / 1048.3 | 837.4 / 850.6 / 1026.1 | -1.6% |
| 4-activity HTTP, sequential | Coalesced | 303.0 / 239.4 / 493.8 | 200.5 / 149.5 / 368.1 | -33.8% |
| 16-activity HTTP, sequential | Immediate | 2889.7 / 2845.7 / 3259.6 | 2905.8 / 2874.9 / 3326.7 | +0.6% |
| 16-activity HTTP, sequential | Coalesced | 1126.2 / 1121.0 / 1351.0 | 467.5 / 480.4 / 764.7 | -58.5% |
| 4-activity HTTP, 4 clients | Immediate | 1372.2 / 1376.5 / 1709.7 | 1292.1 / 1288.4 / 1540.1 | -5.8% |
| 4-activity HTTP, 4 clients | Coalesced | Failed control | 297.8 / 276.1 / 453.8 | Not comparable |
| REST companion admission | Coalesced | 115.4 / 91.8 / 272.4 | 106.2 / 82.4 / 242.6 | -8.0% |

Every passing window retained 60 measured requests, 25 warmups and one preflight: all 86 returned the expected HTTP response or REST admission, and 86 distinct executions completed with zero incidents and the expected persisted cadence. REST output was checked on completed executions; its timer measures admission, so it must not be subtracted from synchronous HTTP response time as an equivalent transport.

## Source and environment

The before source is `e6faa5689814c33353009b13f5b9e44d0e9982a4`; the after source is `7357b91e012c56cb34f1259ef7a60ef56e144afe`. Both use fixture `b9f854cb493c2945c14790bab75c991f46f9307a`. The after build is the source accepted by #2412; the subsequent #2521 merge changed documentation only. Both sources have independently hashed 520-file Debug/net10.0 Workbench output closures.

The same Azure D8ds_v5 runner (8 logical processors, Ubuntu 24.04.4, linux/amd64), .NET SDK 10.0.401/runtime 10.0.12, PowerShell 7.6.6 and PostgreSQL 16.15 image were used. Each window used fresh owned PostgreSQL and ancillary SQLite state. No existing database was replaced or deleted. Provider, workflow, semantic configuration and logging settings match within each source pair. Equivalent JSON property order differences were independently canonicalized after each file's raw hash was verified.

The owner launcher reported `preflightConfig.developmentEfLogLevel=Warning`; root separately checked that value in each retained, hash-verified `appsettings.Development.json`. The evidence projection includes all 14 metadata/configuration hashes. The workload sampler itself reports `hostLoggingLevelVerifiedByScript=false`, and no effective runtime-filter introspection is available. Startup hooks were absent. Persisted diagnostics remained enabled on SQLite. Load gates required both one- and five-minute load averages below the processor count at two observations; actual start/end loads are retained and differ between windows. The plan alternated some before/after ordering but was not a randomized repeated experiment.

## Failed concurrency baseline

The before-source Coalesced four-client window failed: of 60 timed attempts, 38 returned matched HTTP 200, 16 returned HTTP 500 and six returned HTTP 202. The preflight and all 25 warmups passed. Readback found 79 of the 86 expected executions: 67 Completed with zero incidents, six Faulted with one incident, five Completed with one incident and one Running with one incident. Seven executions had no readback.

Logs contain 31 EF observations of concurrent DbContext use and one Npgsql operation-in-progress exception. This identifies a failure in the old-source run, without isolating one delivered fix or establishing the cause of the supplied historical capture. The final-source counterpart passed all 60 timed attempts and all 86 terminal readbacks. No successful before/after latency gain is calculated for this pair.

The driver stopped on the failure. The original plan and execution ledger remain unchanged; a separately pinned continuation executed only the two previously unstarted REST windows. No failed attempt was replaced or retried.

## Interpretation and evidence limits

There is one finite window per source/configuration, with 60 measured samples. Nearest-rank p95 is exploratory. The [sanitized evidence projection](evidence/final-timing-2026-10-09.json) contains recomputed statistics, standard deviations, configuration/build/fixture hashes, load observations and failure disposition. These are workload/environment-specific observations; they are not a 30 ms promise, a production SLA, a process-cold-start measurement, or proof of physical network round trips.

The first timed request followed host startup, fixture publication, preflight and warmups. Startup migration-history errors and pruning warnings remain in the private logs and are kept separate from the passing measured requests. Exact database command, statement, commit and dispatch accounting remains the final companion deliverable.
