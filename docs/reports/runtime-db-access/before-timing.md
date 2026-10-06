# Bounded before timing evidence (T02)

This is a **before-only baseline** for the seven low-EF-logging windows on the runtime-equivalent b3 host. Six windows passed response and terminal correctness; Coalesced HTTP 4 concurrency 4 is retained as a failed correctness control, not a performance result. The aggregate records independent QA review of all seven.

Source packet: `t02-baseline-timing-seven-cases-2026-10-06.json` · SHA-256 `4a650da3297885ae3403b5984dde71f3c60e60339c8cd049db68426d537770b8` · 521,727 bytes. Evidence digest and per-window configuration hashes are in [before timing ledger](evidence/before-timing-2026-10-06.json).

## Fixed source and protocol

- Build candidate `5d28bd0cd3d76004988a01b0f0abd7af3af313d6`; runtime-equivalent host head `b3f55bef32576011d39b93d664949a0cb705fb3b`; fixture head `87698620370ca2b7e9e682b1693c4f3296ec3741`; runtime DLL SHA-256 `05c8eb6ac3aac744cb4878678d90dfd27dce9b37d139422ec1dba0f0a38bb08d`.
- Fixture script SHA-256 `67830a5b25eb5de645c2c5a7e5b03badd5e9a6ab8b29db1af1b22c44b286bed6`; PostgreSQL 16.15, image `sha256:ef738a34a8651d11b2bace81c55c7e2187f786b484add6c83070884340074368`; host was Darwin ARM64 with 8 logical processors.
- Each window used 1 preflight, 25 warmups and 60 measured requests, with 86 expected instances including preflight. The first workload request followed fixture preparation, so the listed preflight time is **not a process-cold-start measurement**.
- The recorded timer starts before `HttpClient.SendAsync` and stops after reading the full response body. HTTP rows use that full-response boundary. The REST companion row times execute admission only; terminal settlement and output readback are separate. Do not subtract or compare these as equivalent request latencies.
- All cases passed the pre-batch guard: two consecutive observations with 1m and 5m load below 8 logical processors. Guard and post-warmup settlement time were excluded from measured latency. The HTTP 4 concurrency 4 Immediate window ended at 1m load 8.27, above 8, which qualifies its timing interpretation despite its clean correctness result.
- No retries or replacement samples; failed measured responses remain in the population. p95 uses nearest rank. Per-window appsettings and shells hashes vary and are preserved in the evidence JSON.

## Measured windows

| Window | Preflight ms | Measured responses | Median / p95 ms | Min–max ms | Load 1m/5m before → after | Correctness |
|---|---:|---|---:|---:|---|---|
| HTTP 4 sequential Coalesced | 1925.1608 | 60×200 | 130.4095 / 237.7586 | 97.4864–364.0199 | 6.62/5.71 → 6.43/5.70 | 86/86 Completed, zero incidents; response/output checks passed. |
| HTTP 4 sequential Immediate | 2140.0989 | 60×200 | 437.5354 / 760.9183 | 324.4115–1325.9308 | 5.99/5.37 → 6.25/5.54 | 86/86 Completed, zero incidents; response/output checks passed. |
| HTTP 16 sequential Coalesced | 3047.6589 | 60×200 | 495.7666 / 663.6608 | 469.3865–738.9338 | 5.41/5.92 → 5.35/5.87 | 86/86 Completed, zero incidents; response/output checks passed. |
| HTTP 16 sequential Immediate | 3549.0081 | 60×200 | 1839.2673 / 3116.8751 | 1250.2527–4318.6492 | 4.27/5.53 → 6.28/6.24 | 86/86 Completed, zero incidents; response/output checks passed. |
| HTTP 4 concurrency 4 Coalesced | 1379.3831 | 41×200, 10×500, 9×202 | 237.4527 / 374.5990 | 14.7902–412.4664 | 3.92/5.18 → 5.29/5.44 | FAIL: 76/86 details read back; 10 absent. 64 clean Completed, 3 Completed + 1 incident, 8 Faulted + 1 incident, 1 Faulted + 2 incidents. Correctness failed. |
| HTTP 4 concurrency 4 Immediate | 2523.9084 | 60×200 | 751.2487 / 1024.5282 | 637.6747–1206.9428 | 6.73/5.58 → 8.27/5.95 | 86/86 Completed, zero incidents; response/output checks passed. End 1m load was 8.27. |
| REST companion Coalesced | 1139.1027 | 60×200 | 70.6213 / 109.3920 | 48.4820–263.5435 | 5.03/5.49 → 5.43/5.56 | 86/86 Completed with Alice Smith, zero incidents; all admitted IDs read back. |

The evidence JSON retains mean and sample standard deviation as well as the values shown above. For the failed Coalesced concurrency window, its latency distribution includes all 60 attempts, including the 10 HTTP 500 and 9 HTTP 202 responses; it is not a successful baseline. Its readback partition is 64 clean Completed, 3 Completed with one incident, 8 Faulted with one incident, and 1 Faulted with two incidents, with 10 expected instances lacking readback. Do not present this as a successful performance control.

## Logging and interpretation

- The recorded configuration has console streaming and persisted diagnostics enabled, with EF Core, transaction and command categories at Warning. Runtime logging options were not introspected; these are retained configuration records, not runtime verification. The packet records no low-level EF logs.
- Each window retains 3 pre-measurement warnings and 4 pre-measurement EF Command errors. [Reviewed startup/background classification](startup-background-classification.md) identifies two OpenIddict pruning warnings with an unsupported `ExecuteDeleteAsync` pattern, one migration warning and four migration-history `SELECT` errors. Effective vendor provider options and the four command-error causes remain unresolved. The Coalesced concurrency window separately retains 23 measured-phase EF Query errors; its DbContext second-operation trace alignment supports a candidate DI lifetime issue without proving exact caller or causality. Do not describe these runs as exception-free.
- The aggregate is bounded to the `5d28` baseline before the T06 production pagination fix. It contains one window per configuration and no after measurement; it supports descriptive baseline accounting only. It does not establish a causal optimization, transport/cadence effect, or network round-trip count. The older noisy 392 ms protocol-only capture is excluded.

The public timing ledger retains every measured attempt (420 across the seven windows), the original sample and independent-QA artifact hashes, settings/configuration hashes, load snapshots and the descriptive statistics. Root recomputed median, nearest-rank p95, mean, sample standard deviation and extrema directly from the accepted aggregate; the report and summary match it. Published ledger SHA-256: `a2a73028707a44d004776e9918d6f9868f141b28b830a75d7d121aea221ab18c` (92,059 bytes). Each window's owned host/container/root/secrets was removed; existing databases were preserved and retained diagnostic database copies were not read.

[PR #2451](https://github.com/elsa-workflows/elsa-foundation/pull/2451) separately corrects the source-confirmed default Coalesced factory lifetime. Its accepted identity/mutation controls, current-head CI and two sequential HTTP/REST controls do not prove that it caused or resolves each error in this before C4 window. That attribution and final concurrency acceptance belong to T17/T18. This evidence packet is before-only; T02 remains in progress.
