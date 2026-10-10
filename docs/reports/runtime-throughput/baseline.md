# Bounded runtime throughput baseline

Track B [#2534](https://github.com/elsa-workflows/elsa-foundation/issues/2534), Program [#2531](https://github.com/elsa-workflows/elsa-foundation/issues/2531); SQLite Immediate follow-up [#2553](https://github.com/elsa-workflows/elsa-foundation/issues/2553). Captured 10 October 2026 under the owner-approved scoped ADR 0073 D7 exception.

Seven single-attempt series reached three valid protocol plateaus, two admission-backpressure stops and two correctness-failure stops. Http4 Immediate with persisted diagnostics reached a PostgreSQL plateau at 16 clients with direct WAL-write contention evidence. Both Http16 series failed at 4 clients, with three HTTP 500s and three instances still Running across the two series. SQLite Immediate reached its plateau at 2 clients; its limiting resource is unattributed.

## Method and scope

The [pinned protocol](throughput-ramp-protocol.md) and capture script were unchanged. Series 1–6 use disposable PostgreSQL 16 runtime databases plus owned SQLite diagnostics, with the [spec 193 provider split](../../../specs/193-bounded-coalesced-pagination/quickstart.md). The owner-approved addition of 10 October, series 7, uses the default Development SQLite composition. Every series had a new host, content root and database. PostgreSQL passed `SELECT 1` before host startup; all hosts passed readiness. Each load preflight recorded two 1- and 5-minute load averages below 8, at least 30 seconds apart.

The closed-loop levels are 1, 2, 4, 8, 16, 32 and 64 clients, with 15 seconds of warm-up and a 60-second measured window per level. A valid step requires matching HTTP 200 output and settled instances without incidents. A less-than-5% gain stops a valid series; any failed request or unsettled instance stops it as invalid. Every attempt is retained. No requests, levels or series were retried or replaced. Unrun higher levels are omitted, not assumed to pass.

Persisted diagnostics Off removes `DiagnosticsOpenTelemetryEntityFrameworkCore` and `DiagnosticsStructuredLogsEntityFrameworkCore` from the temporary shell. OpenTelemetry uses its in-memory fallback. EF logging is Warning in every series. No product code, tests, protocol or capture-script changes were made.

## Series outcomes

| Series | Runtime | Fixture / cadence / diagnostics | Valid plateau | Stop level / reason | Resource attribution | HTTP 429s |
|---|---|---|---|---|---|---|
| 1 | PostgreSQL | Http4 / Immediate / On | 16 | 16 / plateau | PostgreSQL WAL-write contention | 0 |
| 2 | PostgreSQL | Http4 / Coalesced / On | 8 | 8 / plateau | Unattributed | 0 |
| 3 | PostgreSQL | Http4 / Immediate / Off | Not reached | 16 / admission backpressure | Runtime admission controller; underlying resource unattributed | 13 |
| 4 | PostgreSQL | Http4 / Coalesced / Off | Not reached | 32 / admission backpressure | Runtime admission controller; underlying resource unattributed | 134 |
| 5 | PostgreSQL | Http16 / Coalesced / On | Not reached | 4 / failure | Unattributed | 0 |
| 6 | PostgreSQL | Http16 / Coalesced / Off | Not reached | 4 / failure | Unattributed | 0 |
| 7 | SQLite | Http4 / Immediate / On | 2 | 2 / plateau | Unattributed | 0 |

Under the owner’s explicit reporting rule, a 429-only stop is labeled **admission backpressure**, naming the runtime admission controller as the limiting component. Its raw artifact remains invalid for successful throughput. Other failures retain the failure label. Resource attribution is kept separate from the admission signal.

## Curves and counter evidence

Executions/s counts exact-body HTTP 200 completions inside the 60-second window, with durable instance settlement checked afterward; durable-completion timestamps are not bucketed into that window. Histograms and instance expectations also include warm-up and requests finishing after the window. Instance counts are cumulative within a series. SQLite busy/concurrency line deltas cover measurement plus request drain, excluding warm-up and settlement. **p95 is exploratory**, covering only successful in-window requests; it is neither a gate nor an all-request latency estimate. An invalid step has no successful-throughput claim, even when its artifact contains a numeric rate.

### Series 1: Http4 / Immediate / persisted diagnostics On

[Raw series JSON](evidence/2026-10-10-2ea800c/series-1/series-1.json) · [receipt](evidence/2026-10-10-2ea800c/series-1/receipt.json) · [cadence readback](evidence/2026-10-10-2ea800c/series-1/readback/summary.json)

| Clients | Valid executions/s | Exploratory p95 (ms) | Status histogram | Instances / expected | Incomplete / incidents | SQLite busy/concurrency lines |
|---|---:|---:|---|---|---|---:|
| 1 | 1.333 | 954.3 | 200: 102 | 102 / 102 | 0 / 0 | 0 |
| 2 | 1.617 | 1776.2 | 200: 125 | 227 / 227 | 0 / 0 | 0 |
| 4 | 2.200 | 2496.4 | 200: 173 | 400 / 400 | 0 / 0 | 0 |
| 8 | 2.800 | 3703.8 | 200: 227 | 627 / 627 | 0 / 0 | 0 |
| 16 | 2.883 | 7212.8 | 200: 237 | 864 / 864 | 0 / 0 | 0 |

Raw stop: `PlateauAtClients16`. The valid c8→c16 gain is below 5%. At c16, 156/218 PostgreSQL window observations are LWLock/WALWrite, and lock/IO waits together account for 170/218. This meets the protocol’s PostgreSQL-contention rule. Ungranted locks are zero; Npgsql used connections peak at 17/100. Summed Workbench CPU-time component maxima are below 22% of eight cores. This attributes observed database contention, not an underlying physical storage limit.

Supplemental readback after the series inspected 864 instances and found 0 cadence mismatches. This readback occurred outside the measured windows.

### Series 2: Http4 / Coalesced / persisted diagnostics On

[Raw series JSON](evidence/2026-10-10-2ea800c/series-2/series-2.json) · [receipt](evidence/2026-10-10-2ea800c/series-2/receipt.json) · [cadence readback](evidence/2026-10-10-2ea800c/series-2/readback/summary.json)

| Clients | Valid executions/s | Exploratory p95 (ms) | Status histogram | Instances / expected | Incomplete / incidents | SQLite busy/concurrency lines |
|---|---:|---:|---|---|---|---:|
| 1 | 4.050 | 573.8 | 200: 318 | 318 / 318 | 0 / 0 | 0 |
| 2 | 5.617 | 814.4 | 200: 431 | 749 / 749 | 0 / 0 | 0 |
| 4 | 8.033 | 1242.9 | 200: 600 | 1349 / 1349 | 0 / 0 | 0 |
| 8 | 7.983 | 2414.0 | 200: 625 | 1974 / 1974 | 0 / 0 | 0 |

Raw stop: `PlateauAtClients8`. The valid c4→c8 rate falls from 8.033 to 7.983 executions/s. At c8, PostgreSQL lock/IO waits account for 44/123 window observations; the other 79 are ClientRead/idle. WALWrite is the largest active wait class, but does not dominate the full sample set. Npgsql used connections peak at 8/100; summed Workbench CPU-time component maxima are below 25% of eight cores; SQLite busy/concurrency delta and ungranted locks are zero. No resource meets the conservative attribution rule.

Supplemental readback after the series inspected 1,974 instances and found 0 cadence mismatches. This readback occurred outside the measured windows.

### Series 3: Http4 / Immediate / persisted diagnostics Off

[Raw series JSON](evidence/2026-10-10-2ea800c/series-3/series-3.json) · [receipt](evidence/2026-10-10-2ea800c/series-3/receipt.json) · [cadence readback](evidence/2026-10-10-2ea800c/series-3/readback/summary.json)

| Clients | Valid executions/s | Exploratory p95 (ms) | Status histogram | Instances / expected | Incomplete / incidents | SQLite busy/concurrency lines |
|---|---:|---:|---|---|---|---:|
| 1 | 1.917 | 650.8 | 200: 141 | 141 / 141 | 0 / 0 | 0 |
| 2 | 3.367 | 813.0 | 200: 256 | 397 / 397 | 0 / 0 | 0 |
| 4 | 4.550 | 1387.7 | 200: 348 | 745 / 745 | 0 / 0 | 0 |
| 8 | 4.917 | 2446.2 | 200: 380 | 1125 / 1125 | 0 / 0 | 0 |
| 16 | **Invalid** | 6620.3 | 200: 368, 429: 13 | 1493 / 1506 | 0 / 0 | 0 |

Raw stop: `InvalidStepAtClients16`. The series stops on 429-only admission backpressure at c16, so no valid throughput plateau is established. The last valid level is c8 at 4.917 executions/s. At the stop window, PostgreSQL lock/IO waits account for 115/221 observations (105 WALWrite, 9 WALSync, 1 IO/WALWrite). That is direct evidence of database contention in the failure window, but does not establish the resource that caused the admission controller to shed. Npgsql used connections peak at 16/100 and summed Workbench CPU-time component maxima are below 29% of eight cores; ungranted locks and SQLite busy/concurrency deltas are zero. The underlying cause of admission backpressure remains unattributed.

Supplemental readback after the series inspected 1,493 instances and found 0 cadence mismatches. This readback occurred outside the measured windows.

### Series 4: Http4 / Coalesced / persisted diagnostics Off

[Raw series JSON](evidence/2026-10-10-2ea800c/series-4/series-4.json) · [receipt](evidence/2026-10-10-2ea800c/series-4/receipt.json) · [cadence readback](evidence/2026-10-10-2ea800c/series-4/readback/summary.json)

| Clients | Valid executions/s | Exploratory p95 (ms) | Status histogram | Instances / expected | Incomplete / incidents | SQLite busy/concurrency lines |
|---|---:|---:|---|---|---|---:|
| 1 | 8.483 | 123.4 | 200: 615 | 615 / 615 | 0 / 0 | 0 |
| 2 | 15.183 | 194.9 | 200: 1148 | 1763 / 1763 | 0 / 0 | 0 |
| 4 | 19.167 | 430.5 | 200: 1522 | 3285 / 3285 | 0 / 0 | 0 |
| 8 | 23.167 | 752.0 | 200: 1701 | 4986 / 4986 | 0 / 0 | 0 |
| 16 | 24.850 | 1651.7 | 200: 1854 | 6840 / 6840 | 0 / 0 | 0 |
| 32 | **Invalid** | 2842.3 | 200: 2212, 429: 134 | 9052 / 9186 | 0 / 0 | 0 |

Raw stop: `InvalidStepAtClients32`. The 32-client step is invalid because of 134 HTTP 429s, so there is no valid plateau. The last valid level is c16 at 24.850 executions/s. At c32, PostgreSQL lock/IO waits account for 106/323 window observations (94 WALWrite, 10 WALSync, 2 DataFileExtend); ClientRead/idle accounts for 197. Npgsql used connections peak at 26/100; summed Workbench CPU-time component maxima are below 51% of eight cores. Ungranted locks and SQLite busy/concurrency deltas are zero. These counters do not establish an underlying saturating resource.

Supplemental readback after the series inspected 9,052 instances and found 0 cadence mismatches. This readback occurred outside the measured windows.

### Series 5: Http16 / Coalesced / persisted diagnostics On

[Raw series JSON](evidence/2026-10-10-2ea800c/series-5/series-5.json) · [receipt](evidence/2026-10-10-2ea800c/series-5/receipt.json) · [cadence readback](evidence/2026-10-10-2ea800c/series-5/readback/summary.json)

| Clients | Valid executions/s | Exploratory p95 (ms) | Status histogram | Instances / expected | Incomplete / incidents | SQLite busy/concurrency lines |
|---|---:|---:|---|---|---|---:|
| 1 | 2.267 | 735.7 | 200: 175 | 175 / 175 | 0 / 0 | 0 |
| 2 | 2.483 | 1370.5 | 200: 196 | 371 / 371 | 0 / 0 | 0 |
| 4 | **Invalid** | 1815.9 | 200: 272, 500: 2 | 645 / 645 | 2 / 0 | 0 |

Raw stop: `InvalidStepAtClients4`. This is a failure, not admission backpressure: the c4 step contains two HTTP 500 responses and two non-completed instances. No valid plateau was reached. The last valid level is c2 at 2.483 executions/s. At c4, PostgreSQL lock/IO waits account for 19/89 window observations; Npgsql used connections peak at 5/100; summed Workbench CPU-time component maxima are below 23% of eight cores. Ungranted locks and SQLite busy/concurrency deltas are zero. No saturating resource or cause of the HTTP 500s is established.

Supplemental readback after the series inspected 645 instances and found 0 cadence mismatches. This readback occurred outside the measured windows.

### Series 6: Http16 / Coalesced / persisted diagnostics Off

[Raw series JSON](evidence/2026-10-10-2ea800c/series-6/series-6.json) · [receipt](evidence/2026-10-10-2ea800c/series-6/receipt.json) · [cadence readback](evidence/2026-10-10-2ea800c/series-6/readback/summary.json)

| Clients | Valid executions/s | Exploratory p95 (ms) | Status histogram | Instances / expected | Incomplete / incidents | SQLite busy/concurrency lines |
|---|---:|---:|---|---|---|---:|
| 1 | 4.317 | 318.0 | 200: 314 | 314 / 314 | 0 / 0 | 0 |
| 2 | 7.517 | 396.3 | 200: 571 | 885 / 885 | 0 / 0 | 0 |
| 4 | **Invalid** | 659.3 | 200: 820, 500: 1 | 1706 / 1706 | 1 / 0 | 0 |

Raw stop: `InvalidStepAtClients4`. The c4 step fails with one HTTP 500 and one non-completed instance. No valid plateau was reached; the last valid level is c2 at 7.517 executions/s. At c4, PostgreSQL lock/IO waits account for 15/78 window observations. Npgsql used connections peak at 4/100, summed Workbench CPU-time component maxima are below 39% of eight cores, and ungranted locks and SQLite busy/concurrency deltas are zero. These counters do not establish a saturating resource or the HTTP 500 cause.

Supplemental readback after the series inspected 1,706 instances and found 0 cadence mismatches. This readback occurred outside the measured windows.

### Series 7: Http4 / Immediate / persisted diagnostics On

[Raw series JSON](evidence/2026-10-10-2ea800c/series-7/series-7.json) · [receipt](evidence/2026-10-10-2ea800c/series-7/receipt.json) · [cadence readback](evidence/2026-10-10-2ea800c/series-7/readback/summary.json)

| Clients | Valid executions/s | Exploratory p95 (ms) | Status histogram | Instances / expected | Incomplete / incidents | SQLite busy/concurrency lines |
|---|---:|---:|---|---|---|---:|
| 1 | 0.883 | 1516.3 | 200: 70 | 70 / 70 | 0 / 0 | 0 |
| 2 | 0.717 | 4596.7 | 200: 57 | 127 / 127 | 0 / 0 | 0 |

Raw stop: `PlateauAtClients2`. The valid c1→c2 rate falls from 0.883 to 0.717 executions/s, reaching the protocol plateau rule at 2 clients. All 127 instances completed without incidents or cadence mismatches. At c2 the SQLite busy/concurrency log delta is zero, and summed Workbench CPU-time component maxima are below 4.9% of eight cores. No direct SQLite wait or writer-utilization metric is available. The thread-pool instruments are deltas rather than absolute queue depth. The resource remains unattributed; low CPU or exploratory p95 alone cannot identify it.

Supplemental readback after the series inspected 127 instances and found 0 cadence mismatches. This readback occurred outside the measured windows.

## Failed and invalid steps

- **Series 3, 16 clients:** 13 failed attempts including warm-up: 13 HTTP 429s and 0 other failures. Histogram: `{"200":368,"429":13}`. Instances 1493 / 1506; incomplete 0; incidents 0; settled `false`. The raw rate `4.800` executions/s is retained as invalid data, not a throughput result. The 13-instance count gap equals the aggregate 429 count; all observed instances completed without incidents. Individual shed requests cannot be correlated to execution IDs.

- **Series 4, 32 clients:** 134 failed attempts including warm-up: 134 HTTP 429s and 0 other failures. Histogram: `{"200":2212,"429":134}`. Instances 9052 / 9186; incomplete 0; incidents 0; settled `false`. The raw rate `30.317` executions/s is retained as invalid data, not a throughput result. The 134-instance count gap equals the aggregate 429 count. All 9,052 observed instances are Completed without incidents; individual response-to-instance correlation is unavailable.

- **Series 5, 4 clients:** 2 failed attempts including warm-up: 0 HTTP 429s and 2 other failures. Histogram: `{"200":272,"500":2}`. Instances 645 / 645; incomplete 2; incidents 0; settled `false`. The raw rate `3.567` executions/s is retained as invalid data, not a throughput result. The final supplemental readback has 643 Completed and two Running instances: 14EQ5gNYCaj and 14EQ5fe6Bk3, both Coalesced with zero incidents. The capture does not retain HTTP failure response bodies or correlate them to execution IDs. No causal repair or replacement run was attempted.

- **Series 6, 4 clients:** 1 failed attempts including warm-up: 0 HTTP 429s and 1 other failures. Histogram: `{"200":820,"500":1}`. Instances 1706 / 1706; incomplete 1; incidents 0; settled `false`. The raw rate `10.617` executions/s is retained as invalid data, not a throughput result. Readback recorded 1,705 Completed instances and one Running instance, 14EQmrlG6tN, with Coalesced cadence and zero incidents. Response-to-instance correlation and the HTTP 500 body are unavailable. This separate Off series also fails at c4; it is not a retry of series 5 and does not prove a shared cause.

Request-to-execution correlation is unavailable in this capture. An instance-count gap equal to the 429 count is consistent with rejected admissions, but is not proof mapping individual rejected requests to missing instances. A capture-process exit of 0 indicates artifact creation; it does not turn an invalid step into a pass.

[Host log review](evidence/2026-10-10-2ea800c/host-log-review.json) retains matching-line counts and excerpts; the [manifest](evidence/2026-10-10-2ea800c/evidence-manifest.json) pins complete stdout/stderr and host logs retained on the VM. Broad timeout/admission text matches are log-search results, not additional client failure counts.

## SQLite Immediate and #2553

**Yes, SQLite Immediate saturates at or below 32 clients by this protocol’s operational plateau definition in this single run:** it reaches the stop rule at 2 clients, falling from 0.883 executions/s at c1 to 0.717 at c2. Both steps are valid; all 127 instances completed, with zero incidents, zero cadence mismatches, zero HTTP 429s and zero busy/concurrency log matches during capture. Levels 4–64 were not run because the protocol stopped at c2. This establishes the observed plateau, not an intrinsic SQLite capacity limit or its cause. The underlying resource is **unattributed**. Exploratory p95 rises from 1516.3 to 4596.7 ms, but is not used for attribution.

## Provenance

| Input | Pin |
|---|---|
| Source and script HEAD | `2ea800cd5c17232cbfd50e0115344bab0ec79056` (detached) |
| Runner | `elsa-p2382-98e7664aa3`, `rg-elsa-p2382-98e7664aa3`, Standard_D8ds_v5, westeurope; 8 vCPU, 31 GiB visible RAM |
| OS and tools | Ubuntu 24.04.4 LTS x64; .NET SDK 10.0.401; PowerShell 7.6.6; Docker Engine 29.8.2; dotnet-counters 10.0.750501, installed before all windows |
| PostgreSQL | 16.15 (Debian 16.15-1.pgdg13+2), series 1–6 only |
| PostgreSQL image | `postgres@sha256:ca0bd484cb98bf4b24eb1010e73fb3fcbd6714d240fbc1a10eea5b7dbecb641d` (retained A1 digest) |
| Build | Only `dotnet build src/apps/Elsa.Workbench/Elsa.Workbench.csproj -c Release`; exit 0 |
| Build stdout SHA-256 | `09c05c6870d276ebb009f76ec08e13479a94c60cfa38d0197142fe81ca97e540` |
| Release Elsa.Workbench.dll SHA-256 | `bf9152cb290fa8234146c134683a300f58eb4e7358dd7d1fa79beec85ad31a0f` |
| Release output closure manifest SHA-256 | `fae865b939ab95be39b151c86fa692e7eba28c5b4900387f643249b7c7a554a8` |

The [source pins](evidence/2026-10-10-2ea800c/provenance/source-pins.json), [build receipt](evidence/2026-10-10-2ea800c/provenance/build-result.json) and [output closure](evidence/2026-10-10-2ea800c/provenance/build-release-output-SHA256SUMS) fix the executed inputs. The lifecycle helper's launch path resolves to the attested Release output; the source checkout and all 520 original Release build files were verified unchanged after capture. Runtime startup added .nuplane/store-state.json and its lock file; final verification records their hashes.

| Series | Fixture export SHA-256 | Shell SHA-256 |
|---|---|---|
| 1 | `5c2d1770fe194c4466c2c61f1ad10888c8d5bb72bb6f3561c098a693b41c16ce` | `1416b8ee7ec42b7fce07c889d36d377a940e772e982847dfc8656fc5a87599b9` |
| 2 | `ba5705b1bda91f64f10caf9193df58879b67af738220b56a65525c4e6d5c3e4c` | `1416b8ee7ec42b7fce07c889d36d377a940e772e982847dfc8656fc5a87599b9` |
| 3 | `2bc2c3e9ad87f9f5b967872f3f780f81845b47e556018468053ee7d5a3cc1c43` | `396973b46c99ff1486db5d6a42e218a7b014eebacff362d8e4f6ba888d49ffd1` |
| 4 | `7e04d859c38dfaeccdf3edd668912991ae55867e6720b2248ab655ced277185c` | `396973b46c99ff1486db5d6a42e218a7b014eebacff362d8e4f6ba888d49ffd1` |
| 5 | `774ce2aab618a080e9f33da9bbd47139f638622bc47fe3e37783e7b1ba2672b0` | `1416b8ee7ec42b7fce07c889d36d377a940e772e982847dfc8656fc5a87599b9` |
| 6 | `9752e237e8800a74b62d4a7b1ec2e6550fdfa58f0cc3a910aa20c1f69ca32311` | `396973b46c99ff1486db5d6a42e218a7b014eebacff362d8e4f6ba888d49ffd1` |
| 7 | `7c5571fb7c6e6255297be289afae85de36997530638457e599f742ccd7fad6e5` | `5b5e359b3642c3651df3919736923e4738e99b87a26483445cf219b9997d911c` |

Each series receipt also records exact appsettings and Development-config hashes. Protected environment files are represented only by whole-file hashes in the [manifest](evidence/2026-10-10-2ea800c/evidence-manifest.json); their contents are excluded. Fixture exports, effective-cadence readback summaries and per-series load checks accompany the JSON. Full instance lists and per-instance cadence records remain on the VM and are hashed in the manifest.

## Evidence handling and limits

Raw counter files total 18,191,646 bytes, exceeding the 5 MB publication limit. They remain on the retained VM with their SHA-256 hashes in the manifest. [Derived counter summaries](evidence/2026-10-10-2ea800c/derived-analysis.json) and every collector privacy receipt are committed. The complete evidence root is `/home/elsarunner/track-b-evidence/2ea800cd5c17232cbfd50e0115344bab0ec79056/`.

Npgsql pool metric labels default to connection strings ([official documentation](https://www.npgsql.org/doc/diagnostics/metrics.html)). An external collector transport kept raw output in a protected FIFO/RAM path and pseudonymized only pool labels before writing counter JSON. Metric names, timestamps, other tags and numeric values were preserved and checked. A real-tool synthetic preflight ran before the first workload window; every captured level has a successful collector receipt. The product and capture script stayed unchanged. Raw connection strings were never published.

- This is one sequential, loopback-client Release run on one VM size, with six PostgreSQL/SQLite compositions and one SQLite control. It is not a cross-machine or production capacity guarantee.
- There is one measured window per reached level, no randomized order and no repeated-window spread estimate. On/Off deltas are observations; they alone cannot meet the protocol’s spread-based SQLite attribution test.
- PostgreSQL wait observations count sampled backends, not elapsed wait duration. Attribution uses offsets below 60 seconds; drain-tail samples stay retained separately. A WAL contention signal does not identify a physical storage cause.
- The unchanged script computes its host-CPU denominator after settlement, which can dilute that field when settlement is prolonged. Workbench CPU assessment here uses System.Runtime process-counter samples instead; this does not measure every process on the VM. Summed user/system maxima are a conservative upper bound; the maxima need not occur together.
- This dotnet-counters version exports thread-pool queue/thread-count instruments as per-second deltas, including negatives. They do not establish an absolute queue length or thread count, so no thread-pool saturation claim is made.
- Exploratory p95 excludes failed requests and completions outside the window. Closed-loop load and the stop rule limit what higher-client behavior can be inferred.
- No CI gate, budget, SLA, scheduled benchmark or product optimization is introduced. Suggested optimizations require separate, reviewed Program #2531 units.

Unexpected observations: both Http16 compositions returned HTTP 500s and left Running instances without incidents; their causes remain unresolved. Server logs contain OpenIddict pruning/unsupported ExecuteDelete warnings, which are not correlated to those responses. Runtime exception counters are nonzero even in the all-200 SQLite series; thrown-exception counts do not establish unhandled or request-failing exceptions. Runtime startup added two Nuplane state files beside the unchanged build output. The post-run verifier initially lacked permission to list the protected FIFO directory, then flagged those two additions; its inspection was corrected and the original failed verification was retained. These were evidence-inspection issues, not replacement workload runs or causal repairs.

[Final verification](evidence/2026-10-10-2ea800c/provenance/final-verification.json) records source/build integrity, fresh composition checks, stopped owned processes/containers and preserved pre-existing containers. Azure confirmed VM deallocated in westeurope after all owned processes and containers were stopped. The managed OS disk and complete evidence were retained; the control-plane confirmation is in provenance/vm-final-state.json.
